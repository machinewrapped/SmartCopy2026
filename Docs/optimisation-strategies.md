# SmartCopy2026 — Optimisation Strategies

This document is the reference for copy performance optimisation: what we know, what we are testing next, and how benchmark evidence should be interpreted.

The end goal is not a single universally "best" copy strategy. The target is an adaptive routing policy: choose the safest and fastest practical strategy for each file-size range, then validate the whole policy against realistic datasets and destination types.

## 1. The Core Tension

Large files need staged writes and granular progress for integrity and UX. Small files have the opposite problem: their content is trivially small, so per-file *ceremony* — metadata checks, stream setup, temp file create, rename, progress events — dominates wall time in a way that byte throughput never will.

There is no single best strategy. The architecture must be *adaptive*, routing each file through an appropriate code path based on its size — which the pipeline already knows from enumeration before the first byte is copied.

The MixedDataset was designed to expose both ends of this spectrum simultaneously: 72.9% of files are under 64 KiB (1.7% of bytes), while 8 files (<0.1%) exceed 256 MiB (33.4% of bytes).

An important caveat: Every file write incurs unavoidable filesystem work — MFT updates, journal entries, directory allocation table changes — that happens regardless of how the application is structured. Until the strategies below have been benchmarked, we do not know how much of the per-file overhead is reducible in practice. It may turn out that application-level overhead is a small fraction of the total, and the irreducible filesystem floor is the real constraint. The phases are designed to test this empirically rather than assume the answer.

## 2. Benchmark Findings

557,280 per-file records from 18,576 files × 4 variants × 2 runs across four scenarios (SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash).

The four tested variants differ only in byte-copy mechanics:

| Variant | Buffer | Write Mode | ArrayPool | Preallocate |
|---|---|---|---|---|
| `BaselineAuto` | 4 KiB (default) | Auto | default | no |
| `CopyToAsync512KiB` | 512 KiB | CopyToAsync | default | no |
| `ManualLoop512KiBArrayPool` | 512 KiB | ManualLoop | yes | no |
| `ManualLoop1MiBPreallocate` | 1 MiB | ManualLoop | yes | yes |

### 2.1 The Small-File Problem

The 0–64 KiB bucket contains 72.9% of files but only 1.7% of bytes, yet it dominates wall time on every destination except USB:

| Destination | 0–64 KiB % wall time | 256 MiB–2 GiB % wall time |
|---|---:|---:|
| SSDtoSSD | 56.4% | 7.4% |
| SameDriveTest | 51.5% | 10.9% |
| SSDtoHDD | 56.7% | 7.2% |
| SSDtoUSBFlash | 24.7% | 27.3% |

On SSD and HDD, 1.7% of bytes consumes ~57% of wall time. A 4× improvement on the 0–64 KiB bucket saves ~92 seconds on SSDtoSSD. A 20% improvement to large-file throughput saves ~3 seconds. The small-file bucket is simply where almost all the time is.

On USB flash, the split between smallest and largest files is roughly equal. Optimising tiny files alone cannot make USB fast — large-file streaming must also be addressed.

### 2.2 Variant Spread — The Critical Finding

For 0–64 KiB files, all four variants produce essentially identical throughput:

| Destination | Best variant | Worst variant | Spread |
|---|---:|---:|---:|
| SSDtoSSD | 2.19 MiB/s | 1.91 MiB/s | 13% |
| SSDtoHDD | 0.93 MiB/s | 0.89 MiB/s | 4% |
| SSDtoUSBFlash | 0.48 MiB/s | 0.43 MiB/s | 10% |

**~97% of small-file copy time is not spent on byte I/O.** The time is somewhere in the per-file overhead chain — `ExistsAsync`, `File.Exists`, `Directory.CreateDirectory`, stream open/close, staged temp file lifecycle, progress events, the sequential `await foreach` pump, and irreducible filesystem work (MFT updates, journalling) — but the variants do not isolate which part dominates. Tuning buffer sizes or write modes changes nothing; the phases below are designed to find out what does.

For files >4 MiB, `ManualLoop1MiBPreallocate` leads by 10–17% on SSD. On HDD the advantage is modest (~5%). On USB the signal is noisy (only 8 files in the 256 MiB–2 GiB bucket).

