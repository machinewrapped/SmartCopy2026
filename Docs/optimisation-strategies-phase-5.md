# SmartCopy2026 — Phase 5: Buffer Size Scaling (independent track)

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Status:** Cross-drive validation complete (2026-06-07, 8 scenarios, 519 runs); USB validation complete (2026-06-08, see Phase 6). The cross-drive suite confirmed: preallocation retracted universally; 1 MiB for SSD; 512 KiB for HDD and unknown. USB validation found 1 MiB is also the USB optimum, but for different reasons (non-monotonic response; 512 KiB regresses on USB). Later SameDriveHDD production-path stream-only sweeps (2026-07-02) show drive-specific behavior below 512 KiB: one mechanical drive weakly favoured 64 KiB, another favoured 256 KiB and made 64 KiB slowest. The optional SameDriveSSD larger-buffer probe (Step 3 below) remains open.

**Goal:** Find the optimal buffer size for large-file streaming. The MixedDataset cannot isolate this — buffer changes are lost in the noise of 13,000 tiny files.

## Final Phase 5 Findings — Buffer Sweep & Pre-allocation (2026-06-06)

Phase 5 was run against the `LargeFileDataset` using a fully converged sweep across `SSDtoSSD`, `SameDriveSSD`, `SSDtoHDD`, `SameDriveHDD`, and `HDDtoHDD`.

| Scenario | Strongest supported buffer | Pre-allocation effect | Notes |
|---|---|---|---|
| `SSDtoSSD` | 1 MiB – 2 MiB | Regresses (up to -4.1%) | Larger buffers (1-2 MiB) provide ~22% boost. Pre-allocation acts as a blocking tax and should be avoided. |
| `SameDriveSSD` | 1 MiB – 8 MiB | Flat / Noise | Similar to cross-drive SSD. 8 MiB shows a peak at the run level, but 1-2 MiB is safer. Pre-allocation does nothing. |
| `SSDtoHDD` | 512 KiB – 1 MiB | +11.6% | Buffer size alone does not improve throughput and actively regresses at 4-8 MiB. Pre-allocation is highly effective here, unlocking a ~12-20% throughput increase. |
| `SameDriveHDD` | 512 KiB – 1 MiB | Regresses (~ -1.5%) | Both buffer size scaling and pre-allocation regress throughput. Remain conservative. |
| `HDDtoHDD` | 512 KiB – 1 MiB | Flat / Noise | Buffer size and pre-allocation both fall within statistical noise (±1%). Throughput is mechanically constrained. |

**Provisional Conclusion (superseded by the cross-drive run below for the preallocation finding):**
We have weak evidence for a destination-sensitive routing policy (Phase 6):
1.  **Fast Destinations (SSD-to-SSD, SameDriveSSD):** Use `1 MiB` or `2 MiB` buffer. Pre-allocation **OFF**.
2.  **Slow Destinations (SSD-to-HDD):** Use `512 KiB` or `1 MiB` buffer. Pre-allocation **ON**.
3.  **Contended HDD (SameDriveHDD, HDDtoHDD):** Use `1 MiB` buffer. Pre-allocation **OFF**.

`1 MiB` is the safest cross-device large-file default candidate. It is close to the top of the pack on SSD, performs well on HDD, and avoids the major regressions seen with 4–8 MiB on HDD scenarios. `512 KiB` remains a defensible conservative fallback where destination type is unknown or progress reporting frequency needs to be more frequent.

**SameDrive caveat:** Same-drive SSD copies show a signal for larger buffers, even though mechanical seek overhead does not apply. The mechanism is unknown, and reliable SSD-vs-HDD classification is probably not guaranteed across platforms, so we would need more evidence to promote a larger SameDrive buffer.

> [!NOTE]
> **Revised 2026-06-07 — Cross-Drive Validation:** The SSD-to-HDD preallocation finding (+11.6%) has been retracted. Two independent cross-drive SSD-to-HDD drive pairs both show preallocation regresses (-2% to -4% at run level). The prior finding was specific to a single drive pair and does not generalise. The SSD-to-HDD default is revised to **512 KiB buffer, preallocation OFF**. All other conclusions above remain directionally valid.

## Cross-Drive Validation (2026-06-07)

519 runs from 8 cross-drive scenarios on `LargeFileDataset`, covering all four drive-pair directions across three physical SSDs (D, O, R) and three HDDs (E, M, additional). 3 runs per variant per scenario, randomised order. Variants: `BaselineAuto256KiB`, `ManualLoop512KiBArrayPool/Preallocate`, `ManualLoop1MiBArrayPool/Preallocate`, `ManualLoop4MiBArrayPool/Preallocate`. This 8-scenario suite is the authoritative Phase 5 result — it provides better generalisation than the original same-machine run.

