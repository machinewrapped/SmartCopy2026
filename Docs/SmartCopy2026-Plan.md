# SmartCopy2026 — Design & Implementation Plan (Revised)

**Prepared:** 2026-02-18
**Revised:** 2026-02-22 (Phase 1 is now strictly memory-first UX/architecture; local filesystem integration moved to standalone Phase 2; architecture reference split into `Docs/SmartCopy2026-Architecture.md`)
**Author:** Simon Booth
**License:** MIT
**Predecessor:** SmartCopy 2015 (GPL v3, .NET 4.8 WinForms, SourceForge)

This document is the execution plan for the SmartCopy2026 rewrite: sequencing, delivery scope,
acceptance criteria, and verification.

Implementation-oriented architecture, contracts, algorithms, UI behavior, and data schemas now
live in `Docs/SmartCopy2026-Architecture.md`.

---

## Table of Contents

Reference:
- [Architecture Reference (separate document)](SmartCopy2026-Architecture.md)

1. [What the Predecessor Got Right](#1-what-the-predecessor-got-right)
2. [What to Fix and Improve](#2-what-to-fix-and-improve)
3. [Technology Stack](#3-technology-stack)
4. [Architecture Design](#4-architecture-design)
5. [Key Technical Designs](#5-key-technical-designs)
6. [Algorithms and Implementation Notes](#6-algorithms-and-implementation-notes)
7. [UI Design](#7-ui-design)
8. [Phase 1 — Core Workflows](#8-phase-1--core-workflows)
9. [Remaining Phases](#9-remaining-phases)
10. [Data Models](#10-data-models)
11. [Open Questions](#11-open-questions)

---

## 1. What the Predecessor Got Right

Before redesigning, preserve these patterns:

- **Background worker pattern** — clean separation of long-running operations with pause/resume,
  cancellation, progress reporting, and per-operation logging
- **Filesystem wrapper model** — wrapping raw `FileInfo`/`DirectoryInfo` with application state
  (checked, filtered, removed) was the right instinct; `FileSystemNode` continues this
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
| Full pre-scan blocks interaction | Progressive scan: top-level first, stream children in background |

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
algorithm must be efficient (see Architecture `Section 6.2`). This is an implementation concern,
not a reason to switch framework.

**TreeDataGrid:** Avalonia's `TreeDataGrid` control (stable in 11+) may outperform a hand-rolled
`TreeView` + column layout for the file list pane. Evaluate it during Step 1 shell construction;
it provides built-in column resizing and sort headers that would otherwise need custom controls.

### .NET 10 targeting

Use `net10.0` TFM. .NET 10 is LTS (supported until ~2028). If building on a machine with only .NET 9
installed, `global.json` can pin the SDK version. Self-contained publish (`--self-contained true
-p:PublishSingleFile=true`) bundles the runtime and ships a single executable per platform.

---

## 4. Architecture Design

Canonical architecture reference lives in:
- `Docs/SmartCopy2026-Architecture.md#4-architecture-design`

This plan keeps only execution sequencing and acceptance criteria. When architecture or contract details change, update the architecture reference first, then update impacted plan steps.

---

## 5. Key Technical Designs

Detailed technical contracts (providers, filters, pipeline, preview/progress, scanner/watcher, plugin interface) now live in:
- `Docs/SmartCopy2026-Architecture.md#5-key-technical-designs`

Do not duplicate full class/interface signatures here unless a step requires an explicit temporary delta.

---

## 6. Algorithms and Implementation Notes

Canonical algorithm/invariant reference (selection state, tri-state propagation, mirror matching, wildcard matching, sync semantics, safety defaults) now lives in:
- `Docs/SmartCopy2026-Architecture.md#6-algorithms-and-implementation-notes`

Step acceptance criteria may reference algorithm sections (for example `Section 6.11`) but normative algorithm text is maintained in the architecture reference.

---

## 7. UI Design

Canonical UI behavior and interaction specs now live in:
- `Docs/SmartCopy2026-Architecture.md#7-ui-design`

Plan-level rule: keep UI implementation tasks and verification in Phase steps; keep stable interaction specs in architecture.

---

## 8. Phase 1 — Core Workflows

*Goal: ship a reliable cross-platform v1 that can scan, select, filter, preview, copy/move/delete,
and sync safely.*

### Phase 1 sequencing principle (revised 2026-02-22)

1. Prove and refine UX using `MemoryFileSystemProvider` first; seeded `/mem` remains the default integration source until the end-to-end flow is demoable.
2. Treat the UX loop as the primary critical path: scan/seed -> select -> filter -> preview -> run -> verify.
3. `LocalFileSystemProvider` remains in-repo during Phase 1 as a design reference for `IFileSystemProvider` flexibility, but is not exercised as a delivery gate in this phase.
4. All real local-filesystem integration tasks (scanner wiring, trash adapters, watcher-driven rescans) are deferred to standalone Phase 2.
5. "Done" means deliverables shipped, acceptance criteria met, verification commands pass, and UX-loop tests run on memory fixtures first.
6. Any UX/safety requirement marked "mandatory" in this plan blocks step completion.

### Phase 1 delivery order (UX-first, memory-backed)

1. UX loop track (blocking for feature-complete v1 demo)
2. Validation-first hardening track (prioritised while behaviour is still fresh)
3. UX polish track (after end-to-end flow is proven)

### Phase 1 status snapshot (as of 2026-02-22)

| Workstream item | Status | Evidence | Next action |
|---|---|---|---|
| UX-1 (Step 1): Baseline shell | Complete | 3-column shell, seeded `/mem` source, tree->file-list sync, persisted window/column state, CI matrix in place; verification checklist closed | Keep as baseline for UX-loop regression checks in later steps |
| UX-2 (Step 3): Node selection logic | Complete | Tri-state propagation and `IsSelected` behavior implemented in `FileSystemNode` and covered by dedicated transition tests | Expand with scale/perf coverage alongside Step 10 observability work |
| UX-3 (Step 4): Filter chain | Mostly complete | Live filter UX is wired end-to-end (presets, add/edit dialog, drag reorder, tree/file-list reapply), and dedicated filter test suites are in place | Finish chain Save/Load file-picker integration and close the remaining manual verification item |
| UX-4 (Step 5): Transform pipeline | In progress | Core pipeline (`TransformPipeline`, `PipelineRunner`) and built-in steps (`Copy/Move/Delete/Flatten`) implemented with tests; UI design documented (§5.1) | Substeps 5.2–5.8: preset store, step editor VMs, Add Step flyout, EditStepDialog, VM wiring, preview dialog, run/progress/journal |
| UX-5 (Step 7): Sync operations | Started (core skeleton) | `SyncWorkflow` has find-orphans and basic update/mirror builders | Implement full update/mirror semantics (`IfNewer`, orphan delete pass with confirmation) + UI entry points |
| Hardening-1 (Step 2): Memory provider foundation | Complete | `FileSystemNode`, `IFileSystemProvider`, `ProviderCapabilities`, `MemoryFileSystemProvider`, provider contract tests, and shared memory-first fixture builders are implemented | Reuse the shared fixture builder pattern for all new core workflow tests |
| Hardening-2 (Step 8): Selection save/load | In progress | `SelectionSerializer` (`.txt`, `.m3u`, `.sc2sel`) and `SelectionManager` implemented with round-trip tests | Wire File menu flows and add unmatched-path reporting behavior |
| Hardening-3 (Step 9): Settings persistence | In progress | `AppSettings` + `AppSettingsStore` implemented with corrupt-file fallback tests and cross-platform path resolution; `LastSourcePath`/`RecentSources`/`FavouritePaths` now loaded on startup and saved on source-path change | Add schema migration path; wire remaining persisted defaults (sort order, scan options) |
| Polish-1 (Step 10): Shell observability + status | Not started | Implement after Step 7 end-to-end UX loop is proven |
| Polish-2 (Step 11): Keyboard + accessibility baseline | Not started | Implement after Step 10 |

### Step 1 — Project Scaffold + Baseline UI Shell (UX Loop Track)

Deliverables:
- [x] Solution and projects exist: `SmartCopy.Core`, `SmartCopy.App`, `SmartCopy.UI`, `SmartCopy.Tests`
- [x] DI bootstrapping in `AppServiceProvider.cs`
- [x] UI shell layout and persisted window/column state
- [x] Operation progress overlay placeholder (no real operations yet)
- [x] CI workflow runs build + tests on Windows and Linux

Acceptance criteria:
- [x] App launches and renders correctly on Windows
- [x] Layout is persisted and restored on Windows

Verification:
- [x] `dotnet build SmartCopy.App/SmartCopy.App.csproj`
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj`

### Step 2 — Core Models + MemoryFileSystemProvider (Memory-Backed Hardening Track, test-first foundation)

Deliverables:
- [x] Implement `FileSystemNode` full model contract from Architecture `Section 10`
- [x] Implement `IFileSystemProvider` + `ProviderCapabilities`
- [x] Implement `MemoryFileSystemProvider` for fast hermetic tests
- [x] Create shared test fixtures/builders that default to `MemoryFileSystemProvider`

Acceptance criteria:
- [x] No hard dependency on real filesystem for shell startup
- [x] Memory provider supports filesystem contract without disk I/O
- [x] Scanner/filter/pipeline core tests in this step run against `MemoryFileSystemProvider`

Verification:
- [x] Unit tests for `MemoryFileSystemProvider`
- [x] Contract tests for enumerate/read/write/move/delete/create/exists on in-memory trees

### Step 3 — Node Selection Logic (UX Loop Track, validation-priority)

Deliverables:
- [x] Tri-state propagation algorithm from Architecture `Section 6.2` in production nodes/view models
- [x] `IsSelected` wiring (`CheckState == Checked && FilterResult == Included`)

Acceptance criteria:
- [x] Parent/child state transitions are deterministic and O(height) upward
- [x] No per-node `PropertyChanged` storm during bulk updates (batched updates)

Verification:
- [x] Unit tests for checked/unchecked/indeterminate transitions

### Step 4 — Filter Chain (UX Loop Track)

Status update (2026-02-22): sub-steps 4a-4f are implemented in current code and covered by
automated tests. Remaining follow-up is UI completion for chain Save/Load flow (file picker +
JSON round-trip wiring in the shell).

#### Already complete
- [x] `IFilter`, `FilterChain`, `FilterConfig`, `FilterChainConfig`
- [x] `Wildcard`, `Extension`, `Mirror`, `DateRange`, `SizeRange`, `Attribute` filters
- [x] Basic `FilterChain` unit tests
- [x] Filter mode model evolved to `Only | Add | Exclude` with ordered set-based evaluation

#### Sub-step 4a — `FilterPresetStore` + `FilterFactory` (Core)

New files:
- `SmartCopy.Core/Filters/FilterPreset.cs` — `{ Id, Name, IsBuiltIn, FilterConfig }`
- `SmartCopy.Core/Filters/FilterPresetCollection.cs` — JSON root `{ SchemaVersion, Dictionary<string, List<FilterPreset>> UserPresets }`
- `SmartCopy.Core/Filters/FilterPresetStore.cs` — async CRUD: `GetPresetsForTypeAsync`, `SaveUserPresetAsync`, `DeleteUserPresetAsync`; built-ins always prepended; persists to `%APPDATA%/SmartCopy2026/filter-presets.json`
- `SmartCopy.Core/Filters/FilterFactory.cs` — static `FromConfig(FilterConfig) → IFilter`; switch on `FilterType` string

Modify:
- `SmartCopy.Core/Settings/AppSettings.cs` — add `Dictionary<string, List<string>> FilterTypeMruPresetIds`

Built-in presets (hardcoded, never written to disk):
- Extension / "Only Audio files": `mp3;flac;aac;ogg;wav;m4a`, Only
- Extension / "Only Images": `jpg;jpeg;png;gif;webp;bmp;tiff;svg`, Only
- Extension / "Only Documents": `pdf;docx;xlsx;pptx;txt;odt`, Only
- Extension / "Only Log files": `log;txt`, Only
- Wildcard / "Exclude Temp files": `*.tmp;*.bak;~*;Thumbs.db`, Exclude

Tests (`SmartCopy.Tests/Filters/FilterPresetStoreTests.cs`): built-ins present when no user file; save/delete/overwrite round-trips; built-ins precede user presets; `FilterFactory` round-trips all 6 filter types.

#### Sub-step 4b — `FilterEditorViewModel` hierarchy (UI ↔ Core bridge)

New folder: `SmartCopy.UI/ViewModels/Filters/`

New files:
- `FilterEditorViewModelBase.cs` — abstract; `FilterMode Mode`, `string FilterName`, `bool SaveAsPreset`; `abstract IFilter BuildFilter()`, `abstract bool IsValid`, `abstract void LoadFrom(IFilter)`, `virtual string GenerateName()`
- One concrete subclass per filter type: `ExtensionFilterEditorViewModel`, `WildcardFilterEditorViewModel`, `DateRangeFilterEditorViewModel`, `SizeRangeFilterEditorViewModel`, `MirrorFilterEditorViewModel`, `AttributeFilterEditorViewModel`
- `SizeUnit` enum: Bytes/KB/MB/GB/TB (in the size range editor)

Tests (`SmartCopy.Tests/Filters/FilterEditorViewModelTests.cs`): extension normalisation and deduplication; size unit conversion; `LoadFrom → BuildFilter` round-trips for all 6 types.

#### Sub-step 4c — Add-Filter flyout (two-level drill-down)

New files:
- `SmartCopy.UI/ViewModels/AddFilterViewModel.cs` — level-1: 6 `FilterTypeItem` entries; level-2: loads presets + MRU; events `PresetPicked(FilterPreset)`, `NewFilterRequested(string filterType)`; updates MRU on pick (prepend, cap at 5, deduplicate)
- `SmartCopy.UI/Views/AddFilterFlyout.axaml` + `.cs` — `UserControl` hosted in a `Popup` (`PlacementMode=Bottom`); two panels swapped via `IsLevel2Visible`

Modify: `SmartCopy.UI/Views/FilterChainView.axaml` + `.cs` — replace `AddFilter` button with `Popup`; code-behind routes events to `FilterChainViewModel`.

Tests (`SmartCopy.Tests/Filters/AddFilterViewModelTests.cs`): level navigation; MRU population; GoBack; MRU update on pick.

#### Sub-step 4d — `EditFilterDialog` (modal Window)

New files:
- `SmartCopy.UI/ViewModels/EditFilterDialogViewModel.cs` — factory methods `ForNew(filterType, pipelineDestinationPath = "")` and `ForEdit(existingFilter, pipelineDestinationPath = "")`; `Ok()` builds `ResultFilter`; `SaveAsPreset` flag is exposed for caller handling
- `SmartCopy.UI/Views/EditFilterDialog.axaml` + `.cs` — modal `Window`; Only/Add/Exclude toggle → name field → `ContentControl` + `DataTemplate` dispatch → "Save as preset" → Cancel/OK
- `SmartCopy.UI/Views/FilterEditors/` — 6 `UserControl` files (one per type): Extension chips, Wildcard text box, DateRange date pickers, SizeRange numeric inputs, Mirror path + compare mode, Attribute checkboxes

Dialog launch + preset persistence in `FilterChainView.axaml.cs`: `NewFilterDialogRequested` → `ForNew` → `ShowDialog`; edit pencil → `ForEdit` → `ShowDialog`; if OK + SaveAsPreset, save through `FilterPresetStore` before adding/replacing the card.

Tests (`SmartCopy.Tests/Filters/EditFilterDialogViewModelTests.cs`): `ForNew` type dispatch; `ForEdit` pre-population; mode toggles (`Only/Add/Exclude`) update editor state; `IsValid` gates OK; mirror path suggestion is applied.

#### Sub-step 4e — `FilterViewModel` real `IFilter` wiring + `BuildLiveChain()`

Modify `SmartCopy.UI/ViewModels/FilterChainViewModel.cs` (significant rewrite):
- `FilterViewModel` wraps a live `IFilter`; `IsEnabled` toggle fires `ChainChanged`; `ReplaceFilter(IFilter)` swaps instance
- `FilterChainViewModel` gains: `FilterPresetStore`/`AppSettings` constructor params; `AddFilterViewModel AddFilter`; `FilterChain BuildLiveChain()`; `public event EventHandler? ChainChanged`; `AddFilterFromResult`, `ReplaceFilter`, `MoveFilter(int, int)`; `NewFilterDialogRequested` event; `SaveChain`/`LoadChain` commands that raise `SaveChainRequested`/`LoadChainRequested`
- `FilterChainView.axaml.cs` wires Avalonia `DragDrop` on the `ItemsControl`; drop handler calls `MoveFilter`

Modify `MainViewModel`: construct `AppSettings` + `FilterPresetStore`; pass to `FilterChainViewModel`; keep `PipelineDestinationPath` synchronized with pipeline destination.

Tests (`SmartCopy.Tests/Filters/FilterChainViewModelTests.cs`): `BuildLiveChain`; add/remove fire `ChainChanged`; `IsEnabled` toggle fires `ChainChanged`; `ReplaceFilter` updates VM.

#### Sub-step 4f — Live wiring: filter application to tree + file list

Modify `MainViewModel`: subscribe `FilterChain.ChainChanged` → `ApplyFiltersAsync()` (CancellationTokenSource debounce; passes same `MemoryFileSystemProvider` as `comparisonProvider`).

Modify `DirectoryTreeViewModel`: add `ApplyFiltersAsync(FilterChain, IFileSystemProvider?, CancellationToken)` delegating to `chain.ApplyToTreeAsync(RootNodes, …)`.

Modify `FileListViewModel`: remove stub `FilterResult` logic; add `UpdateChain`, `ReapplyFiltersAsync`; expose `VisibleFiles` (respects `ShowFilteredFiles`).

New: `SmartCopy.UI/Converters/FilterResultOpacityConverter.cs` — `Excluded → 0.4`, `Included → 1.0`.

Modify `DirectoryTreeView.axaml`: add `Opacity` style binding via `FilterResultOpacityConverter`. Wire `FileListView.axaml` `DataGrid.ItemsSource` to `VisibleFiles`.

Tests (`SmartCopy.Tests/Filters/FilterLiveWiringTests.cs`) against `MemoryFileSystemProvider`: extension filter excludes non-matching files; `Only`+`Exclude` chain behavior; `ShowFilteredFiles` toggle; disabled filter resets excluded nodes; `ReapplyFiltersAsync` updates existing loaded file nodes.

#### Acceptance criteria
- [x] `Only`/`Add`/`Exclude` semantics match Architecture `Section 5.2`
- [x] Disabled filters have zero effect on `FilterResult`
- [x] Parent node is excluded if all its children and files are excluded
- [x] Excluded nodes have their checkboxes disabled in the tree and file list
- [x] Tree view has a toggle to hide or show excluded nodes
- [x] Mirror filter comparison path suggestion derives from pipeline destination
- [x] Add-filter flyout shows type list → preset list drill-down
- [x] "★ Only Audio files" built-in preset adds a filter and updates tree/file list
- [x] "＋ New..." opens `EditFilterDialog`; OK adds filter and updates tree/file list
- [x] Edit pencil re-opens dialog pre-populated; save-as-preset path is wired
- [x] Filter card checkbox toggle re-evaluates chain without reopening dialog
- [x] `VisibleFiles` respects `ShowFilteredFiles` toggle
- [x] Drag handle `≡` reorders filter cards; chain re-evaluated after reorder
- [x] MirrorFilter is evaluated with comparison provider wiring in memory-backed Phase 1 flow
- [ ] Save/Load chain completes full UI round-trip (file picker + JSON persistence wiring)

#### Verification
- [x] Automated filter suites are currently passing in the user environment (Codex cannot execute tests in this environment)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "FilterPresetStore"` (≥5 tests)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "FilterEditorViewModel"` (≥6 tests)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "AddFilterViewModel"` (≥5 tests)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "EditFilterDialogViewModel"` (≥6 tests)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "FilterChainViewModel"` (≥6 tests)
- [x] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "FilterLiveWiring"` (≥6 tests)
- [ ] Manual: launch app, add "★ Only Audio files" preset, verify tree/file-list filtering behavior
- [ ] Manual: create new Extension filter via dialog, save as preset, reload app, verify preset persists
- [ ] Manual: "Save ▾" writes `.json`; "Load ▾" restores chain from file

### Step 5 — Transform Pipeline (UX Loop Track, built-in steps)

Status update (2026-02-23): core pipeline engine and built-in steps are implemented. UI design
is documented. Remaining work is UX wiring — step editors, flyout, preset store, preview dialog,
run/progress/journal.

#### Already complete
- [x] `ITransformStep`, `TransformPipeline`, `TransformContext`, `PipelineRunner`
- [x] `CopyStep`, `MoveStep`, `DeleteStep`, `FlattenStep` with tests
- [x] `OperationPlan` / `PlannedAction` preview data model
- [x] Delete-mandatory-preview enforcement in `PipelineRunner`
- [x] `PipelineViewModel`, `PipelineStepViewModel`, `PipelineView.axaml` placeholder shell
- [x] `PreviewViewModel`, `OperationProgressViewModel` placeholder shells

#### Sub-step 5.1 — Pipeline UI Design (Architecture)

- [x] Pipeline UX flow documented in `Docs/SmartCopy2026-Architecture.md` §7 "Pipeline UX Flow":
  step card anatomy, Add Step two-level drill-down, `EditStepDialog` forms, Preview Dialog
  layout (including mandatory delete variant), `Load Preset ▾` / Save pipeline menu,
  `AddStepViewModel`, `EditStepDialogViewModel`, `PipelinePresetStore` contracts.

#### Sub-step 5.2 — `PipelinePresetStore` + `PipelineStepFactory` + `PipelineValidator` (Core)

New files:
- `SmartCopy.Core/Pipeline/PipelinePreset.cs` — `{ Id, Name, IsBuiltIn, PipelineConfig }`
- `SmartCopy.Core/Pipeline/PipelinePresetStore.cs` — async CRUD:
  `GetStandardPresetsAsync()` (hardcoded read-only), `GetUserPresetsAsync()` (reads
  `%APPDATA%/SmartCopy2026/pipelines/*.sc2pipe`), `SaveUserPresetAsync`, `DeleteUserPresetAsync`
- `SmartCopy.Core/Pipeline/PipelineStepFactory.cs` — static `FromConfig(TransformStepConfig) → ITransformStep`;
  switch on `StepType` string; throws `UnknownStepTypeException` for unrecognised types
- `SmartCopy.Core/Pipeline/Validation/PipelineValidationIssue.cs` — `{ StepIndex?, Code, Message, Severity }`
- `SmartCopy.Core/Pipeline/Validation/PipelineValidationResult.cs` — `{ IReadOnlyList<Issue>, bool CanRun }`
- `SmartCopy.Core/Pipeline/Validation/PipelineValidator.cs` — declarative rule engine that evaluates
  per-step preconditions/postconditions against fact state (`SourceExists`) and emits issues
- `SmartCopy.Core/Pipeline/Validation/PipelineStepContracts.cs` — contract map keyed by `StepType`
  (Phase 1 baseline: Copy/Move/Delete/Flatten/Rename/Rebase/Convert)

Standard presets (hardcoded, never written to disk):
- "Copy only" → `[CopyStep]`
- "Move only" → `[MoveStep]`
- "Delete to Trash" → `[DeleteStep(Trash)]`
- "Flatten → Copy" → `[FlattenStep → CopyStep]`

Tests (`SmartCopy.Tests/Pipeline/PipelinePresetStoreTests.cs`): standard presets always present;
user save/delete/overwrite round-trips; standard presets precede user presets; `PipelineStepFactory`
round-trips all implemented step types; unknown type throws cleanly.
Tests (`SmartCopy.Tests/Pipeline/PipelineValidatorTests.cs`): no-executable pipeline returns blocking
issue; missing Copy/Move destination returns step-scoped blocking issue; `Copy -> Move` valid;
`Delete -> Copy` invalid at Copy step due to source-existence precondition; `Move -> Delete` invalid
at Delete step due to source-existence precondition; delete-final rule enforced.

#### Sub-step 5.3 — `StepEditorViewModel` hierarchy (UI ↔ Core bridge)

New folder: `SmartCopy.UI/ViewModels/Pipeline/`

New files:
- `StepEditorViewModelBase.cs` — abstract; `abstract ITransformStep BuildStep()`,
  `abstract bool IsValid`, `abstract void LoadFrom(PipelineStepViewModel)`
- `CopyStepEditorViewModel.cs` — `DestinationPath`, `OverwriteMode`; `IsValid` requires non-empty path
- `MoveStepEditorViewModel.cs` — same shape as Copy
- `DeleteStepEditorViewModel.cs` — `DeleteMode` (Trash / Permanent)
- `FlattenStepEditorViewModel.cs` — `ConflictStrategy` (AutoRenameCounter / AutoRenameSourcePath / Skip / Overwrite)
- `RenameStepEditorViewModel.cs` — `Pattern` string, `LivePreviewName` computed from pattern +
  sample tokens; `IsValid` requires non-empty pattern (full token engine deferred to Phase 4)
- `RebaseStepEditorViewModel.cs` — `StripPrefix`, `AddPrefix`; `IsValid` requires at least one non-empty

Tests (`SmartCopy.Tests/Pipeline/StepEditorViewModelTests.cs`): `LoadFrom → BuildStep` round-trips
for Copy/Move/Delete/Flatten; `IsValid` gates per type; Delete mode toggle; Flatten conflict strategy;
Rename `LivePreviewName` updates on pattern change.

#### Sub-step 5.4 — Add Step flyout (two-level drill-down)

New files:
- `SmartCopy.UI/ViewModels/Pipeline/AddStepViewModel.cs` — `IsLevel2Visible`, `SelectedCategory`
  (`Path | Content | Executable`); `StepTypeItems` list for Level 2; `GoBack()`;
  events `StepTypeSelected(StepKind)`; `NavigateToCategory(StepCategory)`
- `SmartCopy.UI/Views/Pipeline/AddStepFlyout.axaml` + `.cs` — `UserControl` hosted in a `Popup`
  (`PlacementMode=Bottom`); two panels swapped via `IsLevel2Visible`; multiple executable steps
  are allowed (no replacement behavior)

Modify: `SmartCopy.UI/Views/PipelineView.axaml` + `.cs` — replace current nested `MenuItem` Add
step menu with `Popup` + `AddStepFlyout`; code-behind routes `StepTypeSelected` to
`PipelineViewModel`.

Tests (`SmartCopy.Tests/Pipeline/AddStepViewModelTests.cs`): category navigation; GoBack; all step
types present in correct categories; `StepTypeSelected` fires; no replacement flag behavior for
additional executable steps.

#### Sub-step 5.5 — `EditStepDialog` (modal Window)

New files:
- `SmartCopy.UI/ViewModels/Pipeline/EditStepDialogViewModel.cs` — factory methods
  `ForNew(StepKind kind)` and `ForEdit(PipelineStepViewModel existing)`; `Ok()` calls
  `editor.BuildStep()`, stores `ResultStep`; `bool IsValid` delegates to active editor
- `SmartCopy.UI/Views/Pipeline/EditStepDialog.axaml` + `.cs` — modal `Window`; step type
  title → `ContentControl` + `DataTemplate` dispatch → Cancel / OK
- `SmartCopy.UI/Views/Pipeline/StepEditors/CopyMoveStepEditor.axaml` — destination TextBox +
  browse button + Overwrite radio group (shared for Copy and Move)
- `SmartCopy.UI/Views/Pipeline/StepEditors/DeleteStepEditor.axaml` — Trash / Permanent radio;
  permanent option styled with amber warning text
- `SmartCopy.UI/Views/Pipeline/StepEditors/FlattenStepEditor.axaml` — ConflictStrategy radio group
- `SmartCopy.UI/Views/Pipeline/StepEditors/RenameStepEditor.axaml` — Pattern TextBox + token
  reference chips + live preview label
- `SmartCopy.UI/Views/Pipeline/StepEditors/RebaseStepEditor.axaml` — StripPrefix + AddPrefix TextBoxes

Dialog launch in `PipelineView.axaml.cs`: `AddStepFlyout.StepTypeSelected` → if step needs config,
call `EditStepDialogViewModel.ForNew` → `ShowDialog`; edit pencil → `ForEdit` → `ShowDialog`.

Tests (`SmartCopy.Tests/Pipeline/EditStepDialogViewModelTests.cs`): `ForNew` type dispatch;
`ForEdit` pre-population for all implemented types; `IsValid` gates OK; Delete mode toggle
surfaces in VM; Flatten conflict strategy roundtrip.

#### Sub-step 5.6 — `PipelineViewModel` real wiring + preset integration

Modify `SmartCopy.UI/ViewModels/PipelineViewModel.cs` (significant rewrite):
- `PipelineStepViewModel` wraps a live `ITransformStep`; `DestinationPath` setter updates
  underlying step config and fires `PipelineChanged`
- `PipelineStepViewModel` gains `string? ValidationMessage`, `bool HasValidationError`
- `PipelineViewModel` gains: `PipelinePresetStore` constructor param;
  `PipelineValidator` constructor param;
  `AddStepViewModel AddStep` property; `TransformPipeline BuildLivePipeline()`;
  `PipelineValidationResult ValidationResult`;
  `bool CanRun` (bound to `ValidationResult.CanRun`);
  `string? BlockingValidationMessage` (first blocking issue for run-button tooltip/helper text);
  `public event EventHandler? PipelineChanged`; `AddStepFromResult(StepKind, ITransformStep)`;
  `ReplaceStep(PipelineStepViewModel, ITransformStep)`;
  `LoadPreset` command hydrates from `PipelinePresetStore`; `SavePipeline` command writes `.sc2pipe`
- `FirstDestinationPath` computed from first Copy/Move step's real `DestinationPath` (drives
  MirrorFilter suggestion)
- Recompute validation after any add/remove/edit/reorder and map step-scoped issues back onto step cards

Modify `MainViewModel`: construct and inject `PipelinePresetStore`; keep `PipelineDestinationPath`
in sync via `Pipeline.PipelineChanged` (already partially wired via `FirstDestinationPath`).

Tests (`SmartCopy.Tests/Pipeline/PipelineViewModelTests.cs`): `BuildLivePipeline` returns correct
step sequence; add/remove fire `PipelineChanged`; `DestinationPath` inline edit fires `PipelineChanged`;
`ReplaceStep` updates VM; `FirstDestinationPath` tracks first Copy/Move destination; invalid step
shows `ValidationMessage`; `CanRun`/`BlockingValidationMessage` update deterministically.

#### Sub-step 5.7 — Preview dialog wiring

Modify `SmartCopy.UI/ViewModels/PreviewViewModel.cs`:
- `LoadFrom(OperationPlan plan)` — populates `Actions` grouped by `PlanWarning?`
  (null = Ready, DestinationExists, NameConflict, PermissionIssue)
- `bool IsDeletePipeline` — drives mandatory-confirmation mode
- `bool CanRun` — always true for non-delete; for delete, requires `IsConfirmed` checkbox
- `ICommand RunCommand` — raised back to `MainViewModel` to execute

Modify `SmartCopy.UI/Views/PreviewView.axaml`:
- Grouped sections (warnings at top; Ready section collapsible via `[show/hide ▾]`)
- Delete variant: amber header, full file list always expanded, confirm button label names the
  destructive action (`🗑 Delete N files to Bin` / `⚠ Permanently Delete N`)
- Cancel button closes dialog without running

Modify `MainViewModel`:
- `PreviewPipelineAsync()` — calls `PipelineRunner.PreviewAsync(selectedNodes, ...)`, passes
  result to `PreviewViewModel.LoadFrom`, opens preview window
- `RunPipelineAsync()` — if `Pipeline.HasDeleteStep`, routes through preview first; otherwise
  executes directly if user chose Run without preview
- `[👁 Preview & Run]` label on Run button when `Pipeline.HasDeleteStep` (bound in AXAML)
- Run command disabled when `Pipeline.CanRun == false`

Tests (`SmartCopy.Tests/Pipeline/PreviewViewModelTests.cs`): `LoadFrom` groups actions correctly;
warning counts match plan; `IsDeletePipeline` sets correct mode; `CanRun` gated by confirmation
for delete pipelines; ready section count.

#### Sub-step 5.8 — Run + progress wiring + operation journal

Modify `SmartCopy.UI/ViewModels/OperationProgressViewModel.cs`:
- Replace mock values with real `IProgress<OperationProgress>` callback binding
- `IsActive` set true on run start, false on completion or cancellation
- `PauseCommand` / `CancelCommand` wired to `CancellationTokenSource`

Modify `MainViewModel.RunPipelineAsync()`:
- Collect selected nodes from `DirectoryTreeViewModel`
- Build `TransformPipeline` via `Pipeline.BuildLivePipeline()`
- Create `CancellationTokenSource`; pass to `PipelineRunner.ExecuteAsync()`
- Report `OperationProgress` updates → `OperationProgressViewModel`

New: `SmartCopy.Core/Progress/OperationJournal.cs` — writes a timestamped log file to
`%APPDATA%/SmartCopy2026/logs/` after each operation: one line per action (copied/moved/deleted/
skipped/failed); rotates by deleting logs older than `AppSettings.LogRetentionDays` on startup.

Tests (`SmartCopy.Tests/Pipeline/PipelineIntegrationTests.cs`) against `MemoryFileSystemProvider`:
scan → select all → no filter → copy pipeline → verify outputs; flatten → copy → verify flat
structure; delete pipeline → verify mandatory preview enforced; overwrite Skip/IfNewer/Always
semantics; journal written and parseable.

#### Acceptance criteria
- [ ] Run remains disabled until pipeline contains at least one executable step with valid config
- [ ] Multiple executable steps are allowed (for example `Copy → Move`) and execute in order
- [ ] `DeleteStep` remains preview-mandatory and is validated as final when present
- [ ] Declarative precondition/postcondition validation rejects logically invalid sequences
  (`Delete → Copy`, `Move → Delete`) without pair-specific hardcoded UI checks
- [ ] Invalid step cards show inline feedback and tooltip with the blocking reason
- [ ] Run button exposes the first blocking validation reason when disabled
- [ ] Add Step flyout uses two-level category → type drill-down
- [ ] Edit pencil on each step card opens `EditStepDialog` pre-populated
- [ ] OK disabled in `EditStepDialog` when required fields empty (Copy/Move destination, Rename pattern)
- [ ] `DeleteStep` card shows Trash/Permanent badge; `⚠ Permanent` styled in amber
- [ ] Standard presets (Copy only / Move only / Delete to Trash / Flatten → Copy) load correctly
- [ ] User pipelines save to and load from `.sc2pipe` files
- [ ] `FirstDestinationPath` updates MirrorFilter suggestion when Copy/Move destination changes
- [ ] Preview dialog groups actions by warning type
- [ ] Delete pipelines always require explicit preview confirmation; Run bypasses preview only for
  non-delete pipelines
- [ ] Overwrite and delete modes are honored per `TransformContext` config
- [ ] Progress overlay shows real file/bytes progress during execution
- [ ] Operation journal written after each run; entries contain source, destination, action, bytes

#### Verification
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "PipelinePresetStore"` (≥5 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "PipelineValidator"` (≥6 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "StepEditorViewModel"` (≥6 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "AddStepViewModel"` (≥5 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "EditStepDialogViewModel"` (≥5 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "PipelineViewModel"` (≥5 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "PreviewViewModel"` (≥5 tests)
- [ ] `dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "PipelineIntegration"` (≥6 tests)
- [ ] Manual: launch app, load "Flatten → Copy" preset, verify step cards render correctly
- [ ] Manual: add a Copy step via flyout, set destination in `EditStepDialog`, verify card shows destination
- [ ] Manual: create path-only pipeline (`Flatten` only), verify Run is disabled; add Copy and verify Run enables
- [ ] Manual: build `Copy → Move` pipeline, run against `/mem` fixture, verify both operations execute in order
- [ ] Manual: build `Delete → Copy` pipeline, verify Copy card shows validation error and Run remains disabled
- [ ] Manual: build `Move → Delete` pipeline, verify Delete card shows validation error and Run remains disabled
- [ ] Manual: build a Delete pipeline, verify Run button becomes "Preview & Run", confirm required
- [ ] Manual: run a Copy pipeline against `/mem` fixture, verify progress overlay and journal file

### Step 6 — Sync Operations (UX Loop Track)

Deliverables:
- [ ] Update target workflow (`MirrorFilter` + `CopyStep` + `IfNewer`)
- [ ] Mirror target workflow (second orphan-delete pass with mandatory preview)
- [x] Find-orphans report mode
- [ ] Menu/preset entry points

Acceptance criteria:
- [ ] Update mode never deletes files
- [ ] Mirror mode deletes only items confirmed in preview
- [x] Find-orphans performs no write/delete actions

Verification:
- [ ] Integration tests against repeatable fixtures for update/mirror/orphan scenarios

### Step 7 — Selection Save/Load (Memory-Backed Hardening Track)

Deliverables:
- [x] `SelectionSerializer` for `.txt`, `.m3u`, `.sc2sel`
- [x] `SelectionManager` snapshot/restore for rescans
- [ ] File menu wiring (save/load/restore)

Acceptance criteria:
- [x] Relative path portability works across machines
- [ ] Missing/unmatched paths are reported and skipped without aborting load

Verification:
- [x] Round-trip tests for all formats
- [ ] Regression tests for mixed path separators and case differences

### Step 8 — Settings Persistence (Memory-Backed Hardening Track)

Deliverables:
- [x] `AppSettings` load/save + schema version
- [x] Cross-platform settings paths (`%APPDATA%` / `~/.config`)
- [x] Source path persistence: `LastSourcePath` restored on startup; `RecentSources`/`FavouritePaths` populate source ComboBox and save on change (pulled forward from Phase 3 as a Phase 1 UX necessity)
- [ ] Remaining persisted defaults (sort order, scan options, other UI state)

Acceptance criteria:
- [x] Missing/corrupt settings file falls back to defaults without crash
- [ ] Forward-compatible migration path exists for schema changes

Verification:
- [ ] Unit tests for serialization/migration/error fallback
- [ ] Manual smoke test across restart on Windows

### Step 9 — Shell Observability and Status Feedback (UX Polish Track)

Deliverables:
- [ ] Collapsible log panel with placeholder entries in the shell layout
- [ ] Status bar live counts/size from Architecture `Section 6.10`
- [ ] Cross-check status-bar values against selected/filter states under `/mem` fixtures

Acceptance criteria:
- [ ] Users can see deterministic selected/file-size/count feedback while changing selection and filters
- [ ] Log panel does not interfere with core tree/list/filter/pipeline interactions

Verification:
- [ ] UI smoke test: selection/filter changes update status-bar counts correctly
- [ ] UI smoke test: log panel expand/collapse persists and restores correctly

### Step 10 — Keyboard Navigation and Accessibility Baseline (UX Polish Track)

Deliverables:
- [ ] Full keyboard baseline (`Tab`, arrows, `Space`, `Ctrl+A`, `Delete`/`Ctrl+D`, `F5`, `Escape`)
- [ ] `AutomationProperties.Name` on all interactive controls
- [ ] Focus-visibility pass for tree, list, filter cards, and pipeline step cards

Acceptance criteria:
- [ ] Core memory-backed workflow is operable without mouse input
- [ ] Screen-reader baseline metadata is present on primary interactive controls

Verification:
- [ ] Keyboard-only smoke test for scan/selection/filter/pipeline-preview path
- [ ] Accessibility checklist pass for automation-name coverage on required controls

---

## 9. Remaining Phases

### Phase 2 — Real Filesystem Integration (standalone)

Goal:
- Exercise `LocalFileSystemProvider` in real usage after memory-backed UX/architecture is proven.
- Validate that `IFileSystemProvider` abstractions hold under real disk behavior without forcing
  early UX compromises.

Scope:
- [ ] Local provider/scanner integration in `DirectoryTreeViewModel` via progressive streams
- [ ] Platform `TrashService` adapters with timeout/fallback behavior
- [ ] Real-disk cancellation and scan-options validation coverage
- [ ] Incremental subtree rescan with selection preservation (Architecture `Section 6.5`)
- [ ] Watcher enable/disable settings, including provider capability gating
- [ ] Debounce/coalescing and subtree-only update tests
- [ ] Add provider parity checks so memory and local providers are exercised against the same
      contract/integration scenarios where feasible

Exit criteria:
- [ ] End-to-end flow runs in UI against real local paths: scan -> select -> filter -> preview -> execute
- [ ] Local-provider failure-path tests and watcher tests are stable in CI
- [ ] No Phase 1 UX behavior regresses when switching source from `/mem` to a real directory

### Phase 3 — Modern Features (post-local-integration hardening)

Scope:
- [ ] Filter chain save/load (`.sc2filter`) + preset library UI
- [ ] Pipeline save/load (`.sc2pipe`) + preset library UI
- [ ] Windows MTP provider (`MtpFileSystemProvider`) + WPD device picker integration
- [ ] `DuplicateFilter` and `PathDepthFilter`
- [x] Bookmarks/favorites for source field (pulled forward to Phase 1 as a UX necessity; editable ComboBox with `RecentSources`/`FavouritePaths` persistence)
- [ ] Drag-and-drop for source/destination fields; bookmarks for pipeline destination field

Exit criteria:
- [ ] MTP copy round-trip validated on at least two physical devices
- [ ] Filter/pipeline preset import/export is stable and versioned

### Phase 4 — Advanced Pipeline Steps

Scope:
- [ ] `RenameStep` token engine (`{name}`, `{ext}`, `{date}`, `{artist}`, `{album}`, `{track:00}`, `{title}`)
- [ ] `RebaseStep`
- [ ] `ConvertStep` + plugin loader + per-plugin settings UI
- [ ] FFmpeg reference plugin and conversion-size preview

Exit criteria:
- [ ] Plugin isolation and loading failures handled without app crash
- [ ] At least one conversion plugin ships with tests and docs

### Phase 5 — Polish and Extensibility

Scope:
- [ ] Session files (`.sc2session`) with full restore
- [ ] Theming, localization infrastructure, update checks
- [ ] Multi-source merge workflow
- [ ] Public plugin SDK documentation

Exit criteria:
- [ ] Release candidate passes cross-platform smoke checklist
- [ ] Plugin SDK docs are sufficient for a third party to build a basic plugin

---

## 10. Data Models

Canonical data model and persistence schemas now live in:
- `Docs/SmartCopy2026-Architecture.md#10-data-models`

Plan-level rule: schema changes should be authored in the architecture reference and then referenced from the relevant implementation step checklists.

---

## 11. Open Questions

Track these as explicit decision records. If no decision is made by the target date, use the
default and continue.

| Topic | Default for v1 | Owner | Target date | Status |
|---|---|---|---|---|
| Packaging/distribution | Ship self-contained binaries first; add `winget` manifest next | Maintainer | 2026-03-15 | Open |
| Plugin trust model | Prompt on first load; remember user choice per plugin hash | Maintainer | 2026-03-20 | Open |
| Session snapshot size | Keep uncompressed; add optional `GZipStream` when file > 4 MB | Maintainer | 2026-04-01 | Open |
| Trash on network drives | Detect unsupported trash and require explicit permanent-delete confirmation | Maintainer | 2026-03-01 | Resolved |

Decision notes:
1. Packaging: installer is optional for v1; prioritize reliable portable binaries.
2. Plugin trust: code-signing and central registry are deferred until plugin ecosystem justifies complexity.
3. Snapshot size: optimize only when measured data shows real UX/storage pain.
4. Network trash behavior is defined in Architecture `Section 6.11` and should be implemented as a hard safety rule.
