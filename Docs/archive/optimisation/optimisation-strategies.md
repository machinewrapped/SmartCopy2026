# SmartCopy2026 — Optimisation Strategies

This document is the reference for copy performance optimisation: what we know, what we are testing next, and how benchmark evidence should be interpreted.

The end goal is not a single universally "best" copy strategy. The target is an adaptive routing policy: choose the safest and fastest practical strategy for each file-size range, then validate the whole policy against realistic datasets and destination types.

**Structure:** §2 is the front door — what we currently believe. §3 is the durable design (invariants, the routing model). §4 is how to run and interpret benchmarks. §5 is the index for detailed benchmark, phase-history, and production-validation files. Phases are referenced by **name** ("Phase 5"), which is stable across edits — do not cite section numbers from code.

## 1. The Core Tension

Large files need staged writes and granular progress for integrity and UX. Small files have the opposite problem: their content is trivially small, so per-file *ceremony* — metadata checks, stream setup, temp file create, rename, progress events — dominates wall time in a way that byte throughput never will.

There is no single best strategy. The architecture must be *adaptive*, routing each file through an appropriate code path based on its size — which the pipeline already knows from enumeration before the first byte is copied.

The MixedDataset was designed to expose both ends of this spectrum simultaneously: 72.9% of files are under 64 KiB (1.7% of bytes), while 8 files (<0.1%) exceed 256 MiB (33.4% of bytes).

An important caveat: Every file write incurs unavoidable filesystem work — MFT updates, journal entries, directory allocation table changes — that happens regardless of how the application is structured. Until the strategies below have been benchmarked, we do not know how much of the per-file overhead is reducible in practice. It may turn out that application-level overhead is a small fraction of the total, and the irreducible filesystem floor is the real constraint. The phases are designed to test this empirically rather than assume the answer.

## 2. Current Policy & Open Questions

*The one place to read for "what do we currently believe." The dated evidence and per-phase history live in the files indexed in §5; this is the signal extracted from them. Last updated 2026-07-10.*

**Shipping policy (validated for Windows promotion).** Encoded in code as a selected `CopyOptimisationPolicy` from `CopyOptimisationPlatformPolicy`. The final Windows production validation pass is complete: Gate 1 production/prototype parity passed, the mitigated full MixedDataset matrix passed, and the reduced USB Flash closure passed ([Production Validation Pass](optimisation-strategies-production-validation.md)). The final Windows verdict is **PASS**. Clean installs give the Windows policy the optimised routing/batch/direct-write values below, while macOS/Linux/Other policies are disabled legacy policies until Gate 3 platform validation exists. Same-volume HDD uses a 256 KiB copy buffer. Batch size ordering is disabled whenever the source drive is HDD; SSD-source copies keep size ordering, including SSD→HDD. Stream-only `LargeFileDataset` sweeps on two SameDriveHDD devices show the buffer response is drive-specific below 512 KiB: one drive weakly favoured `64 KiB`, while the second favoured `256 KiB` and made `64 KiB` the slowest. The shared conclusion is that `2/4 MiB` is not useful and `256 KiB` is the defensible SameDriveHDD baseline:

| Knob | Value | Source of record |
|---|---|---|
| Copy buffer | SSD/USB 1 MiB · cross-volume HDD/Unknown 512 KiB · same-volume HDD 256 KiB | `AppSettings` → `OperationalSettings.CopyBufferRouting`, applied by `DefaultCopyStrategyPolicy` |
| Batch buffer | 1 MiB (eligibility ceiling 512 KiB) | `AppSettings.BatchBufferKb`, `OperationalSettings` |
| Batch traversal order | Order batched files by file size except HDD-source copies, which preserve natural order | `DefaultCopyStrategyPolicy`, `OperationalSettings.BatchOrderByFileSize` |
| Direct-write threshold | 256 KiB | `AppSettings.TinyFileFastPathKb` |
| Manual-loop byte buffer | Always ArrayPool-rented | `StreamCopyEngine` |
| Preallocation | OFF (universal) | `DefaultCopyStrategyPolicy` |
| Platform policy | Per-platform policy objects: Windows enabled with the validated values; macOS/Linux/Other disabled until validated | `AppSettings.CopyOptimisationPlatformPolicy` |

**Confidence — what's earned vs assumed:**

