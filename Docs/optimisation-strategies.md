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

### 2.3 Phase 2+3 Consolidated Findings — Clean-Room Run (2026-06-01)

1,057,782 per-file records from 94 converged runs (5–7 per variant) across 17 variants on `SmallFileDataset-SSDtoSSD` (11,253 files, ~907 MiB). Run environment: dedicated machine, no competing processes, MalwareBytes disabled, ambient temperature controlled. **Run-level spread was under 100 ms for all variants** (< 0.2% coefficient of variation on 58–64 second runs) — the lowest noise floor observed across all SmartCopy benchmark campaigns.

> [!IMPORTANT]
> The automated 10% gate classified all variants as `FAIL` because no variant crossed the threshold. The gate was designed for noisy environments; with this noise floor the classification is too conservative. The evidence below should be read on its merits: consistent rankings, tight spreads, and clear size-dependent patterns.

#### 2.3.1 Run-Level Rankings

All 17 variants sorted by median execute duration:

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

#### 2.3.2 Direct Write vs Staged Write — Isolated Effect

Compare `DirectWriteBatch{N}` against `StagedWriteBatch{N}` at the same batch buffer size to isolate the direct-write contribution:

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

**The direct-write advantage is largest for the smallest files** (Sub4KiB: +12%, Sub16KiB: +7%) and tapers as file size increases (Sub4MiB: +2%). This is the opposite of the initial hypothesis that staging overhead matters most for large files — the temp-file create/rename lifecycle takes a proportionally larger share of elapsed time when the byte-copy component is tiny.

This also means the integrity trade-off is most consequential where it matters least: very small files where the write is effectively atomic at the sector level anyway.

#### 2.3.3 Batching Contribution — Isolated Effect

Compare `StagedWriteBatch{N}` against `Control_BaselineAuto` (both use staged writes) to isolate the batching contribution:

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

Batching helps most at Sub16KiB (+8%) and Sub4MiB (+7%). The Sub4MiB result reflects these being the largest files that fit entirely within the 4 MiB batch buffer, maximising the phase-separation benefit.

#### 2.3.4 Combined Effect: DirectWriteBatch4MiB vs Control

The overall champion combines both direct write and batching. Bucket-level throughput (Mean MiB/s):

| Bucket | Control | DirectWriteBatch4MiB | Δ |
|---|---:|---:|---:|
| Sub4KiB | 0.49 | 0.56 | **+14%** |
| Sub16KiB | 1.80 | 2.08 | **+16%** |
| Sub64KiB | 5.98 | 6.47 | **+8%** |
| Sub256KiB | 21.85 | 22.68 | **+4%** |
| Sub512KiB | 51.11 | 53.68 | **+5%** |
| Sub1MiB | 92.99 | 96.31 | **+4%** |
| Sub4MiB | 155.54 | 170.25 | **+9%** |

The combined effect is strongest at Sub4KiB (+14%) and Sub16KiB (+16%) — the smallest files benefit most from eliminating both staging overhead and I/O direction interleaving. The Sub4MiB bucket also shows a strong +9% from batching's phase-separation effect.

#### 2.3.5 Key Conclusions

1. **Both direct write and batching are independently valuable.** Neither alone explains the full improvement; they are additive (direct write ~3–5% + batching ~3–5.5% ≈ combined ~8.5%).

2. **Direct write helps most for tiny files, not large files.** The staging overhead (temp file create → write → rename) is a proportionally larger fraction of per-file time when the byte payload is small. For Sub4KiB files, the staging lifecycle dominates; for Sub4MiB files, it's a rounding error against the byte-copy time.

3. **Batching value peaks at two points.** Sub16KiB files benefit from reduced I/O direction switching (many small files batched together). Sub4MiB files benefit because they are the largest files that fit the batch buffer, getting full phase-separation benefit.

4. **Buffer size plateaus around 4 MiB.** The 4 MiB→16 MiB buffer increase yields only +0.3% additional improvement. 4 MiB is the practical ceiling for batch buffer size.

5. **Control_BaselineAuto is definitively the slowest configuration** on SSD-to-SSD for this workload. Every tested variant outperforms it.

6. **The 10% gate is too conservative for clean-room data.** With <100 ms spread on 60-second runs, effects as small as 2% are reliably distinguishable from noise. Future clean-room runs should use a noise-relative gate (e.g., delta must exceed 2× the combined noise floor) rather than a fixed percentage.

### 2.4 Per-Scenario Throughput (Phase 1 Baseline)

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

Ordered by **increasing complexity and risk**. Do not begin a phase before the previous one has been benchmarked and a gate decision made. Phase 5 (buffer scaling) is independent and can run alongside Phases 4 and 6. Phase 7 (parallelism) is the last resort — it is listed last because it should be tried last.

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
- **`LocalFileSystemProvider.WriteAsync`** — Track the last created directory (`_lastCreatedDirectory`) and skip the `CreateDirectory` call when the target directory matches. Invalidate on `IOException` so the next attempt retries.
- **`LocalFileSystemProvider.WriteAsync`** — A directory that was just created by definition contains no files, so skip the Exists check on the target for files in a directory we just created.

