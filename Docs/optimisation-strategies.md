# SmartCopy2026 - Optimisation Strategies

This document captures practical optimisation strategies for copy performance.

The evidence so far points to a fundamental tension: **large files need staged writes and granular progress for integrity and UX; small files need minimal per-file ceremony to avoid drowning in overhead.** There is unlikely to be a universally best strategy. The approach must be *adaptive* — selecting the right code path per file based on what we already know at pipeline execution time (file size, count, destination type).

The intentionally diverse MixedDataset surfaces both ends of this spectrum: 73% of files are under 64 KiB (and consume 56% of SSD wall-clock time despite being 1.7% of bytes), while 33% of bytes live in files >256 MiB (where streaming throughput and buffer tuning dominate).

## 1. Current Findings (as of 2026-05-16)

From SSD-to-SSD, SameDrive, SSD-to-HDD, and the first SSD-to-USBFlash baseline run (464,400 per-file records, 18,576 files per run across 6 size buckets):

### 1.1 Throughput and Wall-Clock Contribution by Size Bucket (SSDtoSSD)

Using the target byte budgets per bucket from the MixedDataset prep config and the observed throughput (averaged across all 4 variants):

| Size Bucket | Files | % Files | Bytes | % Bytes | Avg MiB/s | Est. Wall Time | % Wall Time |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0-64 KiB | 13,536 | 72.9% | 256 MiB | 1.7% | 2.1 | 2m 3s | **56.4%** |
| 64-512 KiB | 2,858 | 15.4% | 512 MiB | 3.4% | 17.4 | 29 sec | 13.4% |
| 512 KiB-4 MiB | 1,775 | 9.6% | 2.0 GiB | 13.5% | 84.4 | 24 sec | 11.0% |
| 4-32 MiB | 345 | 1.9% | 3.0 GiB | 20.3% | 248 | 12 sec | 5.7% |
| 32-256 MiB | 54 | 0.3% | 4.1 GiB | 27.7% | 310 | 14 sec | 6.2% |
| 256 MiB-2 GiB | 8 | <0.1% | 5.0 GiB | 33.4% | 313 | 16 sec | 7.4% |
| **Total** | **18,576** | **100%** | **~14.8 GiB** | **100%** | — | **~218 sec** | **100%** |

Key takeaway: **1.7% of the bytes consume 56% of the wall-clock time.** The 0-64 KiB bucket has throughput ~150x worse than the largest files (2.1 vs 313 MiB/s). The 73% file-count headline masks the degrees of freedom:

- **Optimising 0-64 KiB throughput by 4x saves ~92 seconds** (56% of runtime → ~14%).
- **Optimising 256 MiB-2 GiB throughput by even 20% saves ~3.2 seconds** (7.4% → ~5.9%).
- A 10% improvement to large-file streaming is worth ~1.6 seconds for the largest bucket alone. A 10% improvement to tiny-file overhead is worth ~12 seconds. Both matter, but the ROI-per-engineering-hour is dramatically higher on the tiny-file path — *for the SSD/HDD MixedDataset profiles*.

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

### 1.4 SameDrive vs Cross-Drive Split

SameDrive is not uniformly slower. Small-file throughput (0-64 KiB) is **~20% higher** than SSDtoSSD (2.38-2.55 MiB/s vs 1.91-2.19 MiB/s across variants), but large-file throughput is lower, especially in the 256 MiB-2 GiB bucket (~231 MiB/s vs ~313 MiB/s averaged across variants).

Interpretation:
- Same-volume copy may reduce some tiny-file metadata/path overhead or benefit from cache locality.
- The same drive becomes a real contention point once byte streaming dominates.
- Do not use "same drive" as a single destination profile. It behaves like a small-file metadata case for tiny files and a contention-limited streaming case for large files.

### 1.5 HDD Baseline — Same Bottleneck, Different Ceiling

The SSDtoHDD baseline is now complete for the current four variants (2 runs each). It confirms the key SSD finding rather than replacing it: tiny files still dominate wall-clock time. The 0-64 KiB bucket is 1.7% of bytes but **56.7% of estimated wall time** at ~0.91 MiB/s. The 64-512 KiB bucket adds another 13.2%.

