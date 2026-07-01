# SmartCopy2026 — Production Validation Pass

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Current decision (2026-06-29): Gate 1 passed.** The original `SmokeDataset` Gate 1 runs exposed a real production/prototype gap in the staged >64 KiB path, but later MixedDataset validation with the promoted 256 KiB direct-write threshold removed the stable regression signal. On both SSD pairs now tested (`R→D` and `D→O`), `Production_Fixed` is within noise of `Prototype`; one pair lands slightly faster, one slightly slower, and neither shows a gate-quality regression. The remaining question is no longer "can production execute the bundle without unexplained overhead?" but "does the bundle generalise across drive classes?" — Gate 2.

The production copy path (`PipelineRunner → CopyStep → DefaultCopyStrategyPolicy → BatchedCopyStrategy`) had **never been measured with batching/routing enabled**: under `--mode benchmark` the harness diverts any variant carrying batch/direct-write settings to the `BenchmarkCopyRunner` prototype. The new **`--mode validation`** always drives the production runner, mapping each variant's batch/eligibility/direct-write fields onto `OperationalSettings` — so `Production_Routed` measures production where benchmark mode would measure the prototype. (`Legacy_Baseline` carries no batch settings, so it runs the production streaming path under either mode; `Production_Routed` is the variant that requires validation mode.) Gate 1 validates that production *matches* the prototype (parity) with no unexplained wrapper regression. Gate 2 validates whether the candidate bundle generalises across drive classes before broad default promotion. The MixedDataset matrix is a multi-week run, so the ladder fails fast: cheap signals before drive-weeks.

