# SmartCopy2026 - Optimisation Strategies

This document captures practical optimisation strategies for copy performance.

The evidence so far points to a fundamental tension: **large files need staged writes and granular progress for integrity and UX; small files need minimal per-file ceremony to avoid drowning in overhead.** There is unlikely to be a universally best strategy. The approach must be *adaptive* — selecting the right code path per file based on what we already know at pipeline execution time (file size, count, destination type).

The intentionally diverse MixedDataset surfaces both ends of this spectrum: 73% of files are under 64 KiB (and consume 57% of wall-clock time despite being 1.8% of bytes), while 29% of bytes live in files >256 MiB (where streaming throughput and buffer tuning dominate).

## 1. Current Findings (as of 2026-05-03)

From 4 scenarios × 4 variants of SSD-to-SSD and SameDrive benchmarks (297,216 per-file records, 37,116 files per run across 6 size buckets):

### 1.1 Throughput and Wall-Clock Contribution by Size Bucket (SSDtoSSD)

Using the target byte budgets per bucket from the MixedDataset prep config and the observed throughput (averaged across all 4 variants):

| Size Bucket | Files | % Files | Bytes (MiB) | % Bytes | Avg MiB/s | Est. Wall Time | % Wall Time |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0-64 KiB | 27,036 | 72.9% | 256 | 1.8% | 2.1 | 122 sec | **56.8%** |
| 64-512 KiB | 5,716 | 15.4% | 512 | 3.6% | 17.4 | 29 sec | 13.7% |
| 512 KiB-4 MiB | 3,550 | 9.6% | 2,048 | 14.5% | 84.4 | 24 sec | 11.3% |
| 4-32 MiB | 690 | 1.9% | 3,072 | 21.8% | 248 | 12 sec | 5.8% |
| 32-256 MiB | 108 | 0.3% | 4,096 | 29.1% | 310 | 13 sec | 6.2% |
| 256 MiB-2 GiB | 16 | <0.1% | 4,096 | 29.1% | 313 | 13 sec | 6.1% |
| **Total** | **37,116** | **100%** | **14,080** | **100%** | — | **~214 sec** | **100%** |

Key takeaway: **1.8% of the bytes consume 57% of the wall-clock time.** The 0-64 KiB bucket has throughput ~150x worse than the largest files (2.1 vs 313 MiB/s). The 73% file-count headline masks the degrees of freedom:

- **Optimising 0-64 KiB throughput by 4x saves ~92 seconds** (57% of runtime → ~14%).
- **Optimising 256 MiB-2 GiB throughput by even 20% saves ~2.6 seconds** (6% → 5%).
- A 10% improvement to large-file streaming is worth ~2.6 seconds. A 10% improvement to tiny-file overhead is worth ~11 seconds. Both matter, but the ROI-per-engineering-hour is dramatically higher on the tiny-file path — *for this dataset*.

This breakdown also exposes a blind spot: the current benchmarks measure per-bucket throughput but do not report per-bucket aggregate wall-clock time or %-of-bytes. These columns currently require cross-referencing the scenario config and manual calculation. The benchmark tooling should emit them directly (see Section 7).

### 1.2 Per-Bucket Throughput Detail (SSDtoSSD, averaged across variants)

| Size Bucket | Avg MiB/s | P50 MiB/s | P95 MiB/s |
|---|--:|---:|---:|
| 0-64 KiB | ~2.1 | ~1.4 | ~6.2 |
| 64-512 KiB | ~17.4 | ~14.5 | ~38.7 |
| 512 KiB-4 MiB | ~84.4 | ~74.0 | ~173 |
| 4-32 MiB | ~248 | ~247 | ~314 |
| 32-256 MiB | ~310 | ~326 | ~352 |
| 256 MiB-2 GiB | ~313 | ~339 | ~358 |

Throughput scales with file size; only the 0-64 KiB bucket is severely constrained (~2 MiB/s). The 64-512 KiB bucket is also mediocre at ~17 MiB/s.

### 1.3 Byte-Copy Variant Comparison — Key Insight

The four tested variants differ only in I/O mechanics (buffer size, write mode, array pooling, preallocation):

