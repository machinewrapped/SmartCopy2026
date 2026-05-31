## Analysis Summary
- **Mode:** `analysis`
- **Source:** `R:\TestData\SmallFileDataset`
- **Scenario filter:** `all (configured order)`
- **Scenario count:** `1`
- **Run records:** `106`
- **File records:** `596409`
- **Variants:** `Control_BaselineAuto`, `DirectWrite1MiB`, `DirectWriteBatch1MiB`, `StagedWriteBatch1MiB`, `DirectWrite4KiB`, `DirectWriteBatch4MiB`, `StagedWriteBatch4MiB`, `UnbatchedDirectWrite4MiB`, `DirectWrite16KiB`, `DirectWriteBatch16MiB`, `StagedWriteBatch16MiB`, `DirectWrite64KiB`, `DirectWriteBatch64MiB`, `StagedWriteBatch64MiB`, `DirectWrite256KiB`, `DirectWrite512KiB`
- **Run input:** `D:\Development\Github\SmartCopy2026\.benchmarks\benchmark-results-consolidated.ndjson`
- **File input:** `D:\Development\Github\SmartCopy2026\.benchmarks\benchmark-file-results-consolidated.ndjson`
- **Verdicts:** `PASS` means the measured improvement exceeds both the gate and observed variance. `INCONCLUSIVE` means the delta is inside variance or a matched control is missing.

## Scenario: `SmallFileDataset-SSDtoSSD`
- **Run records:** `53`
- **File records:** `596409`
- **Variants:** `Control_BaselineAuto`, `DirectWrite1MiB`, `DirectWriteBatch1MiB`, `StagedWriteBatch1MiB`, `DirectWrite4KiB`, `DirectWriteBatch4MiB`, `StagedWriteBatch4MiB`, `UnbatchedDirectWrite4MiB`, `DirectWrite16KiB`, `DirectWriteBatch16MiB`, `StagedWriteBatch16MiB`, `DirectWrite64KiB`, `DirectWriteBatch64MiB`, `StagedWriteBatch64MiB`, `DirectWrite256KiB`, `DirectWrite512KiB`

### Run-Level Evidence

| Variant | Valid Runs | Invalid Runs | Median Execute | Mean Execute | Min | Max | Spread | Delta vs Control | Noise Floor | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Control_BaselineAuto | 3 | 0 | 1m 20s | 1m 21s | 1m 19s | 1m 23s | 906 ms | - | - | CONTROL |
| DirectWrite1MiB | 3 | 0 | 1m 8s | 1m 15s | 1m 6s | 1m 30s | 1.8 sec | +14.4% (+11.6 sec) | 1.4 sec | PASS |
| DirectWriteBatch1MiB | 3 | 0 | 1m 7s | 1m 7s | 1m 7s | 1m 7s | 1 ms | +15.7% (+12.6 sec) | 454 ms | PASS |
| StagedWriteBatch1MiB | 3 | 0 | 1m 16s | 1m 23s | 1m 15s | 1m 37s | 1.7 sec | +4.2% (+3.4 sec) | 1.3 sec | FAIL |
| DirectWrite4KiB | 3 | 0 | 1m 14s | 1m 15s | 1m 14s | 1m 17s | 397 ms | +7.3% (+5.9 sec) | 651 ms | FAIL |
| DirectWriteBatch4MiB | 3 | 0 | 1m 36s | 1m 32s | 1m 25s | 1m 36s | 259 ms | -20.3% (-16.3 sec) | 583 ms | REGRESSION |
| StagedWriteBatch4MiB | 5 | 0 | 1m 22s | 1m 27s | 1m 14s | 1m 40s | 2.0 sec | -3.3% (-2.7 sec) | 1.5 sec | REGRESSION |
| UnbatchedDirectWrite4MiB | 3 | 0 | 1m 29s | 1m 25s | 1m 15s | 1m 31s | 2.5 sec | -11.4% (-9.2 sec) | 1.7 sec | REGRESSION |
| DirectWrite16KiB | 4 | 0 | 1m 34s | 1m 29s | 1m 12s | 1m 38s | 1.0 sec | -17.2% (-13.8 sec) | 969 ms | REGRESSION |
| DirectWriteBatch16MiB | 4 | 0 | 1m 21s | 1m 21s | 1m 6s | 1m 35s | 2.4 sec | -1.2% (-951 ms) | 1.6 sec | INCONCLUSIVE |
| StagedWriteBatch16MiB | 3 | 0 | 1m 13s | 1m 17s | 1m 12s | 1m 27s | 1.3 sec | +8.5% (+6.8 sec) | 1.1 sec | FAIL |
| DirectWrite64KiB | 3 | 0 | 1m 9s | 1m 17s | 1m 9s | 1m 35s | 65 ms | +13.4% (+10.8 sec) | 486 ms | PASS |
| DirectWriteBatch64MiB | 4 | 0 | 1m 20s | 1m 19s | 1m 6s | 1m 30s | 149 ms | -0.2% (-172 ms) | 528 ms | INCONCLUSIVE |
| StagedWriteBatch64MiB | 3 | 0 | 1m 13s | 1m 13s | 1m 13s | 1m 13s | 102 ms | +8.3% (+6.6 sec) | 504 ms | FAIL |
| DirectWrite256KiB | 3 | 0 | 1m 35s | 1m 27s | 1m 10s | 1m 35s | 379 ms | -18.8% (-15.1 sec) | 643 ms | REGRESSION |
| DirectWrite512KiB | 3 | 0 | 1m 33s | 1m 25s | 1m 8s | 1m 34s | 694 ms | -16.8% (-13.5 sec) | 800 ms | REGRESSION |

