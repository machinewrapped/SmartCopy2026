# Filesystem Watcher Plan

## Summary

This document is the authoritative design and progress tracker for Step `5.2.6` of the main implementation plan. V1 watcher support is intentionally limited to `LocalFileSystemProvider`.

The implemented direction is patch-based, not subtree replacement:
- `DirectoryWatcher` debounces raw `FileSystemWatcher` events, scans created/changed paths in the background, and queues ready-to-apply `DirectoryWatcherBatch` instances.
- `MainViewModel` decides when those pending batches are applied, especially around pipeline execution.
- `DirectoryTreeViewModel` applies batches synchronously to the live tree.
- `DirectoryTreePatcher` is a pure tree mutator. It does not perform filesystem IO.

## Purpose And Non-Goals

Purpose:
- Add watcher-driven incremental updates for local filesystem roots.
- Reuse the existing `MarkForRemoval` flow for deleted paths so the UI reflects removals immediately and commit stays explicit.
- Apply created/changed paths only after they have been scanned off-tree and are ready as concrete mutations.
- Keep watcher application serialized at well-defined sync points so pipeline execution and watcher updates do not modify the tree concurrently.
- Preserve user intent where safe: checked state on existing nodes, expanded state, and selected-node fallback when deleted nodes disappear.

Non-goals for v1:
- No watcher support for `MemoryFileSystemProvider`.
- No watcher support for future MTP providers.
- UNC/network shares are not an acceptance target; any behavior there is best-effort only.
- No direct live-tree mutation from raw `FileSystemWatcher` callbacks.
- No subtree replacement architecture.
- No selection or check-state transfer across external rename. Rename is treated as delete old + add new.
- No automatic full-rescan recovery path yet; overflow/error hardening remains a later milestone.

## Current Architecture Grounding

Relevant components:
- `DirectoryWatcher` wraps `.NET FileSystemWatcher`, debounces raw events, scans created/changed paths, and queues ready batches.
- `DirectoryScanner` builds `DirectoryTreeNode` snapshots from an `IFileSystemProvider`.
- `DirectoryTreePatcher` applies deletions and upserts to an existing tree without performing any filesystem IO.
- `DirectoryTreeNode` holds user intent and UI state such as `CheckState`, `IsExpanded`, selection relationships, and removal markers.
- `MainViewModel` owns watcher lifecycle and chooses when queued watcher batches are applied.
- `DirectoryTreeViewModel` owns tree loading and exposes a synchronous `ApplyWatcherBatch(...)` mutation entrypoint.

Architectural model:
1. Raw filesystem events arrive in `DirectoryWatcher`.
2. Deleted paths are recorded as deletions.
3. Created, changed, and rename-destination paths are queued for background scan.
4. After debounce, `DirectoryWatcher` builds one or more `DirectoryWatcherBatch` instances containing:
   - `Deletions`: relative-path targets
   - `Upserts`: relative-path targets plus prebuilt `DirectoryTreeNode` snapshot subtrees
5. `DirectoryWatcher` retains those ready batches internally and raises `PendingBatchesAvailable`.
6. `MainViewModel` drains and applies pending batches only when it is safe to do so.
7. `DirectoryTreeViewModel` applies a batch synchronously:
   - mark deletions immediately
   - insert or refresh scanned nodes
   - commit marked removals immediately
   - rebuild stats

## Decisions

Capability contract:
- `ProviderCapabilities` gains `CanWatch`.
- `LocalFileSystemProvider` sets `CanWatch = true`.
- `MemoryFileSystemProvider` sets `CanWatch = false`.

Watcher implementation:
- `DirectoryWatcher` remains a local-filesystem wrapper around `.NET FileSystemWatcher`.
- Watcher startup requires:
  - provider capability reports `CanWatch`
  - current root path is the active source root
- Ready-to-apply batch ownership lives in `DirectoryWatcher`, not `MainViewModel`.

Event handling:
- `Deleted` events become deletion patches immediately.
- `Created` and `Changed` events become scan targets.
- `Renamed` becomes delete old + scan new.
- Paths are normalized through the active `IFileSystemProvider`.
- Paths outside the current root are ignored.
- Redundant descendant scan targets are collapsed before scanning.

Patch semantics:
- Delete uses the existing `MarkForRemoval` mechanism.
- Upsert means:
  - refresh metadata in-place when the same-path node already exists with the same kind
  - otherwise insert a cloned snapshot subtree under the nearest existing parent
- Removals are always committed immediately when a batch is applied. Batch application is an explicit sync point.
- `DirectoryTreePatcher` does not scan and does not consult the provider.

Selection and rename handling:
- If the selected node is deleted, selection falls back to its parent, or the root when needed.
- External rename does not preserve selection or check-state onto the new path.
- Existing checked ancestors continue to influence inserted descendants via normal parent-based insertion rules.