| Variant | Buffer | WriteMode | ArrayPool | Preallocate |
|---|---|---|---|---|
| BaselineAuto | default (4 KiB) | Auto | default | default |
| CopyToAsync512KiB | 512 KiB | CopyToAsync | default | default |
| ManualLoop512KiBArrayPool | 512 KiB | ManualLoop | true | default |
| ManualLoop1MiBPreallocate | 1 MiB | ManualLoop | true | true |

**For 0-64 KiB files (72.9% of all files), the spread between best and worst variant is ~0.3 MiB/s — statistically negligible.** All four produce essentially identical results in the bucket that dominates runtime. For 64-512 KiB files, the spread is ~1.4 MiB/s. Only for files >4 MiB do we see meaningful divergence (ManualLoop1MiBPreallocate leads by ~10-15%).

**Conclusion:** ~97% of copy execution time is NOT spent on byte-level I/O. The bottleneck is per-file overhead — metadata calls, stream setup/teardown, staged temp file lifecycle, progress reporting, and the sequential `await foreach` pumping loop. Tuning buffer sizes or write modes without addressing per-file overhead is rearranging deck chairs.

### 1.4 SameDrive vs Cross-Drive Anomaly

SameDrive small-file throughput (0-64 KiB) is **~20% higher** than SSDtoSSD (2.38-2.55 MiB/s vs 1.91-2.19 MiB/s across variants). This is counterintuitive — writing to the same physical drive should be slower due to head contention. Possible explanations:
- NTFS journal write-ordering overhead crossing volume boundaries
- Controller-level queue serialization when spanning physical drives
- Worth profiling with ETW/perf counters before drawing conclusions

### 1.5 SSDtoHDD and SSDtoUSBFlash — Data Gap

Only 2 of 8 planned HDD variant runs and **zero** USB Flash runs have been completed. These scenarios are critical for calibrating destination-sensitive policies (Section 3.5) and ensuring Phase C doesn't regress on slower media. **Priority: complete the HDD and USB baseline before implementing parallelism.**

### 1.6 Workload Diversity and the Case for Adaptive Strategy

The MixedDataset is intentionally diverse — it spans six size buckets from 0 bytes to 2 GiB, designed to expose which bottlenecks dominate for which kinds of files. The wall-clock breakdown in Section 1.1 makes the case concretely: tiny files need overhead reduction; large files need throughput. Both demands coexist in any sufficiently diverse copy job, and we know every file's size before the first byte is copied.

This implies the core architecture should route each file through the appropriate code path dynamically:

| File Size | Code Path | Reasoning |
|---|---|---|
| ≤64 KiB | Parallel batch, direct write (no staging), pooled buffer, minimal progress events | Per-file overhead dwarfs byte copy time; integrity risk from direct write is acceptable given trivially small content |
| 64 KiB-4 MiB | Bounded parallel, staged write, throttled progress | Balance of overhead and integrity; staging cost is amortized over moderate byte volume |
| >4 MiB | Sequential single-stream, staged write, full progress reporting, large buffer | Streaming throughput matters; staged write ensures atomicity; user needs per-file progress for long copies |

This isn't two separate user profiles — it's three code paths within a single `CopyStep.ApplyAsync` that the pipeline runner selects per-file based on the `node.Size` it already has from enumeration. The same technique extends to destination type (SSD vs HDD can tune parallelism caps) and overwrite mode (Skip mode can eliminate the Exists pre-check, Overwrite can use OpenOrCreate).

## 2. Optimisation Goals

- Reduce wall-clock time for copy jobs across the full spectrum of workload profiles.
- Avoid regressing one profile to benefit another (e.g. large-file throughput must not degrade when adding small-file parallelism).
- Maintain stability and correctness (no unhandled exceptions, no partial-state regressions).
- Keep progress UX responsive without introducing high reporting overhead.
- Preserve cross-provider behavior and graceful degradation semantics.

## 3. Candidate Strategies

### 3.1 Bounded Parallel Copy for Small Files (HIGHEST IMPACT)

