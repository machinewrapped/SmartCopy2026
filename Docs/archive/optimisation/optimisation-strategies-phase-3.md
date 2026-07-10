# SmartCopy2026 — Phase 3: Buffered Read-Write Batching

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Status:** Core integration complete; Gate 1 production-path parity passed; Gate 2 cross-drive generalisation pending (see the Production Validation Pass). The batching coordinator lives in `BatchedCopyStrategy` (with `BatchCopyBuffer`), selected by the policy when `BatchBufferBytes > 0`.

**Goal:** Replace the read-write interleave on every file with read-phase then write-phase batching, so many small files share each flush (phase separation).

**What batching does:** The current model interleaves read and write on every file, alternating I/O direction continuously. Batching accumulates multiple small files into a pool-allocated buffer during a read phase, then drains it during a write phase:

```
Current:  Read f₁ → Write f₁ → Read f₂ → Write f₂ → ...
Batched:  Read f₁ → Read f₂ → ... → Read fₙ  [buffer fills]
          Write f₁ → Write f₂ → ... → Write fₙ  [buffer drains]
```

## Clean-Room Run (2026-06-01) — the Phase 2+3 consolidated evidence

1,057,782 per-file records from 94 converged runs (5–7 per variant) across 17 variants on `SmallFileDataset-SSDtoSSD` (11,253 files, ~907 MiB). Run environment: dedicated machine, no competing processes, MalwareBytes disabled, ambient temperature controlled. **Run-level spread was under 100 ms for all variants** (< 0.2% coefficient of variation on 58–64 second runs) — the lowest noise floor observed across all SmartCopy benchmark campaigns. This run is the source for both the Phase 2 direct-write isolation (above) and the batching evidence below.

> [!IMPORTANT]
> The automated 10% gate classified all variants as `BELOW_THRESHOLD` because no variant crossed the threshold. The gate was designed for noisy environments; with this noise floor the classification is too conservative. The evidence below should be read on its merits: consistent rankings, tight spreads, and clear size-dependent patterns.

**Run-level rankings** — all 17 variants sorted by median execute duration:

| Rank | Variant | Median | Spread | Delta vs Control |
|---:|---|---:|---:|---:|
| 1 | **DirectWriteBatch4MiB** | **58.65 sec** | 53 ms | **+8.5%** |
| 2 | DirectWrite512KiB | 59.39 sec | 22 ms | +7.4% |
| 3 | DirectWriteBatch1MiB | 59.41 sec | 3 ms | +7.4% |
| 4 | DirectWriteBatch256KiB | 59.49 sec | 53 ms | +7.2% |
| 5 | DirectWriteBatch512KiB | 59.57 sec | 94 ms | +7.2% |
| 6 | DirectWrite4MiB | 59.83 sec | 73 ms | +6.7% |
| 7 | DirectWrite1MiB | 60.02 sec | 135 ms | +6.9% |
| 8 | DirectWrite256KiB | 60.32 sec | 52 ms | +6.0% |
| 9 | StagedWriteBatch16MiB | 60.61 sec | 105 ms | +5.5% |
| 10 | DirectWrite64KiB | 60.78 sec | 12 ms | +5.2% |
| 11 | StagedWriteBatch4MiB | 60.81 sec | 3 ms | +5.2% |
| 12 | StagedWriteBatch1MiB | 61.49 sec | 19 ms | +4.1% |
| 13 | DirectWrite16KiB | 62.09 sec | 1 ms | +3.8% |
| 14 | StagedWriteBatch512KiB | 62.13 sec | 63 ms | +3.1% |
| 15 | StagedWriteBatch256KiB | 62.41 sec | 43 ms | +2.7% |
| 16 | DirectWrite4KiB | 62.87 sec | 94 ms | +2.1% |
| 17 | **Control_BaselineAuto** | **64.25 sec** | 62 ms | **—** |

**Control_BaselineAuto is the slowest variant.** Every optimised variant outperforms it. The top cluster (ranks 1–5) centres on ~59.5 sec, a consistent ~7–8.5% improvement.

**Batching contribution — isolated effect.** Compare `StagedWriteBatch{N}` against `Control_BaselineAuto` (both use staged writes) to isolate the batching contribution:

