# SmartCopy2026 — Phase 4: Progress Throttling

Back to [Optimisation Strategies](optimisation-strategies.md#5-detail-index). Current policy summary lives in [Current Policy & Open Questions](optimisation-strategies.md#2-current-policy--open-questions).

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