### 2.3 Per-Scenario Throughput

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

**SSDtoUSBFlash** (averaged across all variants; essentially complete — see Section 6):

| Size Bucket | Avg MiB/s | Est. Wall Time | % Wall Time |
|---|---:|---:|---:|
| 0–64 KiB | 0.45 | 9m 25s | 24.7% |
| 64–512 KiB | 2.75 | 3m 5s | 8.1% |
| 512 KiB–4 MiB | 7.44 | 4m 35s | 12.0% |
| 4–32 MiB | 11.36 | 4m 31s | 11.9% |
| 32–256 MiB | 11.48 | 6m 5s | 16.0% |
| 256 MiB–2 GiB | 8.12 | 10m 24s | 27.3% |

## 3. Design Invariants

These apply to every phase and every strategy. They are not trade-offs.

1. **No crashes.** All operations must catch exceptions at the top level and surface them through the log/UI. Never let an exception propagate unhandled.
2. **Cooperative cancellation.** Every code path must honour `CancellationToken` and `PauseToken`. No blocking operations that last more than a few seconds.


## 3.1 UX Guidelines (Not Invariants)

These improve user experience but are subject to benchmarking and user preference:

**Best-effort file integrity on interruption.** Every reasonable effort should be made to ensure that a file is copied in its entirety, as partially copied files can subtly corrupt user data. Staged writes provide this by default; direct writes must implement cleanup logic where feasible (e.g., delete partial destination file on stream error). Edge case: if the destination device is disconnected mid-operation, integrity is limited by hardware behaviour and is not a constraint on application design.

**Directory cohesion:** Interrupted runs ideally complete full directories before stopping rather than scattering files across multiple directories. This improves resume semantics. Benchmark Phase 3 to measure the cost; provide a user setting to control the behaviour (e.g., batch size, or `CoherenceMode: None | PerDirectory | Adaptive`).

## 4. Adaptive Routing And Policy Discovery

The pipeline knows every file's size before execution begins. The target architecture routes each file through an appropriate code path based on size, destination profile, overwrite mode, and provider capabilities rather than using a single strategy for all files, e.g.

| File Size | Code Path |
|---|---|
| ≤T (tiny) | No staging, buffered read/write batching |
| T–S | Staging, buffered read/write batching |
| S–M | Staging, unbuffered, throttled progress updates |
| >M | Staged write, granular progress |

This should be a routing function operating on already-known data (`node.Size`, `overwriteMode`, `providerCapabilities`) — no I/O in the hot path.

Benchmark variants are discovery tools, not necessarily final product configurations. Early phases should identify which strategy wins per file-size bucket. Later phases should compose those bucket-level findings into candidate policies and validate those policies against whole-workload wall-clock time.

Evidence flow:
1. **Discovery:** run strategy variants on fine-grained size buckets.
2. **Bucket recommendation:** identify the best-supported strategy for each size range.
3. **Policy construction:** convert recommendations into a small routing table.
4. **Policy validation:** benchmark the whole policy on MixedDataset and destination-specific scenarios.
5. **Default promotion:** only promote defaults after whole-policy validation passes.

## 5. Phases

Ordered by **increasing complexity and risk**. Do not begin a phase before the previous one has been benchmarked and a gate decision made. Phase 6 (buffer scaling) is independent and can run alongside Phases 4–5. Phase 7 (parallelism) is the last resort — it is listed last because it should be tried last.

---

### Phase 1 — Metadata Overhead Reduction

**Status:** Essentially complete; pending validation. Core changes implemented but not yet benchmarked against historical baseline.

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
- **`LocalFileSystemProvider.WriteAsync`** — Track created directories and avoid redundant calls to create the same directory again. invalidate the entry on `IOException` so the next attempt retries.
- **`LocalFileSystemProvider.WriteAsync`** A directory that was just created by definition contains no files so we can skip the Exists check on the target for files in a directory we just created.

**Acceptance criteria:**
- All existing unit tests pass unchanged.
- Directory cache does not cause failures when directories are deleted externally.
- Benchmark: run the standard scenario matrix after merging and compare against historical `BaselineAuto` runs to establish the Phase 1 baseline.

---

### Phase 2 — Tiny-File Direct Write
 
