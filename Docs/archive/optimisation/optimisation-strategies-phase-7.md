# SmartCopy2026 — Phase 7: Parallelism

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

**Status:** Kept in reserve in case earlier phases yield insufficient performance gains.

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