### Bucket Strategy Evidence

| Bucket | Best Observed Variant | Matched Control | Median File Duration | Control Median | Delta | Noise Floor | Aggregate MiB/s | Verdict | Recommendation |
|---|---|---|---:|---:|---:|---:|---:|---|---|
| Sub4KiB | DirectWriteBatch1MiB | StagedWriteBatch1MiB | 5.343 ms | 6.126 ms | +12.8% (+0.783 ms) | 0.008 ms | 0.36 | PASS | Candidate for policy |
| Sub16KiB | DirectWriteBatch1MiB | StagedWriteBatch1MiB | 5.793 ms | 6.46 ms | +10.3% (+0.667 ms) | 0.031 ms | 1.60 | PASS | Candidate for policy |
| Sub64KiB | DirectWriteBatch16MiB | StagedWriteBatch16MiB | 5.905 ms | 6.559 ms | +10.0% (+0.654 ms) | 0.006 ms | 5.48 | FAIL | No supported change |
| Sub256KiB | DirectWriteBatch1MiB | StagedWriteBatch1MiB | 6.532 ms | 7.437 ms | +12.2% (+0.905 ms) | 0.04 ms | 18.64 | PASS | Candidate for policy |
| Sub512KiB | DirectWriteBatch1MiB | StagedWriteBatch1MiB | 7.138 ms | 7.986 ms | +10.6% (+0.848 ms) | 0.055 ms | 47.20 | PASS | Candidate for policy |
| Sub1MiB | DirectWriteBatch64MiB | StagedWriteBatch64MiB | 7.862 ms | 8.532 ms | +7.9% (+0.67 ms) | 0.009 ms | 79.42 | FAIL | No supported change |
| Sub4MiB | DirectWriteBatch64MiB | StagedWriteBatch64MiB | 9.656 ms | 10.584 ms | +8.8% (+0.928 ms) | 0.046 ms | 119.20 | FAIL | No supported change |

### Bucket Metrics

