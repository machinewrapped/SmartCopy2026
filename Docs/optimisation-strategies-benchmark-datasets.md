# SmartCopy2026 — Benchmark Datasets

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Benchmark protocol and verdict language live in [Operational Reference](optimisation-strategies.md#4-operational-reference).

## SmallFileDataset (granular small-file discovery)

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

## LargeFileDataset (byte-volume-dominated)

The MixedDataset cannot isolate large-file throughput — tiny-file overhead dominates every aggregate metric. A second dataset is needed where large files dominate by byte volume. `LargeFileDataset` provides this role; it is the dataset behind the Phase 5 buffer-sizing and cross-drive validation runs.

## Policy Validation Datasets

After bucket-level discovery identifies promising routing rules, benchmark complete policies against larger and more realistic datasets:

- **MixedDataset:** realistic file-count distribution and directory structure; primary validation for small-file policy value.
- **Byte-volume-dominated dataset:** large-file throughput and buffer-size policy validation.
- **Destination matrix:** SSDtoSSD, SameDriveTest, SSDtoHDD, SSDtoUSBFlash. SameDrive and USB must not inherit SSD defaults without evidence.

Policy validation uses whole-run `executeDuration` as the primary metric. Bucket metrics explain why a policy works or fails; they do not by themselves prove the policy should ship.
