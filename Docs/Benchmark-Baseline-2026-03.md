# SmartCopy2026 Benchmark Baseline - March 2026

**Dataset:** `R:\TestData\MP3`  
**Benchmark runs captured:** 2026-03-19 through 2026-03-22  
**Runner:** `SmartCopy.Benchmarks`

## Purpose

This document records the first real-disk benchmark baseline for SmartCopy2026. The goal was not to compare alternative implementations yet; it was to verify that the benchmark harness works, capture representative timings on several destination types, and establish a baseline for future `LocalFileSystemProvider.WriteAsync()` experiments.

## What Was Tested

Each benchmark run used the same workflow:

1. Scan the source tree with `DirectoryScanner`
2. Select all scanned files
3. Preview a single-step pipeline containing `CopyStep`
4. Execute that `CopyStep`
5. Write an operation journal and append a result row to `benchmark-results.ndjson`

Configuration and execution details:

- Source path: `R:\TestData\MP3`
- Hidden files: excluded (`includeHidden: false`)
- Scan mode: full prescan enabled, lazy expansion disabled, symlink following disabled
- Pipeline: one `CopyStep`
- Overwrite mode: `Always`
- Destination handling: destination contents cleared before each run
- Providers: source and destination both exercised through `LocalFileSystemProvider`

The benchmark scenarios were:

| Scenario | Destination | Intent |
|---|---|---|
| `SameDriveTest` | `R:\TestData\SameDriveTest` | Copy on the same drive as the source |
| `SSDtoSSD` | `D:\TestData\SSDtoSSD` | Copy from source drive to another SSD |
| `SSDtoHDD` | `L:\TestData\SSDtoHDD` | Copy from source drive to HDD |
| `SSDtoUSBFlash` | `T:\TestData\SSDtoUSBFlash` | Copy from source drive to USB flash storage |

## Results

All four runs completed successfully. There were no preview warnings, skipped files, or failed files.

| Scenario | Run date (UTC) | Files | Data | Scan | Preview | Execute | Derived throughput |
|---|---|---:|---:|---:|---:|---:|---:|
| `SameDriveTest` | 2026-03-19 19:53 | 2049 | 22.67 GiB | 0.139 s | 0.077 s | 2m 02.655s | 189.27 MiB/s |
| `SSDtoSSD` | 2026-03-20 18:48 | 2052 | 22.67 GiB | 0.265 s | 0.080 s | 1m 33.398s | 248.57 MiB/s |
| `SSDtoHDD` | 2026-03-21 23:25 | 2053 | 22.67 GiB | 1.188 s | 0.087 s | 3m 07.411s | 123.88 MiB/s |
| `SSDtoUSBFlash` | 2026-03-22 13:06 | 2054 | 22.67 GiB | 0.301 s | 0.131 s | 49m 46.566s | 7.77 MiB/s |

### Immediate read of the data

- `SSDtoSSD` was the fastest recorded destination at 248.57 MiB/s.
- Same-drive copying was slower than SSD-to-SSD, likely due to read/write contention on the same source device.
- HDD throughput dropped to roughly half of SSD-to-SSD.
- USB flash was dramatically slower than every other target and dominates total wall-clock time.
- Scan and preview times were small compared with execute time in every scenario, so this dataset is currently dominated by write throughput rather than scan/planning overhead.

## Limits Of This Baseline

This baseline is useful, but it is not yet a controlled A/B performance comparison.

- No pre-change benchmark was captured before the recent `WriteAsync()` changes, so this document cannot claim an improvement or regression.
- Only one successful run was recorded per scenario.
- The source dataset changed between runs: file count increased from 2049 to 2054 and total bytes changed slightly between 2026-03-19 and 2026-03-22.
- Runs were performed across multiple days and two runtime patch versions (`.NET 10.0.4` and `.NET 10.0.5`).
- No explicit warm-cache versus cold-cache protocol was recorded.
- The benchmark runner originally wrote journals under the benchmark working directory. When the working directory was the source dataset, each run added a small amount of extra source data for later runs.

Because of those factors, the numbers should be treated as a baseline for future comparisons, not as proof that one implementation strategy is better than another.

## Current `WriteAsync()` Strategy

`LocalFileSystemProvider.WriteAsync()` currently uses:

- `FileStream` with async I/O enabled
- `FileOptions.SequentialScan`
- A 256 KiB buffer
- A fast path for small seekable files (`<= 10 MiB`) that delegates to `Stream.CopyToAsync()`
- A manual buffered loop for larger or unseekable streams so per-chunk progress can be reported

This is a reasonable default strategy. The current benchmark data does not yet isolate whether it is better than earlier versions because there is no before/after comparison.

## Candidate Follow-Up Experiments

If future work focuses on `LocalFileSystemProvider.WriteAsync()`, the next step should be to introduce implementation variants behind a simple local toggle so the same scenario can be rerun against controlled alternatives.

Candidate experiments:

1. Compare buffer sizes, for example 128 KiB, 256 KiB, 512 KiB, and 1 MiB.
2. Test `ArrayPool<byte>` for the large-file manual loop to reduce per-file buffer allocation churn.
3. Test a broader `CopyToAsync()` path when progress reporting is not required, since the pipeline often passes `progress: null`.
4. Test file preallocation when the remaining source length is known, to see whether fragmentation/allocation overhead drops on HDD or flash targets.
5. Measure whether the small-file threshold should move up, down, or be removed entirely.
6. Capture repeated runs per scenario with stable source contents so variance can be separated from genuine implementation changes.

## Runner Follow-Up

After this baseline was captured, the benchmark runner was updated so that:

- Benchmark artifacts default to a separate artifact directory when the working directory overlaps the source dataset.
- Each scenario can now override `LocalFileSystemProvider` copy buffer size and small-file threshold, making controlled chunk-size experiments possible without editing production constants.
- The runner now also supports `--mode dataset-prep`, which incrementally builds a benchmark dataset from one source path at a time using a durable manifest and size buckets.

### Dataset prep workflow

The dataset-prep mode is meant for building a more representative benchmark corpus before running copy scenarios.

- It crawls one configured source path per run.
- It assigns files into configured size buckets.
- It copies randomized selections into a shared dataset destination while preserving source-relative layout.
- It never clears the dataset destination automatically.
- It writes a manifest in the artifact directory so later runs from different source paths can continue filling the same dataset without re-importing the same source files.

This makes it possible to assemble a mixed benchmark dataset over time when no single source tree has enough variability to fill every bucket.

## Artifact Locations

Raw artifacts for this baseline live alongside the benchmark dataset:

- `R:\TestData\MP3\benchmark-scenarios.json`
- `R:\TestData\MP3\benchmark-results.ndjson`
- `R:\TestData\MP3\benchmark-tasklist.md`
- `R:\TestData\MP3\benchmark-journals\`