**Run-level summary matrix:**

| Scenario | 512 KiB | 512 KiB + Pre | 1 MiB | 1 MiB + Pre | 4 MiB | 4 MiB + Pre |
|---|---|---|---|---|---|---|
| SSD_R_to_SSD_D | PASS +10% | INCONCLUSIVE | PASS +14% | INCONCLUSIVE | PASS +19% | BELOW_THRESHOLD |
| SSD_D_to_SSD_R | BELOW_THRESHOLD | INCONCLUSIVE | BELOW_THRESHOLD | PASS +3% | PASS +5.5% | REGRESSION |
| SSD_D_to_SSD_O | BELOW_THRESHOLD | BELOW_THRESHOLD | PASS +3.7% | INCONCLUSIVE | BELOW_THRESHOLD | BELOW_THRESHOLD |
| SSD_O_to_HDD_E | PASS +3.2% | REGRESSION | BELOW_THRESHOLD | BELOW_THRESHOLD | PASS +3.3% | REGRESSION |
| SSD_R_to_HDD_E | INCONCLUSIVE | REGRESSION | REGRESSION | REGRESSION | INCONCLUSIVE | REGRESSION |
| HDD_E_to_SSD_O | REGRESSION | REGRESSION | REGRESSION | PASS†  | REGRESSION | REGRESSION |
| HDD_M_to_HDD_E | REGRESSION | BELOW_THRESHOLD | REGRESSION | INCONCLUSIVE | REGRESSION | BELOW_THRESHOLD |
| HDD_E_to_HDD_M | INCONCLUSIVE | REGRESSION | INCONCLUSIVE | REGRESSION | REGRESSION | REGRESSION |

† Suspicious: the non-prealloc 1 MiB variant regresses -6.1% in the same scenario. This PASS is likely variance, not a real effect.

**Key findings:**

1. **Preallocation is retracted universally.** The same-machine run identified +11.6% preallocation benefit for SSD-to-HDD from a single drive pair. Two independent cross-drive SSD-to-HDD scenarios both show preallocation regresses at run level (-2% to -4%). No other scenario shows a reliable positive preallocation effect. The SSD-to-HDD finding does not generalise; it was specific to one drive pair. Preallocation should default OFF for all destination types.

2. **"SSD" is not a uniform device class — buffer scaling is drive-pair specific.** SSD_R→SSD_D shows a strong monotonic curve (PASS at all three buffer sizes; peak +19% at 4 MiB). The same two drives reversed (D→R) show only +5.5% at 4 MiB. A third SSD pair (D→O) shows only +3.7% at 1 MiB. Prior phases used SSD_R→SSD_D as the primary SSD directional indicator; this drive pair represents a best-case NVMe scenario and over-estimates the general SSD opportunity. **1 MiB is the buffer size that passes in 2 of 3 SSD-to-SSD scenarios and is the safe universal SSD promotion.**

3. **Buffer scaling for SSD-to-HDD is small and inconsistent.** SSD_O→HDD_E shows +3.2% at 512 KiB and +3.3% at 4 MiB without preallocation. SSD_R→HDD_E shows nothing passes. The HDD write head is the bottleneck; application buffer size above 512 KiB does not reliably improve throughput. **512 KiB is the defensible SSD-to-HDD choice.**

4. **HDD read speed caps all HDD-source scenarios.** HDD_E→SSD_O, HDD_M→HDD_E, and HDD_E→HDD_M all show regression or inconclusive results across every tested variant. The disk's mechanical read throughput is the constraint regardless of application buffer size. The 256 KiB baseline is as effective as any larger variant; applying larger buffers actively regresses in some pairs.

5. **SSD_R_to_SSD_D over-represented the SSD opportunity in prior phases.** It is a real data point for high-throughput NVMe-to-NVMe pairs, but reversing the same two drives halves the observed gain (19% → 5.5%), and a third SSD pair shows only 3.7%. The strong curve seen in earlier phases was partially a property of that specific drive pair, not a general SSD characteristic.

6. **SameDriveHDD buffer response is drive-specific below 512 KiB.** A 2026-07-02 production-path sweep on `LargeFileDataset` isolated streaming by disabling direct-write and batching. Drive A formed one tight cluster and weakly favoured `64 KiB` (3m 7s median), with `128/256 KiB` at 3m 10s and `512 KiB/1 MiB` at 3m 14s. Drive B did not repeat that: `256 KiB` led at 1m 41s, `512 KiB` followed at 1m 43s, `128 KiB` at 1m 44s, `1 MiB` at 1m 45s, and `64 KiB` was slowest at 1m 49s. The shared conclusion is that `2/4 MiB` is not useful for SameDriveHDD and `64 KiB` is not a safe default. If SameDriveHDD gets a special buffer, `256 KiB` is the only smaller-buffer candidate still worth validating against the current 512 KiB fallback.