Description:
- Copy small files concurrently using a bounded worker pool within CopyStep.ApplyAsync.
- Keep large files single-stream (or low concurrency) to avoid contention.
- This is the single biggest lever available — sequential `await foreach` leaves the SSD idle while per-file overhead accumulates.

Current per-file overhead chain (every file, sequential):
1. `ExistsAsync` on destination (metadata I/O)
2. `OpenReadAsync` → `File.Exists` + `FileStream` open (metadata + handle)
3. `WriteAsync` → `Directory.CreateDirectory` (metadata), staged temp file create, byte copy, atomic rename
4. PipelineRunner `await foreach` + per-result progress + ETR calculation

Parallelism amortizes the synchronous portions of this chain across concurrent files while one file's I/O overlaps another's setup.

Suggested starting configuration:
- `smallFileParallelThresholdBytes = 256 KiB`
- `smallFileMaxConcurrency = 4` (then test 6 and 8)

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs` — partition into parallel/sequential sets
- `SmartCopy.Core/FileSystem/LocalFileSystemProviderOptions.cs` — new knobs
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — new variant fields

Expected effect:
- 2-4x improvement on 0-64 KiB bucket on SSD targets.
- Moderate improvement on 64-512 KiB bucket.

Risks:
- Over-parallelization can hurt HDD/USB targets via seek thrash or queue contention.
- Progress reporting and cancellation semantics become more complex (multiple in-flight files).
- Staged temp file naming must avoid collisions under concurrency.

### 3.2 Reduce Per-File Metadata Round-Trips (HIGH IMPACT)

Description:
- **Eliminate or combine the double `Exists` check:** CopyStep.ApplyAsync calls `targetProvider.ExistsAsync(destination)` and then `OpenReadAsync` calls `File.Exists` on the source. Both are per-file metadata I/O.
  - For `OverwriteMode.Overwrite`, the destination Exists check is unnecessary — just open for write (which will succeed whether the file exists or not).
  - For `OverwriteMode.Skip`, the check could be combined with the write attempt (try-open, skip on conflict) or done lazily.
- **Cache `Directory.CreateDirectory`:** WriteAsync calls `Directory.CreateDirectory` for every file's parent path. The same parent directory is often reused across thousands of consecutive files. A simple cache of recently-created directory paths eliminates redundant filesystem calls.
- Consider an `OpenOrCreate` fast path for `OverwriteMode.Overwrite` that opens the destination stream without an explicit pre-check.

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs` — reduce/eliminate ExistsAsync call
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — skip File.Exists in OpenReadAsync for known-good paths; cache directory creation; add OpenOrCreate path

Expected effect:
- 20-40% improvement on 0-64 KiB bucket (each saved metadata call eliminates a disk seek/ntfs lookup).
- Cumulative benefit across all file sizes but most pronounced on small files where metadata cost dominates total time.

Risks:
- Needs careful handling of overwrite modes and error semantics.
- Cached directory state must account for external directory deletion.

### 3.3 Tiny-File Write Fast Path (MODERATE IMPACT)

Description:
- For very small files (<=64 KiB), bypass the staged temp file + atomic rename cycle. Write directly to the target path. This eliminates the temp file create, flush, and rename I/O operations per tiny file.
- Alternatively, buffer multiple tiny file writes and commit them in batches.
- Note: this introduces a risk of partial writes on failure — acceptable for tiny files if clearly documented, or gated behind an opt-in flag.

Previous version of this strategy focused on buffer tuning. The variant benchmarks proved buffer size is irrelevant for tiny files. The real target is the temp file lifecycle overhead.

Expected effect:
- Moderate improvement in the 0-64 KiB bucket by eliminating ~3 filesystem calls per tiny file (temp create, flush, rename).

Risks:
- Without staging, an interrupted write leaves a partial file at the destination path (rather than no file at all).
- Needs gating or clear documentation of the trade-off.

### 3.4 Progress Reporting Cost Control (MODERATE IMPACT)

Description:
- PipelineRunner computes per-file ExecutionDuration and emits per-result progress for every file. For 27,000+ tiny files copying in <1ms each, this per-file overhead is significant.
- Throttle progress emission: batch updates per N files or per T milliseconds when files are very small.
- The `IFileTransferProgressSink` already throttles byte-level progress to ~10 Hz; extend similar throttling to file-completion progress.

