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

*The one place to read for "what do we currently believe." The dated evidence and per-phase history live in the files indexed in §5; this is the signal extracted from them. Last updated 2026-07-02.*

**Shipping policy (the candidate bundle).** Encoded in code, gated behind `AllowCopyOptimisations` (default **OFF**). Gate 1 production-path validation passed on SSD MixedDataset smoke. Gate 2 then failed on SameDriveHDD with the previous routed 512 KiB HDD policy ([Production Validation Pass](optimisation-strategies-production-validation.md)). Same-volume HDD now uses a 256 KiB copy buffer and disables batch size ordering. Stream-only `LargeFileDataset` sweeps on two SameDriveHDD devices show the response is drive-specific: one drive weakly favoured `64 KiB`, while the second favoured `256 KiB` and made `64 KiB` the slowest. The shared conclusion is that `2/4 MiB` is not useful and `256 KiB` is the defensible SameDriveHDD baseline until the policy is revalidated end-to-end:

| Knob | Value | Source of record |
|---|---|---|
| Copy buffer | SSD/USB 1 MiB · cross-volume HDD/Unknown 512 KiB · same-volume HDD 256 KiB | `AppSettings` → `OperationalSettings.CopyBufferRouting`, applied by `DefaultCopyStrategyPolicy` |
| Batch buffer | 1 MiB (eligibility ceiling 512 KiB) | `AppSettings.BatchBufferKb`, `OperationalSettings` |
| Batch traversal order | Order batched files by file size except same-volume HDD, which preserves natural order | `DefaultCopyStrategyPolicy`, `OperationalSettings.BatchOrderByFileSize` |
| Direct-write threshold | 256 KiB | `AppSettings.TinyFileFastPathKb` |
| Manual-loop byte buffer | Always ArrayPool-rented | `StreamCopyEngine` |
| Preallocation | OFF (universal) | `DefaultCopyStrategyPolicy` |

**Confidence — what's earned vs assumed:**

| Mechanism | Verdict | Where measured |
|---|---|---|
| Buffer routing (1 MiB SSD/USB, 512 KiB cross-volume HDD/Unknown, 256 KiB same-volume HDD) | **Partially validated**; same-volume HDD 512 KiB regressed on MixedDataset, so the policy now canonises 256 KiB pending end-to-end rerun | Phase 5, Phase 6, Production Validation Pass |
| Preallocation OFF | **Validated** — null or regresses everywhere (an earlier +11.6% SSD→HDD finding was retracted) | Phase 5 |
| Tiny-file direct write ≤ 256 KiB | **Validated on SSD**; assumed on HDD/USB | Phase 2 + MixedDataset validation |
| Batching (1 MiB / 512 KiB ceiling) | **SSD validated** (small — see below); **OFF on USB** (regresses); **not isolated on HDD** | Phase 3, Phase 6, Production Validation Pass |
| Batch traversal ordered by file size | **Disabled for same-volume HDD**; SameDriveHDD diagnostic sweep shows size ordering is a clear regression there | Production Validation Pass |
| Operation journal I/O | **Ruled out** as the production/prototype gap; disabling benchmark journals was consistently slightly slower | Production Validation Pass |
| Whole bundle on the production path | **Gate 1 passed; Gate 2 stopped** — SSD pairs passed, SameDriveHDD regressed with the previous 512 KiB same-volume HDD policy | Production Validation Pass |

**Batching is a small effect, easy to over-read.** Its genuine contribution above a 512 KiB buffer is **1–2% on SSD**, concentrated in sub-16 KiB files (Phase 3, key conclusion 1). The often-cited +8.5% "champion" is mostly the buffer/chunk-size win — which the buffer-routing policy already captures independently. Worth keeping, not worth headlining.

**Open questions:**
- **Same-volume HDD streamed buffer is drive-specific below 512 KiB.** The first Gate 2 matrix stopped at SameDriveHDD: `Production_Routed` was slower than `Legacy_Baseline` at run level, with losses concentrated in the Medium/Large streamed ranges. Same-volume HDD size ordering is now disabled. Two follow-up `LargeFileDataset` stream-only sweeps were both well clustered, but they disagree: drive A weakly favoured `64 KiB`; drive B favoured `256 KiB` and made `64 KiB` slowest. That argues against a `64 KiB` default. `256 KiB` is now the only smaller-buffer candidate worth testing against the current `512 KiB` HDD fallback in a full-policy SameDriveHDD validation.
- **HDD batching and traversal order are still only partially isolated.** The SameDriveHDD ordering sweep shows size ordering is bad for same-drive HDD, but HDDtoHDD remains untested. Cross-drive Phase 5 swept buffer/preallocation on large files; batching itself only ran on SSD and USB small-file datasets.
- **USB is too noisy for a static profile.** 1 MiB is the strongest USB signal but a *prior to be overridden by per-device learning*, not a settled finding (Phase 6). The high USB variance does **not** invalidate the SSD/HDD conclusions — it only makes USB defaults provisional.
- **SameDrive SSD larger-buffer probe** (Phase 5, Step 3) remains open; it does not block promotion.
- **Non-atomic move fallback bypasses batching.** Cross-volume / non-atomic moves copy each file individually via `TransferFileAsync`, not the batched `CopySelectionAsync`, so they miss the small-file phase-separation win. Deliberately deferred: only worth closing — a deferred-source-delete refactor (delete each source after its batch flushes, reconciled with `WalkAndMoveAsync`'s directory cleanup) — **if the Production Validation Pass promotes the bundle**. Wiring batching into a second code path before batching has earned its place in the copy path would propagate an unvalidated optimisation. The fix inherits correct per-file timing for free (Architecture §2.4.1).

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
| [Production Validation Pass](optimisation-strategies-production-validation.md) | Gate 1 parity evidence, Gate 2 fail-fast matrix, and current SameDriveHDD regression notes. | Changing default-promotion status, validation verdicts, production/prototype parity, or same-drive HDD policy. |

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