| Variant | Median | Δ vs Control |
|---|---:|---:|
| StagedWriteBatch256KiB | 62.41 sec | +2.7% |
| StagedWriteBatch512KiB | 62.13 sec | +3.1% |
| StagedWriteBatch1MiB | 61.49 sec | +4.1% |
| StagedWriteBatch4MiB | 60.81 sec | +5.2% |
| StagedWriteBatch16MiB | 60.61 sec | +5.5% |

**Batching alone (staged writes) is worth 3–5.5% over unbatched control.** Gains scale with buffer size but plateau around 4–16 MiB (diminishing returns above 4 MiB: only +0.3% more from 4→16 MiB).

Bucket-level throughput (`StagedWriteBatch4MiB` vs `Control_BaselineAuto`, Mean MiB/s):

| Bucket | Control | StagedWriteBatch4MiB | Δ |
|---|---:|---:|---:|
| Sub4KiB | 0.49 | 0.50 | +2% |
| Sub16KiB | 1.80 | 1.95 | +8% |
| Sub64KiB | 5.98 | 6.25 | +5% |
| Sub256KiB | 21.85 | 21.98 | +1% |
| Sub512KiB | 51.11 | 52.12 | +2% |
| Sub1MiB | 92.99 | 93.63 | +1% |
| Sub4MiB | 155.54 | 166.21 | +7% |

Batching helps most at Sub16KiB (+8%) and Sub4MiB (+7%). The Sub16KiB result is genuine phase separation — many small files share each flush. The Sub4MiB result is a different mechanism: files in that range fill most or all of the batch buffer alone, so there is no second file sharing the flush. The gain comes from reading the whole file in one shot rather than in 4 KiB chunks — a large-buffer effect, not a batching effect. This improvement is contingent on the 4 KiB baseline and does not carry forward once 512 KiB is the floor. Files above 512 KiB should route to the ManualLoop copy path (Phase 5), not the batch path.

**Combined effect: DirectWriteBatch4MiB vs Control.** The overall champion combines both direct write and batching. Bucket-level throughput (Mean MiB/s):

| Bucket | Control | DirectWriteBatch4MiB | Δ |
|---|---:|---:|---:|
| Sub4KiB | 0.49 | 0.56 | **+14%** |
| Sub16KiB | 1.80 | 2.08 | **+16%** |
| Sub64KiB | 5.98 | 6.47 | **+8%** |
| Sub256KiB | 21.85 | 22.68 | **+4%** |
| Sub512KiB | 51.11 | 53.68 | **+5%** |
| Sub1MiB | 92.99 | 96.31 | **+4%** |
| Sub4MiB | 155.54 | 170.25 | **+9%** |

The combined effect is strongest at Sub4KiB (+14%) and Sub16KiB (+16%) — the smallest files benefit most from eliminating both staging overhead and I/O direction interleaving. The Sub4MiB bucket shows +9%, but this is a large-buffer effect (reading the whole file at once vs 4 KiB chunks), not phase separation.

**Key conclusions:**

1. **The 8.5% headline improvement is mostly chunk-size, not phase separation.** `DirectWrite512KiB` (unbatched, 512 KiB buffer, no staging) scores +7.4% over the 4 KiB control — almost all the gain comes from replacing 4 KiB chunks with 512 KiB chunks. `DirectWriteBatch4MiB` adds only a further ~1.3% above `DirectWrite512KiB`. The true phase-separation contribution, above a 512 KiB baseline, is 1–2% at run level, concentrated in the Sub16KiB range where many files genuinely share each flush.

2. **Direct write helps most for tiny files, not large files.** The staging overhead (temp file create → write → rename) is a proportionally larger fraction of per-file time when the byte payload is small. For Sub4KiB files, the staging lifecycle dominates; for Sub4MiB files, it's a rounding error against the byte-copy time.

3. **Sub4MiB batch improvement is a large-buffer effect, not phase separation.** Files in that range fill the batch buffer alone — there is no second file sharing the flush. The gain is from reading the whole file in one shot rather than 4 KiB chunks. This does not carry forward once 512 KiB is the floor, and those files should route to the ManualLoop path rather than the batch path.

