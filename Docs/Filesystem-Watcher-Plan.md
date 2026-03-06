# Filesystem Watcher Plan

## Summary

This document is the authoritative design and progress tracker for Step `5.2.6` of the main implementation plan. V1 watcher support is intentionally limited to `LocalFileSystemProvider`. Watcher events are treated as invalidation hints that trigger targeted refreshes; they do not mutate the tree directly.

## Purpose And Non-Goals

Purpose:
- Add watcher-driven incremental refresh for local filesystem roots.
- Preserve user intent during refreshes: checked state, expanded state, active selection, and filter-derived view state.
- Keep UI responsive during bursty filesystem changes by debouncing and coalescing updates.
- Provide a safe fallback to full rescan when watcher state becomes unreliable.

Non-goals for v1:
- No watcher support for `MemoryFileSystemProvider`.
- No watcher support for future MTP providers.
- UNC/network shares are not an acceptance target; any behavior there is best-effort only.
- No direct in-place tree mutation from raw `FileSystemWatcher` events.
- No subtree-only filter re-evaluation requirement if correctness is simpler with whole-tree reapply.

## Current Architecture Grounding

Relevant components:
- `DirectoryWatcher` batches raw filesystem events behind a debounce window.
- `DirectoryScanner` builds `DirectoryTreeNode` instances from an `IFileSystemProvider`.
- `DirectoryTreeNode` holds user intent and derived UI state such as `CheckState`, `IsExpanded`, and filter result.
- `DirectoryTreeViewModel` currently owns full-root scans and will own watcher lifecycle plus incremental refresh orchestration.

Architectural model:
- The watcher reports "something under this path changed".
- The tree layer reduces those hints to one or more affected subtree roots.
- Each affected subtree is rescanned from the provider and merged back into the existing tree.
- User state is restored by relative path after replacement.
- If the watcher overflows or errors, the system logs a warning and schedules a full rescan.

## Decisions

Capability contract:
- `ProviderCapabilities` gains `CanWatch`.
- `LocalFileSystemProvider` sets `CanWatch = true`.
- `MemoryFileSystemProvider` sets `CanWatch = false`.

Watcher implementation:
- `DirectoryWatcher` remains a local-filesystem wrapper around `.NET FileSystemWatcher`.
- Watcher startup requires all of:
  - provider resolves to `LocalFileSystemProvider`
  - provider capability reports `CanWatch`
  - root path exists
  - root path is a directory

Event handling:
- Paths are normalized before any tree lookup.
- Paths outside the current root are ignored.
- Nested paths are collapsed to the shallowest affected ancestor inside the current tree.
- If an event path cannot be mapped to an existing ancestor confidently, the system requests a full rescan.
- Rename events invalidate both old and new paths.
- If rename ancestry is ambiguous, refresh the parent directory subtree.

Error handling:
- Watcher overflow/error must not crash the app.
- Overflow/error logs a warning.
- Overflow/error schedules a full rescan of the current root.

State preservation:
- Before replacing a subtree, capture state for that subtree keyed by `CanonicalRelativePath`.
- Preserve `CheckState`, `IsExpanded`, and selected-node identity.
- Restore state onto matching replacement nodes by `CanonicalRelativePath`.
- Missing nodes remain missing; there is no synthetic recovery for deleted or renamed paths.

Refresh behavior:
- File changes inside an existing directory should refresh only the impacted subtree, not the full root.
- The initial implementation may reapply filters to the whole tree after a watcher batch if that keeps correctness simple.
- Tree statistics should be rebuilt once after each processed watcher batch.

## Implementation Outline

### 1. Capability Gating And Watcher Lifecycle

- Extend `ProviderCapabilities` with `CanWatch`.
- Add capability values to existing providers.
- Make `DirectoryTreeViewModel` create, start, stop, and dispose the watcher with root changes.
- Refuse watcher startup for unsupported providers or unsuitable paths.

### 2. Path Batch Reduction And Invalidation Model

- Accept a batch of changed full paths from `DirectoryWatcher`.
- Normalize paths and discard any outside the current root.
- Collapse descendants so one parent refresh covers nested child paths.
- Escalate to full rescan when no safe existing ancestor can be resolved.

### 3. Subtree Snapshot, Replace, And Restore

- Introduce a small subtree state snapshot keyed by canonical relative path.
- Capture state immediately before subtree replacement.
- Rescan the affected subtree via the active provider.
- Replace the subtree in-place within the current root.
- Restore `CheckState`, `IsExpanded`, and selected-node identity where paths still match.

### 4. Filter And Stats Refresh Integration

- Reapply filters after watcher-driven updates.
- Whole-tree filter reapply is acceptable for v1.
- Rebuild aggregate stats once per processed batch.
- Ensure selection/status bar/file-list updates follow the same existing refresh paths as manual scans.

### 5. Error Fallback And UX Hardening

- Surface watcher error state as a warning log entry.
- Schedule a full rescan instead of attempting partial recovery after overflow/error.
- Handle deleted root, inaccessible directories, and rapid rename/delete churn without unhandled exceptions.

### 6. Validation

- Add automated coverage for batching, subtree targeting, state preservation, and full-rescan fallback.
- Run manual smoke scenarios against a real local directory tree with nested changes and burst churn.

## Milestones

1. Capability gating and watcher lifecycle
2. Path batch reduction and invalidation model
3. Subtree snapshot/replace/restore logic
4. Filter/stat refresh integration
5. Error fallback and UX/logging hardening
6. Automated coverage and manual smoke validation

## Verification Checklist

Automated tests:
- `DirectoryWatcher` debounce/coalescing emits one batch for burst events.
- Nested changed paths reduce to one ancestor refresh target.
- Subtree replacement preserves checked state for unaffected matching descendants.
- Expanded directories remain expanded after subtree refresh.
- Selected node is preserved when still present and cleared or reassigned safely when removed.
- Create/delete/rename in nested directories updates only the impacted subtree.
- Watcher `Error` triggers the full-rescan path without exception or crash.
- Capability gating prevents watcher startup for unsupported providers.

Manual smoke:
- Create, rename, and delete files under a deep local subtree.
- Trigger burst file churn in nested folders without causing UI thrash.
- Delete or rename an expanded checked folder.
- Simulate watcher overflow/error and confirm graceful full refresh.
- Confirm filter state and selection counts remain stable after incremental updates.

## Progress Tracker

Status:
- [x] Capability contract updated with `CanWatch`
- [x] Watcher lifecycle wired into `DirectoryTreeViewModel`
- [ ] Batched path reduction implemented
- [ ] Incremental subtree rescan and replacement implemented
- [ ] Selection/expanded-state restore implemented
- [ ] Filter/stat refresh integration completed
- [ ] Error fallback and warning logging completed
- [ ] Automated tests added
- [ ] Manual smoke scenarios completed

Notes:
- Whole-tree filter reapply after each watcher batch is the default v1 choice unless subtree-only reapply becomes trivial during implementation.
- Cross-platform runtime support remains bounded to local filesystem providers even though `.NET FileSystemWatcher` itself is cross-platform.