Variant changes are not meaningful at job level on HDD:
- Average execute durations are tightly clustered: BaselineAuto ~540s, CopyToAsync512KiB ~530s, ManualLoop512KiBArrayPool ~539s, ManualLoop1MiBPreallocate ~541s.
- The 0-64 KiB bucket varies only 0.89-0.93 MiB/s across variants.
- ManualLoop1MiBPreallocate helps modestly in 4-256 MiB buckets but does not improve the largest bucket.

Conclusion: HDD does not justify buffer/write-mode tuning as the primary MixedDataset lever. It strengthens the case for metadata reduction, directory-create caching, and careful small-file batching, but with a conservative concurrency cap because seek behavior can punish over-parallelization.

### 1.6 USB Flash Baseline — Preliminary but Policy-Relevant

Only one SSDtoUSBFlash run is complete, and only for BaselineAuto, so this is not enough to tune variant defaults. It is enough to show that USB flash is not just "slower SSD":

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0-64 KiB | 0.40 | 10m 34s | 26.5% |
| 64-512 KiB | 2.50 | 3m 25s | 8.6% |
| 512 KiB-4 MiB | 7.10 | 4m 48s | 12.0% |
| 4-32 MiB | 10.88 | 4m 42s | 11.8% |
| 32-256 MiB | 11.78 | 5m 56s | 14.9% |
| 256 MiB-2 GiB | 8.07 | 10m 27s | 26.2% |

Unlike SSD and HDD, USB flash wall time is split between the smallest and largest files. Tiny files remain a serious problem, but large-file streaming is now equally important. The policy implication is different:
- Tiny-file optimisations alone cannot make USB feel fast.
- Large-file buffer strategy and progress behavior matter more on USB than they do in the SSD/HDD MixedDataset results.
- USB parallelism must be treated as unknown until the remaining variants run; queueing more writes may improve latency hiding on some devices and collapse throughput on others.

### 1.7 Workload Diversity and the Case for Adaptive Strategy

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
  - Tiny (≤64 KiB): buffer-batch, direct write, minimal progress events
  - Medium (64 KiB-4 MiB): staged writes, throttled progress
  - Large (>4 MiB): sequential single-stream, staged writes, large buffer, full progress
- Extend selection to include destination type (SSD → higher concurrency cap; HDD → cap at 1-2; network → cap at 1) and overwrite mode (Skip → eliminate Exists pre-check; Overwrite → OpenOrCreate path).
- This makes the previously separate "strategies" (batching, direct write, metadata reduction, progress throttling, destination sensitivity) into composable behaviors selected by a routing function at the top of ApplyAsync.

This is not a new Phase — it's the architecture that Phases B2, B3, B1, and (if warranted) A converge on.

Expected effect:
- Eliminates the risk of over-optimising for one workload profile at the expense of another.
- Makes the strategy selection transparent for benchmarking — each variant tests a specific routing policy rather than a monolithic mode.

Risks:
- Routing function must itself be cheap (no I/O, no allocations in the hot path) — it operates on already-known data (node.Size, provider capabilities).

### 3.7 Buffered Read-Write Batching (NEW — HIGH PRIORITY)

Description:

The current model treats each file as a unit of work: read file₁, write file₁, read file₂, write file₂, ... This interleaves read and write I/O for every file. For tiny files on any device, this means the I/O bus alternates direction on every file — a fundamental inefficiency regardless of seek cost.

**Proposed model:** accumulate multiple files into a memory buffer during a read phase, then drain the buffer during a write phase. The unit of work becomes a *buffer* (potentially containing many small files), not a file.

```
Read file₁ → Read file₂ → ... → Read fileN  [buffer fills]
Write file₁ → Write file₂ → ... → Write fileN  [buffer drains]
```

