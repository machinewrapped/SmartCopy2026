# SmartCopy2026 - Optimisation Strategies

This document captures practical optimisation strategies for copy performance, with emphasis on small-file workloads where current benchmarks show the largest losses.

## 1. Current Findings (as of 2026-04-07)

From SSD-to-SSD benchmark runs and file-level analysis:

- Large files are not the primary bottleneck; throughput is healthy and increases with file size.
- Small files (especially under 64 KiB) dominate runtime overhead.
- `CopyToAsync512KiB` and `ManualLoop512KiBArrayPool` are the strongest all-around variants.
- `ManualLoop1MiBPreallocate` helps larger files but underperforms overall because small files dominate file count.
- `BaselineAuto` appears environment-sensitive (high run-to-run variance).

Implication: optimize metadata/scheduling overhead and tiny-file handling first, not large-file streaming throughput.

## 2. Optimisation Goals

- Reduce wall-clock time for mixed datasets dominated by small files.
- Maintain stability and correctness (no unhandled exceptions, no partial-state regressions).
- Keep progress UX responsive without introducing high reporting overhead.
- Preserve cross-provider behavior and graceful degradation semantics.

## 3. Candidate Strategies

### 3.1 Bounded Parallel Copy for Small Files

Description:
- Copy small files concurrently using a bounded worker pool.
- Keep large files single-stream (or low concurrency) to avoid contention.

Suggested starting configuration:
- `smallFileParallelThresholdBytes = 256 KiB`
- `smallFileMaxConcurrency = 4` (then test 6 and 8)

Expected effect:
- Highest likely win on SSD destinations for tiny/small file buckets.

Risks:
- Over-parallelization can hurt HDD/USB targets via seek thrash or queue contention.
- Progress reporting and cancellation semantics become more complex.

### 3.2 Tiny-File Fast Path

Description:
- For very small files (for example <=64 KiB), use a direct read/write path with minimal overhead and pooled buffers.
- Reduce per-file setup overhead where possible.

Expected effect:
- Moderate to high improvement in the `0-64 KiB` bucket.

Risks:
- Additional complexity in write-path branching.

### 3.3 Reduce Per-File Metadata Round-Trips

Description:
- Minimize `Exists`/stat calls when overwrite semantics allow streamlined create/replace behavior.
- Cache repeated destination directory checks/creation.

Expected effect:
- Moderate improvement across all small file buckets.

Risks:
- Needs careful handling of overwrite modes and error semantics.

### 3.4 Progress Reporting Cost Control

Description:
- Keep user-visible progress, but throttle high-frequency updates during tiny-file bursts.
- Prefer periodic aggregated updates over per-file UI updates when files are very small.

Expected effect:
- Small to moderate throughput improvement and lower UI pressure.

Risks:
- If over-throttled, user perception of responsiveness can degrade.

### 3.5 Destination-Sensitive Strategy Selection

Description:
- Use provider capabilities and destination profile (SSD/HDD/USB/network) to choose safe defaults.
- Example: higher small-file concurrency on SSD, conservative settings on HDD/USB.

Expected effect:
- Improves generalization and avoids regressions on slower media.

Risks:
- Requires robust heuristics; wrong classification can reduce performance.

## 4. Recommended Rollout Order

1. Implement bounded small-file concurrency for `CopyStep` (SSD-friendly defaults).
2. Add tiny-file fast path + buffer pooling.
3. Trim metadata round-trips and directory overhead.
4. Tune progress throttling for tiny-file bursts.
5. Add destination-sensitive policy tuning.

Rationale:
- This sequence targets likely largest gains first while keeping changes measurable and isolated.

## 5. Benchmarking Plan for Each Change

For each optimisation iteration:

1. Run on `SSDtoSSD` first (fast feedback loop).
2. Execute at least 2 runs per variant (preferably cold/warm separated notes).
3. Randomize variant order to reduce systematic order bias.
4. Compare:
   - End-to-end `executeDuration` (primary)
   - File-size bucket medians (`P50`) and tails (`P95`) from `benchmark-file-results.ndjson`
   - Failure/skipped counts (must not regress)

Promotion gate to broader scenarios (`SameDrive`, `SSDtoHDD`, `SSDtoUSBFlash`):
- Consistent improvement on SSD without correctness regressions.
- No severe tail-latency regressions for medium/large files.

## 6. Metrics to Track

Primary:
- Job wall-clock execution time per scenario/variant.

Secondary:
- Per-size-bucket throughput (`Avg`, `P50`, `P95`).
- Variance across runs (coefficient of variation or simple run spread).
- Error rates (`failedFiles`, exceptions).