Expected effect:
- Small to moderate throughput improvement and lower UI thread pressure during tiny-file bursts.

Risks:
- If over-throttled, user perception of responsiveness can degrade.

### 3.6 Adaptive Strategy Selection per File (ARCHITECTURAL)

Description:
- We know every file's size from tree enumeration before `CopyStep.ApplyAsync` begins.
- Rather than a single code path, route each file through the best strategy dynamically:
  - Tiny (≤64 KiB): skip staging, batch in parallel, minimal progress events
  - Medium (64 KiB-4 MiB): bounded parallel, staged writes, throttled progress
  - Large (>4 MiB): sequential single-stream, staged writes, large buffer, full progress
- Extend selection to include destination type (SSD → higher parallelism cap; HDD → cap at 2; network → cap at 1) and overwrite mode (Skip → eliminate Exists pre-check; Overwrite → OpenOrCreate path).
- This makes the previously separate "strategies" (parallelism, fast path, metadata reduction, progress throttling, destination sensitivity) into composable behaviors selected by a routing function at the top of ApplyAsync.

This is not a new Phase — it's the architecture that Phases A, B, and C converge on. The acceptance criteria for each Phase should include "works as part of the adaptive routing switch."

Expected effect:
- Eliminates the risk of over-optimising for one workload profile at the expense of another.
- Makes the strategy selection transparent for benchmarking — each variant tests a specific routing policy rather than a monolithic mode.

Risks:
- Routing function must itself be cheap (no I/O, no allocations in the hot path) — it operates on already-known data (node.Size, provider capabilities).

### 3.5 Destination-Sensitive Strategy Selection

Description:
- Use provider capabilities and destination profile (SSD/HDD/USB/network) to choose safe defaults.
- Example: higher small-file concurrency on SSD, conservative settings on HDD/USB.
- Implement as capability flags or a runtime-detected `DestinationProfile` enum.

Expected effect:
- Improves generalization and prevents regressions on slower media.

Risks:
- Requires robust heuristics; wrong classification can reduce performance.
- **Currently blocked by lack of HDD/USB benchmark data.** Cannot calibrate thresholds without baseline measurements on those media.

## 4. Revised Rollout Order

The goal is an adaptive `CopyStep.ApplyAsync` that selects the right code path per file based on known size. Each Phase builds a piece of that routing table (Section 3.6). The phases are ordered by ROI-per-engineering-hour:

1. **Phase A:** Bounded small-file parallelism — the largest single lever for the file-count-dominated half of any copy job. Start with a simple size threshold: files ≤256 KiB go parallel.
2. **Phase B1:** Eliminate redundant per-file metadata round-trips — `ExistsAsync` pre-check, `File.Exists` in `OpenReadAsync`, per-file `Directory.CreateDirectory`. Benefits all file sizes.
3. **Phase B2:** Tiny-file write fast path — bypass staging for files ≤64 KiB. Pairs with parallelism from Phase A.
4. **Phase C:** Progress throttling — batch completion events during tiny-file bursts.
5. **Phase D:** Destination-sensitive policy — cap parallelism and buffer strategy based on SSD/HDD/USB/network detection. Requires HDD/USB baselines (Section 5.2).

**Buffer scaling (formerly Track 2) is independent of the above sequence** and can proceed in parallel:
- **Phase L:** Test 512 KiB → 1 MiB → 2 MiB → 4 MiB → 8 MiB buffers against large-file-only datasets (Section 5.3) to find the optimal value for files >4 MiB. Isolate preallocation as a separate variable. Feed the result back into the adaptive routing table's large-file path.

The outcome: a single `CopyStep.ApplyAsync` where the per-file routing is a pure function of `(node.Size, providerCapabilities, overwriteMode)` with no I/O in the hot path.

### Phase D (New) — Staged Temp File Optimization