For small files (e.g., 4 KiB) with a 16 MiB buffer, N ≈ 4,000 files per cycle: thousands of sequential reads before any write, thousands of sequential writes before the next read. On HDDs, this reduces seek thrash dramatically — two passes per batch rather than 2N passes. On flash/SSD it reduces per-command overhead and may allow storage controllers to optimize read and write queues independently.

**Implementation sketch:**
- Allocate a fixed-size pooled buffer (e.g., 16 MiB, configurable)
- For each small file in scan order: read entire content into buffer at current offset, record `(node, offset, length)` metadata
- When buffer fills (or we encounter a large file): flush all buffered entries by writing each to its destination path directly (no staging — content is fully in memory, so write starts only when file data is complete)
- Large files (size > buffer capacity) are streamed directly without buffering, using the existing staged write path
- Yield results for a batch of files at once after the write phase completes

**Integrity trade-off:** Writing buffered files directly (without staging) means an interrupted write can leave a partial file at the destination path. This is the same trade-off as Phase B2 direct write: acceptable for files that are trivially small to re-copy, but must be clearly documented and gated behind an opt-in flag until validated.

Expected effect:
- Large reduction in effective I/O operation count for tiny files (thousands of files → two phases per buffer cycle).
- HDD benefit is potentially largest: eliminates per-file head reversal between source and destination.
- Flash/USB benefit: reduces protocol round-trips and allows device-level write buffering to work more efficiently.
- SSD benefit: smaller but still present — fewer kernel transitions per byte, better queue utilization.

Risks:
- Memory usage bounded by buffer size (acceptable; 16-64 MiB is well within normal application limits).
- Progress reporting changes: files in a batch are all marked complete after the write phase, not one-by-one during the read phase. Requires progress model update.
- Partial destination files on interruption (same trade-off as Phase B2 direct write).
- Buffer size selection: too small loses the batching benefit; too large increases memory pressure and latency before first results appear.

### 3.8 Phase-Separated Parallelism (LOWER PRIORITY — TEST AFTER 3.7)

Description:

If buffer batching (3.7) proves insufficient, the next step is concurrency — but structured to avoid mixing reads and writes simultaneously. Rather than N concurrent (read + write) pairs:

**Phase-separated model:**
- Read phase: N concurrent reads from source (parallel reads, no writes)
- Wait for all reads to complete
- Write phase: N concurrent writes to destination (parallel writes, no reads)

This keeps read I/O and write I/O separated in time, preventing bus contention between source and destination and avoiding the HDD seek-thrash concern of interleaved parallel copies. It is less aggressive than mixed parallelism (Section 3.1) and easier to reason about.

Within each phase, the parallelism is scoped to files within the same directory (see directory-sequential note in Section 3.1) to preserve directory cohesion on interrupted runs.

Expected effect:
- Moderate improvement over sequential batching if device read/write throughput can be genuinely parallelized.
- Safer on HDD than mixed parallelism (reads finish before writes start).

Risks:
- Memory pressure: N files simultaneously in the read buffer before any write starts.
- Phase boundary adds latency before first results.
- Not worth implementing if buffer batching (3.7) achieves the target improvement.

### 3.5 Destination-Sensitive Strategy Selection

Description:
- Use provider capabilities and destination profile (SSD/HDD/USB/network) to choose safe defaults.
- Example: higher small-file concurrency on SSD, conservative concurrency on HDD, and separate USB handling because the preliminary USB baseline is split between tiny-file overhead and large-file streaming limits.
- Implement as capability flags or a runtime-detected `DestinationProfile` enum.
- SameDrive should not be collapsed into SSDtoSSD. The benchmark shape is mixed: small files look slightly better than SSDtoSSD, while large files are contention-limited.

Expected effect:
- Improves generalization and prevents regressions on slower media.

Risks:
- Requires robust heuristics; wrong classification can reduce performance.
- HDD has enough baseline data to set conservative starting caps. USB does not: only BaselineAuto has one completed run, so USB thresholds remain provisional until the remaining variants complete.

## 4. Revised Rollout Order

The goal is an adaptive `CopyStep.ApplyAsync` that selects the right code path per file based on known size. Each Phase builds a piece of that routing table (Section 3.6). The phases are ordered by **increasing complexity and risk**, not assumed ROI — each phase must be benchmarked before the next is designed.