| Bucket | Variant | Records | Bytes | Median Duration | P95 Duration | Aggregate MiB/s | Mean MiB/s | P50 MiB/s | P95 MiB/s | Run-Median Spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Sub4KiB | Control_BaselineAuto | 10005 | 16.93 MB | 6.597 ms | 8.421 ms | 0.29 | 0.33 | 0.25 | 1.06 | 0.031 ms |
| Sub4KiB | DirectWrite1MiB | 10005 | 16.93 MB | 5.478 ms | 6.778 ms | 0.36 | 0.40 | 0.32 | 1.27 | 0.008 ms |
| Sub4KiB | DirectWriteBatch1MiB | 10005 | 16.93 MB | 5.343 ms | 6.628 ms | 0.36 | 0.40 | 0.32 | 1.20 | 0.004 ms |
| Sub4KiB | StagedWriteBatch1MiB | 10005 | 16.93 MB | 6.126 ms | 10.678 ms | 0.26 | 0.28 | 0.25 | 0.65 | 0.012 ms |
| Sub4KiB | DirectWrite4KiB | 10005 | 16.93 MB | 5.457 ms | 6.692 ms | 0.36 | 0.40 | 0.31 | 1.26 | 0.005 ms |
| Sub4KiB | DirectWriteBatch4MiB | 10005 | 16.93 MB | 6.143 ms | 14.764 ms | 0.23 | 0.25 | 0.19 | 0.62 | 0.413 ms |
| Sub4KiB | StagedWriteBatch4MiB | 16675 | 28.21 MB | 6.237 ms | 11.357 ms | 0.25 | 0.27 | 0.24 | 0.60 | 0.002 ms |
| Sub4KiB | UnbatchedDirectWrite4MiB | 10005 | 16.93 MB | 5.55 ms | 10.563 ms | 0.28 | 0.33 | 0.27 | 0.85 | 0.08 ms |
| Sub4KiB | DirectWrite16KiB | 13340 | 22.57 MB | 5.667 ms | 14.724 ms | 0.25 | 0.29 | 0.23 | 0.68 | 0.023 ms |
| Sub4KiB | DirectWriteBatch16MiB | 13340 | 22.57 MB | 5.518 ms | 10.861 ms | 0.28 | 0.33 | 0.25 | 0.90 | 0.141 ms |
| Sub4KiB | StagedWriteBatch16MiB | 10005 | 16.93 MB | 5.922 ms | 7.161 ms | 0.33 | 0.36 | 0.29 | 1.16 | 0.053 ms |
| Sub4KiB | DirectWrite64KiB | 10005 | 16.93 MB | 5.494 ms | 10.422 ms | 0.30 | 0.36 | 0.27 | 1.04 | 0.036 ms |
| Sub4KiB | DirectWriteBatch64MiB | 13340 | 22.57 MB | 5.678 ms | 11.741 ms | 0.26 | 0.29 | 0.23 | 0.65 | 0.063 ms |
| Sub4KiB | StagedWriteBatch64MiB | 10005 | 16.93 MB | 5.906 ms | 7.179 ms | 0.33 | 0.37 | 0.29 | 1.23 | 0.001 ms |
| Sub4KiB | DirectWrite256KiB | 10005 | 16.93 MB | 5.566 ms | 10.152 ms | 0.31 | 0.37 | 0.30 | 1.22 | 0.007 ms |
| Sub4KiB | DirectWrite512KiB | 10005 | 16.93 MB | 5.633 ms | 14.677 ms | 0.26 | 0.31 | 0.20 | 0.71 | 0.14 ms |
| Sub16KiB | Control_BaselineAuto | 6771 | 62.64 MB | 6.987 ms | 8.221 ms | 1.32 | 1.37 | 1.36 | 2.21 | 0.024 ms |
| Sub16KiB | DirectWrite1MiB | 6771 | 62.64 MB | 6.002 ms | 14.987 ms | 1.18 | 1.40 | 1.23 | 2.64 | 0.038 ms |
| Sub16KiB | DirectWriteBatch1MiB | 6771 | 62.64 MB | 5.793 ms | 6.89 ms | 1.60 | 1.68 | 1.60 | 2.72 | 0.013 ms |
| Sub16KiB | StagedWriteBatch1MiB | 6771 | 62.64 MB | 6.46 ms | 9.75 ms | 1.37 | 1.46 | 1.43 | 2.39 | 0.049 ms |
| Sub16KiB | DirectWrite4KiB | 6771 | 62.64 MB | 6.612 ms | 10.397 ms | 1.30 | 1.43 | 1.40 | 2.35 | 0.014 ms |
| Sub16KiB | DirectWriteBatch4MiB | 6771 | 62.64 MB | 6.049 ms | 14.811 ms | 1.19 | 1.39 | 1.23 | 2.62 | 0.014 ms |
| Sub16KiB | StagedWriteBatch4MiB | 11285 | 104.4 MB | 6.647 ms | 14.651 ms | 1.22 | 1.33 | 1.24 | 2.30 | 0.059 ms |
| Sub16KiB | UnbatchedDirectWrite4MiB | 6771 | 62.64 MB | 9.649 ms | 15.124 ms | 1.01 | 1.21 | 1.04 | 2.46 | 0.529 ms |
| Sub16KiB | DirectWrite16KiB | 9028 | 83.52 MB | 5.925 ms | 14.843 ms | 1.25 | 1.46 | 1.32 | 2.65 | 0.021 ms |
| Sub16KiB | DirectWriteBatch16MiB | 9028 | 83.52 MB | 5.985 ms | 14.803 ms | 1.23 | 1.40 | 1.27 | 2.59 | 0.016 ms |
| Sub16KiB | StagedWriteBatch16MiB | 6771 | 62.64 MB | 6.684 ms | 14.973 ms | 1.13 | 1.27 | 1.15 | 2.34 | 0.087 ms |
| Sub16KiB | DirectWrite64KiB | 6771 | 62.64 MB | 5.858 ms | 14.761 ms | 1.35 | 1.51 | 1.31 | 2.70 | 0.021 ms |
| Sub16KiB | DirectWriteBatch64MiB | 9028 | 83.52 MB | 5.842 ms | 7.085 ms | 1.58 | 1.65 | 1.58 | 2.67 | 0.014 ms |
| Sub16KiB | StagedWriteBatch64MiB | 6771 | 62.64 MB | 6.492 ms | 7.564 ms | 1.44 | 1.49 | 1.46 | 2.37 | 0.007 ms |
| Sub16KiB | DirectWrite256KiB | 6771 | 62.64 MB | 10.069 ms | 15.281 ms | 0.89 | 1.10 | 0.96 | 2.42 | 0.101 ms |
| Sub16KiB | DirectWrite512KiB | 6771 | 62.64 MB | 6.075 ms | 14.984 ms | 1.14 | 1.32 | 1.12 | 2.62 | 0.442 ms |
| Sub64KiB | Control_BaselineAuto | 10422 | 360.01 MB | 7.239 ms | 8.171 ms | 4.58 | 4.70 | 4.75 | 7.58 | 0.09 ms |
| Sub64KiB | DirectWrite1MiB | 10422 | 360.01 MB | 5.99 ms | 6.845 ms | 5.48 | 5.65 | 5.70 | 9.09 | 0.021 ms |
| Sub64KiB | DirectWriteBatch1MiB | 10422 | 360.01 MB | 5.959 ms | 7.152 ms | 5.47 | 5.64 | 5.65 | 9.05 | 0.01 ms |
| Sub64KiB | StagedWriteBatch1MiB | 10422 | 360.01 MB | 6.63 ms | 9.832 ms | 4.79 | 5.05 | 5.06 | 8.18 | 0.033 ms |
| Sub64KiB | DirectWrite4KiB | 10422 | 360.01 MB | 6.784 ms | 7.653 ms | 4.87 | 5.01 | 5.07 | 8.10 | 0 ms |
| Sub64KiB | DirectWriteBatch4MiB | 10422 | 360.01 MB | 6.122 ms | 14.937 ms | 4.23 | 5.07 | 4.72 | 9.15 | 0.199 ms |
| Sub64KiB | StagedWriteBatch4MiB | 17370 | 600.01 MB | 6.709 ms | 14.589 ms | 4.48 | 4.85 | 4.72 | 8.18 | 0.006 ms |
| Sub64KiB | UnbatchedDirectWrite4MiB | 10422 | 360.01 MB | 5.948 ms | 6.794 ms | 5.52 | 5.68 | 5.69 | 9.09 | 0.02 ms |
| Sub64KiB | DirectWrite16KiB | 13896 | 480.01 MB | 7.046 ms | 14.785 ms | 4.27 | 4.70 | 4.92 | 7.83 | 0.064 ms |
| Sub64KiB | DirectWriteBatch16MiB | 13896 | 480.01 MB | 5.905 ms | 7.127 ms | 5.48 | 5.72 | 5.73 | 9.21 | 0.005 ms |
| Sub64KiB | StagedWriteBatch16MiB | 10422 | 360.01 MB | 6.559 ms | 7.418 ms | 5.03 | 5.16 | 5.21 | 8.28 | 0.007 ms |
| Sub64KiB | DirectWrite64KiB | 10422 | 360.01 MB | 5.998 ms | 7.162 ms | 5.44 | 5.60 | 5.61 | 9.00 | 0.015 ms |
| Sub64KiB | DirectWriteBatch64MiB | 13896 | 480.01 MB | 5.935 ms | 9.333 ms | 5.33 | 5.66 | 5.75 | 9.23 | 0.009 ms |
| Sub64KiB | StagedWriteBatch64MiB | 10422 | 360.01 MB | 6.606 ms | 7.509 ms | 5.00 | 5.13 | 5.14 | 8.24 | 0.012 ms |
| Sub64KiB | DirectWrite256KiB | 10422 | 360.01 MB | 5.993 ms | 6.867 ms | 5.48 | 5.66 | 5.68 | 9.17 | 0.006 ms |
| Sub64KiB | DirectWrite512KiB | 10422 | 360.01 MB | 5.951 ms | 6.788 ms | 5.52 | 5.69 | 5.74 | 9.11 | 0.013 ms |
| Sub256KiB | Control_BaselineAuto | 3624 | 480.01 MB | 7.748 ms | 8.815 ms | 15.93 | 16.78 | 15.29 | 28.87 | 0.109 ms |
| Sub256KiB | DirectWrite1MiB | 3624 | 480.01 MB | 6.787 ms | 15.11 ms | 15.01 | 17.35 | 15.33 | 33.53 | 0.069 ms |
| Sub256KiB | DirectWriteBatch1MiB | 3624 | 480.01 MB | 6.532 ms | 7.718 ms | 18.64 | 19.77 | 18.39 | 33.41 | 0.039 ms |
| Sub256KiB | StagedWriteBatch1MiB | 3624 | 480.01 MB | 7.437 ms | 15.524 ms | 14.12 | 15.83 | 14.24 | 29.82 | 0.041 ms |
| Sub256KiB | DirectWrite4KiB | 3624 | 480.01 MB | 7.275 ms | 8.253 ms | 16.89 | 17.84 | 16.19 | 30.79 | 0.009 ms |
| Sub256KiB | DirectWriteBatch4MiB | 3624 | 480.01 MB | 6.795 ms | 15.73 ms | 14.31 | 16.74 | 15.00 | 32.94 | 0.32 ms |
| Sub256KiB | StagedWriteBatch4MiB | 6040 | 800.01 MB | 7.238 ms | 15.185 ms | 15.19 | 16.74 | 15.10 | 30.35 | 0.029 ms |
| Sub256KiB | UnbatchedDirectWrite4MiB | 3624 | 480.01 MB | 10.15 ms | 15.283 ms | 12.23 | 14.01 | 12.51 | 29.31 | 0.034 ms |
| Sub256KiB | DirectWrite16KiB | 4832 | 640.01 MB | 7.67 ms | 15.103 ms | 13.91 | 15.53 | 14.45 | 28.62 | 0.093 ms |
| Sub256KiB | DirectWriteBatch16MiB | 4832 | 640.01 MB | 9.409 ms | 15.618 ms | 13.43 | 15.67 | 13.97 | 32.25 | 0.007 ms |
| Sub256KiB | StagedWriteBatch16MiB | 3624 | 480.01 MB | 6.969 ms | 8.32 ms | 17.53 | 18.49 | 17.03 | 31.36 | 0.005 ms |
| Sub256KiB | DirectWrite64KiB | 3624 | 480.01 MB | 7.591 ms | 15.177 ms | 13.88 | 15.54 | 13.98 | 29.47 | 0.1 ms |
| Sub256KiB | DirectWriteBatch64MiB | 4832 | 640.01 MB | 9.537 ms | 15.544 ms | 13.45 | 15.62 | 13.90 | 31.95 | 0.014 ms |
| Sub256KiB | StagedWriteBatch64MiB | 3624 | 480.01 MB | 7.019 ms | 8.078 ms | 17.47 | 18.43 | 16.95 | 31.50 | 0.017 ms |
| Sub256KiB | DirectWrite256KiB | 3624 | 480.01 MB | 10.162 ms | 15.308 ms | 12.07 | 13.86 | 12.43 | 28.97 | 0.011 ms |
| Sub256KiB | DirectWrite512KiB | 3624 | 480.01 MB | 10.143 ms | 15.304 ms | 12.23 | 14.13 | 12.69 | 29.37 | 0.037 ms |
| Sub512KiB | Control_BaselineAuto | 1752 | 600.7 MB | 8.34 ms | 9.443 ms | 40.29 | 40.56 | 38.60 | 54.64 | 0.007 ms |
| Sub512KiB | DirectWrite1MiB | 1752 | 600.7 MB | 7.297 ms | 10.217 ms | 44.31 | 45.45 | 43.80 | 62.42 | 0.093 ms |
| Sub512KiB | DirectWriteBatch1MiB | 1752 | 600.7 MB | 7.138 ms | 8.092 ms | 47.20 | 47.51 | 45.40 | 63.32 | 0.047 ms |
| Sub512KiB | StagedWriteBatch1MiB | 1752 | 600.7 MB | 7.986 ms | 15.538 ms | 37.45 | 39.58 | 38.81 | 56.99 | 0.063 ms |
| Sub512KiB | DirectWrite4KiB | 1752 | 600.7 MB | 7.849 ms | 8.884 ms | 42.81 | 43.09 | 40.77 | 58.22 | 0.013 ms |
| Sub512KiB | DirectWriteBatch4MiB | 1752 | 600.7 MB | 10.739 ms | 16.24 ms | 32.10 | 35.72 | 34.87 | 60.97 | 0.034 ms |
| Sub512KiB | StagedWriteBatch4MiB | 2920 | 1001.16 MB | 8.097 ms | 16.08 ms | 35.55 | 38.37 | 37.84 | 57.87 | 0.093 ms |
| Sub512KiB | UnbatchedDirectWrite4MiB | 1752 | 600.7 MB | 7.543 ms | 15.004 ms | 38.23 | 41.41 | 41.04 | 62.62 | 0.013 ms |
| Sub512KiB | DirectWrite16KiB | 2336 | 800.93 MB | 8.594 ms | 15.285 ms | 33.44 | 35.97 | 35.52 | 54.79 | 0.012 ms |
| Sub512KiB | DirectWriteBatch16MiB | 2336 | 800.93 MB | 7.217 ms | 16.07 ms | 38.24 | 41.97 | 42.11 | 63.57 | 0.037 ms |
| Sub512KiB | StagedWriteBatch16MiB | 1752 | 600.7 MB | 7.6 ms | 11.222 ms | 42.45 | 43.56 | 42.07 | 59.50 | 0.001 ms |
| Sub512KiB | DirectWrite64KiB | 1752 | 600.7 MB | 7.917 ms | 10.398 ms | 41.16 | 42.02 | 40.06 | 57.29 | 0.003 ms |
| Sub512KiB | DirectWriteBatch64MiB | 2336 | 800.93 MB | 7.449 ms | 16.157 ms | 35.64 | 39.61 | 40.16 | 62.00 | 0.058 ms |
| Sub512KiB | StagedWriteBatch64MiB | 1752 | 600.7 MB | 7.644 ms | 8.708 ms | 44.06 | 44.32 | 42.16 | 59.45 | 0.003 ms |
| Sub512KiB | DirectWrite256KiB | 1752 | 600.7 MB | 8.048 ms | 14.751 ms | 39.29 | 40.65 | 39.17 | 57.19 | 0.019 ms |
| Sub512KiB | DirectWrite512KiB | 1752 | 600.7 MB | 7.324 ms | 14.362 ms | 42.60 | 44.51 | 42.91 | 62.55 | 0.002 ms |
| Sub1MiB | Control_BaselineAuto | 849 | 600.73 MB | 9.271 ms | 11.111 ms | 73.00 | 74.23 | 72.58 | 96.78 | 0.069 ms |
| Sub1MiB | DirectWrite1MiB | 849 | 600.73 MB | 8.511 ms | 15.145 ms | 72.80 | 77.24 | 77.38 | 108.91 | 0.026 ms |
| Sub1MiB | DirectWriteBatch1MiB | 849 | 600.73 MB | 8.063 ms | 9.44 ms | 84.44 | 86.01 | 83.98 | 112.00 | 0.05 ms |
| Sub1MiB | StagedWriteBatch1MiB | 849 | 600.73 MB | 9.083 ms | 15.079 ms | 70.54 | 73.96 | 73.66 | 102.87 | 0.005 ms |
| Sub1MiB | DirectWrite4KiB | 849 | 600.73 MB | 8.88 ms | 10.487 ms | 76.60 | 77.88 | 76.43 | 102.28 | 0.004 ms |
| Sub1MiB | DirectWriteBatch4MiB | 849 | 600.73 MB | 8.292 ms | 16.803 ms | 70.88 | 76.68 | 77.16 | 111.56 | 0.031 ms |
| Sub1MiB | StagedWriteBatch4MiB | 1415 | 1001.21 MB | 8.823 ms | 16.207 ms | 71.61 | 75.14 | 74.86 | 103.58 | 0.004 ms |
| Sub1MiB | UnbatchedDirectWrite4MiB | 849 | 600.73 MB | 10.109 ms | 15.255 ms | 65.13 | 69.73 | 68.59 | 103.37 | 0.092 ms |
| Sub1MiB | DirectWrite16KiB | 1132 | 800.97 MB | 9.524 ms | 15.358 ms | 65.06 | 68.64 | 68.73 | 95.49 | 0.053 ms |
| Sub1MiB | DirectWriteBatch16MiB | 1132 | 800.97 MB | 10.626 ms | 17.19 ms | 64.31 | 70.78 | 71.69 | 110.69 | 0.027 ms |
| Sub1MiB | StagedWriteBatch16MiB | 849 | 600.73 MB | 8.89 ms | 16.313 ms | 71.30 | 75.45 | 76.25 | 105.50 | 0.064 ms |
| Sub1MiB | DirectWrite64KiB | 849 | 600.73 MB | 9.448 ms | 15.398 ms | 64.47 | 68.49 | 67.37 | 100.05 | 0.012 ms |
| Sub1MiB | DirectWriteBatch64MiB | 1132 | 800.97 MB | 7.862 ms | 13.538 ms | 79.42 | 83.20 | 80.78 | 112.52 | 0.018 ms |
| Sub1MiB | StagedWriteBatch64MiB | 849 | 600.73 MB | 8.532 ms | 10.134 ms | 79.77 | 81.20 | 79.83 | 105.92 | 0 ms |
| Sub1MiB | DirectWrite256KiB | 849 | 600.73 MB | 14.445 ms | 15.577 ms | 55.21 | 59.00 | 58.35 | 92.88 | 0.072 ms |
| Sub1MiB | DirectWrite512KiB | 849 | 600.73 MB | 14.391 ms | 15.464 ms | 55.91 | 59.87 | 58.84 | 94.21 | 0.077 ms |
| Sub4MiB | Control_BaselineAuto | 336 | 600.59 MB | 12.186 ms | 19.287 ms | 115.42 | 130.78 | 122.82 | 202.59 | 0.122 ms |
| Sub4MiB | DirectWrite1MiB | 336 | 600.59 MB | 11.627 ms | 18.772 ms | 117.61 | 134.04 | 124.76 | 204.21 | 0.024 ms |
| Sub4MiB | DirectWriteBatch1MiB | 336 | 600.59 MB | 10.281 ms | 16.826 ms | 129.99 | 150.66 | 139.65 | 227.58 | 0.05 ms |
| Sub4MiB | StagedWriteBatch1MiB | 336 | 600.59 MB | 11.087 ms | 18.078 ms | 122.41 | 139.98 | 128.13 | 211.39 | 0.022 ms |
| Sub4MiB | DirectWrite4KiB | 336 | 600.59 MB | 11.332 ms | 18.518 ms | 120.74 | 137.79 | 125.07 | 209.58 | 0.124 ms |
| Sub4MiB | DirectWriteBatch4MiB | 336 | 600.59 MB | 9.872 ms | 15.822 ms | 135.37 | 157.83 | 143.84 | 244.43 | 0.042 ms |
| Sub4MiB | StagedWriteBatch4MiB | 560 | 1000.98 MB | 10.712 ms | 16.758 ms | 127.24 | 146.33 | 133.48 | 230.93 | 0.061 ms |
| Sub4MiB | UnbatchedDirectWrite4MiB | 336 | 600.59 MB | 10.025 ms | 16.153 ms | 131.68 | 152.77 | 141.21 | 235.98 | 0.014 ms |
| Sub4MiB | DirectWrite16KiB | 448 | 800.78 MB | 11.689 ms | 19.431 ms | 115.74 | 131.51 | 122.39 | 200.04 | 0.274 ms |
| Sub4MiB | DirectWriteBatch16MiB | 448 | 800.78 MB | 9.924 ms | 16.002 ms | 134.61 | 156.54 | 144.46 | 242.10 | 0.027 ms |
| Sub4MiB | StagedWriteBatch16MiB | 336 | 600.59 MB | 10.523 ms | 16.434 ms | 128.26 | 147.66 | 135.37 | 234.02 | 0.065 ms |
| Sub4MiB | DirectWrite64KiB | 336 | 600.59 MB | 11.677 ms | 19.055 ms | 116.95 | 133.65 | 124.38 | 200.85 | 0.026 ms |
| Sub4MiB | DirectWriteBatch64MiB | 448 | 800.78 MB | 9.656 ms | 15.817 ms | 119.20 | 158.67 | 145.96 | 246.14 | 0.004 ms |
| Sub4MiB | StagedWriteBatch64MiB | 336 | 600.59 MB | 10.584 ms | 16.659 ms | 128.27 | 147.37 | 134.41 | 234.71 | 0.089 ms |
| Sub4MiB | DirectWrite256KiB | 336 | 600.59 MB | 11.224 ms | 18.444 ms | 119.12 | 136.59 | 130.14 | 205.72 | 0.185 ms |
| Sub4MiB | DirectWrite512KiB | 336 | 600.59 MB | 11.576 ms | 18.824 ms | 117.59 | 133.72 | 124.08 | 200.35 | 0.351 ms |