Description:
- For SSDs, consider using `FILE_FLAG_DELETE_ON_CLOSE` or equivalent semantics so that temp file creation + rename steps are reduced to a single operation. Or use hardlink-based staging to avoid data copy entirely for same-volume copies.
- Profile whether the temp file lifecycle (create, write, flush, close, rename) is a measurable portion of small-file latency versus just the metadata calls.

Priority: Investigate after Phase B if profiling shows temp file overhead is significant.

## 5. Updated Variant Strategy

### 5.1 Variant Matrix — Adaptive Policy Variants + Buffer Scaling

The goal is to benchmark end-to-end routing policies (not individual knobs in isolation). Each variant below exercises a specific point in the adaptive routing table:

**Routing policy variants (Phases A-C):**
| Variant | Tiny (≤64 KiB) | Medium (64K-4M) | Large (>4M) | Purpose |
|---|---|---|---|---|
| `BaselineAuto` | Sequential, staged, auto write mode, 4 KiB buffer | Same | Same | Control — current heuristic defaults, no adaptation |
| `AdaptiveX2` | Parallel x2, direct write, no Exists pre-check | Staged, sequential | Staged, sequential, 1 MiB buffer | First adaptive step — minimal change, tests parallelism on tiny files only |
| `AdaptiveX4` | Parallel x4, direct write, no Exists pre-check | Parallel x2, staged | Sequential, staged, 1 MiB buffer | Extends parallelism to medium files |
| `AdaptiveX8` | Parallel x8, direct write, no Exists pre-check, batched progress | Parallel x4, staged, throttled progress | Sequential, staged, 1 MiB buffer | Most aggressive — tests parallelism ceiling |
| `AdaptiveFull` | Parallel x4, direct write, no Exists, directory cache, batched progress | Parallel x2, staged, no Exists, directory cache | Sequential, staged, 1 MiB buffer, full progress | All optimisations enabled — acceptance target |

**Buffer scaling variants (Phase L, independent of routing):**
| Variant | Buffer | Purpose |
|---|---|---|
| `ManualLoop512KiB` | 512 KiB | Baseline for large-file streaming |
| `ManualLoop1MiB` | 1 MiB | Current default |
| `ManualLoop2MiB` | 2 MiB | Scale step |
| `ManualLoop4MiB` | 4 MiB | Scale step |
| `ManualLoop8MiB` | 8 MiB | Scale ceiling |
| `ManualLoopXMiBPrealloc` | Best size | Same as best buffer + preallocation enabled — isolates preallocation contribution |

Retire `CopyToAsync512KiB` and `ManualLoop512KiBArrayPool` — they provide no unique signal beyond the above variants.

### 5.2 Benchmark Data Gap — HDD and USB

**Action required:** Complete the SSDtoHDD and SSDtoUSBFlash scenarios across all 4 current variants before Phase A implementation. This establishes:
- Per-bucket throughput baselines on spinning rust and flash media.
- The degree to which small files are bottlenecked by seek latency vs rotational latency.
- Safe starting concurrency values for Phase A (likely 1-2 for HDD, 2-4 for USB flash).

### 5.3 Second Dataset — Byte-Volume-Dominated

**Action required:** Create and benchmark a second dataset where large files dominate by total byte volume (not file count). For example:
- 100 files of 100 MiB each (10 GB total)
- Or: a smaller bucket of 50-100 files in the 64 MiB-2 GiB range from `CandidateData`

This dataset validates that buffer scaling and preallocation produce measurable gains in real-world media/archive copy scenarios, rather than being lost in the noise of the MixedDataset's 27,000 tiny files. Run at minimum `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB` variants against this dataset on SSDtoSSD.

## 6. Benchmarking Plan for Each Change

For each optimisation iteration:

1. Run on `SSDtoSSD` first (fast feedback loop).
2. Execute at least 3 runs per variant (not 2 — high run-to-run variance observed in BaselineAuto warrants a third sample).
3. Randomize variant order to reduce systematic order bias.
4. Compare:
   - End-to-end `executeDuration` (primary)
   - File-size bucket medians (`P50`) and tails (`P95`) from `benchmark-file-results.ndjson`
   - Failure/skipped counts (must not regress)