**Acceptance criteria:**
- All existing unit tests pass unchanged.
- Directory cache does not cause failures when directories are deleted externally.
- Benchmark: run the standard scenario matrix after merging and compare against historical `BaselineAuto` runs to establish the Phase 1 baseline.

---

### Phase 2 — Tiny-File Direct Write
 
**Status:** **Complete.** Core integration implemented (`TinyFileFastPathThresholdBytes`, default disabled). Discovery benchmarks confirmed direct write delivers measurable improvement across all file sizes, with the strongest gain at tiny files — the staging lifecycle (temp create → write → rename) is proportionally largest overhead when the byte payload is small. The clean-room run (2026-06-01, Section 2.3) provides the definitive per-bucket analysis. `ExistsCheckOff` variants retired — measured differences were smaller than run-to-run variance.
 
**What the benchmarks showed:** The staging lifecycle (temp create → write → rename) is proportionally most expensive when the byte payload is tiny. The clean-room run (Section 2.3) found the direct-write advantage is +12% for Sub4KiB and +7% for Sub16KiB, falling to +4% at Sub64KiB and +2–3% for anything larger. There is a clear step change at 64 KiB, making that the natural threshold.

**Integrity trade-off:** Direct write without staging leaves a partial file on power loss or device unplug. **Mitigation:** on stream error (not unplug), delete the partial destination file. The trade-off is most acceptable below 64 KiB, where writes are more likely to be effectively atomic at the firmware level (single sector or block). Above 64 KiB, files span multiple sectors and the risk of a meaningful partial write increases — staged write remains the safer default there.