**Status:** Implemented and benchmarked as a discovery mechanism. `LocalFileSystemProviderOptions.TinyFileFastPathThresholdBytes` exists and defaults to `0` (disabled). The benchmark sweep showed that removing the overwrite existence check produced differences smaller than run-to-run variance, so the `ExistsCheckOff` variants have been retired from the active matrix.
 
**Goal:** Measure the cost of the staged temp-file lifecycle (create + write + rename) for small files by bypassing it entirely, and find the file-size threshold below which direct write is a meaningful win.

**Phase 2 prerequisite:** See Section 6.2 for the dataset specification and tooling requirements. These are implemented for current benchmark runs.

**Integrity trade-off:** Staged writes ensure an interrupted write does not leave a partial file at the destination. For very small files the write may be effectively atomic at the OS or firmware level (writes are per-sector or per-block), so staging may be overhead with no integrity benefit. Benchmarks are needed to weigh the trade-off; the implementation should expose a threshold setting rather than hardcode one.

Direct write *without staging* trades durability for speed: a power loss or unplug mid-write leaves a partial file. **Mitigation:** On stream error (not unplug), delete the partial destination file to restore integrity. If the destination device is unplugged mid-operation, partial files are an unavoidable consequence of direct write — this is the explicit trade-off. Users can opt-in to direct write knowing this risk, or remain on staged (safer) defaults.

**Step 1 — Benchmark matrix (no Core behavioural change by default):**

Add a threshold sweep to `BenchmarkCopyRunner`. Each variant applies direct write via `File.WriteAllBytesAsync` for files below its threshold; files at or above use the existing staged path unchanged.

Overwrite existence-check removal was measured and retired from the active benchmark matrix because the observed differences were smaller than benchmark variance. Keep the normal existence-check path as the active control unless future evidence shows a stable signal.

| Variant | Direct-write threshold |
|---|---|
| `DirectWrite4KiB` | ≤4 KiB |
| `DirectWrite16KiB` | ≤16 KiB |
| `DirectWrite64KiB` | ≤64 KiB |
| `DirectWrite256KiB` | ≤256 KiB |
| `DirectWrite512KiB` | ≤512 KiB |
| `DirectWrite1MiB` | ≤1 MiB |
| `DirectWrite4MiB` | ≤4 MiB |

Files to modify:
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — add threshold variants
- `SmartCopy.Benchmarks/BenchmarkCopyRunner.cs` — direct-write copy engine parameterised by threshold

Run per the standard protocol (Section 7.1) — SSDtoSSD first, `BaselineAuto` included as reference, randomised order. The sweep answers two questions: does direct write help at all, and at what threshold does staging overhead become negligible?

**Gate:**
- Any threshold shows ≥20% P50/P95 improvement over matched baseline within the same size bucket → keep it as a candidate for policy construction.
- No threshold shows ≥10% improvement → staging is not the primary bottleneck; proceed to Phase 3 and revisit Phase 2 priority.

**Step 2 — Core integration (after gate):**

Add `TinyFileFastPathThresholdBytes: long` to `LocalFileSystemProviderOptions` (default 0 = disabled). In `LocalFileSystemProvider.WriteAsync`, when `fileSize ≤ threshold`, open destination with `FileMode.Create` and write directly; skip staging.

Acceptance criteria: improved P50/P95 for files below threshold matching benchmark variant; files bit-identical to staged baseline; no behaviour change when disabled.

---

### Phase 3 — Buffered Read-Write Batching

**Status:** Benchmark tooling is implemented in `SmartCopy.Benchmarks`; discovery and analysis are in progress. Do not integrate into Core until bucket-level evidence identifies where batching helps and whole-policy validation confirms it improves realistic workloads.

**Goal:** Test whether separating read I/O and write I/O into distinct phases reduces overhead for small files, and find the batch-buffer size at which gains plateau.

The current model interleaves read and write on every file, alternating I/O direction continuously. Batching accumulates multiple small files into a memory buffer during a read phase, then drains it during a write phase:

```
Current:  Read f₁ → Write f₁ → Read f₂ → Write f₂ → ...
Batched:  Read f₁ → Read f₂ → ... → Read fₙ  [buffer fills]
          Write f₁ → Write f₂ → ... → Write fₙ  [buffer drains]
```