Promotion gate to broader scenarios (`SameDrive`, `SSDtoHDD`, `SSDtoUSBFlash`):
- Consistent improvement on SSD without correctness regressions.
- No severe tail-latency regressions for medium/large files.
- For parallelism variants specifically: must not regress on HDD (once baseline exists).

## 7. Metrics to Track

Primary:
- Job wall-clock execution time per scenario/variant.

Secondary:
- Per-size-bucket throughput (`Avg`, `P50`, `P95`).
- **Per-bucket aggregate wall-clock time and %-of-bytes.** Currently requires manual computation from the scenario config — the benchmark analysis tool should emit these columns directly so that file-count dominance vs byte-volume dominance is immediately visible.
- Variance across runs (coefficient of variation or simple run spread).
- Error rates (`failedFiles`, exceptions).

Operational sanity:
- CPU usage trend during tiny-file phases.
- Disk queue depth behavior (where available).
- Per-file `ExecutionDuration` distribution (spot-check for outliers >1s).

## 8. Things to Avoid

- Re-architecting the full pipeline model.
- Sacrificing progress visibility to chase raw throughput.
- Destination-specific hardcoding without capability-based fallback.
- Adding parallelism without first understanding HDD/USB impact.
- Over-fitting optimization to MixedDataset — validate against real-world directory structures as well.

## 9. Implementation Notes

- Keep exception handling top-level intact ("must not crash" principle).
- Prefer feature flags or config toggles for new optimization knobs so A/B benchmarking remains easy.
- Continue emitting `benchmark-results.ndjson` and `benchmark-file-results.ndjson` for longitudinal comparison.
- All new parallelism code paths must preserve `CancellationToken` and `PauseToken` semantics.
- Staged temp file naming under concurrency must avoid collisions (append worker index or GUID to temp suffix).

## 10. Execution Tasklist

This section turns the strategy into implementation-ready tasks with ownership boundaries and measurable outcomes.

### Phase A — Small-file Parallelism (highest expected impact)

Goal:
- Improve `0-64 KiB` and `64-512 KiB` performance on SSD targets without correctness regressions.

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs` — partition files, parallel dispatch
- `SmartCopy.Core/Pipeline/PipelineRunner.cs` — pause/cancel integration
- `SmartCopy.Core/FileSystem/LocalFileSystemProviderOptions.cs` — new knobs
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — new variant fields
- `benchmark-scenarios.json` — new variants

Tasks:
1. Add optional tuning knobs in `LocalFileSystemProviderOptions`:
   - `SmallFileParallelThresholdBytes` (default 0 = disabled)
   - `SmallFileMaxConcurrency` (default 1 = disabled)
2. In `CopyStep.ApplyAsync`, partition selected file nodes by size into parallel and sequential sets, routed through the framework that will become the adaptive dispatch table (Section 3.6).
3. Preserve deterministic failure semantics:
   - Each failed file yields a failed `TransformResult` (collected and yielded after all parallel work completes).
   - No unhandled exceptions escape step scope.
4. Preserve cancellation/pause behavior:
   - `CancellationToken` passed to each parallel task.
   - Check `PauseToken` between batches of parallel work.
5. Progress reporting under concurrency:
   - Multiple files can report byte-level progress simultaneously — `IFileTransferProgressSink` must be thread-safe.
   - Aggregate per-result progress after parallel batch completes rather than interleaving yields.
6. Staged temp file naming: ensure concurrent writes to the same destination directory produce unique temp paths.
7. Add `AdaptiveX2`, `AdaptiveX4`, `AdaptiveX8` benchmark variants (Section 5.1).

Acceptance criteria:
- No regressions in copy correctness (`copiedFiles`, `failedFiles`, destination contents).
- No crashes or unhandled exceptions under cancellation.
- SSDtoSSD median `executeDuration` improves by >=1.5x for 0-64 KiB bucket.
- All existing unit tests pass with parallelism enabled.
- The dispatch logic is structured so Phase B1/B2 behaviours (direct write, directory cache) can be wired in without restructuring.

Acceptance criteria:
- No regressions in copy correctness (`copiedFiles`, `failedFiles`, destination contents).
- No crashes or unhandled exceptions under cancellation.
- SSDtoSSD median `executeDuration` improves by >=1.5x for 0-64 KiB bucket.
- All existing unit tests pass with parallelism enabled.

Benchmark gate:
- Minimum 5 SSDtoSSD runs per variant (BaselineAuto + 3 parallelism levels) with randomized order.
- Promote only if median runtime improves and P95 tiny-file throughput does not regress materially.

### Phase B1 — Metadata Overhead Reduction (high expected impact)

Goal:
- Eliminate redundant per-file filesystem metadata calls.

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs` — remove or defer ExistsAsync
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — cache directory creation; skip source Exists

