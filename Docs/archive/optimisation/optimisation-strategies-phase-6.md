# SmartCopy2026 — Phase 6: Destination-Sensitive Policies

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Status:** **Static routing implemented (2026-06-13); benchmark validation pending.** The copy engine was refactored into a policy+strategy system (`SmartCopy.Core/Pipeline/Strategy/`, see `Docs/Architecture.md` §2.4.1): `DefaultCopyStrategyPolicy` selects the copy buffer from the source→destination drive pair per the validated table (Phase 5 policy + the USB addition below), gated behind `OperationalSettings.DestinationRoutingEnabled` (enabled in the app under `AllowCopyOptimisations`; default-off elsewhere, so the refactor is behaviour-preserving). `CopyStep` and `MoveStep` now delegate byte transfer to the shared strategy, removing the duplicated copy engine. The remaining work is the whole-policy benchmark gate (the Production Validation Pass) before promoting defaults, plus the per-device learned profiles in the design note below. USB variance is still too high for a static USB profile to be trusted — treated as a prior pending per-device learning.

**Goal:** Apply appropriate strategy defaults per destination type, with a path toward per-device learned profiles for devices (like USB flash) where static classification is insufficient.

## USB Validation (2026-06-08)

86 runs from 2 scenarios: `LargeFileDataset` (Phase 5, 4 variants) and `SmallestFileDataset` (Phase 6, 4 variants), sourced from O: (SSD) to T: (USB flash). 3 converged runs per variant at `convergenceSpreadPercent: 10` — significantly looser than the 5% used for cross-drive. Even at 10% the convergence threshold tripped repeatedly.

> [!CAUTION]
> The test drive (T:) is a very small USB flash drive and an extreme case. The wide variance (baseline spread: 4m 42s on a 47-minute run) means most bucket-level findings and the batching run-level results are INCONCLUSIVE regardless of the measured delta. The exception is ManualLoop1MiB on LFD, which shows an unusually tight spread (56s) alongside a large improvement (+29.5%). **The primary lesson from this data is not a USB copy strategy — it is that USB variance is too high for a static destination-type profile to be reliable.** See the per-device design note below for the direction this implies.

**LFD buffer sizing on USB:**

| Variant | Median | Spread | Delta vs Control | Verdict |
|---|---:|---:|---:|---|
| BaselineAuto256KiB | 47m 21s | 4m 42s | — | CONTROL |
| ManualLoop512KiBArrayPool | 50m 8s | 33.8s | -5.9% | REGRESSION |
| ManualLoop1MiBArrayPool | 33m 23s | 56.5s | **+29.5%** | **PASS** |
| ManualLoop4MiBArrayPool | 51m 38s | 5m 12s | -9.0% | INCONCLUSIVE |

**ManualLoop1MiB is the decisive USB winner.** The 29.5% improvement is the largest single-variant gain observed across all Phase 5 scenarios. Notably, it is also the *most stable* variant — 56s spread on a 33-minute run (<3% CV) versus the baseline's 4m 42s spread (~10% CV). Larger aligned writes appear to let the USB controller operate within its preferred transfer granularity.

The curve is strongly non-monotonic and USB-specific:

1. **ManualLoop512KiB regresses (-5.9%, REGRESSION).** This is the only destination type where 512KiB ManualLoop is slower than the 256KiB CopyToAsync baseline. USB controller I/O characteristics differ fundamentally from SSD/HDD; cross-drive findings do not transfer.
2. **ManualLoop4MiB shows the highest variance** (5m 12s spread, INCONCLUSIVE). This is the thermal throttling signature: large write chunks sustain the drive at full load, triggering intermittent pauses. Avoid 4 MiB on USB.
3. **1 MiB is uniquely effective** — it hits the USB controller's sweet spot for bulk transfer efficiency without triggering sustained thermal load.

**Batching on USB (SmallestFileDataset):**

| Variant | Median | Spread | Delta vs Control | Verdict |
|---|---:|---:|---:|---|
| BaselineAuto256KiB | 6m 46s | 34.0s | — | CONTROL |
| Batch256KiB | 7m 36s | 34.9s | -12.4% | REGRESSION |
| Batch1MiB | 6m 31s | 13.2s | +3.6% | INCONCLUSIVE |
| Batch2MiB | 6m 24s | 34.7s | +5.3% | INCONCLUSIVE |

The SSD Phase 3 batching gains (3–5.5%) do not replicate on USB. Batch1MiB and Batch2MiB are INCONCLUSIVE. Batch256KiB actively regresses.

Sub-bucket decomposition reveals a split:

- **Sub4KiB, Sub16KiB, Sub64KiB:** Small positive effects from 1MiB and 2MiB batch buffers (+9–14%), but all within the noise floor individually — inconclusive.
- **Sub256KiB:** All batch variants are slower than baseline (Batch256KiB: -23.2%, Batch1MiB: -9.4%, Batch2MiB: -7.6%). Files in this range exceed the 64KiB direct-write threshold, so both baseline and batch use staged writes — the write path is identical. The only thing batching adds for this size range is phase separation, and it is net negative on USB. The mechanism is unclear from this data; phase separation may simply provide no benefit when USB writes are slow enough that reads always complete far ahead of writes regardless.
- **Sub512KiB:** Batch256KiB wins (+16.8%, PASS), but this is a ManualLoop effect, not batching — Sub512KiB files (262–512KiB) exceed the 256KiB buffer ceiling and route to ManualLoop512KiB instead. Consistent with Phase 5 finding that larger sequential writes help on USB.