The original ordering (Phase A parallelism first) assumed parallelism was the primary lever. The benchmark data shows that ~97% of copy time is NOT in byte I/O, but we have not yet isolated *which* component of per-file overhead dominates. The new order explores simpler, less risky strategies first to identify what the bottleneck actually is before adding concurrency.

1. **Phase B1:** Metadata overhead reduction — eliminate redundant `ExistsAsync`, `File.Exists`, per-file `Directory.CreateDirectory` calls. Zero risk, no write-path changes, no feature flags. Goes straight into Core. Establishes a clean baseline before testing anything else.

2. **Phase B2:** Tiny-file direct write — bypass staging (temp file + rename) for files ≤64 KiB. Isolates the staging overhead cost. Expected: 20-40% improvement on 0-64 KiB bucket if staging is the primary bottleneck.

3. **Phase B3:** Buffered read-write batching (Section 3.7) — accumulate N small files into a buffer during a read phase, then write all N during a write phase. Keeps read and write I/O separated in time; likely the largest untested lever for HDD and USB. Expected: varies by destination type; potentially 2-4x for HDD.

4. **Phase C:** Progress throttling — batch completion events during tiny-file bursts. Low-risk cleanup after the copy path is established.

5. **Phase D:** Destination-sensitive policy — cap strategy aggressiveness based on SSD/HDD/USB/network detection. HDD can start from conservative baselines; USB requires remaining baseline variants before defaults are promoted (Section 5.2).

6. **Phase A:** Bounded parallelism — only if Phases B2-B3 prove insufficient. Try phase-separated parallelism (Section 3.8) before mixed parallelism (Section 3.1). HDD seek thrash must be measured before enabling any parallelism on rotating media.

**Buffer scaling (Phase L, independent):**
- **Phase L:** Test 512 KiB → 1 MiB → 2 MiB → 4 MiB → 8 MiB buffers against large-file-only datasets (Section 5.3) to find the optimal value for files >4 MiB. Isolate preallocation as a separate variable. Feed the result back into the adaptive routing table's large-file path.

The outcome: a single `CopyStep.ApplyAsync` where the per-file routing is a pure function of `(node.Size, providerCapabilities, overwriteMode)` with no I/O in the hot path.

## 5. Updated Variant Strategy

### 5.1 Variant Matrix — Small-File Strategy Variants + Buffer Scaling

The goal is to isolate each strategy change as a single variable. Variants are introduced in phase order: simpler changes first, complexity added only after simpler variants are measured.

**Phase B2 — staging removal (test first):**
| Variant | Tiny (≤64 KiB) | Medium / Large | Purpose |
|---|---|---|---|
| `BaselineAuto` | Sequential, staged write, 4 KiB buffer | Same | Control — current behavior, no changes |
| `DirectWrite` | Sequential, direct write (no staging), 4 KiB buffer | Staged (unchanged) | Isolate staging overhead: how much is the temp-file + rename cycle costing? |

**Phase B3 — buffer batching (test after B2):**
| Variant | Tiny (≤64 KiB) | Medium / Large | Purpose |
|---|---|---|---|
| `ReadBatch16MiB` | Buffer 16 MiB, sequential read phase then write phase, staged | Staged (unchanged) | Isolate read/write phase separation — still uses staging for writes |
| `DirectWriteBatch16MiB` | Buffer 16 MiB, sequential read phase then direct write phase | Staged (unchanged) | B2 + B3 combined — eliminates staging and separates I/O phases |
| `DirectWriteBatch64MiB` | Buffer 64 MiB, sequential read phase then direct write phase | Staged (unchanged) | Tests whether a larger buffer materially changes the result |

**Phase A — parallelism (only if B2/B3 prove insufficient):**
| Variant | Tiny (≤64 KiB) | Medium / Large | Purpose |
|---|---|---|---|
| `PhaseParallelX4` | Parallel read x4 then parallel write x4 (phase-separated), direct write | Staged (unchanged) | Phase-separated parallelism — reads don't overlap writes |
| `AdaptiveX4` | Concurrent read+write x4 (mixed), direct write | Staged (unchanged) | Traditional mixed parallelism — highest risk for HDD |