Operational sanity:
- CPU usage trend during tiny-file phases.
- Disk queue depth behavior (where available).

## 7. Things to avoid

- Re-architecting the full pipeline model.
- Sacrificing progress visibility to chase raw throughput.
- Destination-specific hardcoding without capability-based fallback.

## 8. Implementation Notes

- Keep exception handling top-level intact ("must not crash" principle).
- Prefer feature flags or config toggles for new optimization knobs so A/B benchmarking remains easy.
- Continue emitting `benchmark-results.ndjson` and `benchmark-file-results.ndjson` for longitudinal comparison.

## 9. Execution Tasklist (Phase A/B/C)

This section turns the strategy into implementation-ready tasks with ownership boundaries and measurable outcomes.

### Phase A - Small-file Parallelism (highest expected impact)

Goal:
- Improve `0-64 KiB` and `64-512 KiB` performance on SSD targets without correctness regressions.

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs`
- `SmartCopy.Core/Pipeline/PipelineRunner.cs`
- `SmartCopy.Core/FileSystem/LocalFileSystemProviderOptions.cs`
- `SmartCopy.Benchmarks/BenchmarkModels.cs`
- `benchmark-scenarios.json`

Tasks:
1. Add optional tuning knobs for copy behavior:
   - `smallFileParallelThresholdBytes`
   - `smallFileMaxConcurrency`
2. In `CopyStep.ApplyAsync`, partition selected file nodes into:
   - small-file set (parallelized, bounded concurrency)
   - large-file set (existing sequential path)
3. Preserve deterministic failure semantics:
   - each failed file yields a failed `TransformResult`
   - no unhandled exceptions escape step scope
4. Preserve cancellation/pause behavior in parallel mode.
5. Preserve progress behavior and ensure no UI flood from concurrent updates.

Acceptance criteria:
- No regressions in copy correctness (`copiedFiles`, `failedFiles`, destination contents).
- No crashes or unhandled exceptions under cancellation.
- SSDtoSSD median `executeDuration` improves versus baseline variants.

Benchmark gate:
- Minimum 5 SSDtoSSD runs per variant with randomized variant order.
- Promote only if median runtime improves and P95 tiny-file throughput does not regress materially.

### Phase B - Tiny-file Fast Path + Metadata Overhead Reduction

Goal:
- Reduce fixed per-file overhead for files <=64 KiB and improve consistency.

Code focus:
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs`

Tasks:
1. Add tiny-file fast path in provider write logic:
   - low-overhead copy path for very small files
   - pooled buffer usage where useful
2. Minimize redundant metadata round-trips:
   - review overwrite behavior and avoid unnecessary `Exists` calls where semantics allow
   - avoid repeated directory existence checks/creation for same destination parent
3. Keep behavior identical for overwrite modes and error handling.

Acceptance criteria:
- Improved `0-64 KiB` bucket `P50` and `P95` on SSDtoSSD.
- No overwrite-mode behavior changes.

Benchmark gate:
- Same as Phase A, plus explicit comparison of tiny bucket throughput and end-to-end runtime.

### Phase C - Progress/Policy Tuning by Destination Type

Goal:
- Generalize gains while limiting regressions on HDD/USB/network destinations.

Code focus:
- `SmartCopy.Core/Pipeline/PipelineRunner.cs`
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`
- `SmartCopy.Core/FileSystem/ProviderCapabilities` consumers
- `SmartCopy.Benchmarks/Program.cs` (variant matrix)

Tasks:
1. Add destination-aware strategy policy:
   - SSD: allow higher small-file parallelism
   - HDD/USB/network: conservative concurrency defaults
2. Tune progress emission under tiny-file bursts:
   - keep responsive UX while reducing high-frequency update overhead
3. Add benchmark variants that explicitly test policy choices.

Acceptance criteria:
- SSD gains retained.
- No severe regressions on SameDrive/HDD/USB scenarios.
- Progress UX remains clear and stable.

Benchmark gate:
- Run staged matrix in configured scenario order:
  1. SSDtoSSD
  2. SameDriveTest
  3. SSDtoHDD
  4. SSDtoUSBFlash
- Promote policy defaults only after all scenarios pass correctness and acceptable performance criteria.

### Cross-phase Safety Checklist

Apply before each merge:
1. `dotnet build` succeeds.
2. Relevant tests pass.
3. Benchmark run metadata recorded with notes about cold/warm conditions.
4. Results appended to both:
   - `benchmark-results.ndjson`
   - `benchmark-file-results.ndjson`
5. Any architectural behavior changes reflected in `Docs/Architecture.md`.