**Metric.** Median `executeDuration` per scenario/variant; variance = the `ConvergenceSpreadPercent` window (`BenchmarkConvergence`); verdicts PASS / INCONCLUSIVE / REGRESSION / INVALID (and `BELOW_THRESHOLD` where the gate clears the noise floor) per [§4.2.3](optimisation-strategies.md#423-analysis-output-requirements).

**Candidate bundle.** The settings promotion would enable — encoded in code today as off-by-default candidates behind `AllowCopyOptimisations` (default `false`). Gate 1 now clears the production-wrapper/parity risk; Gate 2 is the remaining cross-drive generalisation gate. These are the shipped defaults, not the 4 MiB discovery champion from the Phase 3 clean-room run (which production did not adopt).

| Knob | Value | Source of record |
|---|---|---|
| Copy buffer | SSD/USB 1 MiB, cross-drive HDD/Unknown 512 KiB, same-volume HDD keeps base 256 KiB | `DefaultCopyStrategyPolicy` |
| Batch buffer | 1 MiB | `AppSettings.BatchBufferKb` |
| Batch eligibility ceiling | 512 KiB | `OperationalSettings` default |
| Direct-write threshold | 256 KiB | `AppSettings.TinyFileFastPathKb` |
| Preallocation | OFF | `DefaultCopyStrategyPolicy` |

`AllowCopyOptimisations` gates routing + batch + direct-write as one unit, so the pass validates the bundle **as shipped** — there is no production mode that enables routing alone. `Production_Routed` is this bundle reached via routing; `Prototype` and `Production_Fixed` pin the same values for the Gate 1 equivalence check.

Batching is validated on SSD (Phase 3) and shown to regress on USB (Phase 6), but **never isolated on HDD** — the cross-drive suite (Phase 5) swept buffer and preallocation only, not batching. The first Gate 2 matrix found SameDriveHDD regressed with the originally routed 512 KiB HDD buffer. The candidate policy now exempts same-volume HDD from that buffer promotion; the mitigation still needs a rerun before broad promotion.

**Implementation status.** Complete; retained here as the design checklist that made the validation pass meaningful.

- **Mode + executor seam.** Add `BenchmarkRunMode.Validation`; parse `validation`/`validate` in `BenchmarkCliOptions.ParseMode`; route it in `Program.cs`. Extract the copy invocation in `BenchmarkTask.RunCopyAsync` (today's `if (directWriteThresholdBytes > 0 || bufferBatchBytes > 0)` branch) behind an `ICopyExecutor`: `PrototypeCopyExecutor` (that branch verbatim — `BenchmarkCopyRunner` when batch/direct set, else `PipelineRunner`; used by `--mode benchmark`) and `ProductionCopyExecutor` (always `PipelineRunner.ExecuteAsync`; used by `--mode validation`). `BenchmarkTask` selects the executor from the mode.
- **Settings mapping** (the crux — today nothing maps the new fields). Add `DestinationRoutingEnabled` to `BenchmarkVariant` (`Production_Routed` sets it; `Production_Fixed` leaves it off); `BufferBatchBytes` / `BatchEligibilityThresholdBytes` / `DirectWriteThresholdBytes` / `MatchedControl` already exist. Add `BenchmarkVariant.CreateProductionOperationalSettings`: `BufferBatchBytes → BatchBufferBytes`, `BatchEligibilityThresholdBytes → BatchEligibilityCeilingBytes`, `DirectWriteThresholdBytes → TinyFileFastPathThresholdBytes`, set `DestinationRoutingEnabled`, job `CopyStrategyPolicy = DefaultCopyStrategyPolicy.Instance`. The existing `CreateOperationalSettings` (legacy provider fields only) is left for the prototype path. `ProductionCopyExecutor` logs the resolved `OperationalSettings` (+ the policy's resolved buffer) at startup — the run-1 sanity check.
- **Configs + data** (authored, not code). `validation-smoke.json` (Gate 1) and `validation-matrix.json` (Gate 2). `Prototype` / `Production_Fixed` carry the candidate-bundle values (table above) as per-scenario overrides — buffer pinned to the pair, batch 1 MiB, ceiling 512 KiB, direct-write 256 KiB; the shipped `AppSettings`/policy defaults, not the discovery champion. Equivalence pairing reuses `MatchedControl` (`Production_Fixed` → `Prototype`).
- **Reused unchanged:** convergence, analysis/report, journals, dataset-prep, cooldown/cold-cache, the [§4.2.3](optimisation-strategies.md#423-analysis-output-requirements) verdict machinery, the `BenchmarkSizeScalingAnalysis` invariants, and the existing unit suite.

## Gate 1 — Production/prototype parity smoke — PASSED

**Status:** **PASSED 2026-06-29** on full `MixedDataset` SSD smoke. The earlier `SmokeDataset` runs below are retained because they explain the investigation path; the decision basis is the later MixedDataset evidence after the 256 KiB direct-write threshold, directory/staging cleanup, redundant flush removal, and manual-loop ArrayPool default fix.

- **Run:** `--mode validation --config validation-smoke.json`. **Once** (not re-run for Gate 2).
- **Scenario:** `SmokeDataset` × **D→O** (SSD→SSD). Large-file-weighted (~2.1 GiB, ~23 s/run): ~46% of wall time is ≤512 KiB (batched path), the rest in 512 KiB–32 MiB files so the streamed/routed buffer is actually exercised. **Replaces the original `SmallFileDataset` smoke**, which the new `% Copy Time` metric exposed as ~90% sub-64 KiB wall time — the >512 KiB buffer/routing path was effectively unmeasured. Needs `--mode dataset-prep` before the first run. SSDtoSSD because wrapper overhead is most exposed on the fastest pair; D→O specifically because the D→R pair gave unreproducible verdicts (below).
- **Variants** — all four, shuffled into one session (so the equivalence delta is a matched control):

  | Variant | Runner | Settings |
  |---|---|---|
  | `Prototype` | `BenchmarkCopyRunner` | candidate bundle, buffer pinned to the pair |
  | `Production_Fixed` | `PipelineRunner` | candidate bundle, buffer pinned, routing off |
  | `Production_Routed` | `PipelineRunner` | candidate bundle via routing (`AllowCopyOptimisations` on) |
  | `Legacy_Baseline` | `PipelineRunner` | 256 KiB streaming, staged (`AllowCopyOptimisations` off) |

- **Convergence:** `ConvergenceSpreadPercent`/`GatePercent` 3%, `DesiredRunCount` 5, `MaxConvergenceRuns` 3; `cooldownSeconds` 90 (short copy, low thermal load). The batched variants carry intrinsic run-to-run spread above 3% on SSD (Run C below), so the pair will not converge to the window — read run-level wall clock and treat `GaveUp` as expected, not failure.
- **PASS — all of:**
  - **Equivalence** `Production_Fixed` vs `Prototype`: non-regression — production ≤ prototype + variance.
  - **Value** `Production_Routed` vs `Legacy_Baseline`: `PASS` is the target; `BELOW_THRESHOLD` and `INCONCLUSIVE` are tolerated (a real-but-sub-gate or noise-bound delta means production is no worse); only `REGRESSION` fails here.
  - **No INVALID** — neither production copy faulted or self-reported a failure.
- **On REGRESSION** (production slower than prototype beyond variance): **STOP**, chase wrapper overhead (per-file allocation, progress wiring, `ExistsAsync` pre-check, staging) before any drive-weeks.

**Result (2026-06-21): provisional — the SSDtoSSD verdicts do not reproduce.** SmallFileDataset × SSDtoSSD, ~2m 12s–2m 49s per variant (~134,500 files after pool-clone fill, ~1 ms/file in the flat layout vs the 9 ms/file MixedDataset figure used for the original time estimate). Three sessions have now run, and they do not agree.

*Run A (non-idle, the original session), 5 converged runs/variant:*

| Pair | Role | Verdict | Delta vs Control | Noise Floor |
|---|---|---|---:|---:|
| Prototype vs Legacy_Baseline | Value (reference) | PASS | +14.9% (+25.3 s) | 9.1 s |
| Production_Fixed vs Prototype | Equivalence | INCONCLUSIVE | +0.3% (+441 ms) | 6.6 s |
| Production_Routed vs Production_Fixed | Equivalence | INCONCLUSIVE | -4.7% (-6.7 s) | 6.8 s |

*Run B (idle machine), run-level wall clock — Prototype 2m12s, Production_Fixed 2m20s, Production_Routed 2m20s, Legacy_Baseline 2m21s:*

| Pair | Role | Verdict | Delta vs Control |
|---|---|---|---:|
| Prototype vs Legacy_Baseline | Value (reference) | PASS | +6.2% |
| Production_Fixed vs Prototype | Equivalence | **REGRESSION** | ≈ -6% (≈ 8 s) |
| Production_Routed vs Production_Fixed | Equivalence | INCONCLUSIVE | ≈ 0% |
| Production_Routed vs Legacy_Baseline | production value | INCONCLUSIVE | ≈ 0% |

*Run C (non-idle), 10 runs/variant — did **not** converge:* spreads Prototype 4.05%, Production_Fixed 6.58%, Production_Routed 5.68%, Legacy_Baseline 8.30% — all above the 3% window after twice the desired run count.

**What this establishes — and what it doesn't.**
- **The equivalence leg (Production_Fixed vs Prototype) is unresolved.** Its central estimate swings from +0.3% (A) to ≈ -6% (B) — an ~8 s session-to-session swing that exceeds either session's internal spread. The verdict is a property of the session, not the code; a single session cannot settle it, and Run C shows the pair will not converge to 3% even at 10 runs.
- **Production's measured value over legacy does not reproduce on this pair.** Production_Routed beat Legacy_Baseline by ~11% in Run A but was indistinguishable from it in the idle Run B — largely because Legacy_Baseline itself ran ~28 s faster in B (2m49s → 2m21s). The Prototype→Legacy reference leg stays positive in both (+14.9%, +6.2%); the *production* value leg is the one that evaporates.
- **The bucket-throughput contradiction is a measurement artefact, not a regression.** Production reads <½ of legacy/prototype Sub256KiB MiB/s despite lower per-file medians because aggregate bucket throughput is invalid for batched variants ([§4.2.2](optimisation-strategies.md#422-bucket-level-strategy-discovery-metrics)). It is excluded from the verdict.
- **Parity is not contradicted by any stable signal.** The policy resolves once per job, not per file, so a structural ~8 s production penalty over 134k files is implausible; the REGRESSION reading is one idle session inside the swing band. Gate 1's parity intent is carried as *provisionally met* rather than proven.

**Decision (updated 2026-06-21).** D→O was run next: equivalence was clean there (Production_Fixed ≈ Prototype, +0.8%, 2.4 s spread), confirming the D→R instability was **pair-specific**. But the `% Copy Time` metric showed this dataset is ~90% sub-64 KiB wall time, so the >512 KiB buffer/routing path — the whole point of the bundle on larger files — went unmeasured. So the smoke moves to a **large-file-weighted `SmokeDataset`** (≤512 KiB ~46% of time; 512 KiB–32 MiB the rest) on **D→O**, re-run fresh with the equal-split per-file attribution and `ExistsAsync`-inclusive batched timing now in place. Gate 1 still validates the *implementation* (parity + no regression); whether the policy *generalises* is the Gate 2 matrix's job, decided on run-level wall-clock ([§4.2.2](optimisation-strategies.md#422-bucket-level-strategy-discovery-metrics)).

**Result (2026-06-22): clean-environment `SmokeDataset`, Defender excluded — Value confirmed, equivalence reopened.** The large-file-weighted `SmokeDataset` was run with Windows Defender real-time exclusions on the source/target `TestData` directories (D, E, O, M, R), 120 s cooldown, and no background load. Physical pair was **D→R** (the scenario keeps the `SmokeDataset-DtoO` label from an edited `destinationPath`; D→O is pending). Five runs/variant in the converged window.

The dominant finding is environmental: **a large fraction of all copy time measured before this run was Windows Defender activity.** With exclusions in place every run finished in <10 s and large-file throughput reached **>1 GiB/s** (Prototype Sub32MiB 1008 MiB/s) — multiples of the throughput recorded in the Defender-active Phase 5 cross-drive and buffer-scaling runs. **The Phase 5 absolute magnitudes — and any buffer/policy conclusion that rests on them — are therefore suspect and need re-validation under Defender exclusion.** The real headroom from the strategies is correspondingly larger than anything measured with AV in the path.

| Variant | Convergence | Median | Pair | Role | Verdict | Δ vs control | Noise floor |
|---|---|---:|---|---|---|---:|---:|
| Legacy_Baseline | converged @1.4% | 9.7 s | — | control | — | — | — |
| Prototype | converged @2.9% | 6.7 s | vs Legacy | Value | **PASS** | +31.1% (+3.0 s) | 168 ms |
| Production_Fixed | gave up @3.6% | 7.2 s | vs Prototype | Equivalence | **REGRESSION\*** | -6.6% (-445 ms) | 225 ms |
| Production_Routed | converged @2.2% | 7.2 s | vs Fixed | Equivalence | parity | -0.7% (-48 ms) | 207 ms |

- **Value (Prototype vs Legacy): PASS, +31%** — unambiguous, and the largest value signal recorded for the bundle, consistent with the Defender-removal reframe.
- **Routing parity (Routed vs Fixed): clean, -0.7% inside the 207 ms floor** — on a same-class SSD pair routing is a no-op by design, so parity is the intended result.
- **Equivalence (Production_Fixed vs Prototype): unresolved, -6.6% / -445 ms, outside the 225 ms floor.** Production is slower than the bare prototype beyond noise. Production_Fixed is still ~26% faster than Legacy, so this is a production-vs-prototype shortfall, not a regression against baseline.
- Distribution is **bimodal** — Production_Fixed ran 8 @ 6.9–8.8 s and 2 @ 24.4–25.1 s (the slow pair excluded from the window; likely residual AV/contention even with exclusions), which is why it gave up at 3.6%.

**The ~445 ms is localized but not explained.** Per-bucket copy time (Production_Fixed − Prototype) puts the whole gap in the **staged large-file path (>64 KiB)**; the direct-write small-file buckets are even or production-faster:

| Bucket | Write path | Δ throughput (Fixed vs Proto) |
|---|---|---|
| Sub16KiB | direct | Fixed faster |
| Sub256KiB | direct (mostly) | ≈ even |
| Sub512KiB | staged | -9% (P95 1.47→3.04 ms) |
| Sub4MiB | staged | -13% (largest contributor, ~+188 ms/run) |
| Sub32MiB | staged | -4% |

Both engines stage the identical set: `CreateProductionOperationalSettings` maps `DirectWriteThresholdBytes → TinyFileFastPathThresholdBytes` (64 KiB), so production and prototype both write >64 KiB via temp-file + atomic rename with the same buffers and batch settings. This is not a config mismatch — it is two copy engines (`BenchmarkCopyRunner` vs `PipelineRunner → BatchedCopyStrategy`).

**Ruled out as the cause (verified in code, not inferred):**
- **Per-file `ExistsAsync` destination stat** — a flat per-file cost would hit the 12 k + 4 k small files hardest, and those buckets are fine. (`SkipExistsCheckForOverwrite`, the flag that suppressed it, has since been deleted end-to-end — it traded correctness for a stat no real copy should skip; `ExistsAsync` now always runs in both engines, removing it as a variable.)
- **Preallocation / `SetLength`** — gated on `PreallocateDestinationFile`, OFF universally (`StreamCopyEngine.CopyAsync`), so it never fires.
- **`FlushAsync` (`StreamCopyEngine.CopyAsync`)** — previously flushed the managed buffer to the OS after each file, before stream close and the rename. It was **not** `Flush(flushToDisk: true)` (no `FlushFileBuffers`), so it was functionally the same flush `FileStream.DisposeAsync` already performs on close; the redundant explicit call has been removed from production.
- **Operation journal artifact I/O** — `ExecuteDuration` stops before the optional benchmark journal write, so the wall-clock comparison is not measuring journal serialization directly. A `Production_NoJournal` validation variant was added anyway to test later-run perturbation from post-execute journal artifacts; run records confirm it used `journalEnabled=false` and empty journal paths, while `Production_Fixed` wrote journals. The variant was consistently slightly slower and is now retired from active smoke runs (runtime `WriteOperationJournal` remains available, default OFF).

**Open questions:**
1. **Where does the ~445 ms actually go?** Remaining candidates are production-only work in the large-file path — the per-chunk progress path (`CopyWithManualLoopAsync` / `ProgressReportingReadStream`) vs the prototype's bare `CopyToAsync`, provider/staging ceremony, and per-file `TransformResult` yielding. None is confirmed; isolating the split needs a profiler or toggle-and-remeasure, not more reasoning over the NDJSON.
2. **Is matching the bare prototype the right bar?** Some production-only work (progress reporting, per-file result records) is legitimately required by the app and absent from the prototype by design. Gate 1's parity intent may need restating as "no *unexplained* overhead" rather than byte-for-byte equivalence with a harness that omits production responsibilities.
3. **What do the Phase 5 numbers look like with Defender excluded?** Buffer-scaling and cross-drive magnitudes were all measured with AV in the path; re-validation is needed before any absolute throughput figure or buffer choice rests on them.
4. **Does the equivalence gap reproduce on D→O?** ~~The run above was physically D→R; the D→O re-run is pending.~~ **Answered (2026-06-22b, below): yes** — D→O reproduces the gap (-5.4% / -358 ms) on a converged window. Only open question 1 (where the time goes) remains.

**Action items:** ~~delete `SkipExistsCheckForOverwrite` end-to-end~~ (done 2026-06-22 — removed from `CopyStep`, `ICopyStrategy`/`CopyStrategyBase`/`Streaming`/`Batched`, the benchmark models/executors/runner, and all config JSON; `DestinationResult.Written`, only ever returned by the skip branch, removed with it; 569 tests green); re-run Gate 1 on D→O; schedule a Phase 5 re-validation under Defender exclusion.

**Result (2026-06-22b): D→O re-run — equivalence gap reproduces and stabilizes.** The same large-file-weighted `SmokeDataset` was re-run physically D→O (Defender excluded, 120 s cooldown). This time **Production_Fixed converged** (@2.9%, no bimodal split), removing the "gave up" asterisk and confirming the gap is a stable engine difference, not measurement noise.

| Variant | Convergence | Median | Pair | Role | Verdict | Δ vs control | Noise floor |
|---|---|---:|---|---|---|---:|---:|
| Legacy_Baseline | gave up @5.8% | 10.0 s | — | control | — | — | — |
| Prototype | gave up @4.6% | 6.7 s | vs Legacy | Value | **PASS\*** | +33.4% (+3.4 s) | 445 ms |
| Production_Fixed | converged @2.9% | 7.0 s | vs Prototype | Equivalence | **REGRESSION** | -5.4% (-358 ms) | 259 ms |
| Production_Routed | gave up @3.6% | 7.3 s | vs Fixed | Equivalence | **REGRESSION\*** | -3.6% (-256 ms) | 235 ms |

- **Value holds: +33%** — consistent with the D→R run and the Defender reframe.
- **Equivalence gap confirmed on a second, independent pair: -5.4% / -358 ms**, same sign and similar magnitude as D→R's -445 ms, now on a *converged* window. This answers open question 4: **yes, the gap reproduces off D→R.**
- **Same bucket localization.** The deficit is again entirely in the staged >64 KiB path, largest in Sub4MiB (Prototype 611.6 vs Fixed 514.1 MiB/s, **-16%**); the direct-write small-file buckets are even-to-production-faster (Sub16KiB Fixed 8.22 vs Proto 7.87). Two runs, two pairs, identical shape — the localization is solid and the cause is the two-engine difference in the staged path (open question 1), not configuration, `ExistsAsync`, preallocation, or `FlushAsync`.

**Result (2026-06-27): MixedDataset R→D — less bimodal, journal-off retired.** The full `MixedDataset` smoke was run on **R→D**, a drive pair that had previously shown anomalous buffer-size scaling but usually converges better. This run was much less bimodal than the D→O/O→D MixedDataset sweeps: all three variants were reported as one broad duration cluster, and all three reached the 3% convergence window in the selected slice.

| Variant | Runs used/ran | Convergence | Selected median | All-run median | All-run mean | Global spread |
|---|---:|---|---:|---:|---:|---:|
| `Prototype` | 5/10 | converged @1.6% | 56.3 s | 56.3 s | 57.3 s | 12.0 s |
| `Production_Fixed` | 5/9 | converged @2.5% | 56.9 s | 57.8 s | 58.8 s | 6.3 s |
| `Production_NoJournal` | 5/7 | converged @1.2% | 58.8 s | 59.3 s | 60.0 s | 7.2 s |

- **Production_Fixed vs Prototype:** `INCONCLUSIVE`, -1.2% (-651 ms) with a 1.2 s noise floor. The production/prototype gap is still the same sign, but on this pair it is below the run-level noise floor and closer to ~1–3% than the earlier 5–8% smoke signal.
- **Production_NoJournal vs Production_Fixed:** `REGRESSION`, -3.2% (-1.8 s) with a 1.1 s noise floor. It was also slightly worse or effectively equal in every bucket (Tiny through Huge), so journal artifact I/O is not the explanation for the persistent production/prototype shortfall.
- **Decision:** retire `Production_NoJournal` from active benchmark configs. Leave the runtime `WriteOperationJournal` option in place because the product value question is separate; it defaults OFF and remains user-controllable.

**Follow-up (2026-06-27): manual-loop buffer pooling default fixed.** `StreamCopyEngine.CopyWithManualLoopAsync` already supported `ArrayPool<byte>`, but `OperationalSettings.UseArrayPoolForManualLoop` defaulted to `false`. With the promoted 1 MiB copy buffer, that meant any path using the manual loop without an explicit override allocated a fresh LOH-sized array per file copy. The loop did **not** allocate per 1 MiB chunk — one buffer was reused inside each copy call — but allocating one per file is still the wrong production default. The default is now `true`; explicit benchmark variants can still force `false` when an allocation-control comparison is needed.

**Result (2026-06-28/29): MixedDataset SSD smoke with 256 KiB direct-write threshold — Gate 1 passed.** `validation-smoke-mixed-dataset.json` was run with RAMMap cache clearing disabled, `Prototype`, `Production_Fixed`, and diagnostic `Production_DirectAll` active. The active production candidate (`Production_Fixed`) matched `Prototype` within noise on both SSD pairs:

| Scenario | Pair | Role | Verdict | Delta vs control | Noise floor | Decision |
|---|---|---|---|---:|---:|---|
| `MixedDataset-RtoD` | `Production_Fixed` vs `Prototype` | Equivalence | `INCONCLUSIVE` | **+2.4% (+1.4 s)** | 1.5 s | parity within noise |
| `MixedDataset-RtoD` | `Production_DirectAll` vs `Prototype` | Diagnostic | `PASS` | **+4.6% (+2.7 s)** | 1.2 s | staged-write overhead remains, but DirectAll is not the policy |
| `MixedDataset-DtoO` | `Production_Fixed` vs `Prototype` | Equivalence | `INCONCLUSIVE*` | **-1.8% (-736 ms)** | 3.0 s | parity within noise |
| `MixedDataset-DtoO` | `Production_DirectAll` vs `Prototype` | Diagnostic | `INCONCLUSIVE*` | **-4.0% (-1.7 s)** | 2.7 s | no supported gain on this noisy pair |

`D→O` is still bivalent and production did not converge cleanly there, but the fast-band comparison no longer reproduces the former stable 5-8% production deficit. `R→D` converged and put `Production_Fixed` slightly faster than `Prototype`; `D→O` put it slightly slower, within a much larger noise floor. The correct reading is **production/prototype parity, not a performance win**.

**Decision:** Gate 1 is passed. The 256 KiB direct-write threshold is the active candidate policy because it removes the reproducible staged 64-256 KiB production shortfall without applying direct writes to medium/large files. `Production_DirectAll` remains useful as a diagnostic for staged-write overhead, but it is not promoted as a durability policy. Proceed to Gate 2; do not keep chasing prototype parity on SSD smoke unless a new run produces a gate-quality `REGRESSION`.

## Gate 2 — Full matrix, fail-fast ordered · hours → weeks

**Status: STOPPED 2026-06-30 at SameDriveHDD.** `Production_Routed` passed the SSD pairs but regressed on SameDriveHDD, so the original candidate bundle does not promote. The Legacy_Baseline sleep outlier is excluded from the fast-cluster reading; the fast cluster still shows `Production_Routed` slower.

| Scenario | Production_Routed | Legacy_Baseline | Verdict | Notes |
|---|---:|---:|---|---|
| `SSDtoSSD` | 41.8 s | 48.7 s | `PASS` | +14.3% selected-window gain |
| `SameDriveSSD` | 30.8 s | 42.4 s | `PASS*` | +27.4%; both variants gave up convergence |
| `SameDriveHDD` | 7m 47s | 6m 48s | `REGRESSION*` | -14.4%; fast-cluster median still -9.6% |

SameDriveHDD bucket evidence explains the failure shape: Tiny/Small improved strongly, but Medium regressed (-15.3%) and Large was slightly slower (-2.2%). Those streamed ranges are above the 512 KiB batch ceiling, so the routed 512 KiB HDD buffer remains suspect. The batching traversal was also suspect because the batched strategy sorted each directory's files smallest-first; the follow-up ordering sweep below confirmed that size ordering is wrong for same-volume HDD, but it does **not** settle the streamed-buffer question.

**Follow-up (2026-07-01): SameDriveHDD ordering sweep — size ordering disabled for same-volume HDD.** `benchmark-samedrivehdd-ordering.json` was run with the production executor (`usePrototypeExecutor: false`) and compared `Legacy_Baseline`, `Current_Routed_OrderBySize`, and `Current_Routed_NaturalOrder`. Baseline failed formal convergence this time, but the run clusters are visually readable: `OrderBySize` formed a tight slow cluster around ~445-485 s, while `NaturalOrder` had a fast cluster around ~395-420 s plus two slow outliers around ~670 s. The conclusion is not that natural order is fully solved; it is that ordering by size is a clear SameDriveHDD regression and should not remain in the same-volume HDD policy.

Allocation/GC counters also stayed elevated for both routed variants:

| Variant | Runs with GC | Median allocated | Median Gen0 | Median Gen1 | Median Gen2 | Median heap delta | Median fragmentation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| `Legacy_Baseline` | 8 | 4.66 GB | 393 | 384 | 384 | -18.51 MB | +2.25 MB |
| `Current_Routed_OrderBySize` | 5 | 17.17 GB | 1158 | 1151 | 1150 | -5.19 MB | +5.81 MB |
| `Current_Routed_NaturalOrder` | 6 | 17.17 GB | 1203 | 1195.5 | 1195 | +1.2 MB | +12.07 MB |

Natural order's slightly higher GC count is consistent with more frequent flushes as small and large files interleave in directory order, but total allocation is effectively identical. That points to the routed production path's per-file plumbing rather than the ordering flag alone. Immediate action: disable `BatchOrderByFileSize` for same-volume HDD. Deferred action: test HDDtoHDD separately, because the same seek/read-write ordering mechanisms may apply there too.

**Next diagnostic:** run `benchmark-samedrivehdd-stream-buffer-sweep.json` before changing the HDD buffer policy. It isolates streamed copy by disabling direct-write and batching, then compares 256 KiB, 512 KiB, and 1 MiB production-path manual-loop buffers on SameDriveHDD. This answers whether the current conservative 512 KiB buffer is helping or hurting independent of batching traversal.

- **Run:** `--mode validation --config validation-matrix.json`. **Resumes across cold boots** — `BenchmarkPass` schedules only unconverged scenario/variant pairs, so a restart continues where it left off.
- **Scenarios:** `MixedDataset` × drive pairs **in this order** (cheapest / most-likely-to-fail first, so SSDtoSSD ~hours gates the multi-week HDD/USB pairs): `SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash`.
- **Variants** — two, no prototype runner: `Production_Routed` and `Legacy_Baseline` (settings as Gate 1).
- **Convergence:** `GatePercent` 3%, `DesiredRunCount` 5, `MaxConvergenceRuns` 5. USB/HDD spread is 3–9% (Phase 6 USB), so noisy pairs will not reach 3% → `GaveUp` (analysis uses the tightest window), and an effect smaller than a pair's spread reads INCONCLUSIVE per [§4.2.1](optimisation-strategies.md#421-whole-policy-validation-metrics). Do **not** loosen the threshold — the verdict already compares the delta to the actual measured spread.
- **Per-pair outcome:** `PASS` is the goal on pairs whose noise floor sits below the gate (SSD); `BELOW_THRESHOLD` and `INCONCLUSIVE` continue the matrix (routed is no worse than legacy). On noisy pairs (HDD/USB) the floor exceeds the gate, so a genuine win reads `PASS` and anything smaller reads `INCONCLUSIVE` — `BELOW_THRESHOLD` cannot arise there.
- **Abort** on the first pair returning `REGRESSION` or `INVALID` — do not finish the set then report. `BELOW_THRESHOLD` is a (sub-gate) improvement, not a failure, and never aborts.