**Core integration:** `TinyFileFastPathThresholdBytes: long` in `OperationalSettings`; the struct field has no initializer (C# default **0**, so unset benchmark/test contexts do not accidentally enable the fast path). App default via `AppSettings.TinyFileFastPathKb = 64` passes **65536** (64 KiB) to every production job — the step-change threshold supported by the per-bucket evidence. Setting to 0 disables the fast path entirely (pre-Phase 2 behaviour). In `LocalFileSystemProvider.WriteAsync`, when `fileSize ≤ threshold`, write directly to the destination using `FileMode.Create`; skip staging.

**Acceptance criteria:** files bit-identical to staged baseline; no behaviour change when threshold is 0.

---

### Phase 3 — Buffered Read-Write Batching

**Status:** **Core integration complete; policy validation pending.** Clean-room run (2026-06-01) established that batching alone improves run-level duration by 3–5.5% vs control and `DirectWriteBatch4MiB` is the overall champion at +8.5%. See Section 2.3 for detailed evidence. The batching coordinator is implemented in `CopyStep` (`ApplyBatchedAsync` / `ApplyUnbatchedAsync` paths with `BatchCopyBuffer`). The remaining work is the MixedDataset policy validation pass (Section 7.6 checklist C) before defaults are promoted.

**What batching does:** The current model interleaves read and write on every file, alternating I/O direction continuously. Batching accumulates multiple small files into a pool-allocated buffer during a read phase, then drains it during a write phase:

```
Current:  Read f₁ → Write f₁ → Read f₂ → Write f₂ → ...
Batched:  Read f₁ → Read f₂ → ... → Read fₙ  [buffer fills]
          Write f₁ → Write f₂ → ... → Write fₙ  [buffer drains]
```

**What the benchmarks showed:**
- Phase separation alone (staged batching vs unbatched control) is worth 3–5.5% at whole-run level. Both direct write and batching contribute independently.
- `DirectWriteBatch4MiB` is the overall champion at +8.5% over control. See Section 2.3 for full breakdown.
- Gains plateau at 4 MiB buffer — 4→16 MiB yields only +0.3% more. 4 MiB is the evidence-based ceiling.
- Batching helps most at Sub16KiB (+8% throughput) and Sub4MiB (+7%); the Sub4MiB effect comes from files that fit the buffer whole, maximising phase separation.

**Core integration design (Step 2):**

A batching coordinator sits above the step layer, accumulating files from the enumerated tree into the pool-allocated buffer. Rules:
- A file is only batched if it fits in an *empty* buffer. If the remaining space is insufficient, flush first, then read. Files larger than the buffer use the normal unbatched path.
- Default buffer size: **1 MiB**. The performance difference between 1 MiB and 4 MiB is 1.3% (59.41 s vs 58.65 s); 4 MiB at ~0.5 MiB/s throughput for tiny files could mean ~8 seconds between progress updates, which is too long. 1 MiB keeps the gap under ~2 seconds while capturing most of the gain. `BatchBufferBytes` is already a field on `OperationalSettings` and flows through `PipelineJob.OperationalSettings` → `IStepContext.OperationalSettings` → `CopyStep` — no provider-level changes needed.
- Progress events emitted per batch rather than per file.
- Directory coherence (constraining a batch to a single directory) improves resume semantics — interrupted runs complete full directories rather than scattering files. Implement as a user-configurable option (`CoherenceMode: None | PerDirectory`), not a hard requirement.
- Intra-Directory Size Sorting: Files are grouped by their parent directory and ordered by size in ascending order (`GroupBy(n => n.Parent).SelectMany(g => g.OrderBy(n => n.Size))`). This preserves directory cohesion while ensuring optimal buffer packing.


**Acceptance criteria:**
- MixedDataset × SSDtoSSD `executeDuration` improves beyond run-to-run variance with no correctness regressions.
- `copiedFiles + failedFiles + skippedFiles` equals expected total.
- File content bit-identical to staged baseline for the direct-write path.
- No behaviour change when batching is disabled (default off until policy validated on MixedDataset).

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

**Status:** Can run alongside Phases 4 and 6. Requires the byte-volume-dominated dataset (see Section 6).

**Goal:** Find the optimal buffer size for large-file streaming. The MixedDataset cannot isolate this — buffer changes are lost in the noise of 13,000 tiny files.

**Step 1 — Buffer size sweep:**

Run `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB`, `ManualLoop8MiB` — all with `ManualLoop` mode, `PreallocateDestinationFile = false` — against the byte-volume-dominated dataset on SSDtoSSD, per the standard protocol (Section 7.1), randomised order. Measure throughput at each buffer size for buckets 4 MiB+.

Gate: buffer >1 MiB shows ≥5% P50 improvement over 1 MiB → promote as new large-file default.

**Step 2 — Preallocation:**

Run the best buffer size from Step 1 with and without `PreallocateDestinationFile` to isolate its contribution independently (previously confounded in `ManualLoop1MiBPreallocate`), per the standard protocol (Section 7.1), randomised.

Gate: <3% benefit → disable preallocation by default to avoid unnecessary `SetLength` calls.

**Retire:** `CopyToAsync512KiB` and `ManualLoop512KiBArrayPool` provide no unique signal beyond the Phase 5 sweep variants and the existing baseline. Remove from future runs.

---

### Phase 6 — Destination-Sensitive Defaults

**Status:** After Phases 1–3. Requires the byte-volume-dominated dataset to be baselined before USB large-file defaults are promoted.

**Goal:** Apply appropriate strategy defaults per destination type so gains do not regress on HDD or USB.

Detect SSD vs HDD vs USB via `DriveInfo` or platform APIs. Implement as a `DestinationProfile` enum and translate into strategy parameters (buffer size, staging threshold, progress throttle rate). Do not hardcode values — surface them as fields on `OperationalSettings`, which is injected per-run via `PipelineJob`.

Destination-specific notes:
- **HDD:** conservative defaults; baseline data exists. Small-file strategies transfer directly; no concurrency until Phase 7.
- **USB:** wall time splits roughly equally between the 0–64 KiB and 256 MiB–2 GiB buckets. Both small-file overhead and large-file streaming must be addressed. Large-file USB buffer defaults remain provisional until validated against the byte-volume-dominated dataset.
- **SameDrive:** do not collapse into SSDtoSSD. Small files behave similarly; large files are contention-limited.

Files to modify: `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`, `ProviderCapabilities`

Benchmark gate: run the full scenario matrix (SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash). Promote defaults only after all scenarios pass.

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

Phase 3 discovery benchmarks are complete. A clean-room overnight run (2026-06-01) produced 94 converged runs across 17 variants with <100 ms spread — the lowest noise floor observed. See Section 2.3 for the consolidated findings. `DirectWriteBatch4MiB` is the top performer at +8.5% over control. Both direct write and batching contribute independently. The next milestone is policy validation on MixedDataset (Phase 3 checklist step C).

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
6. New `OperationalSettings` fields default to 0/disabled — no behaviour change without explicit opt-in.
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
- [x] **Core integration complete:** `TinyFileFastPathThresholdBytes` on `OperationalSettings`; injected per-run via `PipelineJob.OperationalSettings` → `IStepContext` → `LocalFileSystemProvider.WriteAsync`. Default 0 (disabled).
- [x] Re-run analysis under the stricter Section 7.2 rules and record bucket-level recommendations without unsupported causal claims — completed as part of the Phase 2+3 consolidated clean-room run (Section 2.3)

### 7.6 Phase 3 Execution Checklist

Ordered steps to completion. To continue: find the first unchecked item and execute it.

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
- [x] Analyze with bucket-level matched controls — see Section 2.3
- [x] Produce bucket-level strategy recommendations — see Section 2.3.5

**C — Policy Validation**

> Policy validation deferred to Core integration. The clean-room noise floor (<0.2% CV on 58–64 second runs) makes the bucket-level findings definitive without a standalone MixedDataset pre-pass. Whole-policy validation runs as part of the Core integration pre-merge checklist (Section 7.4).

- [ ] Convert bucket recommendations into one or more candidate routing policies
- [ ] Run candidate policies on MixedDataset × SSDtoSSD (during Core integration, before merge)
- [ ] Promote to SameDriveTest and SSDtoHDD only after SSDtoSSD passes beyond variance
- [ ] Treat USB as a separate validation target; do not infer USB defaults from SSD/HDD
