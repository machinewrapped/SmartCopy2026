# SmartCopy2026 — Optimisation Strategies

This document is the reference for copy performance optimisation: what we know, what we are testing next, and how benchmark evidence should be interpreted.

The end goal is not a single universally "best" copy strategy. The target is an adaptive routing policy: choose the safest and fastest practical strategy for each file-size range, then validate the whole policy against realistic datasets and destination types.

**Structure:** §2 is the front door — what we currently believe. §3 is the durable design (invariants, the routing model). §4 is how to run and interpret benchmarks. §5 describes the benchmark datasets. §6 is the per-phase history (question → method → findings → conclusion) and is the dated evidence trail everything else is extracted from. Phases are referenced by **name** ("Phase 5"), which is stable across edits — do not cite section numbers from code.

## 1. The Core Tension

Large files need staged writes and granular progress for integrity and UX. Small files have the opposite problem: their content is trivially small, so per-file *ceremony* — metadata checks, stream setup, temp file create, rename, progress events — dominates wall time in a way that byte throughput never will.

There is no single best strategy. The architecture must be *adaptive*, routing each file through an appropriate code path based on its size — which the pipeline already knows from enumeration before the first byte is copied.

The MixedDataset was designed to expose both ends of this spectrum simultaneously: 72.9% of files are under 64 KiB (1.7% of bytes), while 8 files (<0.1%) exceed 256 MiB (33.4% of bytes).

An important caveat: Every file write incurs unavoidable filesystem work — MFT updates, journal entries, directory allocation table changes — that happens regardless of how the application is structured. Until the strategies below have been benchmarked, we do not know how much of the per-file overhead is reducible in practice. It may turn out that application-level overhead is a small fraction of the total, and the irreducible filesystem floor is the real constraint. The phases are designed to test this empirically rather than assume the answer.

## 2. Current Policy & Open Questions

*The one place to read for "what do we currently believe." The dated evidence and per-phase history live in §6; this is the signal extracted from them. Last updated 2026-06-20.*

**Shipping policy (the candidate bundle).** Encoded in code, gated behind `AllowCopyOptimisations` (default **OFF**) pending the production-path validation (§6, Production Validation Pass):

| Knob | Value | Source of record |
|---|---|---|
| Copy buffer | SSD/USB 1 MiB · HDD/Unknown 512 KiB | `DefaultCopyStrategyPolicy.SelectBufferBytes` |
| Batch buffer | 1 MiB (eligibility ceiling 512 KiB) | `AppSettings.BatchBufferKb`, `OperationalSettings` |
| Direct-write threshold | 64 KiB | `AppSettings.TinyFileFastPathKb` |
| Preallocation | OFF (universal) | `DefaultCopyStrategyPolicy` |

**Confidence — what's earned vs assumed:**

| Mechanism | Verdict | Where measured |
|---|---|---|
| Buffer routing (1 MiB SSD/USB, 512 KiB HDD/Unknown) | **Validated** across multiple drive pairs | Phase 5, Phase 6 |
| Preallocation OFF | **Validated** — null or regresses everywhere (an earlier +11.6% SSD→HDD finding was retracted) | Phase 5 |
| Tiny-file direct write ≤ 64 KiB | **Validated on SSD**; assumed on HDD/USB | Phase 2 |
| Batching (1 MiB / 512 KiB ceiling) | **SSD validated** (small — see below); **OFF on USB** (regresses); **HDD never measured** | Phase 3, Phase 6 |
| Whole bundle on the production path | **Unmeasured — pending the Production Validation Pass** | — |

**Batching is a small effect, easy to over-read.** Its genuine contribution above a 512 KiB buffer is **1–2% on SSD**, concentrated in sub-16 KiB files (Phase 3, key conclusion 1). The often-cited +8.5% "champion" is mostly the buffer/chunk-size win — which the buffer-routing policy already captures independently. Worth keeping, not worth headlining.

**Open questions:**
- **HDD batching is unmeasured.** The cross-drive suite (Phase 5) swept buffer/preallocation on large files; batching only ran on SSD and USB small-file datasets. Gate 2's SSDtoHDD pair (Production Validation Pass) is where it's earned.
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
| ≤ 64 KiB | Direct write (no staging), batch-eligible — Phase 2+3 |
| 64 KiB – 512 KiB | Staged write, batch-eligible — Phase 3 |
| > 512 KiB | ManualLoop, 512 KiB–1 MiB copy buffer, destination-sensitive — Phase 5+6 |

The 512 KiB boundary is the batch eligibility ceiling: files above it bypass the batch path and route directly to ManualLoop. **Evidence gap:** the 512 KiB – ~8 MiB range has no direct ManualLoop measurements (Phase 3 tested files in that range via the batch path; Phase 5 measured files above ~8 MiB). The 512 KiB copy buffer is the defensible default for that range — proven better than 4 KiB, and consistent with Phase 5 findings that 512 KiB is safe on all destination types.

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

> [!CAUTION]
> **Batched per-file timing: fixed 2026-06-21 for new runs; data recorded before that date carries the old artefact.** Aggregate bucket throughput divides bucket bytes by the *sum of per-file copy durations*. For **streaming** variants each file's recorded duration ≈ its own transfer time, so the sum ≈ wall-clock and the figure is meaningful. For **batched** variants the per-file duration is not a direct measurement (batching reads many files before any write; OS write-back decouples a WriteAsync return from bytes-to-disk — the batch is the only real unit). The pipeline now times each batch's destination-check + read + flush as a unit and splits it **evenly across the batch's files** (Architecture §2.4.1) — even, *not* bytes-proportional, because small-file copy time is overhead-dominated, so a 1-byte and a 100-byte file cost essentially the same. The per-file `ExistsAsync` ceremony is banked too, so batched timing matches the streaming control (whose cadence already includes it) — bucket comparisons are apples-to-apples. Per-file durations sum to the batch's elapsed time, so **run-level and scenario-level aggregate throughput are conserved and cross-strategy comparable**; and because time is split evenly, per-bucket *throughput* rises with file size — the genuine overhead-dominated signal — rather than collapsing to one batch rate.
> **Two caveats remain.** (1) *Data recorded before 2026-06-21* used yield-cadence attribution, which dumped the whole batch's read cost on the first file flushed — for that data batched per-bucket throughput is an attribution artefact and only intra-strategy comparisons hold; re-run rather than mine it. (2) The even split is honest where overhead dominates (the bulk of batch-eligible files) but slightly *under*-times the largest batch-eligible files (256–512 KiB), where byte-copy time is no longer negligible — their bucket throughput reads a little optimistic. **Gate 2's cross-strategy verdict (Production_Routed batched vs Legacy_Baseline streaming) rests on run-level wall-clock regardless** — the simplest measure that needs no per-file attribution at all.

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

## 5. Benchmark Datasets

### 5.1 SmallFileDataset (granular small-file discovery)

**Role:** Required for Phase 2/3 discovery. Tooling exists and `SmallFileDataset` has been used for Phase 2/3. Keep using it for per-bucket strategy discovery.

The MixedDataset aggregates all files under 64 KiB into a single "Tiny" bucket. This is sufficient to establish that per-file overhead dominates, but cannot identify *at which size threshold* staging removal becomes effective — the core question of Phase 2. A dedicated dataset with fine-grained sub-64 KiB buckets is required for Phase 2 benchmark results to be actionable.

**Design rationale:** Optimises for signal over realism. Files are selected by size from the source pool and written into flat per-bucket subdirectories rather than preserving source directory structure. This deliberately produces a controlled layout — same-sized files grouped together — which maximises the signal-to-noise ratio for per-file overhead measurement by size band. The MixedDataset (source structure preserved) serves as the real-world validation pass once strategies are identified.

**Run-time basis:** MixedDataset SSDtoSSD data gives ~9.1 ms/file overhead rate (123 s ÷ 13,540 files in the 0–64 KiB bucket). At ~1,000 files per sub-bucket, ~5,100 total files × 9 ms ≈ **~60 seconds per variant** on SSD. The active matrix size changes as variants are retired or added; use the scenario config and desired run counts as the source of truth for total runtime.