The Sub256KiB regression under batching is the key Phase 6 USB finding: on USB, batch-path overhead exceeds the phase-separation benefit for medium-small files (64–256KiB). This is the opposite of SSD/HDD behaviour (Phase 3 batching contribution).

**USB policy addition:**

| Destination Profile | Buffer | Batching | Notes |
|---|---|---|---|
| USB flash | **1 MiB** (prior) | **OFF** | The LFD 1 MiB result is the strongest signal in this dataset (tight spread, large delta). All other USB results are dominated by noise. Use 1 MiB as the starting prior; treat it as a default to be overridden by per-device learning rather than a settled finding. |

The Phase 5 provisional policy for SSD, HDD, and Unknown profiles is unchanged.

## Static destination-type routing (initial implementation)

Detect SSD vs HDD vs USB where reliable platform APIs are available, but assume classification can fail or be ambiguous. Implement as a `DestinationProfile` enum and translate into strategy parameters (buffer size, staging threshold, progress throttle rate). Do not hardcode values — surface them as fields on `OperationalSettings`, which is injected per-run via `PipelineJob`. Unknown or ambiguous destinations must use the conservative profile.

Destination-specific notes (buffer defaults from the Phase 5 cross-drive policy; preallocation OFF universally):
- **Unknown / ambiguous:** 512 KiB buffer. Do not assume SSD behaviour.
- **SSD:** 1 MiB buffer. Cross-drive validation confirms 1 MiB passes in 2/3 SSD-to-SSD scenarios. 4 MiB may benefit on fast NVMe pairs but buffer response is drive-specific and cannot be classified from software alone.
- **HDD:** 512 KiB buffer. Larger buffers provide no consistent benefit and regress on some HDD pairs. Small-file strategies transfer directly; no concurrency until Phase 7.
- **USB / removable:** 1 MiB buffer as the starting prior (USB LFD PASS; also consistent with SSD/HDD evidence that 1 MiB is broadly safe). Batching inconclusive. USB variance is high enough that no static profile should be trusted — treat this as a temporary default pending per-device learning.
- **SameDrive:** do not collapse into SSDtoSSD. Small files behave similarly; large files are contention-limited. SameDriveSSD may benefit from larger buffers (4–8 MiB), but this is device- and cache-sensitive; treat as an optional specialised profile (Phase 5 Step 3) and fall back to 512 KiB–1 MiB when SSD/HDD classification is uncertain.

## Design note — per-device adaptive profiles

USB flash (and potentially other removable or slow media) exposes a fundamental limitation of static destination-type routing: "USB" is an interface, not a device class. Two flash drives can perform completely differently. The wide USB variance is partly thermal, but also reflects genuine device-to-device spread that no classification heuristic can capture.

The better long-term architecture is a **per-device learned profile**, keyed by device identity and updated from observed copy performance:

- **Device identity:** VolumeID (volume serial number from `GetVolumeInformation` on Windows, UUID from mount metadata on Linux). Already accessible from `LocalFileSystemProvider`; `ProviderCapabilities` is the natural place to surface it. This identifies a specific formatted volume, not the physical drive — sufficient for USB sticks and external drives where the user sees a consistent drive letter/path.
- **Per-device profile store:** a small persistent JSON file (in `IAppDataStore`) mapping `{VolumeID → {bufferSize, batchEnabled, ...}}`. Indexed via `IAppContext`. Falls back to the static destination-type profile for unknown devices.
- **In-vivo measurement:** during normal copies, record aggregate throughput per size bucket to the device's profile. After enough observations accumulate, the profile overrides the static default. No user action needed.
- **Explicit calibration:** a "Calibrate this drive" action (in the copy dialog or device context menu) that runs a brief A/B test — a few dozen small files, a few large files, enough to distinguish strategy variants in under a minute. Faster to converge than passive learning and available to power users who want accurate profiles immediately.
- **Strategy experimentation:** for unknown or low-confidence devices, occasionally shadow a copy with an alternative strategy on a small sample of files and compare throughput. Converges toward the best strategy for the specific device. Resembles a multi-armed bandit policy, not a fixed schedule.

This framing reorients Phase 6: static destination-type routing is the first step and needed regardless, but the destination profile should be treated as a *prior* that in-vivo measurement can override, not as a permanent classification. The infrastructure required (VolumeID in `ProviderCapabilities`, profile store, measurement hooks) is a modest addition to the copy path and the data model.

Files to modify: `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`, `ProviderCapabilities`, `IAppDataStore` (new profile store)

**Benchmark gate:** run the full scenario matrix (SSDtoSSD → SameDriveTest → SSDtoHDD → SSDtoUSBFlash) via the Production Validation Pass. Promote static defaults only after all scenarios pass. Per-device learning is a separate incremental feature that can ship after static routing is in place.

---