### Batching Isolation Evidence

Compares each `DirectWriteBatch*` variant against `UnbatchedDirectWrite4MiB` to isolate the contribution of batching beyond direct write alone (Section 7.2.2).

| Bucket | Variant | Unbatched Control | Median Duration | Control Median | Delta | Noise Floor | Verdict |
|---|---|---|---:|---:|---:|---:|---|
| Sub4KiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 5.343 ms | 5.55 ms | +3.7% (+0.208 ms) | 0.042 ms | FAIL |
| Sub4KiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 6.143 ms | 5.55 ms | -10.7% (-0.593 ms) | 0.247 ms | REGRESSION |
| Sub4KiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 5.518 ms | 5.55 ms | +0.6% (+0.033 ms) | 0.111 ms | INCONCLUSIVE |
| Sub4KiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 5.678 ms | 5.55 ms | -2.3% (-0.128 ms) | 0.072 ms | REGRESSION |
| Sub16KiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 5.793 ms | 9.649 ms | +40.0% (+3.857 ms) | 0.271 ms | PASS |
| Sub16KiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 6.049 ms | 9.649 ms | +37.3% (+3.6 ms) | 0.272 ms | PASS |
| Sub16KiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 5.985 ms | 9.649 ms | +38.0% (+3.664 ms) | 0.273 ms | PASS |
| Sub16KiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 5.842 ms | 9.649 ms | +39.5% (+3.807 ms) | 0.272 ms | PASS |
| Sub64KiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 5.959 ms | 5.948 ms | -0.2% (-0.011 ms) | 0.015 ms | INCONCLUSIVE |
| Sub64KiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 6.122 ms | 5.948 ms | -2.9% (-0.173 ms) | 0.109 ms | REGRESSION |
| Sub64KiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 5.905 ms | 5.948 ms | +0.7% (+0.043 ms) | 0.012 ms | FAIL |
| Sub64KiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 5.935 ms | 5.948 ms | +0.2% (+0.013 ms) | 0.014 ms | INCONCLUSIVE |
| Sub256KiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 6.532 ms | 10.15 ms | +35.7% (+3.619 ms) | 0.036 ms | PASS |
| Sub256KiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 6.795 ms | 10.15 ms | +33.1% (+3.356 ms) | 0.177 ms | PASS |
| Sub256KiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 9.409 ms | 10.15 ms | +7.3% (+0.741 ms) | 0.021 ms | FAIL |
| Sub256KiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 9.537 ms | 10.15 ms | +6.0% (+0.613 ms) | 0.024 ms | FAIL |
| Sub512KiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 7.138 ms | 7.543 ms | +5.4% (+0.405 ms) | 0.03 ms | FAIL |
| Sub512KiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 10.739 ms | 7.543 ms | -42.4% (-3.196 ms) | 0.024 ms | REGRESSION |
| Sub512KiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 7.217 ms | 7.543 ms | +4.3% (+0.326 ms) | 0.025 ms | FAIL |
| Sub512KiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 7.449 ms | 7.543 ms | +1.2% (+0.094 ms) | 0.036 ms | FAIL |
| Sub1MiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 8.063 ms | 10.109 ms | +20.2% (+2.045 ms) | 0.071 ms | PASS |
| Sub1MiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 8.292 ms | 10.109 ms | +18.0% (+1.817 ms) | 0.061 ms | PASS |
| Sub1MiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 10.626 ms | 10.109 ms | -5.1% (-0.517 ms) | 0.06 ms | REGRESSION |
| Sub1MiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 7.862 ms | 10.109 ms | +22.2% (+2.246 ms) | 0.055 ms | PASS |
| Sub4MiB | DirectWriteBatch1MiB | UnbatchedDirectWrite4MiB | 10.281 ms | 10.025 ms | -2.6% (-0.256 ms) | 0.032 ms | REGRESSION |
| Sub4MiB | DirectWriteBatch4MiB | UnbatchedDirectWrite4MiB | 9.872 ms | 10.025 ms | +1.5% (+0.153 ms) | 0.028 ms | FAIL |
| Sub4MiB | DirectWriteBatch16MiB | UnbatchedDirectWrite4MiB | 9.924 ms | 10.025 ms | +1.0% (+0.101 ms) | 0.021 ms | FAIL |
| Sub4MiB | DirectWriteBatch64MiB | UnbatchedDirectWrite4MiB | 9.656 ms | 10.025 ms | +3.7% (+0.369 ms) | 0.009 ms | FAIL |