**Dataset name:** `SmallFileDataset`, destination e.g. `R:\TestData\SmallFileDataset`

**Bucket specification** — boundaries deliberately aligned to Phase 2 variant thresholds (4 / 16 / 64 / 256 / 512 / 1024 KiB) plus a 1-4 MiB tail so each variant's impact falls cleanly within named buckets:

| Bucket | Min | Max | Target | Approx files |
|--------|-----|-----|--------|--------------|
| `Sub4KiB` | 0 | 4 KiB | 40 MiB | source-dependent |
| `Sub16KiB` | 4 KiB+1 | 16 KiB | 80 MiB | source-dependent |
| `Sub64KiB` | 16 KiB+1 | 64 KiB | 120 MiB | source-dependent |
| `Sub256KiB` | 64 KiB+1 | 256 KiB | 160 MiB | source-dependent |
| `Sub512KiB` | 256 KiB+1 | 512 KiB | 200 MiB | source-dependent |
| `Sub1MiB` | 512 KiB+1 | 1 MiB | 200 MiB | source-dependent |
| `Tail` | 1 MiB+1 | 4 MiB | 200 MiB | source-dependent |

Total: ~1 GiB. File counts depend on the available source corpus. If the primary source directory does not supply enough candidates to fill all buckets, point the tool at an additional source directory and re-run — the existing `ExistingDestinationSkips` logic ensures already-filled buckets are not overwritten. Do not pad with synthetic files.

**Tooling requirements:**

1. **`OrganizeByBucket` flag in `DatasetPreparationConfig`** — when `true`, files are written to `{DestinationPath}\{BucketName}\{filename}` (flat per bucket) rather than preserving source relative paths. Default `false` preserves existing MixedDataset behaviour exactly. On filename collision within a bucket directory, skip the candidate and source the next one — do not rename or append an index. This naturally suppresses ubiquitous filenames (`desktop.ini`, `thumbs.db`, etc.) that would otherwise crowd out more varied candidates in small buckets. The existing `ExistingDestinationSkips` counter covers skipped collisions.

2. **New `SmallFileDataset` scenario in scenario config** — bucket config as above with `OrganizeByBucket: true` and destination path. Generation runs with the existing `--mode dataset-prep` flow:
   ```
   dotnet run --project .\SmartCopy.Benchmarks --mode dataset-prep --scenario SmallFileDataset
   ```

3. **Analysis tool — configurable bucket breakpoints** — analysis must use the dataset's own bucket definitions so SmallFileDataset results show the Sub4KiB / Sub16KiB breakdown rather than one aggregated "Tiny" row. The current tool reads bucket boundaries from `datasetPreparation` when present; keep this behaviour for future discovery datasets.

4. **`--config <file>` CLI flag in `BenchmarkCliOptions`** — override the config file name (default: `benchmark-scenarios.json`). Phase 2 lives in its own config file (e.g. `benchmark-scenarios-phase2.json`) so the Phase 1 historical results and scenario definitions are frozen and isolated. Run dataset prep and benchmarks with:
   ```
   dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json --mode dataset-prep
   dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json
   ```

5. **`SourcePath` on `BenchmarkScenario`** (optional, falls back to global `config.SourcePath`) — allows a single Phase 2 config to hold both SmallFileDataset discovery scenarios and MixedDataset validation scenarios without switching config files mid-phase. MixedDataset validation scenarios start `Enabled: false` and are enabled manually after the top candidates are identified from SmallFileDataset results.

### 5.2 LargeFileDataset (byte-volume-dominated)

The MixedDataset cannot isolate large-file throughput — tiny-file overhead dominates every aggregate metric. A second dataset is needed where large files dominate by byte volume. `LargeFileDataset` provides this role; it is the dataset behind the Phase 5 buffer-sizing and cross-drive validation runs.

### 5.3 Policy Validation Datasets

After bucket-level discovery identifies promising routing rules, benchmark complete policies against larger and more realistic datasets:

- **MixedDataset:** realistic file-count distribution and directory structure; primary validation for small-file policy value.
- **Byte-volume-dominated dataset:** large-file throughput and buffer-size policy validation.
- **Destination matrix:** SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash. SameDrive and USB must not inherit SSD defaults without evidence.

Policy validation uses whole-run `executeDuration` as the primary metric. Bucket metrics explain why a policy works or fails; they do not by themselves prove the policy should ship.

## 6. Phase History

The dated evidence trail, organised by phase. Each phase follows one arc — **question → method → findings → conclusion**. Phases are ordered by **increasing complexity and risk**; do not begin a phase before the previous one has been benchmarked and a gate decision made. Phase 5 (buffer scaling) is independent and can run alongside Phases 4 and 6. Phase 7 (parallelism) is the last resort — it is listed last because it should be tried last. The live work is the Production Validation Pass at the end.

---

### Phase 1 — Metadata Overhead Reduction & Baseline

**Status:** Essentially complete; pending validation. Core changes implemented but not yet benchmarked against historical baseline. All four baseline variants have two successful runs on SSDtoSSD, SameDriveTest, and SSDtoHDD; the USB baseline is essentially complete (BaselineAuto and CopyToAsync512KiB have two USB runs each; the ManualLoop variants one each — the pending second runs are unlikely to shift conclusions).

**Goal:** Eliminate per-file filesystem overhead. Establishes the baseline against which all subsequent phases are measured.

The current per-file overhead chain:
1. `OpenReadAsync` → `File.Exists` + `FileStream` open
2. `ExistsAsync` on destination
3. `Directory.CreateDirectory` for each file.
4. `WriteAsync` → staged temp create + byte copy + atomic rename
5. PipelineRunner `await foreach` + per-result progress + ETR calculation

**Changes:**

- **`LocalFileSystemProvider.OpenReadAsync`** — remove the `File.Exists` pre-check. Let `FileStream` throw `FileNotFoundException` directly (already caught by the error handler). No observable behaviour change.
- **`CopyStep.ApplyAsync`** — test removing the `ExistsAsync` pre-check `OverwriteMode.Overwrite`. This costs us information, i.e. the ability to record whether the destination file was created or overwritten, so it should be gated on a switch and disabled if the performance gain is not significant.
- **`LocalFileSystemProvider.WriteAsync`** — Track the last created directory (`_lastCreatedDirectory`) and skip the `CreateDirectory` call when the target directory matches. Invalidate on `IOException` so the next attempt retries.
- **`LocalFileSystemProvider.WriteAsync`** — A directory that was just created by definition contains no files, so skip the Exists check on the target for files in a directory we just created.

#### First sweep — four-variant baseline

*The original broad sweep that framed the problem the later phases attack; its variant comparisons are superseded by the clean-room run in Phase 2/3.*

557,280 per-file records from 18,576 files × 4 variants × 2 runs across four scenarios (SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash). The four variants differ only in byte-copy mechanics:

| Variant | Buffer | Write Mode | ArrayPool | Preallocate |
|---|---|---|---|---|
| `BaselineAuto` | 4 KiB (default) | Auto | default | no |
| `CopyToAsync512KiB` | 512 KiB | CopyToAsync | default | no |
| `ManualLoop512KiBArrayPool` | 512 KiB | ManualLoop | yes | no |
| `ManualLoop1MiBPreallocate` | 1 MiB | ManualLoop | yes | yes |

#### The small-file problem — and why byte-tuning can't fix it

The 0–64 KiB bucket contains 72.9% of files but only 1.7% of bytes, yet it dominates wall time on every destination except USB:

| Destination | 0–64 KiB % wall time | 256 MiB–2 GiB % wall time |
|---|---:|---:|
| SSDtoSSD | 56.4% | 7.4% |
| SameDriveTest | 51.5% | 10.9% |
| SSDtoHDD | 56.7% | 7.2% |
| SSDtoUSBFlash | 24.7% | 27.3% |

On SSD and HDD, 1.7% of bytes consumes ~57% of wall time. A 4× improvement on the 0–64 KiB bucket saves ~92 seconds on SSDtoSSD. A 20% improvement to large-file throughput saves ~3 seconds. The small-file bucket is simply where almost all the time is.

On USB flash, the split between smallest and largest files is roughly equal. Optimising tiny files alone cannot make USB fast — large-file streaming must also be addressed.