| Mechanism | Verdict | Where measured |
|---|---|---|
| Buffer routing (1 MiB SSD/USB, 512 KiB cross-volume HDD/Unknown, 256 KiB same-volume HDD) | **Validated for the final production pass**; same-volume HDD 512 KiB regressed, and the mitigated 256 KiB / HDD-source-natural-order policy passed the matrix | Phase 5, Phase 6, Production Validation Pass |
| Preallocation OFF | **Validated** — null or regresses everywhere (an earlier +11.6% SSD→HDD finding was retracted) | Phase 5 |
| Tiny-file direct write ≤ 256 KiB | **Validated on SSD and HDDtoHDD MixedDataset policy rerun**; USB remains provisional | Phase 2 + MixedDataset validation |
| Batching (1 MiB / 512 KiB ceiling) | **Validated as part of the final production bundle**; isolated evidence remains strongest on SSD, USB-specific batching stays disabled/provisional, and HDD is validated at bundle level with natural source order | Phase 3, Phase 6, Production Validation Pass |
| Batch traversal ordered by file size | **Validated as a routing rule**: disabled for HDD-source copies, retained for SSD-source copies | Production Validation Pass |
| Operation journal I/O | **Ruled out** as the production/prototype gap; disabling benchmark journals was consistently slightly slower | Production Validation Pass |
| Whole bundle on the production path | **Validated for Windows promotion** — final matrix verdict PASS, with no regression or invalid run; noisy scenarios were non-regressions inside variance | Production Validation Pass |

**Batching is a small effect, easy to over-read.** Its genuine contribution above a 512 KiB buffer is **1–2% on SSD**, concentrated in sub-16 KiB files (Phase 3, key conclusion 1). The often-cited +8.5% "champion" is mostly the buffer/chunk-size win — which the buffer-routing policy already captures independently. Worth keeping, not worth headlining.

**Residual questions:**
- **Same-volume HDD streamed buffer remains device-specific below 512 KiB, but no longer blocks promotion.** The first Gate 2 matrix stopped at SameDriveHDD; the mitigated final matrix records SameDriveHDD as `INCONCLUSIVE` with a tiny +0.3% directionally positive delta inside a 30.0 s noise floor. Two follow-up `LargeFileDataset` stream-only sweeps still disagree below 512 KiB, so `256 KiB` remains the conservative baseline rather than a universal optimum.
- **HDD batching is validated at bundle level, not fully isolated.** The SameDriveHDD and HDD-source ordering sweeps show size ordering is bad when the source is HDD (`HDDtoSSD`, `HDDtoHDD`) and only mildly helpful when the target alone is HDD (`SSDtoHDD`). Current policy preserves natural order for HDD-source copies and keeps size ordering for SSD-source copies. Batching itself is still not fully isolated on HDD; cross-drive Phase 5 swept buffer/preallocation on large files, while batching was primarily proven on SSD and USB small-file datasets.
- **Alternative natural-order batching remains open.** The current natural-order path flushes before streaming a file above the eligibility ceiling. A possible next strategy is "flush when full": read files in natural order, accumulate eligible files until the batch buffer must be flushed, and write larger files as encountered instead of reordering small files by size. That may retain source-HDD locality while recovering some packing efficiency; it needs a dedicated benchmark before replacing the current traversal rule.
- **USB is too noisy for a static profile.** The reduced USB Flash closure passed, but 1 MiB remains a *prior to be overridden by per-device learning*, not a settled universal finding (Phase 6). The high USB variance does **not** invalidate the SSD/HDD conclusions — it only makes USB defaults provisional.
- **SameDrive SSD larger-buffer probe** (Phase 5, Step 3) remains open; it does not block promotion.
- **Gate 3: non-Windows platform validation.** The production validation evidence is Windows-only. macOS/Linux therefore keep disabled legacy `CopyOptimisationPolicy` entries by default. A limited macOS validation pass can populate and enable the macOS entry later if it produces the same no-regression signal.
- **Non-atomic move fallback bypasses batching.** Cross-volume / non-atomic moves copy each file individually via `TransferFileAsync`, not the batched `CopySelectionAsync`, so they miss the small-file phase-separation win. This is now worth scheduling after the default flip: use a deferred-source-delete refactor that deletes each source after its batch flushes, reconciled with `WalkAndMoveAsync`'s directory cleanup. The fix inherits correct per-file timing for free (Architecture §2.4.1).

## 3. Design Principles