**Buffer scaling variants (Phase L, independent of small-file strategy):**
| Variant | Buffer | Purpose |
|---|---|---|
| `ManualLoop512KiB` | 512 KiB | Baseline for large-file streaming |
| `ManualLoop1MiB` | 1 MiB | Current default |
| `ManualLoop2MiB` | 2 MiB | Scale step |
| `ManualLoop4MiB` | 4 MiB | Scale step |
| `ManualLoop8MiB` | 8 MiB | Scale ceiling |
| `ManualLoopXMiBPrealloc` | Best size from above | Isolates preallocation contribution independently of buffer size |

Retire `CopyToAsync512KiB` and `ManualLoop512KiBArrayPool` — they provide no unique signal beyond the above variants.

### 5.2 Benchmark Data Gap — USB

HDD is no longer a baseline blocker: all four current variants have two successful SSDtoHDD runs. Use those results to start Phase A with conservative HDD caps and to verify that small-file optimisations do not create seek-driven regressions.

**Action required:** Complete the SSDtoUSBFlash scenario across all 4 current variants before promoting USB defaults. Current status is one BaselineAuto run only. The remaining USB runs establish:
- Whether larger buffers or preallocation help large-file USB throughput.
- Whether CopyToAsync/manual-loop differences are visible on flash media.
- Safe starting concurrency values for Phase A on USB flash. Do not assume 2-4 is safe until measured.

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
- For parallelism variants specifically: must not regress on HDD and must be measured separately on USB before USB defaults are enabled.

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
- Adding parallelism before exhausting sequential strategies (Phases B2, B3). Parallelism is Phase A — a last resort if simpler approaches prove insufficient.
- Adding parallelism without first understanding HDD/USB impact. HDD baselines now exist; USB still needs variant coverage.
- Over-fitting optimization to MixedDataset — validate against real-world directory structures as well.

## 9. Implementation Notes

- Keep exception handling top-level intact ("must not crash" principle).
- Prefer feature flags or config toggles for new optimization knobs so A/B benchmarking remains easy.
- Continue emitting `benchmark-results.ndjson` and `benchmark-file-results.ndjson` for longitudinal comparison.
- All new parallelism code paths must preserve `CancellationToken` and `PauseToken` semantics.
- Staged temp file naming under concurrency must avoid collisions (append worker index or GUID to temp suffix).

## 10. Execution Tasklist

This section turns the strategy into implementation-ready tasks with ownership boundaries and measurable outcomes. **Phases are ordered by increasing complexity — do not start a phase before the previous one has been benchmarked.**

Phase B1 goes directly into Core (zero risk, no flags needed). All other benchmark exploration is done in `SmartCopy.Benchmarks` first; Core changes follow only after benchmark data validates a strategy.

### Phase B1 — Metadata Overhead Reduction (Core, no feature flag)

Goal:
- Eliminate redundant per-file filesystem metadata calls before measuring anything else. Zero write-path risk; establishes a clean baseline for all subsequent phases.

Code focus:
- `SmartCopy.Core/Pipeline/Steps/CopyStep.cs` — remove `ExistsAsync` for Overwrite mode
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — cache directory creation; remove `File.Exists` in `OpenReadAsync`

Tasks:
1. In `CopyStep.ApplyAsync`: for `OverwriteMode.Overwrite`, remove the `ExistsAsync` pre-check. The staged write creates or overwrites via atomic rename regardless of whether the destination already exists. Since we no longer know which happened, report `DestinationResult.Overwritten` in all Overwrite-mode cases — consistent with the user's stated intent.
2. In `LocalFileSystemProvider.WriteAsync`: add a `HashSet<string>` instance field (`_createdDirectories`) tracking directories already passed to `Directory.CreateDirectory`. Before each call, check the set; skip if present. Remove the entry on any `IOException` during directory creation so the next attempt retries.
3. In `LocalFileSystemProvider.OpenReadAsync`: remove the `File.Exists` pre-check. Let `FileStream` throw `FileNotFoundException` directly — it is already an `IOException` and is caught by `CopyStep.ApplyAsync`'s error handler. No observable behavior change.