Tasks:
1. In `CopyStep.ApplyAsync`:
   - For `OverwriteMode.Overwrite`: remove the `ExistsAsync` pre-check entirely. The staged write path handles overwrite via atomic rename.
   - For `OverwriteMode.Skip`: evaluate whether the check can be deferred to a try-open/conflict detection pattern instead of a pre-emptive stat call.
2. In `LocalFileSystemProvider.WriteAsync`:
   - Add a `ConcurrentDictionary<string, byte>` or similar cache for recently-created directories. Before calling `Directory.CreateDirectory`, check the cache. Invalidate entries lazily (e.g., LRU with TTL) or on explicit error.
3. In `LocalFileSystemProvider.OpenReadAsync`:
   - The `File.Exists` check is redundant when the source path is known-valid from the directory tree scan. Consider an overload that skips this check for paths already validated during enumeration.
4. Keep error semantics identical for all overwrite modes.

Acceptance criteria:
- Reduced per-file `ExecutionDuration` for the 0-64 KiB bucket by >=20%.
- No overwrite-mode behavior changes.
- Directory creation cache does not cause failures when directories are externally deleted.

Benchmark gate:
- Same as Phase A gate, applied to metadata-optimized variant vs BaselineAuto.
- Profile per-file timing distribution to confirm metadata call reduction.

### Phase B2 — Tiny-file Write Fast Path (moderate expected impact)

Goal:
- Eliminate staged temp file overhead for files <=64 KiB.

