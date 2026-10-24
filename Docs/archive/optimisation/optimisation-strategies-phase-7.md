# SmartCopy2026 — Phase 7: Parallelism

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Status:** Kept in reserve in case earlier phases yield insufficient performance gains. Inter-phase overlap was prototyped and benchmarked on USB HDD → SATA HDD (2026-10-22): no gain, not promoted. See [the results below](#inter-phase-overlap-prototype-usb-hdd--sata-hdd-2026-10-22).

**Goal:** Add concurrency to the copy path. This is the most complex option — it complicates cancellation semantics, progress reporting, and error handling in ways that no sequential strategy does. It is listed last because it should be tried last.

Parallel copies introduce risks like HDD head thrashing and bus contention that could make performance unpredictable and very sensitive to the specifics of source, destination and file size. Some devices such as USB flash can be **permanently** damaged by excessive concurrency.

There are two possible forms of parallelism to test:
- **inter-phase parallelism** - Producer/Consumer queue where read buffers are filled then drained concurrently
- **intra-phase parallelism** - N concurrent reads then N concurrent writes, no simultaneous read/write phase

Both need to be tested with same-drive and different-drive source & target pairs.

**Benchmark gate:**
- Run per the standard protocol ([§4.1](optimisation-strategies.md#41-running-benchmarks)), randomised order. Parallelism variants are likely to exhibit higher variance — expect the 3rd-run threshold to trip more often.
- Must compare against the best strategies from earlier phases.
- Must not regress on SSDtoHDD.

---

## Inter-Phase Overlap Prototype: USB HDD → SATA HDD (2026-10-22)

**Question.** Live perf counters on a large-file copy from a USB 2.5" HDD (G:, Seagate Backup Plus, exFAT) to an internal SATA HDD (M:, WD 18 TB, NTFS) showed Explorer reading the source at 136 MiB/s (~2 MiB reads, queue depth ~1.7) and SmartCopy at 122 MiB/s (~720 KiB reads, queue depth ~1.3), with the source ~97% busy and the destination ~50% idle in both. `StreamCopyEngine` is strictly serial (await read, await write), so the hypothesis was that keeping a read in flight during each write would raise source queue depth and close the gap. The two observed copies used different files, so the gap itself was unconfirmed.

**Prototype (benchmark-only; no production change).** `StreamPumpPrototype` (in `SmartCopy.Benchmarks`) pumps one file between `SafeFileHandle`s using positional `RandomAccess` I/O. An unbuffered async `FileStream`, the production configuration, issues exactly these calls, so the control is the production loop. `BenchmarkCopyRunner.RunStreamPumpAsync` mirrors production's per-file mechanics (natural selection order, sequential-scan source, staged temp file + rename, no batching, no direct write) and swaps the pump via the variant's `prototypeStreamPump`:

| Variant | Pump |
|---|---|
| `P7_Serial_512K` | Control: await read, await write |
| `P7_Overlapped_512K` | Double-buffered: the read of chunk N+1 is in flight while chunk N is written (2 buffers, ≤1 read + ≤1 write outstanding) |
| `P7_Overlapped2Reads_512K` | As above with up to 2 reads in flight (3 buffers) |
| `Prod_Stream_512K_NoBatch` | Parity anchor: the real production runner (`StreamingCopyStrategy` + `StreamCopyEngine`), routing/batching/direct write off |

All variants use a 512 KiB buffer, so overlap is the only variable. The overlapped pump issues the read-ahead *before* the write, because NTFS completes file-extending writes synchronously even on an overlapped handle. A read issued after the write would not overlap it at all. A scratch harness verified byte-identical output for all three pumps across edge sizes (0, 1, 512 KiB ± 1, multi-chunk, 64 MiB) and clean cancellation.

**Cold cache without RAMMap.** The `clearCacheBetweenRuns`/`ramMapPath` keys in older configs are not read by the current harness. Cold runs depended on path pools and reboot prompts. The new config option `purgeSourceCacheBeforeRun` opens each selected source file once with `FILE_FLAG_NO_BUFFERING` before the copy. Windows flushes and purges a file's cached pages on a non-cached open, so no admin rights or reboot are needed. Verified by re-reading a file: cached ≈7 GiB/s, then back to cold speed after the purge on G: (exFAT, 7.1 GiB/s → 129 MiB/s), M: (NTFS HDD) and D: (NTFS NVMe). With the purge, the benchmark needs no path pools.

**Config.** [`configs/benchmark-phase7-overlapped-pump.json`](configs/benchmark-phase7-overlapped-pump.json). Dataset: one real 6.55 GiB `.7z` archive (`Last Life In The Universe.7z`, from the motivating `G:\DVDs` set) staged at `G:\TestData\Phase7DVDs`. Randomised variant order, 3 runs per variant, 30 s cooldown, source purged before every run. SSD→HDD and same-volume HDD scenarios remain in the config (disabled). They were not run, because the investigation is specifically the G→M SmartCopy/Explorer gap.

**Results: USBHDDtoHDD-G-M** (3 runs per variant)

| Variant | Median | Runs | Spread | Δ vs control | Verdict |
|---|---:|---|---:|---:|---|
| `P7_Serial_512K` (control) | 50.7 s | 55.7 / 50.5 / 50.7 s | 10.4%¹ | – | CONTROL |
| `P7_Overlapped_512K` | 50.9 s | 50.9 / 50.8 / 51.2 s | 0.7% | −0.5% | INCONCLUSIVE |
| `P7_Overlapped2Reads_512K` | 50.7 s | 50.7 / 50.7 / 50.3 s | 0.6% | +0.1% | INCONCLUSIVE |
| `Prod_Stream_512K_NoBatch` | 50.8 s | 51.2 / 50.7 / 50.8 s | 0.9% | −0.2% | INCONCLUSIVE |
| Windows `CopyFile2` (`File.Copy`, ad hoc, same purge + cooldown) | 50.6 s | 50.9 / 50.4 / 50.6 s | 1.0% | +0.2% | — |

¹ The 55.7 s outlier was the first run of the session. The other two serial runs sit inside the cluster every variant shares.

Every configuration copies the file at **≈132 MiB/s**. This equals a plain cold sequential read of a G: file (≈130 MiB/s) and the 136 MiB/s observed for Explorer. Production matches the prototype control, so the harness is at parity.

**GUI and Explorer confirmation (same staged file, cold).** A SmartCopy GUI copy to M: took ≈51 s. `Watch-CopyDiskIO.ps1` showed the source read at ~135 MiB/s, with G: ~98% busy and ~680 KiB average I/O. It dipped to ~128 MiB/s for ~13 s, and in that stretch average read latency rose from 6.0 to 6.35 ms while the source stayed saturated. M: wrote in lazy-writer bursts and was ~50% idle. Explorer on the same file showed the same profile, including the same dip. The dip is therefore drive-side (block placement or fragmentation), not the copy path. Earlier GUI readings of 127.6–128.7 MiB/s (shown as "MB/s" before the GUI's rate units were corrected) were taken inside that region on the original file. One GUI run read 104.6 and did not reproduce. It happened minutes after a 6.5 GB move onto M:, which suggests destination contention such as a Defender scan or writeback rather than the copy loop.

**Conclusions.**
1. **On the same file, SmartCopy already matches the Windows copy engine on this pair.** The production serial loop, both overlapped pumps, and `CopyFile2` are indistinguishable (all within 0.5 s of 50.7 s). The 122 vs 136 MiB/s gap seen live came from comparing different files, not from the copy loop. Transfer rate on a mechanical drive depends on where a file sits on the platter (outer tracks are faster) and how fragmented it is. That is likely to vary across the files in a large `G:\DVDs` archive set.
2. **The source drive is the ceiling.** The USB HDD is saturated at its sequential rate whether SmartCopy reads 512 KiB at queue depth ~1 or `CopyFile2` reads ~2 MiB at a higher queue depth. Read-ahead from `SequentialScan` already keeps the device busy, so extra outstanding reads have nothing left to fill.
3. **Overlap adds cost without benefit here.** It needs ~3–4 MB more allocation per run (task objects per chunk), plus the cancellation, buffer-lifetime and error-path complexity this phase warned about.

**Recommendation: do not promote an overlapped pump for cross-volume pairs.** It produces no measurable gain on the motivating case, and the gap it was meant to close does not reproduce on the same file. Keep the serial `StreamCopyEngine` loop. If a SmartCopy/Explorer gap is reported again, first reproduce it on the **same file**, cold, back-to-back (this config plus a `CopyFile2` timing does this in minutes) before attributing it to the copy path. A gap that survives a same-file A/B, especially on a source that is not already at its sequential ceiling, would justify re-running these variants. The prototype stays in the harness for that.