**Revised provisional policy (SSD/HDD/Unknown — USB added in Phase 6):**

| Destination Profile | Buffer | Preallocation | Notes |
|---|---|---|---|
| SSD destination | 1 MiB | **OFF** | 4 MiB may benefit on fast NVMe pairs but cannot be classified from software |
| HDD destination | 512 KiB | **OFF** | 1 MiB provides no consistent benefit; regresses on some HDD pairs. Same-volume HDD has conflicting sub-512 KiB signals; 256 KiB is the only smaller-buffer challenger worth full-policy validation. |
| Unknown / ambiguous | 512 KiB | **OFF** | Conservative; does not assume SSD behaviour |

**Preallocation is OFF universally.** No drive pair or scenario in this suite shows a reliable positive preallocation effect at run level. Do not re-enable preallocation without a controlled rerun that isolates the `SetLength` contribution for a specific destination type.

> [!NOTE]
> **USB validation:** USB has a distinct non-monotonic buffer-size response not predictable from SSD/HDD data; the cross-drive conclusions above do not apply to USB. See Phase 6.

## Discovery steps (method)

**Step 1 — Buffer size sweep:**

Run `ManualLoop512KiB`, `ManualLoop1MiB`, `ManualLoop2MiB`, and `ManualLoop4MiB` — all with `ManualLoop` mode, `ArrayPool` enabled, and `PreallocateDestinationFile = false` — against the byte-volume-dominated dataset on SSDtoSSD and SSDtoHDD, per the standard protocol ([§4.1](optimisation-strategies.md#41-running-benchmarks)), randomised order. Measure throughput at each buffer size for buckets 4 MiB+.

`ManualLoop128KiB` and `ManualLoop256KiB` are low-value rerun candidates: the initial run already showed 128 KiB regressing and 256 KiB failing to provide meaningful improvement over control. `ManualLoop8MiB` is a SameDriveSSD-only follow-up candidate because it regressed on HDD destinations.

Gate: buffer >1 MiB shows ≥5% P50 improvement over 1 MiB → promote as new large-file default.

Current provisional default candidate: **1 MiB**. It is close to the best observed SSD throughput, avoids HDD regressions, and is safer than larger buffers when destination media cannot be classified reliably. `512 KiB` remains the conservative fallback if the clean rerun shows 1 MiB is not consistently better beyond variance.

**Step 2 — Preallocation (settled — retracted):**

The intent was to isolate `PreallocateDestinationFile` from buffer size (the two were confounded in `ManualLoop1MiBPreallocate`). This question was answered by the cross-drive validation, whose `+ Pre` variants show **no reliable positive effect on any drive pair** — the one apparent +11.6% SSD→HDD gain came from a single drive pair and did not generalise. Preallocation is off by default universally.

**Step 3 — SameDriveSSD larger-buffer probe (open):**

Only after Step 1 confirms the safe cross-device default, run `ManualLoop1MiB`, `ManualLoop2MiB`, `ManualLoop4MiB`, and optionally `ManualLoop8MiB` on SameDrive SSD, randomised, to confirm whether the same-drive larger-buffer signal (an 8 MiB peak appeared in the same-machine sweep) is real and reproducible. Preallocation is settled off (Step 2), so there is no with/without arm — this is purely a buffer-size question. Treat the result as an optional specialised profile, not a general SameDrive default. If drive type cannot be classified confidently, fall back to the safe 512 KiB–1 MiB range. The cross-drive data shows 1 MiB is the safe universal SSD default; same-drive SSD requires dedicated measurement before any larger buffer default is promoted.

**Baseline reset:** `ManualLoop512KiBArrayPool` is the new benchmark control for Phase 5/6 buffer-sizing runs. The 4 KiB `CopyToAsync` control (`BaselineAuto` / `Control_BaselineAuto`) is excluded from those runs — definitively suboptimal, it would only flatten the comparison scale. However, `BaselineAuto` should be **retained in the MixedDataset policy validation run** as a legacy anchor: the before/after gain on realistic data is the headline result that justifies the whole programme of work, and it should be readable from a single run. `CopyToAsync512KiB` is retired — it provided no unique signal beyond the ManualLoop variants.

---