4. **Genuine phase separation requires ≥2 files per flush, which constrains the batch eligibility threshold.** With a 4 MiB batch buffer, the effective batch eligibility ceiling is **512 KiB** — this guarantees at least 8 files per flush and ensures phase separation is always the operating mechanism. Files above 512 KiB bypass the batch path and route to ManualLoop (Phase 5 parameters).

5. **Batch buffer size plateaus around 4 MiB.** The 4 MiB→16 MiB buffer increase yields only +0.3% additional improvement. 4 MiB is the practical ceiling.

6. **The 4 KiB baseline (Control_BaselineAuto) is excluded from Phase 5/6 buffer-sizing benchmarks** — definitively suboptimal, it would flatten the comparison scale. It is retained in the MixedDataset policy validation run as a legacy anchor: the gain from old default → adaptive policy on realistic data is the headline result. Future buffer-sizing benchmarks use `ManualLoop512KiB` as the control.

7. **The 10% gate is too conservative for clean-room data.** With <100 ms spread on 60-second runs, effects as small as 2% are reliably distinguishable from noise. Future clean-room runs should use a noise-relative gate (e.g., delta must exceed 2× the combined noise floor) rather than a fixed percentage.

## Resulting design (as built)

The implementation follows from the conclusions above. A batching coordinator (`BatchedCopyStrategy` with `BatchCopyBuffer`) sits above the step layer, accumulating files from the enumerated tree into a pool-allocated buffer:

- **Batch eligibility ceiling: 512 KiB** (`OperationalSettings.BatchEligibilityCeilingBytes`, default on) — files above it bypass the batch path and route to ManualLoop. This is conclusion 4: keeping ≥2 files per flush is what makes phase separation happen, rather than a solo-file flush whose "gain" is just a large-buffer effect.
- **Default buffer size: 1 MiB.** One progress event per buffer flush regardless of file count. Flush frequency is `(tiny-file accumulation rate) × (buffer size)`, not the eligibility ceiling — a 1 MiB buffer flushes 4× more often than 4 MiB, finer wall-time progress at a 1.3% throughput cost (59.41 s vs 58.65 s). 4 MiB is available for max-throughput workloads via `BatchBufferBytes` (flows `PipelineJob.OperationalSettings` → `IStepContext.OperationalSettings` → `CopyStep` — no provider changes).
- Progress events per batch, not per file.
- **Intentional depth-first walk with per-directory ascending-size sorting** — each directory's selected files are copied smallest-first, then each child subtree is completed before the next sibling. This preserves directory cohesion and improves resume semantics while keeping useful buffer packing. Directory coherence (constraining a batch to one directory) remains a possible user option (`CoherenceMode: None | PerDirectory`), not a hard requirement.

As of 2026-06-14 this full design — measured by the `BenchmarkCopyRunner` harness — is propagated to production. Because the ceiling is already motivated on SSD by the clean-room run (conclusion 4), it does **not** need re-proving by the multi-week validation matrix; a cheap SmallFileDataset × SSDtoSSD A/B suffices if re-confirmation is ever wanted.

**Acceptance criteria:**
- MixedDataset × SSDtoSSD `executeDuration` improves beyond run-to-run variance with no correctness regressions.
- `copiedFiles + failedFiles + skippedFiles` equals expected total.
- File content bit-identical to staged baseline for the direct-write path.
- No behaviour change when batching is disabled (default off until policy validated on MixedDataset).

## Execution checklist (complete)

**A — Tooling**

- [x] Add `BufferBatchBytes` to benchmark scenario/variant/run models
- [x] Implement buffered read/write batching in `BenchmarkCopyRunner`
- [x] Enforce batch-fit rule: a file is batched only if it fits in the configured batch buffer
- [x] Add Phase 3 scenario configs for staged/direct batch pairs
- [x] Verify `dotnet build SmartCopy.Benchmarks/SmartCopy.Benchmarks.csproj`

**B — Discovery Benchmarks**

- [x] Run `benchmark-scenarios-phase3.json` on SmallFileDataset × SSDtoSSD
- [x] Re-run variants until each has at least 2 successful runs — clean-room run: 5–7 converged runs per variant, <100 ms spread
- [x] Add a 3rd run where run-level spread exceeds the variance threshold — all variants converged within 3% tolerance
- [x] Analyze with bucket-level matched controls — see the clean-room run above
- [x] Produce bucket-level strategy recommendations — see the key conclusions above

---
