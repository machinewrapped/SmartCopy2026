# SmartCopy2026 - Architecture Reference

**Prepared:** 2026-02-22

This document is the implementation-oriented reference for architecture, technical contracts,
algorithms and data models.

## Table of Contents

1. [Architecture Design](#1-architecture-design)
2. [Key Technical Designs](#2-key-technical-designs)
3. [Algorithms and Implementation Notes](#3-algorithms-and-implementation-notes)
4. [UI Design](#4-ui-design)

---

## 1. Architecture Design

Key classes:

```
SmartCopy2026/
├── SmartCopy.Core/                 # Business logic — no UI references
│   ├── FileSystem/
│   │   ├── IFileSystemProvider.cs      # Abstraction over local + MTP (incl. ProviderCapabilities)
│   │   ├── LocalFileSystemProvider.cs
│   │   ├── MtpFileSystemProvider.cs    # Windows only (#if Windows); includes retry policy
│   │   ├── MemoryFileSystemProvider.cs # In-memory implementation for unit/integration tests
│   │   ├── FileSystemNode.cs           # Unified file/folder model
│   ├── Scanning/
│   │   ├── DirectoryScanner.cs         # Async recursive scan with progressive loading
│   │   └── DirectoryWatcher.cs         # FileSystemWatcher + 300ms debounce
│   ├── Filters/
│   │   ├── IFilter.cs
│   │   ├── FilterBase.cs
│   │   ├── FilterChain.cs
│   │   ├── FilterConfig.cs             # Serialisable per-filter config
│   │   ├── FilterChainConfig.cs
│   │   ├── FilterFactory.cs
│   │   ├── FilterPreset.cs
│   │   ├── FilterPresetStore.cs
│   │   └── Filters/
│   │       ├── .. various filters
│   ├── Pipeline/
│   │   ├── ITransformStep.cs
│   │   ├── TransformPipeline.cs
│   │   ├── TransformContext.cs
│   │   ├── PipelineRunner.cs
│   │   ├── StepKind.cs
│   │   ├── Validation/
│   │   │   ├── PipelineValidator.cs
│   │   │   ├── PipelineValidationContext.cs
│   │   └── Steps/
│   │       ├── CopyStep.cs             # Executable: write to target
│   │       ├── MoveStep.cs             # Executable: move to target
│   │       ├── DeleteStep.cs           # Executable: delete source (via trash by default)
│   │       ├── ... etc.
│   ├── Selection/
│   │   ├── SelectionManager.cs
│   │   ├── SelectionSerializer.cs
│   │   └── SelectionSnapshot.cs
│   ├── Progress/
│   │   ├── OperationProgress.cs
│   │   └── OperationJournal.cs
│   ├── Settings/
│   │   ├── AppSettings.cs
│   │   └── AppSettingsStore.cs
│   └── Workflows/
│       ├── WorkflowConfig.cs
│       ├── WorkflowPreset.cs
│       └── WorkflowPresetStore.cs
│
├── SmartCopy.App/                  # Avalonia application host + DI root
│   ├── ...
│
├── SmartCopy.UI/                   # Avalonia Views + ViewModels
│   ├── Services/                     # Application Services
│   │   ├── DialogService.cs
│   │   ├── NavigationService.cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   ├── DirectoryTreeViewModel.cs
│   │   ├── FileListViewModel.cs
│   │   ├── FilterChainViewModel.cs
│   │   ├── PipelineViewModel.cs
│   │   ├── AddFilterViewModel.cs
│   │   ├── EditFilterDialogViewModel.cs
│   │   ├── LogPanelViewModel.cs
│   │   ├── SelectionViewModel.cs
│   │   ├── StatusBarViewModel.cs
│   │   ├── PipelineStepDisplay.cs
│   │   ├── Pipeline/
│   │   │   ├── AddStepViewModel.cs
│   │   │   ├── EditStepDialogViewModel.cs
│   │   │   └── *StepEditorViewModel.cs
│   │   ├── Workflows/
│   │   ├── OperationProgressViewModel.cs
│   │   └── PreviewViewModel.cs
│   ├── Views/
│   │   ├── MainWindow.axaml
│   │   ├── DirectoryTreeView.axaml
│   │   ├── FileListView.axaml
│   │   ├── FilterChainView.axaml
│   │   ├── PipelineView.axaml
│   │   ├── AddFilterFlyout.axaml
│   │   ├── EditFilterDialog.axaml
│   │   ├── LogPanelView.axaml
│   │   ├── SelectionView.axaml
│   │   ├── StatusBarView.axaml
│   │   ├── Pipeline/
│   │   │   ├── AddStepFlyout.axaml
│   │   │   ├── EditStepDialog.axaml
│   │   │   └── StepEditors/*.axaml
│   │   ├── Workflows/
│   │   ├── OperationProgressView.axaml
│   │   └── PreviewView.axaml
│   ├── Controls/
│   │   ├── ...
│   └── Converters/
│       ├── ...
│
└── SmartCopy.Tests/
    ├── Filters/
    ├── Pipeline/
    ├── Selection/
    └── Scanning/
```

---

## 2. Key Technical Designs

### 2.1 IFileSystemProvider

The single abstraction that makes all pipeline steps provider-agnostic:

```csharp
public interface IFileSystemProvider
{
    string RootPath { get; }
    bool SupportsProgress { get; }       // false for MTP on some platforms
    ProviderCapabilities Capabilities { get; }

    Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct);
    Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    Task MoveAsync(string sourcePath, string destPath, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    // Returns the relative portion of fullPath with respect to basePath,
    // using this provider's own path conventions.
    string GetRelativePath(string basePath, string fullPath);

    // Splits any path string into ordered, separator-free segments.
    // Accepts both provider-native and canonical forward-slash paths.
    // Use this to ingest a string path into the separator-agnostic segment representation.
    string[] SplitPath(string path);

    // Appends segments onto basePath using this provider's own path conventions.
    // Use this to egress segments back to a provider-native string path.
    string JoinPath(string basePath, IReadOnlyList<string> segments);
}

public record ProviderCapabilities(
    bool CanSeek,           // MTP streams are not seekable
    bool CanAtomicMove,     // Local same-volume can Directory.Move(); MTP cannot
    int MaxPathLength       // 260 on Windows NTFS (without long-path opt-in), unlimited on MTP
);
```

`CopyStep` is identical whether copying local→local, local→MTP, or MTP→local.

**Connection resilience (MTP):** MTP devices can disconnect briefly mid-transfer. Wrap `MtpFileSystemProvider`
operations in a retry policy (up to 3 attempts, 500ms back-off) inside the provider itself — transparent to
the pipeline. `LocalFileSystemProvider` needs no retry logic.

### 2.2 IFilter and FilterChain

```csharp
public interface IFilter
{
    string Name { get; }
    string TypeDisplayName { get; }
    FilterMode Mode { get; }        // Only | Add | Exclude
    bool IsEnabled { get; set; }
    string? CustomName { get; set; }
    bool AppliesToDirectories { get; }
    FilterConfig Config { get; }    // serialisable

    /// <summary>
    /// Human-readable one-liner for the filter card face.
    /// Generated from the current config, e.g. "Only .mp3 and .flac files".
    /// </summary>
    string Summary { get; }

    /// <summary>
    /// Compact technical description shown as a subtitle on the card.
    /// e.g. "Extension: *.mp3; *.flac"
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Returns true if this filter matches the node.
    /// comparisonProvider is non-null when filter needs to check a second location (MirrorFilter).
    /// </summary>
    ValueTask<bool> MatchesAsync(
        FileSystemNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default);
}

public sealed class FilterChain
{
    public IReadOnlyList<IFilter> Filters { get; }

    public Task<IReadOnlyList<FileSystemNode>> ApplyAsync(
        IEnumerable<FileSystemNode> nodes,
        IFileSystemProvider? comparisonProvider = null,
        CancellationToken ct = default);

    public Task ApplyToTreeAsync(
        IEnumerable<FileSystemNode> roots,
        IFileSystemProvider? comparisonProvider = null,
        CancellationToken ct = default);

    public FilterChainConfig ToConfig(string name = "Default", string? description = null);
    public static FilterChain FromConfig(FilterChainConfig config);
}
```

Runtime semantics are set-based and order-sensitive:
- Start state is `inSet = true` for each node
- `Only`: intersection step; if a currently-included node does not match, it is excluded
- `Add`: union step; any matching node is re-included, even if excluded earlier
- `Exclude`: subtraction step; any matching node is excluded

### 2.3 Transform Pipeline

A **pipeline** is an ordered sequence of `ITransformStep` objects applied to each selected file.
Steps are categorised:

**Executable steps** — perform filesystem side effects (`Copy`, `Move`, `Delete`).
  A pipeline may contain multiple executable steps. 
  Execution is enabled only when at least one executable step exists and is valid.

**Validation** — The pipeline is validated by evaluating step preconditions and postconditions. 
This rejects invalid flows like `Delete -> Copy` because `Delete` sets `SourceExists=false` and `Copy` requires `SourceExists=true`.

```csharp
public interface ITransformStep
{
    string StepType { get; }
    bool IsExecutable { get; }

    /// <summary>
    /// True for selection steps (SelectAll, InvertSelection, ClearSelection).
    /// These steps must see every filter-included file, not just the current working set.
    /// </summary>
    bool ProvidesInput => false;

    TransformStepConfig Config { get; }

    TransformResult Preview(TransformContext context);
    Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct);
    void Validate(StepValidationContext context);
}
```

`PipelineRunner` uses a **step-first** loop and accepts two input lists:

- `filterIncludedFiles` — all nodes with `FilterResult == Included` (the universe for selection steps)
- `selectedFiles` — nodes with `CheckState == Checked && FilterResult == Included` (the initial working set)

For each step:
- **Selection steps** (`ProvidesInput = true`, e.g. `SelectAll`, `InvertSelection`, `ClearSelection`): run over `filterIncludedFiles`, mutating each node's `CheckState`. After the step completes, the working set is recomputed as `filterIncludedFiles.Where(n => n.CheckState == Checked)`.
- **All other steps** (path, content, executable): run over the current working set. A `failedNodes` set tracks per-node failures across steps so a node that fails at step N is skipped for steps N+1 onwards.

A `Dictionary<FileSystemNode, TransformContext>` is maintained across steps so that path mutations (e.g. from `FlattenStep`) are preserved when the same node is processed by a later step.

Selection step `Preview()` methods mutate `CheckState` identically to `ApplyAsync()` and return `DestinationPath: null`, ensuring no spurious entries appear in the `OperationPlan`.

Progress is reported after each node through an executable step. `totalBytes` is derived from `filterIncludedFiles` as a conservative upper bound.

**Destination ownership:** `TargetProvider` in `TransformContext` is populated by `CopyStep` and
`MoveStep` from their own `Config.DestinationPath` (resolved to an `IFileSystemProvider` at
pipeline execution time). It is not set from a global target path — there is no global target.
Each executable step carries its own destination, which means a single pipeline could in principle
contain multiple Copy/Move steps writing to different locations.

**Critical invariant:** `PathSegments` is initialized from `node.RelativePathSegments`, which must
be relative to the user's selected source directory (not the filesystem or provider root).

**Path segment convention:** All path steps mutate `PathSegments` directly, no separator assumptions. `StepPathHelper.BuildDestinationPath` has two overloads: one provider-aware (used in `ApplyAsync`, calls `provider.JoinPath`) and one canonical-slash (used in `Preview` and for display).

**Example pipelines:**

| Use case | Steps |
|---|---|
| Simple copy | `[CopyStep]` |
| Flatten copy | `[FlattenStep → CopyStep]` |
| Transcode | `[ConvertStep(mp3, 320k) → CopyStep]` |
| Archive move | `[FlattenStep → MoveStep]` |
| Copy then move | `[CopyStep("/mem/Backup") → MoveStep("/mem/Archive")]` |
| Copy unselected | `[InvertSelectionStep → CopyStep]` |
| Copy everything | `[SelectAllStep → CopyStep]` |

Pipelines saved as `.sc2pipe` (JSON). The simple presets (Copy, Move, Delete) appear as toolbar
buttons and internally create single-step pipelines.

### 2.4 Preview Mode

`PipelineRunner.PreviewAsync()` returns an `OperationPlan`:

```csharp
public class OperationPlan
{
    public IReadOnlyList<PlannedAction> Actions { get; }
    public long TotalInputBytes { get; }
    public long TotalEstimatedOutputBytes { get; }
    public int WarningCount { get; }
}
```

**Destructive preview:** Pipelines containing destructive actions (Delete, Overwrite) always show the preview before execution. The user must explicitly confirm.

### 2.6 Directory Scanner

**Default: progressive scan.** The scanner shows top-level folders immediately, then streams
children in the background via `IAsyncEnumerable<FileSystemNode>`. 

The user can browse and select folders while deeper levels are still loading, so that large libraries, network shares, and MTP sources don't block the UI.

Selecting a folder that hasn't been fully scanned yet triggers a prioritised scan of that subtree.

All enumeration runs on the threadpool. `DirectoryScanner` yields `FileSystemNode` objects via
`IAsyncEnumerable<FileSystemNode>` so the UI can show nodes as they arrive.

### 2.7 Filesystem Watcher

`DirectoryWatcher` wraps `FileSystemWatcher` with:
- 300ms debounce (reset timer on each new event before firing)
- Event coalescing (a Create + Delete for the same path in the debounce window = no-op)
- Fires `DirectoryChanged(string affectedPath)` event

`DirectoryTreeViewModel` handles this by rescanning only the affected subtree and running
save-selections / rescan / restore-selections for that subtree (see §3.5).

### 2.9 Workflows

A **workflow** encapsulates a high-level user operation, coordinating multiple subsystems such as filters, pipelines, and synchronization logic. Workflows simplify complex tasks by providing predefined configurations.

Key components:
- `WorkflowConfig`: Defines the full state of a workflow, including its name, description, selected source/destination paths, filter chain configuration, and the pipeline steps to execute.
- `WorkflowPreset`: A saved instance of a `WorkflowConfig`, allowing users to quickly load and execute workflows.
- `WorkflowPresetStore`: Manages the persistence (CRUD operations) of workflow presets to disk.

---

## 3. Algorithms and Implementation Notes

This section documents the key algorithms and behaviours from the predecessor that must be
correctly reimplemented. It exists because the predecessor source is not in this repository.

### 3.1 Node Selection State

Each `FileSystemNode` carries two independent pieces of application state:

**`CheckState`** — user intent:
- `Checked` — user has explicitly selected this node
- `Unchecked` — user has explicitly deselected this node (or it was never selected)
- `Indeterminate` — folder where some but not all descendants are checked

**`FilterResult`** — filter chain output:
- `Included` — passes all enabled filters (or no filters are active)
- `Excluded` — caught by at least one active filter; carries the name of the first excluding filter

A directory automatically becomes `Excluded` if all its children and files are `Excluded`, and ceases to be `Excluded` if any of its children or files are `Included`.

The UI models an `IsFilterIncluded` property (true if `Included`) which drives checkbox enabled state and visibility.

A file is **selected** (i.e. will be included in the next operation) if and only if:
```
IsSelected = CheckState == Checked && FilterResult == Included
```

### 3.2 Tri-State Checkbox Propagation

Rules for maintaining consistency when check states change:

**When a user checks/unchecks a directory node:**
1. Set `CheckState` on the directory to `Checked` or `Unchecked`
2. Recursively set all descendants to the same state
3. Do not propagate upward — parent state will be recalculated next

**When any node's `CheckState` changes:**
- Walk up the ancestor chain
- For each ancestor directory, recalculate its `CheckState`:
  - All direct children `Checked` → `Checked`
  - All direct children `Unchecked` → `Unchecked`
  - Mixed → `Indeterminate`
- Stop when a recalculated value equals the current value (no further change needed)

**Performance:** For large trees, the upward walk is bounded by tree depth, not node count.
The downward propagation is bounded by the subtree size. Avoid triggering full-tree redraws;
only notify changed nodes.

**BatchUpdate context:** When checking a parent folder, suppress `INotifyPropertyChanged`
notifications for all descendant nodes during the downward propagation pass. Fire a single
coalesced notification (or collection-reset signal) at the end. Without this, checking a
folder with 10k descendants fires 10k binding updates and will freeze the UI even with
virtualization, because data-model updates still flow through the binding system.

### 3.3 Mirror Filter

Determines whether a source node has a counterpart at `JoinPath(ComparisonPath, node.RelativePathSegments)` in a comparison provider.

Current parameters:
- `CompareMode = NameOnly`
- `CompareMode = NameAndSize`

Directories are treated as matched if and only if counterpart exists and contents are identical.

**Automatic ComparisonPath from pipeline:** 
Mirror filters have the option to auto-deduce the path from the pipeline. Filters are updated automatically when the pipeline changes.

### 3.4 Wildcard Pattern Matching

Pattern string uses `;` as separator for multiple patterns. Each pattern is applied to the
**filename only** (not the full path), unless the pattern contains a directory separator.

Conversion from wildcard to regex:
```
.  → \.        (escape literal dot)
*  → .*        (any sequence of characters)
?  → .         (any single character)
```
Matching is case-insensitive. If any pattern in a `;`-separated list matches, the file matches.

Example: `*.mp3;*.flac;*.ogg` matches any file ending in `.mp3`, `.flac`, or `.ogg`.

### 3.5 Tree Rescan with Selection Preservation

When a full rescan is triggered (user action or filesystem watcher on a subtree root):

1. **Snapshot:** walk the current tree and record `{ relativePath → CheckState }` for all nodes
   with `CheckState != Unchecked`
2. **Clear:** remove affected nodes from the tree
3. **Rescan:** run `DirectoryScanner` for the affected root; build new nodes
4. **Restore:** for each new node, look up its `relativePath` in the snapshot; if found, apply
   the saved `CheckState`; otherwise leave as `Unchecked`
5. **Propagate:** recalculate `Indeterminate` states bottom-up

The snapshot can use relative paths (relative to the scan root) for portability within the tree.
For a full rescan of the source root, the snapshot covers the entire tree; for an incremental
watcher-triggered rescan, only the affected subtree is snapshotted and restored.

### 3.6 Selection File Formats

Three formats for saving and restoring selections:

**Plain text (`.txt`):**
```
# SmartCopy selection — /home/user/Music
Rock/Beatles/Abbey Road/01 Come Together.flac
Rock/Beatles/Abbey Road/02 Something.flac
#NODE Jazz/Miles Davis
```
- One file path per line, relative to source root
- `#NODE foldername` selects the entire named folder and all descendants
- Lines starting with `#` (other than `#NODE`) are comments, ignored on load
- Empty lines ignored

**M3U playlist (`.m3u` / `.m3u8`):**
```
#EXTM3U
#EXTINF:256,The Beatles - Come Together
Rock/Beatles/Abbey Road/01 Come Together.flac
#EXTINF:183,The Beatles - Something
Rock/Beatles/Abbey Road/02 Something.flac
```
- Standard extended M3U; compatible with media players
- `#EXTINF:duration,display-title` on the line before each path
- Duration in seconds; display title is `Artist - Title` if known, else filename
- Paths are relative to the source root (or absolute if relative is ambiguous)

**JSON (`.sc2sel`):**
```json
{
  "SchemaVersion": 2,
  "PathParts": [
    ["Rock", "Beatles", "Abbey Road", "01 Come Together.flac"],
    ["Jazz", "Miles Davis", "Kind of Blue", "So What.flac"]
  ]
}
```

### 3.7 Move Operation Strategy

`MoveStep` should attempt the efficient path first:

1. **Cross-provider check** — if `SourceProvider != TargetProvider` (e.g. local → MTP), an atomic
   move is impossible. Degrade immediately to copy-then-delete. This is checked via
   `Capabilities.CanAtomicMove` and provider identity before any other logic. Failing to handle
   this explicitly risks a "half-moved" state on error where the file has been deleted from the
   source but not fully written to the target.
2. **Whole-directory rename** — if same provider, and the entire selected contents of a directory
   are selected (none filtered or unchecked), attempt `provider.MoveAsync(sourceDir, destDir)`. On
   a local provider this maps to `Directory.Move()`, a single OS call that handles large directories
   instantly on the same volume.
3. **File-by-file fallback** — if whole-directory move fails (cross-volume, partial selection,
   or provider doesn't support it), fall back to: copy each file, delete source file.
4. **Empty directory cleanup** — after moving all files from a directory, check if the source
   directory is now empty; if so, delete it. Walk up ancestors and delete empty directories.

### 3.8 Flatten Conflict Detection

When `FlattenStep` strips directory structure, files with the same name from different directories
collide at the destination. `FlattenStep` must detect this during preview and handle it at
execution time.

### 3.9 Safety Defaults for Destructive Operations

The predecessor had no safety net for destructive operations. SmartCopy2026 adds explicit safety
defaults that protect users from accidental data loss while remaining easy to override for
experienced users.

**Delete operations:**
- `DeleteStep` uses the platform trash/recycle bin by default (`DeleteMode.Trash`)
- A `DeleteMode.Permanent` option is available in settings and per-pipeline config
- When `DeleteMode.Permanent` is selected, the pipeline displays a warning badge in the UI

**Trash service (`TrashService`):**
- Windows: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile()` with `SendToRecycleBin`
- Linux: Move to `~/.local/share/Trash/` following the freedesktop.org trash specification
- macOS: `NSFileManager.trashItem` via P/Invoke
- **Timeout:** Wrap all trash operations in a 500ms timeout. If the operation does not complete
  within that window (common on network drives where trash is unsupported or slow), treat trash
  as unavailable and offer a dialog option to switch to permanent delete. Never let a slow
  trash call block the UI or the pipeline.
- Fallback: if trash is unavailable (e.g. network drive with no trash support), warn the user
  and require explicit confirmation for permanent delete

### 3.10 Pipeline Validation (Declarative Contracts)

Pipeline validation runs whenever steps are added, removed, reordered, or edited.

1. Start with baseline fact state: `SourceExists=true`
2. For each step:
   - validate step-level config requirements
   - evaluate preconditions against current fact state
   - emit blocking issue when a precondition fails
   - apply postconditions to produce the next fact state
3. Apply pipeline-level rules (for example: at least one executable step)
4. Return `PipelineValidationResult` (no side effects)

---

### 3.11 Editable ComboBox + ObservableCollection Pitfall (Source Path Selector)

The Source path field is an **editable `ComboBox`** bound to `SourceBookmarks` (items) and
`SourcePath` (text). Two binding pitfalls must be avoided whenever this pattern is used:

**Pitfall 1 — Do not apply side effects from `SelectedItem` changes.**
An editable ComboBox fires `SelectedItem` changes on every arrow-key press (including when the
dropdown is closed). If the ViewModel reacts to `SelectedItem` by triggering an expensive
operation (directory rescan, API call, etc.), the UI becomes sluggish and unintended states are committed.

**Correct pattern:** `OnSelectedSourceBookmarkChanged` should only populate the text field. 
The actual application must be deferred to an explicit commit action:
- **Keyboard:** Enter key fires `ApplySourcePathCommand` (wired via tunnel handler in code-behind)
- **Mouse:** A `SelectionChanged` handler sets `_applyOnDropDownClose = true` when the dropdown
  is open. `DropDownClosed` checks this flag and applies via `Dispatcher.UIThread.Post`.

**Pitfall 2 — `ObservableCollection.Clear()` wipes the ComboBox `Text` binding.**
The ComboBox nulls its `SelectedItem` (e.g. during `RefreshSourceBookmarks`), which causes Avalonia to wipe the `Text` property. The two-way binding propagates that blank back to the ViewModel, erasing the user's input.

**Correct pattern:** Save contents before clearing and restore it after repopulation:

---

### UI Improvements Over Predecessor

1. **Source and destination fields accept drag-and-drop** from Explorer/Finder
2. **Proper tri-state tree checkboxes** — `▣` for indeterminate
3. **Filter chain is visual** — each filter is a card; drag to reorder; toggle without removing
4. **Pipeline is visual** — steps shown as an arrow chain; presets as buttons
   and cards use human-readable summary + technical subtitle formatting
5. **Three-column layout** — Filters | Folders | Files in a single resizable row; all three columns
   have draggable splitters; column widths are persisted across sessions
6. **Filter cards are human-friendly** — each card shows a readable summary ("Only .mp3 and .flac
   files") above a dimmed technical subtitle; enable/disable via checkbox; edit via pencil icon
7. **Preview** — shows exactly what will happen before running
8. **Device picker** — MTP devices appear in the destination path picker on Copy/Move pipeline
   steps alongside local paths (the 📁 Browse button becomes a "Local folder... / Phone (MTP)..."
   split flyout when MTP devices are available)
9. **Keyboard-first** — every action reachable via keyboard; focus indicators on all controls

### Keyboard Navigation (Phase 1 baseline)

All critical actions must be keyboard-accessible from the initial shell:
- **Tab** cycles between the source field, tree, file list, filter chain, pipeline steps
  and action buttons
- **Arrow keys** navigate the tree and file list
- **Space** toggles checkbox on the focused tree node or file list row
- **Enter** expands/collapses a tree node
- **Ctrl+A** selects all visible nodes in the current panel
- **Delete** or **Ctrl+D** removes a selected filter card or pipeline step
- **Ctrl+Shift+P** opens the pipeline preset menu
- **F5** triggers a rescan
- **Escape** cancels a running operation (with confirmation) or closes a modal

Screen-reader labels and focus states are part of the UI shell, not a polish item. Avalonia
supports `AutomationProperties.Name` — use it from the start.