This is likely to be especially beneficial on same-drive copies (reduces seek/head reversal) and may allow device-level buffering to operate more efficiently.

**Coherence benefit:** Batching can optionally be constrained to a single directory to improve resume semantics — interrupted runs complete full directories rather than scattering files. This is a UX guideline, not a hard constraint; Phase 3 benchmarking will determine whether the coherence cost is acceptable and whether it should be user-configurable.

**Memory:** Buffer is pool-allocated. Progress events are emitted per batch rather than per file.

**Core batching rule:** a file is batched only if it fits in an empty batch buffer. If a file fits the configured buffer but not the remaining space, flush the current batch before reading it. Files larger than the configured batch buffer use the normal unbatched path. This prevents a small buffer variant from accidentally benchmarking an impossible buffer layout.

**Benchmark variants (no Core changes):**

Run matched staged/direct pairs across buffer sizes to find where gains plateau or reverse. Direct variants use the active direct-write threshold under test; current Phase 3 scenario files include the extended 4 MiB threshold so the 1-4 MiB tail can participate in policy discovery.

Run matched pairs at every buffer size. `DirectWriteBatch{N} − StagedWriteBatch{N}` estimates the direct-write contribution at that buffer size; `StagedWriteBatch{N} − Control_BaselineAuto` estimates phase separation alone. `DirectWriteBatch{N} − UnbatchedDirectWrite{T}` estimates the incremental value of batching over direct write alone. Without matched controls, a plateau in the `DirectWriteBatch` sweep cannot be attributed to either factor.

| Variant | Batch buffer | Write mode |
|---|---|---|
| `Control_BaselineAuto` | none | staged |
| `UnbatchedDirectWrite{T}` | none | direct |
| `StagedWriteBatch1MiB` | 1 MiB | staged |
| `DirectWriteBatch1MiB` | 1 MiB | direct |
| `StagedWriteBatch4MiB` | 4 MiB | staged |
| `DirectWriteBatch4MiB` | 4 MiB | direct |
| `StagedWriteBatch16MiB` | 16 MiB | staged |
| `DirectWriteBatch16MiB` | 16 MiB | direct |
| `StagedWriteBatch64MiB` | 64 MiB | staged |
| `DirectWriteBatch64MiB` | 64 MiB | direct |

Files to modify:
- `SmartCopy.Benchmarks/BenchmarkCopyRunner.cs` — `BufferBatchBytes` support and batch-fit rule implemented
- `SmartCopy.Benchmarks/BenchmarkModels.cs` — `BufferBatchBytes` on scenarios, variants, and run records
- `benchmark-scenarios-phase3.json` / `benchmark-scenarios-consolidated.json` — active scenario matrices

Run per the standard protocol (Section 7.1), randomised order. Run SSDtoSSD first for fast feedback. Promote to SameDriveTest and SSDtoHDD only after the SSD results show a bucket-level signal that exceeds variance. USB remains a separate validation target.

**Gate:**
- A bucket-level strategy recommendation is only valid when its improvement over matched controls exceeds run-to-run variance.
- A whole-policy candidate is only valid when MixedDataset wall-clock `executeDuration` improves beyond variance with no correctness regressions.
- All `StagedWriteBatch*` ≈ `Control_BaselineAuto` → phase separation alone does not produce a useful signal; any direct batch gain is probably from direct write rather than batching.
- Best `DirectWriteBatch*` ≈ `UnbatchedDirectWrite{T}` → batching does not add enough value to justify Core complexity.

**Step 2 — Core integration (after gate):** Design TBD from benchmark results. Likely a new batching coordinator above the step layer.

---

### Phase 4 — Progress Throttling

**Status:** After Phases 1–3.

**Goal:** Reduce per-file progress-reporting overhead during tiny-file bursts.

PipelineRunner emits per-file progress for every file. For 13,000+ tiny files copying in under 1 ms each, this is a meaningful overhead source and a source of UI thread pressure.

**Changes:**
- Batch file-completion progress events: emit at most once per 100 ms or per 50 files, whichever comes first.
- Byte-level progress (~10 Hz cap) already exists; no change needed there.

Files to modify: `SmartCopy.Core/Pipeline/PipelineRunner.cs`

