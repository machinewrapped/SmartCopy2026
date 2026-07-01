# SmartCopy2026 — Phase 1: Metadata Overhead Reduction & Baseline

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

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

## First sweep — four-variant baseline

*The original broad sweep that framed the problem the later phases attack; its variant comparisons are superseded by the clean-room run in Phase 2/3.*

557,280 per-file records from 18,576 files × 4 variants × 2 runs across four scenarios (SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash). The four variants differ only in byte-copy mechanics:

| Variant | Buffer | Write Mode | ArrayPool | Preallocate |
|---|---|---|---|---|
| `BaselineAuto` | 4 KiB (default) | Auto | default | no |
| `CopyToAsync512KiB` | 512 KiB | CopyToAsync | default | no |
| `ManualLoop512KiBArrayPool` | 512 KiB | ManualLoop | yes | no |
| `ManualLoop1MiBPreallocate` | 1 MiB | ManualLoop | yes | yes |

## The small-file problem — and why byte-tuning can't fix it

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

## Per-scenario throughput (Phase 1 baseline)

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