Acceptance criteria:
- All existing unit tests pass unchanged.
- No overwrite-mode behavior changes observable by tests.
- Directory creation cache does not cause failures when directories are externally deleted (IOException invalidation covers this).

Benchmark: run the standard scenario matrix once after merging to establish the new B1 baseline. Compare against historical `BaselineAuto` runs.

### Phase B2 — Tiny-file Direct Write (benchmark first)

Goal:
- Isolate the cost of the staged temp file lifecycle (create + write + rename) for files ≤64 KiB.
- This is the simplest possible change to small-file copy behavior and establishes the baseline for all further optimisation.

**Step 1 — Benchmark variant (no Core changes):**

Code focus:
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — add `DirectWrite` variant
- `SmartCopy.Benchmarks/BenchmarkCopyRunner.cs` — new benchmark-only direct-write copy engine
- `SmartCopy.Benchmarks/Program.cs` — route `DirectWrite` variant through `BenchmarkCopyRunner`

The benchmark runner bypasses `LocalFileSystemProvider.WriteAsync` for files ≤64 KiB and calls `File.WriteAllBytesAsync` (or equivalent) directly on the resolved destination path, skipping staging. Large files continue through the existing pipeline unchanged.

Tasks:
1. Add `SmallFileMaxConcurrency: int?` to `BenchmarkVariant` and `BenchmarkScenario` (for later phases; defaults to 1 = sequential).
2. Implement `BenchmarkCopyRunner.RunAsync` with a `DirectWrite: bool` option.
3. Add `DirectWrite` variant to config template.
4. Run ≥5 SSDtoSSD runs of `DirectWrite` and `BaselineAuto`, randomized order.

Benchmark gate:
- Compare `DirectWrite` vs `BaselineAuto` on 0-64 KiB P50, P95, and job wall-clock time.
- If `DirectWrite` improves the 0-64 KiB bucket by ≥20%: proceed to Phase B2 Core integration.
- If improvement is <10%: staging is not the primary bottleneck. Proceed to Phase B3 (buffer batching) and reconsider Phase B2 priority.

**Step 2 — Core integration (only after benchmark gate passed):**

Code focus:
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — add `TinyFileFastPathThresholdBytes` option

Tasks:
1. Add `TinyFileFastPathThresholdBytes: long` to `LocalFileSystemProviderOptions` (default 0 = disabled).
2. In `LocalFileSystemProvider.WriteAsync`, when `fileSize <= TinyFileFastPathThresholdBytes`: open destination directly with `FileMode.Create`, write content, skip staging.
3. Document the trade-off explicitly: a write interrupted mid-stream leaves a partial file at the destination path rather than no file. Gate behind opt-in.
4. All existing tests must pass unchanged (default = disabled means no behavior change).

Acceptance criteria:
- Improved 0-64 KiB bucket P50/P95 matching the benchmark variant result.
- Files written via fast path are bit-identical to those written via staged path.
- No behavior change when option is disabled (default).

### Phase B3 — Buffered Read-Write Batching (benchmark first)

Goal:
- Test whether separating read I/O and write I/O into distinct phases reduces overhead, particularly on HDD and USB where seek thrash is a concern.

**Step 1 — Benchmark variants (no Core changes):**

Code focus:
- `SmartCopy.Benchmarks/BenchmarkCopyRunner.cs` — add `BufferBatchBytes` option
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — add `ReadBatch16MiB`, `DirectWriteBatch16MiB`, `DirectWriteBatch64MiB` variants

Implementation:
- Allocate a pooled buffer of `BufferBatchBytes` (e.g., 16 MiB).
- For each small file in scan order: read entire content into buffer at current offset; record `(node, offset, length)` metadata.
- When buffer fills (or a file is too large for the remaining buffer space): flush all buffered entries — write each to its destination path sequentially using either staged or direct write depending on variant config.
- Large files: stream directly without buffering (unchanged).
- Results for a batch are yielded together after the write phase completes.