Acceptance criteria: progress UX remains clear and responsive during tiny-file bursts; no perceived regression in granularity for large files.

---

### Phase 5 — Destination-Sensitive Defaults

**Status:** After Phases 1–3. Requires the byte-volume-dominated dataset to be baselined before USB large-file defaults are promoted.

**Goal:** Apply appropriate strategy defaults per destination type so gains do not regress on HDD or USB.

Detect SSD vs HDD vs USB via `DriveInfo` or platform APIs. Implement as a `DestinationProfile` enum and translate into strategy parameters (buffer size, staging threshold, progress throttle rate). Do not hardcode values — allow override via `LocalFileSystemProviderOptions`.

Destination-specific notes:
- **HDD:** conservative defaults; baseline data exists. Small-file strategies transfer directly; no concurrency until Phase 7.
- **USB:** wall time splits roughly equally between the 0–64 KiB and 256 MiB–2 GiB buckets. Both small-file overhead and large-file streaming must be addressed. Large-file USB buffer defaults remain provisional until validated against the byte-volume-dominated dataset.
- **SameDrive:** do not collapse into SSDtoSSD. Small files behave similarly; large files are contention-limited.

Files to modify: `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`, `ProviderCapabilities`

Benchmark gate: run the full scenario matrix (SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash). Promote defaults only after all scenarios pass.

---

### Phase 6 — Buffer Size Scaling *(independent track)*

**Status:** Can run alongside Phases 4–5. Requires the byte-volume-dominated dataset (see Section 6).

**Goal:** Find the optimal buffer size for large-file streaming. The MixedDataset cannot isolate this — buffer changes are lost in the noise of 13,000 tiny files.

**Step 1 — Buffer size sweep:**

Run `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB`, `ManualLoop8MiB` — all with `ManualLoop` mode, `PreallocateDestinationFile = false` — against the byte-volume-dominated dataset on SSDtoSSD, per the standard protocol (Section 7.1), randomised order. Measure throughput at each buffer size for buckets 4 MiB+.

Gate: buffer >1 MiB shows ≥5% P50 improvement over 1 MiB → promote as new large-file default.

**Step 2 — Preallocation:**

Run the best buffer size from Step 1 with and without `PreallocateDestinationFile` to isolate its contribution independently (previously confounded in `ManualLoop1MiBPreallocate`), per the standard protocol (Section 7.1), randomised.

Gate: <3% benefit → disable preallocation by default to avoid unnecessary `SetLength` calls.

**Retire:** `CopyToAsync512KiB` and `ManualLoop512KiBArrayPool` provide no unique signal beyond the Phase 6 sweep variants and the existing baseline. Remove from future runs.

---

### Phase 7 — Parallelism *(last resort)*

**Status:** Only if Phases 2–3 prove insufficient. Do not begin until both Phase 2 and Phase 3 benchmark gates have been evaluated and neither alone achieved the target improvement.

**Goal:** Add concurrency to the copy path. This is the most complex option — it complicates cancellation semantics, progress reporting, and error handling in ways that no sequential strategy does. It is listed last because it should be tried last.

Try phase-separated parallelism first (N concurrent reads → wait → N concurrent writes) before mixed parallelism (N concurrent read+write pairs). Phase-separated keeps read and write I/O separated in time, reducing bus contention and HDD seek-thrash risk.

Starting configuration to test: `smallFileMaxConcurrency = 4`, then 6 and 8.

**Benchmark gate:**
- Run per the standard protocol (Section 7.1), randomised order. Parallelism variants are likely to exhibit higher variance — expect the 3rd-run threshold to trip more often.
- Must compare against the best Phase 3 batch variant — not `BaselineAuto` — to isolate the concurrency contribution.
- Must not regress on SSDtoHDD.
- USB: parallelism is entirely unmeasured on USB flash; do not enable USB parallelism defaults without dedicated measurement.

---

## 6. Benchmark Status

### 6.1 Current Coverage

All four variants have two successful runs on SSDtoSSD, SameDriveTest, and SSDtoHDD. The USB baseline is essentially complete: BaselineAuto and CopyToAsync512KiB have two USB runs each; ManualLoop512KiBArrayPool and ManualLoop1MiBPreallocate have one run each. The second ManualLoop USB runs are pending but results are consistent and unlikely to shift any conclusions.