### 3.1 Invariants

These apply to every phase and every strategy. They are not trade-offs.

1. **No crashes.** All operations must catch exceptions at the top level and surface them through the log/UI. Never let an exception propagate unhandled.
2. **Cooperative cancellation.** Every code path must honour `CancellationToken` and `PauseToken`. No blocking operations that last more than a few seconds.

### 3.2 UX Guidelines (Not Invariants)

These improve user experience but are subject to benchmarking and user preference:

**Best-effort file integrity on interruption.** Every reasonable effort should be made to ensure that a file is copied in its entirety, as partially copied files can subtly corrupt user data. Staged writes provide this by default; direct writes must implement cleanup logic where feasible (e.g., delete partial destination file on stream error). Edge case: if the destination device is disconnected mid-operation, integrity is limited by hardware behaviour and is not a constraint on application design.

**Directory cohesion:** Interrupted runs ideally complete full directories before stopping rather than scattering files across multiple directories. This improves resume semantics. Benchmark Phase 3 to measure the cost; provide a user setting to control the behaviour (e.g., batch size, or `CoherenceMode: None | PerDirectory | Adaptive`).

### 3.3 Adaptive Routing And Policy Discovery

The pipeline knows every file's size before execution begins. The target architecture routes each file through an appropriate code path based on size, destination profile, overwrite mode, and provider capabilities rather than using a single strategy for all files, e.g.

| File Size | Code Path |
|---|---|
| ≤ 256 KiB | Direct write (no staging), batch-eligible — Phase 2+3 |
| 256 KiB – 512 KiB | Staged write, batch-eligible — Phase 3 |
| > 512 KiB | ManualLoop, 512 KiB–1 MiB copy buffer, destination-sensitive — Phase 5+6 |

The 512 KiB boundary is the batch eligibility ceiling: files above it bypass the batch path and route directly to ManualLoop. **Evidence gap:** the 512 KiB – ~8 MiB range has no direct ManualLoop measurements (Phase 3 tested files in that range via the batch path; Phase 5 measured files above ~8 MiB). The 512 KiB copy buffer is the defensible default for that range — proven better than 4 KiB, and consistent with Phase 5 findings that 512 KiB is safe on most destination types. Same-volume HDD is the current exception under investigation: the 2026-07-02 stream-only LargeFileDataset sweeps suggest `256 KiB` may be competitive with `512 KiB`, but do not support a universal smaller buffer.

This should be a routing function operating on already-known data (`node.Size`, `overwriteMode`, `providerCapabilities`) — no I/O in the hot path.

Benchmark variants are discovery tools, not necessarily final product configurations. Early phases should identify which strategy wins per file-size bucket. Later phases should compose those bucket-level findings into candidate policies and validate those policies against whole-workload wall-clock time.

Evidence flow:
1. **Discovery:** run strategy variants on fine-grained size buckets.
2. **Bucket recommendation:** identify the best-supported strategy for each size range.
3. **Policy construction:** convert recommendations into a small routing table.
4. **Policy validation:** benchmark the whole policy on MixedDataset and destination-specific scenarios.
5. **Default promotion:** only promote defaults after whole-policy validation passes.

## 4. Operational Reference

### 4.1 Running Benchmarks

```bash
dotnet run --project .\SmartCopy.Benchmarks          # run suite
dotnet run --project .\SmartCopy.Benchmarks --mode analyze  # analyse results
```

**Note on OS File Cache (Standby List):**
Between scenarios, or between variants if configured, the benchmark needs a cold OS file cache to ensure consistent I/O measurement. By default, the suite will pause and ask you to reboot.
To automate cache clearing without rebooting, download Sysinternals [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) and configure its path in your `BenchmarkConfig.json`:

```json
  "clearCacheBetweenRuns": true,
  "ramMapPath": "D:\\Tools\\RAMMap\\RAMMap64.exe"
```

**Important:** RAMMap requires Administrator privileges to empty the working sets and standby list. You *must* run the benchmark suite from an **Administrator command prompt or PowerShell instance**. If you run it from a standard console, Windows UAC will intercept the RAMMap execution and prompt you for permission on every single cache clear.

Per-iteration protocol:
1. Run SSDtoSSD first — fastest feedback loop.
2. **Run count:** at least 2 runs per variant, randomised order. Additional run if the spread is too wide on the primary metric (total duration or throughput).
3. Promote to broader scenarios (SameDriveTest → SSDtoHDD → SSDtoUSBFlash) only after consistent SSD improvement with no correctness regressions.