Concurrency model:
- `DirectoryWatcher` owns raw-event buffering and ready-batch buffering.
- `MainViewModel` owns watcher lifecycle and apply timing.
- Watcher application is deferred while pipeline execution is in progress.
- After pipeline completion, the tree commits pipeline removals first, then drains pending watcher batches.
- This preserves the existing `MarkForRemoval` safety model and avoids concurrent mutation during traversal.

Error handling:
- Watcher overflow/error must not crash the app.
- Overflow/error should log a warning and schedule a full rescan of the current root.
- This fallback path is planned but not fully implemented yet.

## Implementation Outline

### 1. Capability Gating And Watcher Lifecycle

- Extend `ProviderCapabilities` with `CanWatch`.
- Add capability values to existing providers.
- Start and stop the watcher with source-root changes.
- Refuse watcher startup for unsupported providers.

Status: complete

### 2. Debounced Event Collection And Background Scan

- Collect raw filesystem events in `DirectoryWatcher`.
- Separate deletions from scan targets.
- Normalize provider-relative paths.
- Remove redundant descendant scan targets.
- Build pre-scanned upsert snapshots off-tree before surfacing them.

Status: complete

### 3. Patch Application Model

- Represent watcher output as `DirectoryWatcherBatch`.
- Apply deletes through `MarkForRemoval`.
- Apply creates/changes as scanned upserts.
- Commit removals immediately as part of synchronous batch application.
- Keep `DirectoryTreePatcher` pure and scan-free.

Status: complete

### 4. Apply Orchestration And Pipeline Coordination

- Let `DirectoryWatcher` own pending ready batches.
- Let `MainViewModel` decide when those batches are drained.
- Defer watcher apply while the pipeline is running.
- Drain pending watcher batches after pipeline completion.

Status: complete

### 5. Filter And Stats Refresh Integration

- Rebuild aggregate stats after each applied watcher batch.
- Keep file-list/status updates flowing through existing view-model refresh paths.
- Reapply filters after watcher-driven updates when required for correctness.

Status: partial

Current state:
- stats are rebuilt after patch application
- status refresh is triggered after drained batches
- whole-tree filter reapply is not wired yet

### 6. Error Fallback And UX Hardening

- Log watcher errors clearly.
- Escalate overflow/error to full rescan.
- Handle deleted root, inaccessible directories, and rapid rename/delete churn without unhandled exceptions.

Status: partial

Current state:
- watcher errors are surfaced to `MainViewModel` and logged via debug output
- full-rescan fallback is not wired yet

### 7. Validation

- Add automated coverage for patch application and selection behavior.
- Run manual smoke scenarios against a real local directory tree with nested changes and burst churn.

Status: partial

Current state:
- automated tests exist and are green
- manual smoke validation is still outstanding

## Milestones

1. Capability gating and watcher lifecycle
2. Debounced event collection and background scan
3. Patch-based tree mutation
4. Pipeline-aware apply orchestration
5. Filter/stat refresh integration
6. Error fallback and warning logging
7. Automated and manual validation

## Verification Checklist

Automated tests:
- `DirectoryWatcher` debounce/coalescing emits one batch for burst events.
- Created/changed paths are scanned off-tree before they are applied.
- Deleted paths use `MarkForRemoval` and are committed safely at apply time.
- Existing checked/expanded/selected state remains stable when unaffected nodes remain in place.
- Selected node is reassigned safely when the selected node is deleted.
- Rename behaves as delete old + add new, without selection transfer.
- Capability gating prevents watcher startup for unsupported providers.
- Watcher `Error` full-rescan fallback path is still to be added.

Manual smoke:
- Create, rename, and delete files under a deep local subtree.
- Trigger burst file churn in nested folders without causing UI thrash.
- Delete or rename an expanded checked folder.
- Run a long pipeline operation while external changes accumulate, then confirm pending watcher batches apply safely afterward.
- Confirm filter state and selection counts remain stable after incremental updates.
- Simulate watcher overflow/error and confirm graceful full refresh once fallback is implemented.

## Progress Tracker

Status:
- [x] Capability contract updated with `CanWatch`
- [x] Watcher lifecycle wired into `MainViewModel`
- [x] `DirectoryWatcher` debouncing and background scan implemented
- [x] Ready-to-apply batch queue moved into `DirectoryWatcher`
- [x] Patch-based tree mutation implemented
- [x] Immediate removal commit integrated with watcher apply
- [x] Pipeline-aware watcher apply deferral implemented
- [x] Automated tests added and passing
- [ ] Whole-tree filter reapply after watcher updates
- [ ] Error fallback and warning logging hardening
- [ ] Manual smoke scenarios completed

Notes:
- The implementation intentionally prefers explicit synchronization points over opportunistic timer-driven commits.
- Cross-platform runtime support remains bounded to local filesystem providers even though `.NET FileSystemWatcher` itself is cross-platform.
- If future performance work is needed, optimize batch building or patch diffing before reintroducing subtree replacement semantics.