These four variants establish the Phase 1 baseline only.

Phase 2 direct-write discovery has since been run using `SmallFileDataset`. The active lesson is that direct write is worth keeping as a policy candidate, while `ExistsCheckOff` is retired because its measured effect was smaller than run-to-run variance. Treat precise Phase 2 summary numbers as provisional unless backed by the current benchmark artifacts and variance checks.

Phase 3 batching support and scenario configs are now present in the benchmark suite. The next task is to collect and analyze Phase 3 data with enough structure to identify bucket-level strategy recommendations, not just an overall "winner."

### 6.2 Small-File Granular Dataset

**Status:** Required for Phase 2/3 discovery. Tooling exists and `SmallFileDataset` has been used for Phase 2. Keep using it for per-bucket strategy discovery.

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

---

### 6.3 Pending: Byte-Volume-Dominated Dataset

**Required before Phases 5 and 6 can be completed.**

The MixedDataset cannot isolate large-file throughput — tiny-file overhead dominates every aggregate metric. A second dataset is needed where large files dominate by byte volume:

- Example: 100 files × 100 MiB = 10 GiB total
- Or: 50–100 files in the 64 MiB–2 GiB range from `CandidateData`

Run at minimum `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB` on SSDtoSSD, ≥3 runs per variant, randomised. This validates that buffer scaling and preallocation produce measurable gains in real-world large-file scenarios before promoting any defaults.

### 6.4 Pending: Policy Validation Datasets

After bucket-level discovery identifies promising routing rules, benchmark complete policies against larger and more realistic datasets:

- **MixedDataset:** realistic file-count distribution and directory structure; primary validation for small-file policy value.
- **Byte-volume-dominated dataset:** large-file throughput and buffer-size policy validation.
- **Destination matrix:** SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash. SameDrive and USB must not inherit SSD defaults without evidence.

Policy validation uses whole-run `executeDuration` as the primary metric. Bucket metrics explain why a policy works or fails; they do not by themselves prove the policy should ship.

## 7. Operational Reference

### 7.1 Running Benchmarks

```bash
dotnet run --project .\SmartCopy.Benchmarks          # run suite
dotnet run --project .\SmartCopy.Benchmarks --mode analyze  # analyse results
```

Per-iteration protocol:
1. Run SSDtoSSD first — fastest feedback loop.
2. **Run count:** 2 runs per variant, randomised order. Add a 3rd run if the spread between the 2 runs exceeds 10% on the primary metric (wall-clock `executeDuration`). Start with 10% as the threshold; adjust if it trips too often or not enough as experience accumulates.
3. Promote to broader scenarios (SameDriveTest → SSDtoHDD → SSDtoUSBFlash) only after consistent SSD improvement with no correctness regressions.

### 7.2 Metrics

Use different metrics for different questions. Do not collapse discovery and validation into one headline number.

#### 7.2.1 Whole-Policy Validation Metrics

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

#### 7.2.2 Bucket-Level Strategy Discovery Metrics

Use these when deciding which strategy belongs to which file-size range:

- File count, byte count, `% files`, `% bytes`.
- Mean, median, and P95 per-file copy duration.
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

#### 7.2.3 Analysis Output Requirements

Reports should separate measurement from interpretation:

1. **Run-level table:** successful run count, median/mean/min/max `executeDuration`, spread, delta versus matched baseline, verdict.
2. **Bucket strategy table:** best observed strategy per bucket, delta versus matched control, variance status, recommendation.
3. **Missing-control warnings:** list variants that cannot support causal conclusions.
4. **Policy candidate summary:** proposed routing table built from bucket evidence.
5. **Policy validation summary:** whole-run result for candidate policies on MixedDataset and destination matrix.

Use mechanical verdict language:
- `PASS`: exceeds gate and variance.
- `INCONCLUSIVE`: delta is within variance or matched control is missing.
- `FAIL`: below required improvement.
- `REGRESSION`: slower beyond variance.
- `INVALID`: correctness or run-integrity problem.

**Operational sanity:** CPU usage trend during tiny-file phases; per-file `ExecutionDuration` distribution (flag outliers >1 s).