### 4.2 Metrics

Use different metrics for different questions. Do not collapse discovery and validation into one headline number.

#### 4.2.1 Whole-Policy Validation Metrics

Use these when deciding whether a complete routing policy should be promoted:

**Primary:** job wall-clock `executeDuration` per scenario/variant/policy.

**Secondary:**
- Run-to-run variance (coefficient of variation or simple spread).
- `failedFiles` and exception counts.
- `copiedFiles + failedFiles + skippedFiles` versus expected selected file count.
- Destination free-space sanity, source/destination paths, path-pool usage, cold/warm notes.
- Execute-window GC counters (`executeAllocatedBytes`, Gen0/Gen1/Gen2 collection deltas,
  heap-size delta, fragmentation delta) to distinguish copy-path overhead from allocation/GC pressure.

Verdict rules:
- **Strong improvement:** median `executeDuration` improvement exceeds the gate and is larger than run-to-run variance.
- **Noise:** candidate delta is smaller than or equal to run-to-run variance.
- **Regression:** candidate is slower beyond variance.
- **Invalid:** any run has failed files, unexpected skipped files, or a correctness mismatch.

#### 4.2.2 Bucket-Level Strategy Discovery Metrics

Use these when deciding which strategy belongs to which file-size range:

- File count, byte count, `% files`, `% bytes`.
- Mean, median, and P95 per-file copy duration.
- **`% Copy Time` per bucket**: each bucket's share of the summed per-file durations (≈ the copy-phase wall clock), exposing where time is actually spent — e.g. whether tiny-file overhead dominates the run while contributing almost no bytes.
- Aggregate bucket throughput: `sum(bytes) / sum(copy durations)`.
- Mean, median, and P95 MiB/s from `benchmark-file-results.ndjson`.
- Delta versus matched controls within the same bucket.
- Variance or spread across runs for the same bucket/variant.

Bucket comparisons must be within the same file-size bucket. Total wall-clock time is misleading for comparing buckets because buckets intentionally contain different file counts and total bytes. Bucket metrics are the discovery engine for adaptive routing; whole-run wall-clock is the final reality check.

Matched controls:
- Direct-write threshold variants compare against `Control_BaselineAuto` in the same bucket.
- `StagedWriteBatch{N}` compares against `Control_BaselineAuto` to estimate phase separation.
- `DirectWriteBatch{N}` compares against `StagedWriteBatch{N}` to estimate direct write at the same buffer size.
- `DirectWriteBatch{N}` compares against `UnbatchedDirectWrite{T}` to estimate batching beyond direct write alone.

If a matched control is missing, analysis must say "cannot isolate effect" rather than infer a cause.

#### 4.2.3 Analysis Output Requirements

Reports should separate measurement from interpretation:

1. **Run-level table:** successful run count, median/mean/min/max `executeDuration`, spread, delta versus matched baseline, verdict.
2. **Bucket strategy table:** best observed strategy per bucket, delta versus matched control, variance status, recommendation.
3. **Missing-control warnings:** list variants that cannot support causal conclusions.
4. **Policy candidate summary:** proposed routing table built from bucket evidence.
5. **Policy validation summary:** whole-run result for candidate policies on MixedDataset and destination matrix.

Use mechanical verdict language. The terms are adoption decisions, not a single speed ranking — `BELOW_THRESHOLD` is faster than the control (a better measured result than `INCONCLUSIVE` or `REGRESSION`); it just doesn't clear the gate that justifies promoting it. The `BELOW_THRESHOLD` band only exists where the gate sits above the measured noise floor (e.g. SSD, ~0.2% spread vs a 3% gate); on noisy media whose spread exceeds the gate it is empty, since clearing the noise already clears the gate.
- `PASS`: faster, clears both the gate and the observed variance.
- `BELOW_THRESHOLD`: faster beyond variance, but short of the gate — real improvement, not enough to promote.
- `INCONCLUSIVE`: delta is within variance, or a matched control is missing.
- `REGRESSION`: slower beyond variance.
- `INVALID`: correctness or run-integrity problem.

**Operational sanity:** CPU usage trend during tiny-file phases; per-file `ExecutionDuration` distribution (flag outliers >1 s).

### 4.3 Things to Avoid

