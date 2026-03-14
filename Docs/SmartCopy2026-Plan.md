# SmartCopy2026 — Design & Implementation Plan

**Prepared:** 2026-02-18
**Predecessor:** SmartCopy 2015 (GPL v3, .NET 4.8 WinForms, SourceForge)

This document is the execution plan for the SmartCopyTool rewrite.

## Table of Contents

Reference:
1. [What the Predecessor Got Right](#1-what-the-predecessor-got-right)
2. [What to Fix and Improve](#2-what-to-fix-and-improve)
3. [Technology Stack](#3-technology-stack)
4. [UI Design Reference](#4-ui-design-reference)
5. [Implementation Phases](#5-implementation-phases)
6. [Open Questions](#6-open-questions)

---

## 1. What the Predecessor Got Right

Before redesigning, preserve these patterns:

- **Background worker pattern** — clean separation of long-running operations with pause/resume,
  cancellation, progress reporting, and per-operation logging
- **Filesystem wrapper model** — wrapping raw `FileInfo`/`DirectoryInfo` with application state
  (checked, filtered, removed) was the right instinct; `DirectoryTreeNode` continues this
- **Wildcard → Regex** — simple, effective, semicolon-delimited multi-pattern matching
- **Selection persistence** — txt and m3u formats with relative paths, portable across machines
- **Mirror detection options** — name-only / name+size / extension-agnostic comparison modes
- **Operation breadth** — copy, move, delete, flatten, sync, merge, find-orphans all have real uses

---

## 2. What to Fix and Improve

| Problem | Solution |
|---|---|
| Windows-only (WinForms) | Cross-platform UI framework (Avalonia) |
| Monolithic MainForm (~2360 lines) | MVVM + clean architecture with separate projects |
| Filters are imperative, one-shot mutations | Composable declarative filter chain, saveable |
| Only per-file copy progress | Streaming copy with byte-level progress |
| No MTP device support | `IFileSystemProvider` abstraction + Windows MTP provider |
| No format conversion | Plugin-based `ConvertStep` in transform pipeline |
| No preview mode | Dry-run produces an `OperationPlan` before execution |
| No filesystem watch | `FileSystemWatcher` + debounce + incremental rescan |
| Settings as XML `.ini` | `appsettings.json` with strongly-typed classes |
| `BackgroundWorker` threading | async/await + `IProgress<T>` + `CancellationToken` |
| Hard-coded operation types | Composable transform pipeline |
| Binary (checked/unchecked) only | Tri-state checkboxes with proper propagation |
| No safety net for destructive ops | Trash/recycle-bin by default, mandatory preview for deletes |

---

## 3. Technology Stack

### Decisions and rationale

| Concern | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | LTS release (3-year support); best async/IO; cross-platform; single-file publish |
| UI | **Avalonia UI 11+** | Best cross-platform .NET desktop framework; MVVM-first; hardware-accelerated |
| MVVM | **CommunityToolkit.Mvvm** | Source-generator based; no runtime reflection overhead |
| DI | **Microsoft.Extensions.DependencyInjection** | Standard; lightweight |
| Config | **System.Text.Json** | Built-in; fast; no extra dependency |
| MTP (Windows) | **MediaDevices** | Wraps Windows WPD API; Windows only; abstracted behind `IFileSystemProvider` |
| Plugins | **AssemblyLoadContext** | Built-in isolation; exactly suited for per-plugin loading |
| Testing | **xUnit + NSubstitute** | Standard .NET test stack; NSubstitute mocks `IFileSystemProvider` cleanly |
| Logging | **Serilog** | Structured rolling-file logs; makes operation journal parsing trivial; no need to reinvent |
| License | **MIT** | Clean break from predecessor's GPL v3 |

### Cross-platform capability matrix

Not all features are available on all platforms. This is fine — the app adapts gracefully.

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Local filesystem | Yes | Yes | Yes |
| MTP device access | Yes (MediaDevices) | No | No |
| Filesystem watcher | Yes | Yes | Yes (with caveats on some FS types) |
| Trash/recycle bin | Yes (`FileSystem.DeleteFile` + `SendToRecycleBin`) | Yes (freedesktop trash spec) | Yes (`NSFileManager.trashItem`) |
| Single-file publish | Yes | Yes | Yes |
| Path case sensitivity | Case-insensitive (NTFS default) | Case-sensitive | Case-insensitive (APFS default) |

**Path matching semantics:** 
Path comparisons for filter matching should follow idiomatic behavior for the host sytem.

### Avalonia tree performance note

Avalonia's `VirtualizingStackPanel` handles large lists efficiently, but propagating tri-state
checkbox state across a deep tree of 100k+ nodes requires deliberate care. The state propagation
algorithm must be efficient (see Architecture `Tri-State Checkbox Propagation`). This is an implementation concern,
not a reason to switch framework.

**TreeDataGrid:** Avalonia's `TreeDataGrid` control (stable in 11+) may outperform a hand-rolled
`TreeView` + column layout for the file list pane. Evaluate it during Step 1 shell construction;
it provides built-in column resizing and sort headers that would otherwise need custom controls.

### .NET 10 targeting

Use `net10.0` TFM. .NET 10 is LTS (supported until ~2028). If building on a machine with only .NET 9
installed, `global.json` can pin the SDK version. Self-contained publish (`--self-contained true
-p:PublishSingleFile=true`) bundles the runtime and ships a single executable per platform.

---
## 4. UI Design Reference

Canonical UI/interaction behavior lives in `Docs/UI+UX.md`. This plan tracks implementation and validation only.

---
## 5. Implementation Phases

### 5.1 Phase 1: Core Workflows (COMPLETE)

*Goal: ship a v1 prototype that can scan, select, filter, preview, copy/move/delete to validate architecture and UX.*

Prove and refine UX using a mock file system (`MemoryFileSystemProvider`) first; seeded `/mem` remains the default integration source until the end-to-end flow is proven.

**Phase 1 status (complete as of 2026-02-28)**

| Workstream item | Status | Next action |
|---|---|---|
| UX-1 (Step 1): Baseline shell | Complete | Baseline for regression checks |
| UX-2 (Step 3): Node selection logic | Complete |
| UX-3 (Step 4): Filter chain | Complete |
| UX-4 (Step 5): Transform pipeline | Complete |
| UX-5 (Step 6): Workflow presets and menu | Complete |
| Hardening-1 (Step 2): Memory provider foundation | Complete |
| Hardening-2 (Step 7): Selection save/load | Complete |
| Hardening-3 (Step 8): Settings persistence | Complete | — |
| Polish-1 (Step 9): Shell observability + status | Complete | — |
| Polish-2 (Step 10): Keyboard + accessibility | Complete |

#### UI/UX Validation

UI/UX checklist:
- [x] Main window with correct proportions, resizable split panes (3-column: Filters/Folders/Files)
- [x] Window size, position, maximised state, and column widths persisted to
      `%LOCALAPPDATA%/SmartCopy2026/window.json`; restored on next open with off-screen safety guard
- [x] Source field with browse button (no real browsing yet)
- [x] Bookmarks/favorites for source field; editable ComboBox with `RecentSources`/`FavouritePaths` persistence
- [x] TreeView with tri-state checkbox behaviour
- [x] FileListView with column resizing
- [x] Filter chain area: filter cards with human-readable summary + technical subtitle,
      enable/disable checkbox, edit button, remove button, "+ Add filter" card
- [x] Filter chain save/load (`.sc2filter`) + preset library
- [x] Pipeline area: horizontal scrollable step chain with + Add step flyout + Run and Preview buttons
- [x] Pipeline save/load (`.sc2pipe`) + preset library
- [x] Status bar: file count, size, filtered count, progress bar, time remaining, current file
- [x] Operation progress overlay: progress bars, pause/cancel buttons, status labels
- [x] Log panel: collapsible, scrollable, log entries
- [x] Full keyboard navigation: Tab order, arrow keys in tree/list, focus indicators
- [x] Automation properties on all interactive controls

#### 5.1.1 — Project Scaffold + Baseline UI Shell

Deliverables:
- [x] Solution and projects exist: `SmartCopy.Core`, `SmartCopy.App`, `SmartCopy.UI`, `SmartCopy.Tests`
- [x] DI bootstrapping in `AppServiceProvider.cs`
- [x] UI shell layout and persisted window/column state
- [x] CI workflow runs build + tests on Windows and Linux

Acceptance criteria:
- [x] App launches and renders correctly on Windows
- [x] Layout is persisted and restored on Windows

Verification:
- [x] `dotnet build SmartCopy.App/SmartCopy.App.csproj`
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj`

#### 5.1.2 — Core Models + MemoryFileSystemProvider (test-first foundation)

Deliverables:
- [x] Implement `FileSystemNode` full model contract from Architecture `Data Models`
- [x] Implement `IFileSystemProvider` + `ProviderCapabilities`
- [x] Implement `MemoryFileSystemProvider` for fast hermetic tests
- [x] Create shared test fixtures/builders

Acceptance criteria:
- [x] Memory provider supports filesystem contract without disk I/O
- [x] Scanner/filter/pipeline core tests in this step run against `MemoryFileSystemProvider`

Verification:
- [x] Unit tests for `MemoryFileSystemProvider`
- [x] Contract tests for enumerate/read/write/move/delete/create/exists on in-memory trees

#### 5.1.3 — Node Selection Logic

Deliverables:
- [x] Tri-state propagation algorithm from Architecture `Tri-State Checkbox Propagation`
- [x] `IsSelected` wiring (`CheckState == Checked && FilterResult == Included`)

Acceptance criteria:
- [x] Parent/child state transitions are deterministic and O(height) upward
- [x] No per-node `PropertyChanged` storm during bulk updates (batched updates)

Verification:
- [x] Unit tests for checked/unchecked/indeterminate transitions

#### 5.1.4 — Filter Chain

Full filter chain implementation delivered across six sub-steps. Implementation details and UI flows are in Architecture and UI+UX documentation.

Delivered:
- [x] Core filter engine: `IFilter`, `FilterChain`, `FilterConfig`, `FilterChainConfig`
- [x] All Phase 1 filter types: Wildcard, Extension, Mirror, DateRange, SizeRange, Attribute
- [x] `Only | Add | Exclude` ordered set-based evaluation semantics
- [x] `FilterPresetStore` with built-in presets (Audio, Images, Documents, Log files, Temp files)
- [x] `FilterEditorViewModel` hierarchy — one editor per type with `BuildFilter`/`LoadFrom` round-trips
- [x] Add-Filter two-level drill-down flyout (type → preset/MRU picker)
- [x] `EditFilterDialog` modal with mode toggle, type-specific editors, save-as-preset
- [x] `FilterChainViewModel` live wiring
- [x] Live filter application to tree + file list
- [x] `FilterResultOpacityConverter`, `ShowFilteredFiles` toggle, excluded-node checkbox disabling

Acceptance criteria — all met:
- [x] `Only`/`Add`/`Exclude` semantics match Architecture `IFilter and FilterChain`
- [x] Disabled filters have zero effect
- [x] Excluded nodes non-selectable; tree toggle hides/shows excluded nodes
- [x] Mirror filter comparison path suggestion derives from pipeline destination
- [x] Full add/edit/toggle/reorder/preset workflow operational end-to-end
- [x] `VisibleFiles` respects `ShowFilteredFiles` toggle
- [x] MirrorFilter evaluated with comparison provider in memory-backed flow

Verification:
- [x] all automated suites passing
- [x] Manual: built-in preset adds filter and updates tree/file-list
- [x] Manual: new Extension filter via dialog, save as preset, persists across restart

#### 5.1.5 — Transform Pipeline

Delivered:
- [x] Core pipeline extension and validation layer:
- [x] Step model expansion:
- [x] Step editor VM hierarchy:
- [x] Add Step flyout (two-level category -> type):
- [x] Edit Step dialog
- [x] validation feedback, delete badge, dynamic run button label
- [x] Preview workflow
- [x] Run/progress/journal wiring

Acceptance criteria status:
- [x] Run disabled until at least one valid executable step exists
- [x] Multiple executable steps are allowed and execute in sequence
- [x] Delete operations trigger preview
- [x] Validator rejects invalid sequences (`Delete -> Copy`, `Move -> Delete`)
- [x] Invalid step cards surface blocking validation text
- [x] Add Step flyout uses two-level category -> type drill-down
- [x] Edit pencil opens `EditStepDialog` with pre-populated values
- [x] `EditStepDialog` OK is validity-gated (Copy/Move destination, Rename pattern, Rebase fields)
- [x] Delete step shows badge (`Trash` or `⚠ Permanent`)
- [x] User pipelines save/load as `.sc2pipe` via preset store-backed commands

Validation performed:
- [x] Added new pipeline test suites
- [x] `dotnet test` execution
- [x] Manual UI scenario checks for preview/delete/progress/journal flows

#### 5.1.6 — Workflow Presets and Menu

Entire workflow (source path + filter chain + pipeline) persists as `.sc2workflow`, with UX wired into the main menu.

Delivered:
- [x] `WorkflowPreset`, `WorkflowConfig`, `WorkflowPresetStore`
- [x] `WorkflowMenuViewModel` — save/load/manage commands
- [x] `SaveWorkflowDialogViewModel` + `SaveWorkflowDialog`
- [x] `ManageWorkflowsDialogViewModel` + `ManageWorkflowsDialog`
- [x] `RenameInputViewModel` + `RenameInputDialog`

Acceptance criteria — all met:
- [x] Entire workflow can be persisted as `.sc2workflow`
- [x] Workflow presets load via preset menu integration
- [x] Workflow presets can be renamed and deleted via Manage Workflows dialog

Verification:
- [x] `dotnet build SmartCopy.App/SmartCopy.App.csproj`
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj`
- [x] Manual UI scenario checks for workflow menu integration

#### 5.1.7 — Selection management

Full selection save/load and bulk selection operations via Selection menu.

Delivered:
- [x] `SelectionSerializer` for `.txt`, `.m3u`, `.sc2sel`
- [x] `SelectionSerializer` for `.m3u8` (UTF-8 playlist format)
- [x] `SelectionManager.Restore` accepts both relative and absolute-path snapshots
- [x] `SelectionManager.Capture` — `useAbsolutePaths` opt-in parameter
- [x] `SelectionManager` bulk ops: `SelectAll`, `ClearAll`, `InvertAll`
- [x] Selection menu wired in `MainWindow.axaml.cs` code-behind
- [x] Window-level `Ctrl+A` / `Ctrl+Shift+A` / `Ctrl+I` key bindings

Acceptance criteria:
- [x] Save and restore works with non-ASCII characters
- [x] Relative path portability works across machines
- [x] Missing/unmatched paths are reported and skipped

Verification:
- [x] Round-trip tests for all formats (including M3U8)
- [x] Regression tests for mixed path separators and case differences
- [x] Non-ASCII path round-trip tests across `.txt`, `.m3u8`, `.sc2sel`
- [x] `dotnet build SmartCopy.App/SmartCopy.App.csproj`
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj`
- [x] Manual UI smoke scenarios for save/restore/bulk-selection flows

#### 5.1.8 — Settings Persistence

Deliverables:
- [x] `AppSettings` load/save + schema version
- [x] Cross-platform settings paths (`%APPDATA%` / `~/.config`)
- [x] `LastSourcePath`, `RecentSources`, `FavouritePaths` 

Acceptance criteria:
- [x] Settings are persisted across restarts
- [x] Missing/corrupt settings file falls back to defaults

Verification:
- [x] Unit tests for serialization and error fallback
- [x] Manual smoke test across restart

#### 5.1.9 — Shell Observability and Status Feedback

Deliverables:
- [x] Status bar live counts/size
- [x] Cross-check status-bar values against selected/filter states
- [x] Collapsible log panel
- [x] "Scanning..." indicator in status bar
- [x] "Ready" indicator on right side of status bar when idle
- [x] Log panel wired: startup entry, pipeline results, expand-on-run

Acceptance criteria:
- [x] selected/file-size/count feedback while changing selection and filters
- [x] Log panel reports pipeline operations

Verification:
- [x] All tests pass
- [x] selection/filter changes update status-bar counts correctly
- [x] log panel expand/collapse persists and restores correctly

#### 5.1.10 — Keyboard Navigation and Accessibility Baseline

Deliverables:
- [x] Full keyboard baseline (`Tab`, arrows, `Space`, `Ctrl+A`, `Delete`/`Ctrl+D`, `F5`, `Escape`)
- [x] `AutomationProperties.Name` on all interactive controls
- [x] Focus-visibility pass for tree, list, filter cards, and pipeline step cards

Acceptance criteria:
- [x] Core memory-backed workflow is operable without mouse input
- [x] Screen-reader baseline metadata is present on primary interactive controls

Verification:
- [x] Keyboard-only smoke test for scan/selection/filter/pipeline-preview path

#### 5.1.11 — User Options and usability tweaks

New options added, persisted, and grouped into categories in the Options menu.

Deliverables:
- [x] Startup: restore last used workflow on startup
- [x] Startup: restore last used source path on startup
- [x] Pipeline: disable destructive preview (run delete/overwrite without preview)
- [x] Pipeline: default overwrite mode (skip, always, if newer)
- [x] Pipeline: delete to recycle bin (if available)
- [x] Scan: perform full scan instead of progressive scan
- [x] Scan: perform lazy scan (only scan inspected or selected paths)

Acceptance criteria:
- [x] All options are accessible from the Options menu
- [x] All options are persisted across application restarts

Verification:
- [x] `dotnet build`
- [x] `dotnet test`

### 5.2 Phase 2 — Filesystem Integration

*Goal: switch from memory-only interaction to real file systems without regressing UX guarantees established in Phase 1.*

Approach: front-load validation and risk burn-down. After each user-facing integration step, add or adapt automated coverage to lock behavior and avoid late regressions.

#### 5.2.1 — Validation Harness and Capability Contract (first)

Deliverables:
- [x] Expand provider contract tests so they run against both `MemoryFileSystemProvider` and `LocalFileSystemProvider`
- [x] Add targeted tests for path normalization/splitting/joining on Windows + POSIX-style inputs
- [x] Add coverage for cross-volume move fallback behavior (copy+delete when atomic move is unavailable)
- [x] Add explicit capability assertions (`CanSeek`, `CanAtomicMove`, path limits) per provider
- [x] Define a Phase 2 smoke dataset for repeatable manual verification (small tree + large files + mixed attributes)

Acceptance criteria:
- [x] Provider contract suite passes for memory and local providers
- [x] Capability-dependent code paths are covered by tests and do not depend on implicit platform assumptions
- [x] Failures in local-provider behavior are caught by tests before UI wiring

Verification:
- [x] `dotnet build SmartCopy2026.slnx`
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj`

New files:
- `SmartCopy.Tests/TestInfrastructure/CapabilityOverrideProvider.cs` — capability decorator for testing fallback paths
- `SmartCopy.Tests/TestInfrastructure/SmokeDataset.cs` — canonical Phase 2 smoke dataset (`Seed` + `SeedTo`)
- `SmartCopy.Tests/FileSystem/ProviderContractTests.cs` — 11-test abstract contract + Memory/Local subclasses
- `SmartCopy.Tests/FileSystem/PathHandlingTests.cs` — 14 path normalization/splitting/joining tests
- `SmartCopy.Tests/Pipeline/MoveStepFallbackTests.cs` — 4 tests for cross-provider and CanAtomicMove=false fallback paths

Real filesystem test strategy (three tiers):
1. **Hermetic** — `TempDirectory` + `MemoryFileSystemProvider`; always-on, no external deps; runs in CI
2. **Smoke** — programmatic `SmokeDataset.Seed*()` into temp dirs; same dataset for both providers
3. **Slow** (Phase 2.X) — opt-in via `SMARTCOPY_SLOW_TEST_PATH` env var; tagged `[Trait("Category", "Slow")]`; use for validation matrix items (100k nodes, large files, locked files, network paths)

#### 5.2.2 — Source Acquisition UX (native browse + path commit)

Deliverables:
- [x] Replace disabled Browse buttons with native folder picker wiring (source + destination contexts)
- [x] Keep editable `SourcePath` commit semantics (Enter/dropdown close) while allowing picker-driven path updates
- [x] Ensure bookmarks/MRU normalization works across multiple drives, UNC paths, and trailing-separator variations
- [x] Add validation/error messaging for inaccessible or missing paths without freezing UI

Acceptance criteria:
- [x] User can pick a local folder and immediately scan it
- [x] MRU/bookmarks de-duplicate equivalent paths reliably
- [x] Invalid/unreachable path entry fails safely and preserves last good state

Verification:
- [x] Automated tests for bookmark/MRU normalization edge cases
- [ ] Manual smoke: local drive path, UNC/network path, disconnected path recovery

#### 5.2.3 — Real Provider Runtime Integration (scanner + pipeline path)

Deliverables:
- [x] Introduce runtime provider selection (memory for debug flows, local for real filesystem flows) - DONE: path based resolution
- [x] Wire `DirectoryTreeViewModel` and `FileListViewModel` to the active provider at root-change time
- [x] Ensure pipeline preview/run uses active source provider and correct target providers
- [x] Preserve existing progress, cancellation, and log semantics when using local files

Acceptance criteria:
- [x] No hard dependency on memory-only paths (`/mem`) remains in execution paths
- [x] Phase 1 workflows behave the same from a user perspective when pointed at real folders
- [x] UI remains responsive during large local scans
- [x] Functional pause button

Verification:
- [x] Manual smoke: scan/filter/preview/run on a real directory tree with at least 10k files
- [x] Automated tests around provider switching and pipeline execution provider routing

### 5.2.4 - Directory browser integration for filter and pipeline editors

Deliverables:
- [x] Browse/Bookmark functionality for MirrorFilterEditorView
- [x] Generalise the browse/bookmark functionality to use in CopyMoveStepEditor (UserControl)

Acceptance criteria:
- [x] Target directory pickers have full parity with source picker in MainWindow
- [x] Code duplication minimised, should be easy to re-use in other views in future

Verification
- [x] Manual testing of UI and functionality
- [x] Add automated tests if applicable

#### 5.2.5— Drag-and-Drop Integration and Hardening

Deliverables:
- [x] Add drag-and-drop for source and destination path inputs
- [x] Validate dropped paths and reject unsupported payloads safely
- [x] Confirm keyboard-first behavior remains intact after DnD additions

Acceptance criteria:
- [x] Dragging folders from OS shell updates source/destination fields reliably
- [x] DnD does not bypass validation/confirmation safeguards
- [x] Accessibility and keyboard workflows remain unchanged

Verification:
- [x] UI-level smoke checks for DnD + keyboard navigation regression

#### 5.2.6 — Watcher and Incremental Rescan (selection-preserving)

Authoritative design and progress tracking for this step lives in `Docs/Filesystem-Watcher-Plan.md`.

Deliverables:
- [x] Gate watcher startup by provider capability and path suitability
- [x] Integrate `DirectoryWatcher` batching into directory tree update flow
- [x] Implement watcher-driven incremental patch updates for changed paths only
- [x] Preserve user selection/filter state across watcher-driven updates

Acceptance criteria:
- [x] Burst filesystem changes are coalesced (debounced) without UI thrash
- [x] External create/rename/delete events update only impacted branches
- [x] Selection/check-state is stable after incremental updates

Verification:
- [x] Automated tests for debounce/coalescing and branch-local updates
- [x] Manual smoke with scripted file churn in nested directories

### 5.2.7 Capability gates and cross-filesystem hardening

Add filesystem-aware features that adapt to the capabilities of the source and target systems.

Scope:
- [x] SMB/Network share provider-aware operations
- [x] Atomic move enabled within the same volume, even when roots are different (rationalise IFileSystemProvider.RootPath vs DirectoryTreeNode.RootPath)
- [x] Introduce `TrashService` abstraction/adapters with timeout and fallback behavior
- [x] Route delete behavior through capability/availability checks (trash when available, permanent fallback)
- [x] Surface capability-derived safety messaging in preview/run UX
- [x] Linux remote-mount capability gating via mount type detection (`/proc/self/mountinfo`) for watcher/trash/atomic-move safety

Verification:
- [x] Manual validation of operations over SMB
- [x] Unit tests for trash fallback, timeout, and move fallback paths
- [x] Manual smoke: delete-to-trash and permanent delete on local files

Exit criteria:
- [x] SMB/Network share provider-aware operations validated
- [x] Operations across file systems (local, network) validated
- [x] Delete operations are deterministic and journaled as `Trashed` vs `Deleted`

### Phase 5.2.8 — UI State Locking (scan + execution)

*Goal: prevent the user from mutating state while the app is busy. All items share the same underlying mechanism (`IsScanning` / `IsExecuting` flags propagated through ViewModels).*

Scope:
- [x] Disable Pipeline Run & Preview whilst scan is in progress
- [x] Lock Pipeline view whilst execution is in progress (no run, preview, remove or edit steps)
- [x] Lock Filter view whilst execution is in progress (no add, remove, edit or re-arrange filters)
- [x] Confirmation required to change source path whilst pipeline execution is in progress
- [x] Confirm quit whilst pipeline execution is in progress

Exit criteria:
- [x] All locking/unlocking transitions are deterministic and covered by automated tests
- [x] Manual smoke: start a run, attempt each locked action, confirm correct blocking behavior

### Phase 5.2.9 — Execution Progress UX

*Goal: surface meaningful visual feedback during plan preparation and pipeline execution. No state locking — purely observability.*

Scope:
- [x] Active step highlighted during pipeline execution
- [x] Open PreviewView and show a progress bar whilst OperationPlan is being prepared
- [x] Cancellation/abort of preview preparation
- [x] Suppress redundant preview for Copy/Move with Overwrite != Skip if destination path does not exist

Exit criteria:
- [x] Active step card is visually distinct during run; advances correctly through multi-step pipelines
- [x] PreviewView progress indicator visible during plan generation and dismissed on completion
- [x] Abort preview preparation and confirm the progress indicator is dismissed
- [x] Automatically proceed if no actual overwrites are found in a preview triggered by a potential overwrite

### Phase 5.2.10 — Pre-flight Safety Validation

*Goal: detect and surface insufficient disk space before committing to a copy or cross-volume move.*

Scope:
- [x] Free space check against target volume for Copy steps
- [x] Free space check against target volume for Move steps that cross volumes (copy+delete fallback path)
- [x] Surface warning/blocking message in PreviewView when space is insufficient

Exit criteria:
- [x] Insufficient-space condition surfaces as a blocking warning in the preview before any file I/O begins
- [x] Automated tests for free-space gate logic using a mock/capped provider
- [x] Manual smoke: attempt copy to a near-full volume and confirm warning appears

### Phase 5.2.11 Swap Source & Destination Paths

*Goal: allow the user to swap the source path with the destination of a copy/move step.*

Scope:
- [x] Context menu on pipeline steps with IHasDestinationPath with "swap destination with source path"

Validation:
- [x] Add tests for the functionality

Exit criteria:
- [x] Manual smoke: swap source and destination paths and confirm the directory tree and file list update correctly, and the pipeline step target is set to the previous source

### Phase 5.2 Bug fixes

- [x] Memory to Memory moves report that atomic move is not possible (null volumeId?)
- [x] Incorrect progress bar status after Select All step (X/0 files)
- [x] Incorrect progress bar for multi-step pipelines (e.g. Copy + Move shows 500/400 files during Move)

Exit criteria:
- [x] All bug fixes are implemented and tests are added
- [x] Manual smoke tests confirm the bug fixes

#### 5.3 Validation and initial release

Validation:

- [x] End-to-end scan/filter/preview/run flows validated against real file systems
- [x] Large tree scan (>100k nodes) remains responsive with cancellable scan
- [x] Long-path and non-ASCII filename handling validated
- [ ] Locked/in-use file behavior validated (copy/move/delete error reporting)
- [x] Cross-volume/local-network move/copy behavior validated
- [x] Watcher overflow/error path handled without app crash
- [x] Operation journal correctness validated for copy/move/delete/trash outcomes
- [x] Automated suites cover provider contract, watcher behavior, and capability-gated operations
- [x] Cross-platform CI execution validates local file system tests on Windows, macOS and Linux

Exit criteria:
- [ ] User acceptance tests confirms ready to launch
- [x] Release candidate passes cross-platform smoke checklist
- [x] Initial release published

### 5.4 MTP and media devices support

Scope:
- [ ] MTP provider (`MtpFileSystemProvider`) + device picker integration
- [ ] Operation gating based on device capabilities (e.g. CanDelete)
- [ ] Concerns such as device disconnection addressed, with error handling and user feedback

Exit criteria:
- [ ] MTP devices can be enumerated and selected
- [ ] Source root directory can be selected from an MTP device
- [ ] MTP copy round-trip validated
- [ ] Move/Delete operations validated

### 5.5 — Plug-Ins and Extensibility

Scope:
- [ ] `ConvertStep` + plugin loader + per-plugin settings UI
- [ ] FFmpeg reference plugin
- [ ] Public plugin SDK documentation

Exit criteria:
- [ ] At least one conversion plugin with tests
- [ ] Plugin isolation and loading failures handled without app crash
- [ ] Plugin SDK docs are sufficient for a third party to build a basic plugin


### Phase X - Future opportunities

Possible
- [ ] Theming, localization infrastructure, update checks
- [ ] `RenameStep` metadata token engine (`{name}`, `{ext}`, `{date}`, `{artist}`, `{album}`, `{track:00}`, `{title}`)
- [ ] Multi-source merge workflow


## 6. Open Questions

| Topic | Default for v1 | Target date | Status |
|---|---|---|---|
| Packaging/distribution | Portable .NET Publish Profiles + MSBuild asset bundling | 2026-03-14 | Addressed |
| Plugin trust model | Prompt on first load; remember user choice per plugin hash | 2026-03-20 | Open |

Decision notes:
1. Packaging: installer is optional for v1; prioritize reliable portable binaries.
2. Plugin trust: code-signing and central registry are deferred until plugin ecosystem justifies complexity.