**The critical finding — variant spread.** For 0–64 KiB files, all four variants produce essentially identical throughput:

| Destination | Best variant | Worst variant | Spread |
|---|---:|---:|---:|
| SSDtoSSD | 2.19 MiB/s | 1.91 MiB/s | 13% |
| SSDtoHDD | 0.93 MiB/s | 0.89 MiB/s | 4% |
| SSDtoUSBFlash | 0.48 MiB/s | 0.43 MiB/s | 10% |

**~97% of small-file copy time is not spent on byte I/O.** The time is somewhere in the per-file overhead chain — `ExistsAsync`, `File.Exists`, `Directory.CreateDirectory`, stream open/close, staged temp file lifecycle, progress events, the sequential `await foreach` pump, and irreducible filesystem work (MFT updates, journalling) — but the variants do not isolate which part dominates. Tuning buffer sizes or write modes changes nothing; the later phases are designed to find out what does.

For files >4 MiB, `ManualLoop1MiBPreallocate` leads by 10–17% on SSD. On HDD the advantage is modest (~5%). On USB the signal is noisy (only 8 files in the 256 MiB–2 GiB bucket).

#### Per-scenario throughput (Phase 1 baseline)

**SSDtoSSD** (averaged across variants):

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0–64 KiB | 2.07 | 2m 3s | 56.4% |
| 64–512 KiB | 17.43 | 29s | 13.4% |
| 512 KiB–4 MiB | 84.42 | 24s | 11.0% |
| 4–32 MiB | 247.91 | 12s | 5.7% |
| 32–256 MiB | 310.24 | 14s | 6.2% |
| 256 MiB–2 GiB | 313.48 | 16s | 7.4% |

**SameDriveTest** (averaged across variants):

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0–64 KiB | 2.47 | 1m 43s | 51.5% |
| 64–512 KiB | 20.47 | 25.0s | 12.4% |
| 512 KiB–4 MiB | 93.11 | 22.0s | 10.9% |
| 4–32 MiB | 241.34 | 12.8s | 6.3% |
| 32–256 MiB | 263.85 | 15.9s | 7.9% |
| 256 MiB–2 GiB | 230.98 | 21.9s | 10.9% |

Small-file throughput (0–64 KiB ~2.47 MiB/s) is slightly better than SSDtoSSD (2.07), likely from cache locality. Large-file throughput (256 MiB–2 GiB ~231 MiB/s) is notably worse than SSDtoSSD (313 MiB/s), reflecting drive contention during sustained streaming. Do not treat SameDrive as equivalent to SSDtoSSD for strategies that affect large-file throughput.

**SSDtoHDD** (averaged across variants):

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0–64 KiB | 0.91 | 4m 41s | 56.7% |
| 64–512 KiB | 7.84 | 1m 5s | 13.2% |
| 512 KiB–4 MiB | 37.75 | 54s | 10.9% |
| 4–32 MiB | 105.52 | 29s | 5.9% |
| 32–256 MiB | 139.71 | 30s | 6.1% |
| 256 MiB–2 GiB | 141.03 | 36s | 7.2% |

**SSDtoUSBFlash** (averaged across all variants; essentially complete):

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0–64 KiB | 0.45 | 9m 25s | 24.7% |
| 64–512 KiB | 2.75 | 3m 5s | 8.1% |
| 512 KiB–4 MiB | 7.44 | 4m 35s | 12.0% |
| 4–32 MiB | 11.36 | 4m 31s | 11.9% |
| 32–256 MiB | 11.48 | 6m 5s | 16.0% |
| 256 MiB–2 GiB | 8.12 | 10m 24s | 27.3% |

**Acceptance criteria:**
- All existing unit tests pass unchanged.
- Directory cache does not cause failures when directories are deleted externally.
- Benchmark: run the standard scenario matrix after merging and compare against historical `BaselineAuto` runs to establish the Phase 1 baseline.

---

### Phase 2 — Tiny-File Direct Write

**Status:** **Complete.** Core integration implemented (`TinyFileFastPathThresholdBytes`, default disabled). Discovery benchmarks confirmed direct write delivers measurable improvement across all file sizes, with the strongest gain at tiny files. The clean-room run (2026-06-01, see Phase 3) provides the definitive per-bucket analysis. `ExistsCheckOff` variants retired — measured differences were smaller than run-to-run variance.

**Goal:** Skip the staged temp-file lifecycle (temp create → write → rename) for files small enough that the lifecycle dominates per-file time.

**What the benchmarks showed:** The staging lifecycle is proportionally most expensive when the byte payload is tiny. The clean-room run (Phase 3) found the direct-write advantage is +12% for Sub4KiB and +7% for Sub16KiB, falling to +4% at Sub64KiB and +2–3% for anything larger. There is a clear step change at 64 KiB, making that the natural threshold.

Direct write vs staged write, isolated effect (compare `DirectWriteBatch{N}` against `StagedWriteBatch{N}` at the same batch buffer size; from the 2026-06-01 clean-room run — full ranking in Phase 3):

| Buffer Size | Direct Batch (median) | Staged Batch (median) | Δ (direct advantage) |
|---|---:|---:|---:|
| 256 KiB | 59.49 sec | 62.41 sec | **+4.7%** |
| 512 KiB | 59.57 sec | 62.13 sec | **+4.1%** |
| 1 MiB | 59.41 sec | 61.49 sec | **+3.4%** |
| 4 MiB | 58.65 sec | 60.81 sec | **+3.6%** |

**Direct write consistently adds 3–5% over staged write** at every tested buffer size. The advantage is consistent and does not depend strongly on buffer size.

Bucket-level throughput comparison (`DirectWriteBatch4MiB` vs `StagedWriteBatch4MiB`, Mean MiB/s):

| Bucket | DirectWriteBatch4MiB | StagedWriteBatch4MiB | Δ |
|---|---:|---:|---:|
| Sub4KiB | 0.56 | 0.50 | +12% |
| Sub16KiB | 2.08 | 1.95 | +7% |
| Sub64KiB | 6.47 | 6.25 | +4% |
| Sub256KiB | 22.68 | 21.98 | +3% |
| Sub512KiB | 53.68 | 52.12 | +3% |
| Sub1MiB | 96.31 | 93.63 | +3% |
| Sub4MiB | 170.25 | 166.21 | +2% |

**The direct-write advantage is largest for the smallest files** (Sub4KiB: +12%, Sub16KiB: +7%) and tapers as file size increases (Sub4MiB: +2%). This is the opposite of the initial hypothesis that staging overhead matters most for large files — the temp-file create/rename lifecycle takes a proportionally larger share of elapsed time when the byte-copy component is tiny. This also means the integrity trade-off is most consequential where it matters least: very small files where the write is effectively atomic at the sector level anyway.

**Integrity trade-off:** Direct write without staging leaves a partial file on power loss or device unplug. **Mitigation:** on stream error (not unplug), delete the partial destination file. The trade-off is most acceptable below 64 KiB, where writes are more likely to be effectively atomic at the firmware level (single sector or block). Above 64 KiB, files span multiple sectors and the risk of a meaningful partial write increases — staged write remains the safer default there.

