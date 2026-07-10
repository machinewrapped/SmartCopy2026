# SmartCopy2026 — Phase 2: Tiny-File Direct Write

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

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

**Integrity trade-off:** Direct write without staging can leave a partial file on power loss or device unplug. **Mitigation:** on stream error (not unplug), delete the partial destination file. The trade-off is most acceptable for small files, where the staged temp-file lifecycle is proportionally expensive and partial-write cleanup is usually enough for ordinary failures. It is not a true filesystem atomicity guarantee; staged write remains the safer default for larger files.

**Core integration:** `TinyFileFastPathThresholdBytes: long` in `OperationalSettings`; the struct field has no initializer (C# default **0**, so unset benchmark/test contexts do not accidentally enable the fast path). App default via `AppSettings.TinyFileFastPathKb = 256` passes **262144** (256 KiB) to every production job. The original Phase 2 threshold sweep showed the clearest step change at 64 KiB, but the later MixedDataset production-path diagnostics repeatedly localized extra production overhead in the staged 64-256 KiB path; 256 KiB is the conservative promotion that captures that bucket without applying direct writes to medium/large files. Setting to 0 disables the fast path entirely (pre-Phase 2 behaviour). In `LocalFileSystemProvider.WriteAsync`, when `fileSize ≤ threshold`, write directly to the destination using `FileMode.Create`; skip staging.

**Acceptance criteria:** files bit-identical to staged baseline; no behaviour change when threshold is 0.

## Execution checklist (complete)

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
- [x] Re-run analysis under the stricter [§4.2](optimisation-strategies.md#42-metrics) rules and record bucket-level recommendations without unsupported causal claims — completed as part of the Phase 2+3 consolidated clean-room run (see Phase 3)

---