Code focus:
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`

Tasks:
1. Add `TinyFileFastPathThresholdBytes` option (default 0 = disabled, suggested 65536).
2. When a file size is below the threshold and `OverwriteMode` allows:
   - Open the destination file directly (not a temp file) with `FileMode.Create`.
   - Write content directly (single `CopyToAsync` call or `Write` for byte-sized buffers).
   - Skip the staged rename cycle entirely.
3. Document the trade-off: tiny files written directly may leave partial files if the write is interrupted mid-stream. Gate behind opt-in until the trade-off is validated.
4. Consider reading the entire tiny file into a pooled buffer and writing in a single operation (removes even the streaming overhead for files where the whole content fits in one chunk).

Acceptance criteria:
- Improved 0-64 KiB bucket P50 and P95 on SSDtoSSD.
- No partial-file corruption under normal (non-interrupted) operation.
- Skipped/overwritten semantics preserved.

Benchmark gate:
- Same as Phase A.
- Explicit validation that files written via fast path are bit-identical to those written via staged path.

### Phase C — Progress Throttling and Destination-Sensitive Policy

Goal:
- Generalize gains while limiting regressions on HDD/USB/network destinations. Applies to both Tracks 1 and 2. Requires HDD and USB baseline data that is currently missing.

Prerequisites:
- Complete SSDtoHDD and SSDtoUSBFlash baselines with all 4 current variants (see Benchmark Gap, Section 5.2).
- Byte-volume-dominated dataset created and baselined (Section 5.3).

Code focus:
- `SmartCopy.Core/Pipeline/PipelineRunner.cs` — progress throttling
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — destination detection
- `SmartCopy.Core/FileSystem/ProviderCapabilities` — destination profile
- `SmartCopy.Benchmarks/Program.cs` — staged scenario matrix

Tasks:
1. Add destination-aware strategy policy:
   - Detect SSD vs HDD vs USB via `DriveInfo` or platform APIs.
   - SSD: allow higher small-file parallelism (4-8).
   - HDD: conservative concurrency (1-2).
   - USB flash: moderate concurrency (2-4, profile-dependent).
2. Tune progress emission under tiny-file bursts:
   - Batch file-completion progress: emit at most once per 100ms or per 50 files, whichever comes first.
   - Keep byte-level progress throttling at ~10 Hz as currently implemented.
3. Add benchmark variants that explicitly test policy choices against each destination type.

Acceptance criteria:
- SSD gains from Phase A and Phase L retained.
- No severe regressions on SameDrive/HDD/USB scenarios.
- Progress UX remains clear and stable even with batching.

Benchmark gate:
- Run staged matrix in configured scenario order:
  1. SSDtoSSD
  2. SameDriveTest
  3. SSDtoHDD (must be baselined first)
  4. SSDtoUSBFlash (must be baselined first)
- Run the byte-volume-dominated dataset on SSDtoSSD to confirm buffer scaling gains transfer.
- Promote policy defaults only after all scenarios pass correctness and acceptable performance criteria.

### Phase L1 — Buffer Size Scaling

Goal:
- Determine the optimal buffer size for large-file streaming throughput on modern SSDs.

Code focus:
- `SmartCopy.Core/FileSystem/LocalFileSystemProviderOptions.cs` — no code changes needed; buffer is already configurable
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — buffer scaling variants
- `benchmark-scenarios.json` — byte-volume-dominated dataset scenario

Tasks:
1. Add buffer scaling variants to the benchmark matrix: `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB`, `ManualLoop8MiB` — all with `ManualLoop` write mode and `PreallocateDestinationFile = false` to isolate buffer size as the sole variable.
2. Run against the byte-volume-dominated dataset (SSDtoSSD) with 3+ runs per variant, randomized order.
3. Measure throughput at each buffer size for buckets 4 MiB+.
4. Select the buffer size with the best P50 throughput as the new default for `ManualLoop` mode.

Acceptance criteria:
- Clear throughput curve showing diminishing returns (or a plateau) at some buffer size.
- No regressions on the MixedDataset (small files should be unaffected by buffer size changes, as Section 1.3 already shows).

Benchmark gate:
- Minimum 3 runs per buffer size on the byte-volume-dominated dataset.
- If a buffer >1 MiB shows >=5% throughput improvement over 1 MiB, promote it as the new default for large-file copy.

### Phase L2 — Isolate Preallocation Contribution

Goal:
- Determine whether `PreallocateDestinationFile` provides measurable benefit independent of buffer size.

Code focus:
- No code changes — benchmark variant configuration only.

Tasks:
1. Run `ManualLoop1MiB` (no preallocate) vs `ManualLoop1MiBPrealloc` on the byte-volume-dominated dataset.
2. If preallocation shows benefit, repeat the comparison at the buffer size selected in Phase L1 (e.g. `ManualLoop4MiB` vs `ManualLoop4MiBPrealloc`).
3. Measure: wall-clock time, filesystem fragmentation after copy (if measurable), P95 and P50 throughput.

Acceptance criteria:
- Quantified preallocation benefit in isolation (previously confounded with buffer size in the `ManualLoop1MiBPreallocate` variant).
- Decision on whether preallocation should be enabled by default or kept opt-in.

Benchmark gate:
- Minimum 3 runs per variant on byte-volume-dominated dataset.
- If preallocation shows <3% benefit, disable by default to avoid unnecessary SetLength calls on every file.

### Cross-phase Safety Checklist

Apply before each merge:
1. `dotnet build` succeeds.
2. All unit tests pass (`dotnet test`).
3. Benchmark run metadata recorded with notes about cold/warm conditions.
4. Results appended to both:
   - `benchmark-results.ndjson`
   - `benchmark-file-results.ndjson`
5. Any architectural behavior changes reflected in `Docs/Architecture.md`.
6. New `LocalFileSystemProviderOptions` fields have safe defaults (0/disabled) so existing behavior is preserved.
7. Routing-policy changes validated on MixedDataset; buffer scaling changes validated on byte-volume-dominated dataset (Section 5.3).
8. For parallelism changes: verify that total `copiedFiles` + `failedFiles` + `skippedFiles` equals the expected total across all runs.