Variants to add:
- `ReadBatch16MiB` — 16 MiB buffer, staged writes (isolates read/write phase separation from staging cost)
- `DirectWriteBatch16MiB` — 16 MiB buffer, direct write (B2 + B3 combined)
- `DirectWriteBatch64MiB` — 64 MiB buffer, direct write (tests buffer size sensitivity)

Tasks:
1. Extend `BenchmarkCopyRunner` with buffer batching logic.
2. Add variants to config template.
3. Run ≥5 SSDtoSSD runs per variant, randomized.
4. Run `DirectWriteBatch16MiB` on SSDtoHDD once to check for HDD regression before committing.

Benchmark gate:
- Compare `DirectWriteBatch16MiB` vs `DirectWrite` (B2 benchmark) vs `BaselineAuto`.
- If batching adds ≥10% improvement over direct-write-alone: proceed to Core integration.
- If `ReadBatch16MiB` ≈ `BaselineAuto`: the read/write phase separation itself is not the bottleneck; staging cost is; B2 is sufficient.

**Step 2 — Core integration (only after benchmark gate passed):**
- Design TBD based on benchmark results. Likely involves a new `IFileSystemProvider` API or a higher-level batching coordinator above the step layer.

### Phase A — Parallelism (only if B2-B3 prove insufficient)

Goal:
- Add concurrency to the copy path if sequential strategies do not reach the target improvement.

**Try phase-separated parallelism (Section 3.8) before mixed parallelism (Section 3.1).**

Phase-separated: N concurrent reads → wait → N concurrent writes. Keeps read and write I/O separated in time.
Mixed: N concurrent (read + write) pairs. Higher risk on HDD; test only on SSD first.

Acceptance criteria (for any parallelism variant):
- No regressions in copy correctness (`copiedFiles`, `failedFiles`, destination contents).
- No crashes or unhandled exceptions under cancellation.
- No regression on SSDtoHDD relative to `BaselineAuto`.
- USB must not be enabled for parallelism until all USB baseline variants complete (Section 5.2).

Benchmark gate:
- Minimum 5 SSDtoSSD runs per parallelism level, randomized order.
- Must compare against `DirectWriteBatch16MiB` (not just `BaselineAuto`) to isolate the concurrency contribution.

### Phase C — Progress Throttling and Destination-Sensitive Policy

Goal:
- Generalize gains while limiting regressions on HDD/USB/network destinations. Applies to both Tracks 1 and 2. HDD baseline data is available; USB policy remains provisional until the remaining USB variant runs complete.

Prerequisites:
- Complete SSDtoUSBFlash baselines with all 4 current variants (see Benchmark Gap, Section 5.2).
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
   - USB flash: keep conservative until measured. Preliminary BaselineAuto data splits wall time between tiny files and large streams, so USB policy must tune both small-file overhead and large-file throughput.
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
  3. SSDtoHDD
  4. SSDtoUSBFlash (must complete variant baselines before defaults are promoted)
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

Apply before each Core merge (benchmark-only changes do not require all of these):
1. `dotnet build` succeeds.
2. All unit tests pass (`dotnet test`).
3. Benchmark run metadata recorded with notes about cold/warm conditions.
4. Results appended to both:
   - `benchmark-results.ndjson`
   - `benchmark-file-results.ndjson`
5. Any architectural behavior changes reflected in `Docs/Architecture.md`.
6. New `LocalFileSystemProviderOptions` fields have safe defaults (0/disabled) so existing behavior is preserved with no options specified.
7. Small-file strategy changes validated on MixedDataset; buffer scaling changes validated on byte-volume-dominated dataset (Section 5.3).
8. Verify that total `copiedFiles` + `failedFiles` + `skippedFiles` equals the expected total across all runs.
9. For any direct-write (non-staged) changes: spot-check file content bit-identity against staged baseline.