**Core integration:** `TinyFileFastPathThresholdBytes: long` in `OperationalSettings`; the struct field has no initializer (C# default **0**, so unset benchmark/test contexts do not accidentally enable the fast path). App default via `AppSettings.TinyFileFastPathKb = 64` passes **65536** (64 KiB) to every production job — the step-change threshold supported by the per-bucket evidence. Setting to 0 disables the fast path entirely (pre-Phase 2 behaviour). In `LocalFileSystemProvider.WriteAsync`, when `fileSize ≤ threshold`, write directly to the destination using `FileMode.Create`; skip staging.

**Acceptance criteria:** files bit-identical to staged baseline; no behaviour change when threshold is 0.

#### Execution checklist (complete)

**A — Tooling (code changes, no benchmarks yet)**

- [x] Implement `OrganizeByBucket` in `DatasetPreparationConfig` (`BenchmarkModels.cs`) and `DatasetPreparationService.cs`
- [x] Add `--config <file>` flag to `BenchmarkCliOptions.Parse()` (default: `benchmark-scenarios.json`)
- [x] Add optional `SourcePath` to `BenchmarkScenario` (falls back to global `config.SourcePath`)
- [x] Verify/fix analysis tool bucket parameterization — confirm it reads bucket boundaries from the scenario config rather than hardcoding MixedDataset names
- [x] Add Phase 2 variants to scenario config: `DirectWrite4KiB`, `DirectWrite16KiB`, `DirectWrite64KiB`, `DirectWrite256KiB`, `DirectWrite512KiB`, `DirectWrite1MiB`, and extended `DirectWrite4MiB`; plus `Control_BaselineAuto`
- [x] Implement direct-write copy engine in `BenchmarkCopyRunner.cs` — parameterised by threshold bytes, using `File.WriteAllBytesAsync` for files below threshold; existing staged path for files at or above
- [x] Create `benchmark-scenarios-phase2.json` — SmallFileDataset prep config (`OrganizeByBucket: true`), SmallFileDataset × SSDtoSSD discovery scenarios, MixedDataset validation scenarios (`Enabled: false` initially)
- [x] Retire `ExistsCheckOff` variants after benchmark differences proved smaller than variance

**B — Dataset generation**

- [x] Generate SmallFileDataset:
  ```
  dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json --mode dataset-prep
  ```
- [x] Check bucket fill report in output; re-run against an additional source directory if any bucket is underfilled

**C — Discovery benchmarks (SmallFileDataset × SSDtoSSD)**

- [x] Run Phase 2 variants — re-run until 2 successful runs per variant:
  ```
  dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json
  ```
- [x] Analyze:
  ```
  dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json --mode analyze
  ```
- [x] Apply gate (Phase 2): identify which thresholds show useful bucket-level improvement over `Control_BaselineAuto`; record top candidates

**D — MixedDataset validation (top candidates only)**

- [x] In `benchmark-scenarios-phase2.json`: enable MixedDataset-SSDtoSSD validation scenarios for the top-3 candidates; set `Enabled: false` on all others
- [x] Run:
  ```
  dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json --scenario MixedDataset-SSDtoSSD
  ```
- [x] Analyze; confirm improvement holds on real-world directory structure
- [x] If confirmed: extend to SameDriveTest, then SSDtoHDD

**E — Gate decision**

- [x] Update Phase 2 status
- [x] **Core integration complete:** `TinyFileFastPathThresholdBytes` on `OperationalSettings`; injected per-run via `PipelineJob.OperationalSettings` → `IStepContext` → `LocalFileSystemProvider.WriteAsync`. Default 0 (disabled).
- [x] Re-run analysis under the stricter §4.2 rules and record bucket-level recommendations without unsupported causal claims — completed as part of the Phase 2+3 consolidated clean-room run (see Phase 3)

---

### Phase 3 — Buffered Read-Write Batching

**Status:** Core integration complete; production-path policy validation pending (see the Production Validation Pass). The batching coordinator lives in `BatchedCopyStrategy` (with `BatchCopyBuffer`), selected by the policy when `BatchBufferBytes > 0`.

**Goal:** Replace the read-write interleave on every file with read-phase then write-phase batching, so many small files share each flush (phase separation).

**What batching does:** The current model interleaves read and write on every file, alternating I/O direction continuously. Batching accumulates multiple small files into a pool-allocated buffer during a read phase, then drains it during a write phase:

```
Current:  Read f₁ → Write f₁ → Read f₂ → Write f₂ → ...
Batched:  Read f₁ → Read f₂ → ... → Read fₙ  [buffer fills]
          Write f₁ → Write f₂ → ... → Write fₙ  [buffer drains]
```

#### Clean-Room Run (2026-06-01) — the Phase 2+3 consolidated evidence

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

#### Resulting design (as built)

The implementation follows from the conclusions above. A batching coordinator (`BatchedCopyStrategy` with `BatchCopyBuffer`) sits above the step layer, accumulating files from the enumerated tree into a pool-allocated buffer:

- **Batch eligibility ceiling: 512 KiB** (`OperationalSettings.BatchEligibilityCeilingBytes`, default on) — files above it bypass the batch path and route to ManualLoop. This is conclusion 4: keeping ≥2 files per flush is what makes phase separation happen, rather than a solo-file flush whose "gain" is just a large-buffer effect.
- **Default buffer size: 1 MiB.** One progress event per buffer flush regardless of file count. Flush frequency is `(tiny-file accumulation rate) × (buffer size)`, not the eligibility ceiling — a 1 MiB buffer flushes 4× more often than 4 MiB, finer wall-time progress at a 1.3% throughput cost (59.41 s vs 58.65 s). 4 MiB is available for max-throughput workloads via `BatchBufferBytes` (flows `PipelineJob.OperationalSettings` → `IStepContext.OperationalSettings` → `CopyStep` — no provider changes).
- Progress events per batch, not per file.
- **Intentional depth-first walk with per-directory ascending-size sorting** (`GroupBy(n => n.Parent).SelectMany(g => g.OrderBy(n => n.Size))`) — preserves directory cohesion and optimal buffer packing. Directory coherence (constraining a batch to one directory) improves resume semantics; implemented as a user option (`CoherenceMode: None | PerDirectory`), not a hard requirement.

As of 2026-06-14 this full design — measured by the `BenchmarkCopyRunner` harness — is propagated to production. Because the ceiling is already motivated on SSD by the clean-room run (conclusion 4), it does **not** need re-proving by the multi-week validation matrix; a cheap SmallFileDataset × SSDtoSSD A/B suffices if re-confirmation is ever wanted.

**Acceptance criteria:**
- MixedDataset × SSDtoSSD `executeDuration` improves beyond run-to-run variance with no correctness regressions.
- `copiedFiles + failedFiles + skippedFiles` equals expected total.
- File content bit-identical to staged baseline for the direct-write path.
- No behaviour change when batching is disabled (default off until policy validated on MixedDataset).

#### Execution checklist (complete)

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

### Phase 4 — Progress Throttling

**Status:** **Complete.**

**Goal:** Reduce per-file progress-reporting overhead during tiny-file bursts.

PipelineRunner emits per-file progress for every file. For 13,000+ tiny files copying in under 1 ms each, this is a meaningful overhead source and a source of UI thread pressure.

**What was implemented:** Progress reporting was extracted from `PipelineRunner` into a dedicated `ExecutionProgressReporter` class with a dual-gate throttle:

- **Completion events** (file-completion notifications): reported on first completion, last completion, or whenever 100 ms has elapsed or 100 files have accumulated — whichever triggers first. Controlled via `CompletionProgressIntervalMs` and `CompletionProgressBatchFiles` on `OperationalSettings`.
- **In-flight progress** (byte-level transfer updates): throttled to ~2 Hz (~500 ms intervals) using a `Stopwatch.Frequency / 2` gate. Files that complete in a single write bypass this entirely.

First and last completions are always reported unconditionally, so progress visibility is preserved at the start and end of every job.

**Files changed:** `SmartCopy.Core/Pipeline/ExecutionProgressReporter.cs` (new), `SmartCopy.Core/Pipeline/PipelineRunner.cs` (refactored), `SmartCopy.Core/FileSystem/OperationalSettings.cs` (two new settings).

**Acceptance criteria:** Progress UX remains clear and responsive during tiny-file bursts; no perceived regression in granularity for large files. Both throttle gates default to values that meet this criterion and are configurable via `OperationalSettings`.

---

### Phase 5 — Buffer Size Scaling *(independent track)*

**Status:** Cross-drive validation complete (2026-06-07, 8 scenarios, 519 runs); USB validation complete (2026-06-08, see Phase 6). The cross-drive suite confirmed: preallocation retracted universally; 1 MiB for SSD; 512 KiB for HDD and unknown. USB validation found 1 MiB is also the USB optimum, but for different reasons (non-monotonic response; 512KiB regresses on USB). The optional SameDriveSSD larger-buffer probe (Step 3 below) remains open.

**Goal:** Find the optimal buffer size for large-file streaming. The MixedDataset cannot isolate this — buffer changes are lost in the noise of 13,000 tiny files.

#### Final Phase 5 Findings — Buffer Sweep & Pre-allocation (2026-06-06)

Phase 5 was run against the `LargeFileDataset` using a fully converged sweep across `SSDtoSSD`, `SameDriveSSD`, `SSDtoHDD`, `SameDriveHDD`, and `HDDtoHDD`.

| Scenario | Strongest supported buffer | Pre-allocation effect | Notes |
|---|---|---|---|
| `SSDtoSSD` | 1 MiB – 2 MiB | Regresses (up to -4.1%) | Larger buffers (1-2 MiB) provide ~22% boost. Pre-allocation acts as a blocking tax and should be avoided. |
| `SameDriveSSD` | 1 MiB – 8 MiB | Flat / Noise | Similar to cross-drive SSD. 8 MiB shows a peak at the run level, but 1-2 MiB is safer. Pre-allocation does nothing. |
| `SSDtoHDD` | 512 KiB – 1 MiB | +11.6% | Buffer size alone does not improve throughput and actively regresses at 4-8 MiB. Pre-allocation is highly effective here, unlocking a ~12-20% throughput increase. |
| `SameDriveHDD` | 512 KiB – 1 MiB | Regresses (~ -1.5%) | Both buffer size scaling and pre-allocation regress throughput. Remain conservative. |
| `HDDtoHDD` | 512 KiB – 1 MiB | Flat / Noise | Buffer size and pre-allocation both fall within statistical noise (±1%). Throughput is mechanically constrained. |

**Provisional Conclusion (superseded by the cross-drive run below for the preallocation finding):**
We have weak evidence for a destination-sensitive routing policy (Phase 6):
1.  **Fast Destinations (SSD-to-SSD, SameDriveSSD):** Use `1 MiB` or `2 MiB` buffer. Pre-allocation **OFF**.
2.  **Slow Destinations (SSD-to-HDD):** Use `512 KiB` or `1 MiB` buffer. Pre-allocation **ON**.
3.  **Contended HDD (SameDriveHDD, HDDtoHDD):** Use `1 MiB` buffer. Pre-allocation **OFF**.

`1 MiB` is the safest cross-device large-file default candidate. It is close to the top of the pack on SSD, performs well on HDD, and avoids the major regressions seen with 4–8 MiB on HDD scenarios. `512 KiB` remains a defensible conservative fallback where destination type is unknown or progress reporting frequency needs to be more frequent.

**SameDrive caveat:** Same-drive SSD copies show a signal for larger buffers, even though mechanical seek overhead does not apply. The mechanism is unknown, and reliable SSD-vs-HDD classification is probably not guaranteed across platforms, so we would need more evidence to promote a larger SameDrive buffer.

> [!NOTE]
> **Revised 2026-06-07 — Cross-Drive Validation:** The SSD-to-HDD preallocation finding (+11.6%) has been retracted. Two independent cross-drive SSD-to-HDD drive pairs both show preallocation regresses (-2% to -4% at run level). The prior finding was specific to a single drive pair and does not generalise. The SSD-to-HDD default is revised to **512 KiB buffer, preallocation OFF**. All other conclusions above remain directionally valid.

#### Cross-Drive Validation (2026-06-07)

519 runs from 8 cross-drive scenarios on `LargeFileDataset`, covering all four drive-pair directions across three physical SSDs (D, O, R) and three HDDs (E, M, additional). 3 runs per variant per scenario, randomised order. Variants: `BaselineAuto256KiB`, `ManualLoop512KiBArrayPool/Preallocate`, `ManualLoop1MiBArrayPool/Preallocate`, `ManualLoop4MiBArrayPool/Preallocate`. This 8-scenario suite is the authoritative Phase 5 result — it provides better generalisation than the original same-machine run.

**Run-level summary matrix:**

| Scenario | 512 KiB | 512 KiB + Pre | 1 MiB | 1 MiB + Pre | 4 MiB | 4 MiB + Pre |
|---|---|---|---|---|---|---|
| SSD_R_to_SSD_D | PASS +10% | INCONCLUSIVE | PASS +14% | INCONCLUSIVE | PASS +19% | BELOW_THRESHOLD |
| SSD_D_to_SSD_R | BELOW_THRESHOLD | INCONCLUSIVE | BELOW_THRESHOLD | PASS +3% | PASS +5.5% | REGRESSION |
| SSD_D_to_SSD_O | BELOW_THRESHOLD | BELOW_THRESHOLD | PASS +3.7% | INCONCLUSIVE | BELOW_THRESHOLD | BELOW_THRESHOLD |
| SSD_O_to_HDD_E | PASS +3.2% | REGRESSION | BELOW_THRESHOLD | BELOW_THRESHOLD | PASS +3.3% | REGRESSION |
| SSD_R_to_HDD_E | INCONCLUSIVE | REGRESSION | REGRESSION | REGRESSION | INCONCLUSIVE | REGRESSION |
| HDD_E_to_SSD_O | REGRESSION | REGRESSION | REGRESSION | PASS†  | REGRESSION | REGRESSION |
| HDD_M_to_HDD_E | REGRESSION | BELOW_THRESHOLD | REGRESSION | INCONCLUSIVE | REGRESSION | BELOW_THRESHOLD |
| HDD_E_to_HDD_M | INCONCLUSIVE | REGRESSION | INCONCLUSIVE | REGRESSION | REGRESSION | REGRESSION |

† Suspicious: the non-prealloc 1 MiB variant regresses -6.1% in the same scenario. This PASS is likely variance, not a real effect.

**Key findings:**

1. **Preallocation is retracted universally.** The same-machine run identified +11.6% preallocation benefit for SSD-to-HDD from a single drive pair. Two independent cross-drive SSD-to-HDD scenarios both show preallocation regresses at run level (-2% to -4%). No other scenario shows a reliable positive preallocation effect. The SSD-to-HDD finding does not generalise; it was specific to one drive pair. Preallocation should default OFF for all destination types.

2. **"SSD" is not a uniform device class — buffer scaling is drive-pair specific.** SSD_R→SSD_D shows a strong monotonic curve (PASS at all three buffer sizes; peak +19% at 4 MiB). The same two drives reversed (D→R) show only +5.5% at 4 MiB. A third SSD pair (D→O) shows only +3.7% at 1 MiB. Prior phases used SSD_R→SSD_D as the primary SSD directional indicator; this drive pair represents a best-case NVMe scenario and over-estimates the general SSD opportunity. **1 MiB is the buffer size that passes in 2 of 3 SSD-to-SSD scenarios and is the safe universal SSD promotion.**

3. **Buffer scaling for SSD-to-HDD is small and inconsistent.** SSD_O→HDD_E shows +3.2% at 512 KiB and +3.3% at 4 MiB without preallocation. SSD_R→HDD_E shows nothing passes. The HDD write head is the bottleneck; application buffer size above 512 KiB does not reliably improve throughput. **512 KiB is the defensible SSD-to-HDD choice.**

4. **HDD read speed caps all HDD-source scenarios.** HDD_E→SSD_O, HDD_M→HDD_E, and HDD_E→HDD_M all show regression or inconclusive results across every tested variant. The disk's mechanical read throughput is the constraint regardless of application buffer size. The 256 KiB baseline is as effective as any larger variant; applying larger buffers actively regresses in some pairs.

5. **SSD_R_to_SSD_D over-represented the SSD opportunity in prior phases.** It is a real data point for high-throughput NVMe-to-NVMe pairs, but reversing the same two drives halves the observed gain (19% → 5.5%), and a third SSD pair shows only 3.7%. The strong curve seen in earlier phases was partially a property of that specific drive pair, not a general SSD characteristic.

**Revised provisional policy (SSD/HDD/Unknown — USB added in Phase 6):**

| Destination Profile | Buffer | Preallocation | Notes |
|---|---|---|---|
| SSD destination | 1 MiB | **OFF** | 4 MiB may benefit on fast NVMe pairs but cannot be classified from software |
| HDD destination | 512 KiB | **OFF** | 1 MiB provides no consistent benefit; regresses on some HDD pairs |
| Unknown / ambiguous | 512 KiB | **OFF** | Conservative; does not assume SSD behaviour |

**Preallocation is OFF universally.** No drive pair or scenario in this suite shows a reliable positive preallocation effect at run level. Do not re-enable preallocation without a controlled rerun that isolates the `SetLength` contribution for a specific destination type.

> [!NOTE]
> **USB validation:** USB has a distinct non-monotonic buffer-size response not predictable from SSD/HDD data; the cross-drive conclusions above do not apply to USB. See Phase 6.

#### Discovery steps (method)

**Step 1 — Buffer size sweep:**

Run `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, and `ManualLoop4MiB` — all with `ManualLoop` mode, `ArrayPool` enabled, and `PreallocateDestinationFile = false` — against the byte-volume-dominated dataset on SSDtoSSD and SSDtoHDD, per the standard protocol (§4.1), randomised order. Measure throughput at each buffer size for buckets 4 MiB+.

`ManualLoop128KiB` and `ManualLoop256KiB` are low-value rerun candidates: the initial run already showed 128 KiB regressing and 256 KiB failing to provide meaningful improvement over control. `ManualLoop8MiB` is a SameDriveSSD-only follow-up candidate because it regressed on HDD destinations.

Gate: buffer >1 MiB shows ≥5% P50 improvement over 1 MiB → promote as new large-file default.

Current provisional default candidate: **1 MiB**. It is close to the best observed SSD throughput, avoids HDD regressions, and is safer than larger buffers when destination media cannot be classified reliably. `512 KiB` remains the conservative fallback if the clean rerun shows 1 MiB is not consistently better beyond variance.

**Step 2 — Preallocation (settled — retracted):**

The intent was to isolate `PreallocateDestinationFile` from buffer size (the two were confounded in `ManualLoop1MiBPreallocate`). This question was answered by the cross-drive validation, whose `+ Pre` variants show **no reliable positive effect on any drive pair** — the one apparent +11.6% SSD→HDD gain came from a single drive pair and did not generalise. Preallocation is off by default universally.

**Step 3 — SameDriveSSD larger-buffer probe (open):**

Only after Step 1 confirms the safe cross-device default, run `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB`, and optionally `ManualLoop8MiB` on SameDrive SSD, randomised, to confirm whether the same-drive larger-buffer signal (an 8 MiB peak appeared in the same-machine sweep) is real and reproducible. Preallocation is settled off (Step 2), so there is no with/without arm — this is purely a buffer-size question. Treat the result as an optional specialised profile, not a general SameDrive default. If drive type cannot be classified confidently, fall back to the safe 512 KiB–1 MiB range. The cross-drive data shows 1 MiB is the safe universal SSD default; same-drive SSD requires dedicated measurement before any larger buffer default is promoted.

**Baseline reset:** `ManualLoop512KiBArrayPool` is the new benchmark control for Phase 5/6 buffer-sizing runs. The 4 KiB `CopyToAsync` control (`BaselineAuto` / `Control_BaselineAuto`) is excluded from those runs — definitively suboptimal, it would only flatten the comparison scale. However, `BaselineAuto` should be **retained in the MixedDataset policy validation run** as a legacy anchor: the before/after gain on realistic data is the headline result that justifies the whole programme of work, and it should be readable from a single run. `CopyToAsync512KiB` is retired — it provided no unique signal beyond the ManualLoop variants.

---

### Phase 6 — Destination-Sensitive Policies

**Status:** **Static routing implemented (2026-06-13); benchmark validation pending.** The copy engine was refactored into a policy+strategy system (`SmartCopy.Core/Pipeline/Strategy/`, see `Docs/Architecture.md` §2.4.1): `DefaultCopyStrategyPolicy` selects the copy buffer from the source→destination drive pair per the validated table (Phase 5 policy + the USB addition below), gated behind `OperationalSettings.DestinationRoutingEnabled` (enabled in the app under `AllowCopyOptimisations`; default-off elsewhere, so the refactor is behaviour-preserving). `CopyStep` and `MoveStep` now delegate byte transfer to the shared strategy, removing the duplicated copy engine. The remaining work is the whole-policy benchmark gate (the Production Validation Pass) before promoting defaults, plus the per-device learned profiles in the design note below. USB variance is still too high for a static USB profile to be trusted — treated as a prior pending per-device learning.

**Goal:** Apply appropriate strategy defaults per destination type, with a path toward per-device learned profiles for devices (like USB flash) where static classification is insufficient.

#### USB Validation (2026-06-08)

86 runs from 2 scenarios: `LargeFileDataset` (Phase 5, 4 variants) and `SmallestFileDataset` (Phase 6, 4 variants), sourced from O: (SSD) to T: (USB flash). 3 converged runs per variant at `convergenceSpreadPercent: 10` — significantly looser than the 5% used for cross-drive. Even at 10% the convergence threshold tripped repeatedly.

> [!CAUTION]
> The test drive (T:) is a very small USB flash drive and an extreme case. The wide variance (baseline spread: 4m 42s on a 47-minute run) means most bucket-level findings and the batching run-level results are INCONCLUSIVE regardless of the measured delta. The exception is ManualLoop1MiB on LFD, which shows an unusually tight spread (56s) alongside a large improvement (+29.5%). **The primary lesson from this data is not a USB copy strategy — it is that USB variance is too high for a static destination-type profile to be reliable.** See the per-device design note below for the direction this implies.

**LFD buffer sizing on USB:**

| Variant | Median | Spread | Delta vs Control | Verdict |
|---|---:|---:|---:|---|
| BaselineAuto256KiB | 47m 21s | 4m 42s | — | CONTROL |
| ManualLoop512KiBArrayPool | 50m 8s | 33.8s | -5.9% | REGRESSION |
| ManualLoop1MiBArrayPool | 33m 23s | 56.5s | **+29.5%** | **PASS** |
| ManualLoop4MiBArrayPool | 51m 38s | 5m 12s | -9.0% | INCONCLUSIVE |

**ManualLoop1MiB is the decisive USB winner.** The 29.5% improvement is the largest single-variant gain observed across all Phase 5 scenarios. Notably, it is also the *most stable* variant — 56s spread on a 33-minute run (<3% CV) versus the baseline's 4m 42s spread (~10% CV). Larger aligned writes appear to let the USB controller operate within its preferred transfer granularity.

The curve is strongly non-monotonic and USB-specific:

1. **ManualLoop512KiB regresses (-5.9%, REGRESSION).** This is the only destination type where 512KiB ManualLoop is slower than the 256KiB CopyToAsync baseline. USB controller I/O characteristics differ fundamentally from SSD/HDD; cross-drive findings do not transfer.
2. **ManualLoop4MiB shows the highest variance** (5m 12s spread, INCONCLUSIVE). This is the thermal throttling signature: large write chunks sustain the drive at full load, triggering intermittent pauses. Avoid 4 MiB on USB.
3. **1 MiB is uniquely effective** — it hits the USB controller's sweet spot for bulk transfer efficiency without triggering sustained thermal load.

**Batching on USB (SmallestFileDataset):**

| Variant | Median | Spread | Delta vs Control | Verdict |
|---|---:|---:|---:|---|
| BaselineAuto256KiB | 6m 46s | 34.0s | — | CONTROL |
| Batch256KiB | 7m 36s | 34.9s | -12.4% | REGRESSION |
| Batch1MiB | 6m 31s | 13.2s | +3.6% | INCONCLUSIVE |
| Batch2MiB | 6m 24s | 34.7s | +5.3% | INCONCLUSIVE |

The SSD Phase 3 batching gains (3–5.5%) do not replicate on USB. Batch1MiB and Batch2MiB are INCONCLUSIVE. Batch256KiB actively regresses.

Sub-bucket decomposition reveals a split:

- **Sub4KiB, Sub16KiB, Sub64KiB:** Small positive effects from 1MiB and 2MiB batch buffers (+9–14%), but all within the noise floor individually — inconclusive.
- **Sub256KiB:** All batch variants are slower than baseline (Batch256KiB: -23.2%, Batch1MiB: -9.4%, Batch2MiB: -7.6%). Files in this range exceed the 64KiB direct-write threshold, so both baseline and batch use staged writes — the write path is identical. The only thing batching adds for this size range is phase separation, and it is net negative on USB. The mechanism is unclear from this data; phase separation may simply provide no benefit when USB writes are slow enough that reads always complete far ahead of writes regardless.
- **Sub512KiB:** Batch256KiB wins (+16.8%, PASS), but this is a ManualLoop effect, not batching — Sub512KiB files (262–512KiB) exceed the 256KiB buffer ceiling and route to ManualLoop512KiB instead. Consistent with Phase 5 finding that larger sequential writes help on USB.

The Sub256KiB regression under batching is the key Phase 6 USB finding: on USB, batch-path overhead exceeds the phase-separation benefit for medium-small files (64–256KiB). This is the opposite of SSD/HDD behaviour (Phase 3 batching contribution).

**USB policy addition:**

| Destination Profile | Buffer | Batching | Notes |
|---|---|---|---|
| USB flash | **1 MiB** (prior) | **OFF** | The LFD 1 MiB result is the strongest signal in this dataset (tight spread, large delta). All other USB results are dominated by noise. Use 1 MiB as the starting prior; treat it as a default to be overridden by per-device learning rather than a settled finding. |

The Phase 5 provisional policy for SSD, HDD, and Unknown profiles is unchanged.

#### Static destination-type routing (initial implementation)

Detect SSD vs HDD vs USB where reliable platform APIs are available, but assume classification can fail or be ambiguous. Implement as a `DestinationProfile` enum and translate into strategy parameters (buffer size, staging threshold, progress throttle rate). Do not hardcode values — surface them as fields on `OperationalSettings`, which is injected per-run via `PipelineJob`. Unknown or ambiguous destinations must use the conservative profile.

Destination-specific notes (buffer defaults from the Phase 5 cross-drive policy; preallocation OFF universally):
- **Unknown / ambiguous:** 512 KiB buffer. Do not assume SSD behaviour.
- **SSD:** 1 MiB buffer. Cross-drive validation confirms 1 MiB passes in 2/3 SSD-to-SSD scenarios. 4 MiB may benefit on fast NVMe pairs but buffer response is drive-specific and cannot be classified from software alone.
- **HDD:** 512 KiB buffer. Larger buffers provide no consistent benefit and regress on some HDD pairs. Small-file strategies transfer directly; no concurrency until Phase 7.
- **USB / removable:** 1 MiB buffer as the starting prior (USB LFD PASS; also consistent with SSD/HDD evidence that 1 MiB is broadly safe). Batching inconclusive. USB variance is high enough that no static profile should be trusted — treat this as a temporary default pending per-device learning.
- **SameDrive:** do not collapse into SSDtoSSD. Small files behave similarly; large files are contention-limited. SameDriveSSD may benefit from larger buffers (4–8 MiB), but this is device- and cache-sensitive; treat as an optional specialised profile (Phase 5 Step 3) and fall back to 512 KiB–1 MiB when SSD/HDD classification is uncertain.

#### Design note — per-device adaptive profiles

USB flash (and potentially other removable or slow media) exposes a fundamental limitation of static destination-type routing: "USB" is an interface, not a device class. Two flash drives can perform completely differently. The wide USB variance is partly thermal, but also reflects genuine device-to-device spread that no classification heuristic can capture.

The better long-term architecture is a **per-device learned profile**, keyed by device identity and updated from observed copy performance:

- **Device identity:** VolumeID (volume serial number from `GetVolumeInformation` on Windows, UUID from mount metadata on Linux). Already accessible from `LocalFileSystemProvider`; `ProviderCapabilities` is the natural place to surface it. This identifies a specific formatted volume, not the physical drive — sufficient for USB sticks and external drives where the user sees a consistent drive letter/path.
- **Per-device profile store:** a small persistent JSON file (in `IAppDataStore`) mapping `{VolumeID → {bufferSize, batchEnabled, ...}}`. Indexed via `IAppContext`. Falls back to the static destination-type profile for unknown devices.
- **In-vivo measurement:** during normal copies, record aggregate throughput per size bucket to the device's profile. After enough observations accumulate, the profile overrides the static default. No user action needed.
- **Explicit calibration:** a "Calibrate this drive" action (in the copy dialog or device context menu) that runs a brief A/B test — a few dozen small files, a few large files, enough to distinguish strategy variants in under a minute. Faster to converge than passive learning and available to power users who want accurate profiles immediately.
- **Strategy experimentation:** for unknown or low-confidence devices, occasionally shadow a copy with an alternative strategy on a small sample of files and compare throughput. Converges toward the best strategy for the specific device. Resembles a multi-armed bandit policy, not a fixed schedule.

This framing reorients Phase 6: static destination-type routing is the first step and needed regardless, but the destination profile should be treated as a *prior* that in-vivo measurement can override, not as a permanent classification. The infrastructure required (VolumeID in `ProviderCapabilities`, profile store, measurement hooks) is a modest addition to the copy path and the data model.

Files to modify: `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`, `ProviderCapabilities`, `IAppDataStore` (new profile store)

**Benchmark gate:** run the full scenario matrix (SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash) via the Production Validation Pass. Promote static defaults only after all scenarios pass. Per-device learning is a separate incremental feature that can ship after static routing is in place.

---

### Phase 7 — Parallelism

**Status:** Kept in reserve in case earlier phases yield insufficient performance gains.

**Goal:** Add concurrency to the copy path. This is the most complex option — it complicates cancellation semantics, progress reporting, and error handling in ways that no sequential strategy does. It is listed last because it should be tried last.

Parallel copies introduce risks like HDD head thrashing and bus contention that could make performance unpredictable and very sensitive to the specifics of source, destination and file size. Some devices such as USB flash can be **permanently** damaged by excessive concurrency.

There are two possible forms of parallelism to test:
- **inter-phase parallelism** - Producer/Consumer queue where read buffers are filled then drained concurrently
- **intra-phase parallelism** - N concurrent reads then N concurrent writes, no simultaneous read/write phase

Both need to be tested with same-drive and different-drive source & target pairs.

**Benchmark gate:**
- Run per the standard protocol (§4.1), randomised order. Parallelism variants are likely to exhibit higher variance — expect the 3rd-run threshold to trip more often.
- Must compare against the best strategies from earlier phases.
- Must not regress on SSDtoHDD.

---

## 7. Production Validation Pass

Gate 1 ran 2026-06-21. The first (non-idle) session read PASS, but idle re-runs did not reproduce a clean equivalence verdict and the SmallFileDataset × SSDtoSSD pair does not converge to the 3% window even at 10 runs (see the Gate 1 result below). Gate 1's intent — validating that the production wrapper executes the bundle without gross regression — is carried as **provisionally met**; the live work moves to a **different source/target drive pair** and onward to the Gate 2 MixedDataset × drive matrix, where whether the policy *generalises* (and delivers value) is the actual question. A clean-environment re-run (2026-06-22, Windows Defender excluded) then reframed the baseline — AV scanning dominated prior copy-time measurements — and reopened the production-vs-prototype equivalence question; see **Result (2026-06-22)** below.

The production copy path (`PipelineRunner → CopyStep → DefaultCopyStrategyPolicy → BatchedCopyStrategy`) had **never been measured with batching/routing enabled**: under `--mode benchmark` the harness diverts any variant carrying batch/direct-write settings to the `BenchmarkCopyRunner` prototype. The new **`--mode validation`** always drives the production runner, mapping each variant's batch/eligibility/direct-write fields onto `OperationalSettings` — so `Production_Routed` measures production where benchmark mode would measure the prototype. (`Legacy_Baseline` carries no batch settings, so it runs the production streaming path under either mode; `Production_Routed` is the variant that requires validation mode.) This pass validates that production *matches* the prototype (parity) and that the candidate bundle beats the legacy default (value). A PASS is what justifies *promoting* the bundle — flipping `AllowCopyOptimisations` from its off-by-default state; the bundle is unpromoted by design until this pass clears it. The MixedDataset matrix is a multi-week run, so the ladder fails fast: cheap signals before drive-weeks.

**Metric.** Median `executeDuration` per scenario/variant; variance = the `ConvergenceSpreadPercent` window (`BenchmarkConvergence`); verdicts PASS / INCONCLUSIVE / REGRESSION / INVALID (and `BELOW_THRESHOLD` where the gate clears the noise floor) per §4.2.3.

**Candidate bundle (under validation).** The settings promotion would enable — encoded in code today as off-by-default candidates behind `AllowCopyOptimisations` (default `false`). The bundle was never promoted *by design*; this pass is the gate that justifies it. These are the shipped defaults, not the 4 MiB discovery champion from the Phase 3 clean-room run (which production did not adopt).

| Knob | Value | Source of record |
|---|---|---|
| Copy buffer | SSD/USB 1 MiB, HDD/Unknown 512 KiB | `DefaultCopyStrategyPolicy.SelectBufferBytes` |
| Batch buffer | 1 MiB | `AppSettings.BatchBufferKb` |
| Batch eligibility ceiling | 512 KiB | `OperationalSettings` default |
| Direct-write threshold | 64 KiB | `AppSettings.TinyFileFastPathKb` |
| Preallocation | OFF | `DefaultCopyStrategyPolicy` |

`AllowCopyOptimisations` gates routing + batch + direct-write as one unit, so the pass validates the bundle **as shipped** — there is no production mode that enables routing alone. `Production_Routed` is this bundle reached via routing; `Prototype` and `Production_Fixed` pin the same values for the Gate 1 equivalence check.

Batching is validated on SSD (Phase 3) and shown to regress on USB (Phase 6), but **never measured on HDD** — the cross-drive suite (Phase 5) swept buffer and preallocation only, not batching. The candidate bundle assumes batching-on for HDD; Gate 2's SSDtoHDD pair is where that assumption is earned rather than presumed.

**Implementation — new code required.** None of the gates can run until this is built; it is the bulk of the work.

- **Mode + executor seam.** Add `BenchmarkRunMode.Validation`; parse `validation`/`validate` in `BenchmarkCliOptions.ParseMode`; route it in `Program.cs`. Extract the copy invocation in `BenchmarkTask.RunCopyAsync` (today's `if (directWriteThresholdBytes > 0 || bufferBatchBytes > 0)` branch) behind an `ICopyExecutor`: `PrototypeCopyExecutor` (that branch verbatim — `BenchmarkCopyRunner` when batch/direct set, else `PipelineRunner`; used by `--mode benchmark`) and `ProductionCopyExecutor` (always `PipelineRunner.ExecuteAsync`; used by `--mode validation`). `BenchmarkTask` selects the executor from the mode.
- **Settings mapping** (the crux — today nothing maps the new fields). Add `DestinationRoutingEnabled` to `BenchmarkVariant` (`Production_Routed` sets it; `Production_Fixed` leaves it off); `BufferBatchBytes` / `BatchEligibilityThresholdBytes` / `DirectWriteThresholdBytes` / `MatchedControl` already exist. Add `BenchmarkVariant.CreateProductionOperationalSettings`: `BufferBatchBytes → BatchBufferBytes`, `BatchEligibilityThresholdBytes → BatchEligibilityCeilingBytes`, `DirectWriteThresholdBytes → TinyFileFastPathThresholdBytes`, set `DestinationRoutingEnabled`, job `CopyStrategyPolicy = DefaultCopyStrategyPolicy.Instance`. The existing `CreateOperationalSettings` (legacy provider fields only) is left for the prototype path. `ProductionCopyExecutor` logs the resolved `OperationalSettings` (+ the policy's resolved buffer) at startup — the run-1 sanity check.
- **Configs + data** (authored, not code). `validation-smoke.json` (Gate 1) and `validation-matrix.json` (Gate 2). `Prototype` / `Production_Fixed` carry the candidate-bundle values (table above) as per-scenario overrides — buffer pinned to the pair, batch 1 MiB, ceiling 512 KiB, direct-write 64 KiB; the shipped `AppSettings`/policy defaults, not the discovery champion. Equivalence pairing reuses `MatchedControl` (`Production_Fixed` → `Prototype`).
- **Reused unchanged:** convergence, analysis/report, journals, dataset-prep, cooldown/cold-cache, the §4.2.3 verdict machinery, the `BenchmarkSizeScalingAnalysis` invariants, and the existing unit suite.

#### Gate 1 — Parity smoke · ~45 min

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
- **The bucket-throughput contradiction is a measurement artefact, not a regression.** Production reads <½ of legacy/prototype Sub256KiB MiB/s despite lower per-file medians because aggregate bucket throughput is invalid for batched variants (§4.2.2). It is excluded from the verdict.
- **Parity is not contradicted by any stable signal.** The policy resolves once per job, not per file, so a structural ~8 s production penalty over 134k files is implausible; the REGRESSION reading is one idle session inside the swing band. Gate 1's parity intent is carried as *provisionally met* rather than proven.

**Decision (updated 2026-06-21).** D→O was run next: equivalence was clean there (Production_Fixed ≈ Prototype, +0.8%, 2.4 s spread), confirming the D→R instability was **pair-specific**. But the `% Copy Time` metric showed this dataset is ~90% sub-64 KiB wall time, so the >512 KiB buffer/routing path — the whole point of the bundle on larger files — went unmeasured. So the smoke moves to a **large-file-weighted `SmokeDataset`** (≤512 KiB ~46% of time; 512 KiB–32 MiB the rest) on **D→O**, re-run fresh with the equal-split per-file attribution and `ExistsAsync`-inclusive batched timing now in place. Gate 1 still validates the *implementation* (parity + no regression); whether the policy *generalises* is the Gate 2 matrix's job, decided on run-level wall-clock (§4.2.2).

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
- **`FlushAsync` (`StreamCopyEngine.CopyAsync`)** — flushes the *managed* buffer to the OS after each file, before stream close and the rename. It is **not** `Flush(flushToDisk: true)` (no `FlushFileBuffers`), so it is functionally the same flush `FileStream.DisposeAsync` already performs on close; the prototype gets the identical effect once, on dispose. One extra near-empty async call per file — not a 445 ms source.

**Open questions:**
1. **Where does the ~445 ms actually go?** Remaining candidates are production-only work in the large-file path — batch flush/packing boundaries (the prototype flushes on a folder boundary; `BatchedCopyStrategy` packs depth-first and is not directory-confined), the per-chunk progress path (`CopyWithManualLoopAsync` / `ProgressReportingReadStream`) vs the prototype's bare `CopyToAsync`, and per-file `TransformResult` yielding. None is confirmed; isolating the split needs a profiler or toggle-and-remeasure, not more reasoning over the NDJSON.
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

#### Gate 2 — Full matrix, fail-fast ordered · hours → weeks

- **Run:** `--mode validation --config validation-matrix.json`. **Resumes across cold boots** — `BenchmarkPass` schedules only unconverged scenario/variant pairs, so a restart continues where it left off.
- **Scenarios:** `MixedDataset` × drive pairs **in this order** (cheapest / most-likely-to-fail first, so SSDtoSSD ~hours gates the multi-week HDD/USB pairs): `SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash`.
- **Variants** — two, no prototype runner: `Production_Routed` and `Legacy_Baseline` (settings as Gate 1).
- **Convergence:** `GatePercent` 3%, `DesiredRunCount` 5, `MaxConvergenceRuns` 5. USB/HDD spread is 3–9% (Phase 6 USB), so noisy pairs will not reach 3% → `GaveUp` (analysis uses the tightest window), and an effect smaller than a pair's spread reads INCONCLUSIVE per §4.2.1. Do **not** loosen the threshold — the verdict already compares the delta to the actual measured spread.
- **Per-pair outcome:** `PASS` is the goal on pairs whose noise floor sits below the gate (SSD); `BELOW_THRESHOLD` and `INCONCLUSIVE` continue the matrix (routed is no worse than legacy). On noisy pairs (HDD/USB) the floor exceeds the gate, so a genuine win reads `PASS` and anything smaller reads `INCONCLUSIVE` — `BELOW_THRESHOLD` cannot arise there.
- **Abort** on the first pair returning `REGRESSION` or `INVALID` — do not finish the set then report. `BELOW_THRESHOLD` is a (sub-gate) improvement, not a failure, and never aborts.