### 7.3 Things to Avoid

- Re-architecting the full pipeline model to chase a performance hypothesis.
- Sacrificing progress visibility to chase raw throughput.
- Destination-specific hardcoding without a capability-based fallback.
- Beginning Phase 7 (parallelism) before Phases 2 and 3 have been benchmarked and shown insufficient. Parallelism adds correctness complexity — cancellation, progress semantics, collision avoidance — that sequential strategies avoid entirely.
- Over-fitting to MixedDataset. Validate strategies against the byte-volume-dominated dataset and real-world directory structures before promoting defaults.
- Promoting USB parallelism defaults without dedicated measurement. The USB baseline covers buffer/write-mode variants only; concurrency behaviour on USB flash is entirely unknown.

### 7.4 Pre-Merge Checklist

Apply before each Core merge. Benchmark-only changes (no Core code) do not require all of these.

1. `dotnet build` succeeds.
2. All unit tests pass (`dotnet test`).
3. Benchmark run metadata recorded with cold/warm condition notes.
4. Results appended to `benchmark-results.ndjson` and `benchmark-file-results.ndjson`.
5. Architectural changes reflected in `Docs/Architecture.md`.
6. New `LocalFileSystemProviderOptions` fields default to 0/disabled — no behaviour change without explicit opt-in.
7. Small-file strategy changes validated on SmallFileDataset first; real-world validation pass on MixedDataset before promoting defaults. Buffer scaling changes validated on byte-volume-dominated dataset.
8. `copiedFiles + failedFiles + skippedFiles` equals the expected total across all runs.
9. Direct-write (non-staged) changes: spot-check file content bit-identity against staged baseline.

---

### 7.5 Phase 2 Execution Checklist

Ordered steps to completion. To continue: find the first unchecked item and execute it.

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
- [x] Apply gate (Section 5, Phase 2): identify which thresholds show useful bucket-level improvement over `Control_BaselineAuto`; record top candidates

**D — MixedDataset validation (top candidates only)**

- [x] In `benchmark-scenarios-phase2.json`: enable MixedDataset-SSDtoSSD validation scenarios for the top-3 candidates; set `Enabled: false` on all others
- [x] Run:
  ```
  dotnet run --project .\SmartCopy.Benchmarks --config benchmark-scenarios-phase2.json --scenario MixedDataset-SSDtoSSD
  ```
- [x] Analyze; confirm improvement holds on real-world directory structure
- [x] If confirmed: extend to SameDriveTest, then SSDtoHDD

**E — Gate decision**

- [x] Update Phase 2 status in Section 5 of this document
- [x] **Core integration complete:** add `TinyFileFastPathThresholdBytes` to `LocalFileSystemProviderOptions`, integrate in `LocalFileSystemProvider.WriteAsync`, default disabled
- [ ] Re-run analysis under the stricter Section 7.2 rules and record bucket-level recommendations without unsupported causal claims

### 7.6 Phase 3 Execution Checklist

Ordered steps to completion. To continue: find the first unchecked item and execute it.

**A — Tooling**

- [x] Add `BufferBatchBytes` to benchmark scenario/variant/run models
- [x] Implement buffered read/write batching in `BenchmarkCopyRunner`
- [x] Enforce batch-fit rule: a file is batched only if it fits in the configured batch buffer
- [x] Add Phase 3 scenario configs for staged/direct batch pairs
- [x] Verify `dotnet build SmartCopy.Benchmarks/SmartCopy.Benchmarks.csproj`

**B — Discovery Benchmarks**

- [ ] Run `benchmark-scenarios-phase3.json` on SmallFileDataset × SSDtoSSD
- [ ] Re-run variants until each has at least 2 successful runs
- [ ] Add a 3rd run where run-level spread exceeds the variance threshold
- [ ] Analyze with bucket-level matched controls
- [ ] Produce bucket-level strategy recommendations

**C — Policy Validation**

- [ ] Convert bucket recommendations into one or more candidate routing policies
- [ ] Run candidate policies on MixedDataset × SSDtoSSD
- [ ] Promote to SameDriveTest and SSDtoHDD only after SSDtoSSD passes beyond variance
- [ ] Treat USB as a separate validation target; do not infer USB defaults from SSD/HDD