- Re-architecting the full pipeline model to chase a performance hypothesis.
- Sacrificing progress visibility to chase raw throughput.
- Destination-specific hardcoding without a capability-based fallback.
- Beginning Phase 7 (parallelism) before Phases 2 and 3 have been benchmarked and shown insufficient. Parallelism adds correctness complexity — cancellation, progress semantics, collision avoidance — that sequential strategies avoid entirely.
- Over-fitting to MixedDataset. Validate strategies against the byte-volume-dominated dataset and real-world directory structures before promoting defaults.
- Promoting USB parallelism defaults without dedicated measurement. The USB baseline covers buffer/write-mode variants only; concurrency behaviour on USB flash is entirely unknown.

### 4.4 Pre-Merge Checklist

Apply before each Core merge. Benchmark-only changes (no Core code) do not require all of these.

1. `dotnet build` succeeds.
2. All unit tests pass (`dotnet test`).
3. Benchmark run metadata recorded with cold/warm condition notes.
4. Results appended to `benchmark-results.ndjson` and `benchmark-file-results.ndjson`.
5. Architectural changes reflected in `Docs/Architecture.md`.
6. New `OperationalSettings` fields default to 0/disabled — no behaviour change without explicit opt-in.
7. Small-file strategy changes validated on SmallFileDataset first; real-world validation pass on MixedDataset before promoting defaults. Buffer scaling changes validated on byte-volume-dominated dataset.
8. `copiedFiles + failedFiles + skippedFiles` equals the expected total across all runs.
9. Direct-write (non-staged) changes: spot-check file content bit-identity against staged baseline.

## 5 Reference Files

Dated evidence, raw findings, progress reports and dataset detail can be found in adjunct files.

This document should contain summaries with enough context to know when and where to look for further information.

| File | Contains | Open when |
|---|---|---|
| [Benchmark Datasets](optimisation-strategies-benchmark-datasets.md) | Dataset roles: SmallFileDataset, LargeFileDataset, and policy validation datasets. | Preparing fixtures, interpreting bucket coverage, or deciding which dataset answers a benchmark question. |
| [Production Validation Pass](optimisation-strategies-production-validation.md) | Final production validation results, Gate 1 parity evidence, mitigated Gate 2 matrix, and USB Flash closure. | Changing default-promotion status, validation verdicts, production/prototype parity, or drive-class policy. |

**Phase History Archive**

| Phase | Status | File | Summary | Open when |
|---|---|---|---|---|
| Phase 1 | DONE | [Metadata Overhead Reduction & Baseline](optimisation-strategies-phase-1.md) | Established the baseline and proved that small-file overhead, not byte-copy mechanics, dominates SSD/HDD copy time. | Rebuilding historical baselines or explaining why buffer tuning alone could not fix tiny-file performance. |
| Phase 2 | DONE | [Tiny-File Direct Write](optimisation-strategies-phase-2.md) | Validated direct writes for small files and records the 256 KiB production threshold rationale. | Changing the direct-write threshold, staged-write trade-off, or partial-write cleanup behaviour. |
| Phase 3 | DONE | [Buffered Read-Write Batching](optimisation-strategies-phase-3.md) | Captures the clean-room SSD evidence for batching, direct-write isolation, and the `BatchedCopyStrategy` design. | Touching batch eligibility, batch buffer sizing, per-file timing, or claims about batching contribution. |
| Phase 4 | DONE | [Progress Throttling](optimisation-strategies-phase-4.md) | Documents the progress reporter extraction and dual-gate throttling defaults. | Changing progress event cadence, progress UX, or execution progress settings. |
| Phase 5 | DONE | [Buffer Size Scaling](optimisation-strategies-phase-5.md) | Cross-drive large-file buffer sweep; retracts preallocation and supports 1 MiB SSD / 512 KiB HDD defaults. | Changing large-file copy buffers, preallocation, or the open SameDriveSSD larger-buffer probe. |
| Phase 6 | DONE | [Destination-Sensitive Policies](optimisation-strategies-phase-6.md) | Static destination routing, USB validation, and per-device learned profile design notes. | Changing destination profiles, provider capabilities, USB policy, or adaptive per-device learning. |
| Phase 7 | RESERVED | [Parallelism](optimisation-strategies-phase-7.md) | Reserved last-resort concurrency design and benchmark gates. | Considering any parallel copy path or concurrency default. |
