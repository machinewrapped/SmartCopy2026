# SmartCopy2026 - Architecture Reference

**Prepared:** 2026-02-22
**Source:** Extracted from `Docs/SmartCopy2026-Plan.md` to keep execution planning and implementation reference separate.

This document is the implementation-oriented reference for architecture, technical contracts,
algorithms, UI design, and persisted data models. It preserves section numbering used by the plan
(for example `Section 6.11`) so cross-references remain stable.

## Table of Contents

1. [Architecture Design](#4-architecture-design)
2. [Key Technical Designs](#5-key-technical-designs)
3. [Algorithms and Implementation Notes](#6-algorithms-and-implementation-notes)
4. [UI Design](#7-ui-design)
5. [Data Models](#10-data-models)

---

## 4. Architecture Design

```
SmartCopy2026/
├── SmartCopy.Core/                 # Business logic — no UI references
│   ├── FileSystem/
│   │   ├── IFileSystemProvider.cs      # Abstraction over local + MTP (incl. ProviderCapabilities)
│   │   ├── LocalFileSystemProvider.cs
│   │   ├── MtpFileSystemProvider.cs    # Windows only (#if Windows); includes retry policy
│   │   ├── MemoryFileSystemProvider.cs # In-memory implementation for unit/integration tests
│   │   └── FileSystemNode.cs           # Unified file/folder model
│   ├── Scanning/
│   │   ├── DirectoryScanner.cs         # Async recursive scan with progressive loading
│   │   ├── ScanOptions.cs
│   │   └── DirectoryWatcher.cs         # FileSystemWatcher + 300ms debounce
│   ├── Filters/
│   │   ├── IFilter.cs
│   │   ├── FilterChain.cs
│   │   ├── FilterConfig.cs             # Serialisable per-filter config
│   │   └── Filters/
│   │       ├── WildcardFilter.cs
│   │       ├── ExtensionFilter.cs
│   │       ├── MirrorFilter.cs
│   │       ├── DateRangeFilter.cs
│   │       ├── SizeRangeFilter.cs
│   │       ├── DuplicateFilter.cs
│   │       ├── PathDepthFilter.cs
│   │       └── AttributeFilter.cs
│   ├── Pipeline/
│   │   ├── ITransformStep.cs
│   │   ├── TransformPipeline.cs
│   │   ├── TransformContext.cs
│   │   ├── PipelineRunner.cs
│   │   ├── PipelineConfig.cs
│   │   └── Steps/
│   │       ├── CopyStep.cs             # Terminal: write to target
│   │       ├── MoveStep.cs             # Terminal: move to target
│   │       ├── DeleteStep.cs           # Terminal: delete source (via trash by default)
│   │       ├── FlattenStep.cs          # Path: strip directory structure
│   │       ├── RenameStep.cs           # Path: rename by pattern tokens
│   │       ├── RebaseStep.cs           # Path: add/remove directory levels
│   │       └── ConvertStep.cs          # Content: delegate to IConversionPlugin
│   ├── Plugins/
│   │   ├── IConversionPlugin.cs
│   │   └── PluginLoader.cs
│   ├── Selection/
│   │   ├── SelectionManager.cs
│   │   ├── SelectionSerializer.cs      # txt / m3u / json
│   │   └── SelectionSnapshot.cs
│   └── Progress/
│       └── OperationProgress.cs
│
├── SmartCopy.App/                  # Avalonia application host + DI root
│   ├── App.axaml / App.axaml.cs
│   ├── AppServiceProvider.cs
│   └── Services/
│       ├── DialogService.cs
│       ├── NavigationService.cs
│       └── TrashService.cs            # Platform-specific trash/recycle-bin
│
├── SmartCopy.UI/                   # Avalonia Views + ViewModels
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   ├── DirectoryTreeViewModel.cs
│   │   ├── FileListViewModel.cs
│   │   ├── FilterChainViewModel.cs
│   │   ├── PipelineViewModel.cs
│   │   ├── OperationProgressViewModel.cs
│   │   └── PreviewViewModel.cs
│   ├── Views/
│   │   ├── MainWindow.axaml
│   │   ├── DirectoryTreeView.axaml
│   │   ├── FileListView.axaml
│   │   ├── FilterChainView.axaml
│   │   ├── PipelineView.axaml
│   │   ├── OperationProgressView.axaml
│   │   └── PreviewView.axaml
│   ├── Controls/
│   │   ├── CheckboxTreeItem.axaml      # Tri-state checkbox tree node
│   │   ├── FilterCard.axaml            # One filter in the chain
│   │   └── PipelineStepCard.axaml      # One step in the pipeline
│   └── Converters/
│       ├── FileSizeConverter.cs
│       ├── CheckStateConverter.cs
│       └── FilterResultColorConverter.cs
│
└── SmartCopy.Tests/
    ├── Filters/
    ├── Pipeline/
    ├── Selection/
    └── Scanning/
```

---

## 5. Key Technical Designs

### 5.1 IFileSystemProvider

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

### 5.2 IFilter and FilterChain

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

Runtime semantics are now set-based and order-sensitive:
- Start state is `inSet = true` for each node
- `Only`: intersection step; if a currently-included node does not match, it is excluded
- `Add`: union step; any matching node is re-included, even if excluded earlier
- `Exclude`: subtraction step; any matching node is excluded

Disabled filters are skipped entirely. Filters that do not apply to directories are skipped for
directory nodes (`AppliesToDirectories`).

**Filter types:**

| Filter | Key config | Notes |
|---|---|---|
| `WildcardFilter` | `Pattern` (`;`-separated) | Matches on filename only; `*` and `?` wildcards |
| `ExtensionFilter` | `Extensions` list | Case-insensitive; normalised without leading dot |
| `MirrorFilter` | `ComparisonPath`, `CompareMode` | See §6.3 for full algorithm. `ComparisonPath` is suggested from the first Copy/Move destination path in the current pipeline. |
| `DateRangeFilter` | `Field` (Created/Modified), `Min`, `Max` | Either bound can be null (open range) |
| `SizeRangeFilter` | `MinBytes`, `MaxBytes` | Either bound can be null |
| `AttributeFilter` | `Attributes` flags | Hidden, ReadOnly, System |

Saved as `.sc2filter` (JSON). See §10 for `FilterChainConfig` schema.

### 5.3 Transform Pipeline

A **pipeline** is an ordered sequence of `ITransformStep` objects applied to each selected file.
Steps are categorised:

- **Path steps** — modify `TransformContext.CurrentPath` and/or `CurrentExtension`
- **Content steps** — replace `TransformContext.ContentStream`
- **Terminal steps** — write or delete; exactly one required as the final step

```csharp
public interface ITransformStep
{
    string StepType { get; }
    bool IsPathStep { get; }
    bool IsContentStep { get; }
    bool IsTerminal { get; }
    TransformStepConfig Config { get; }

    TransformResult Preview(TransformContext context);
    Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct);
}

public class TransformContext
{
    public FileSystemNode SourceNode { get; init; }
    public IFileSystemProvider SourceProvider { get; init; }
    public IFileSystemProvider? TargetProvider { get; set; }
    public string CurrentPath { get; set; }         // mutated by path steps
    public string CurrentExtension { get; set; }    // mutated by ConvertStep
    public Stream? ContentStream { get; set; }      // mutated by content steps
    public OverwriteMode OverwriteMode { get; init; }  // Skip | IfNewer | Always
    public DeleteMode DeleteMode { get; init; }     // Trash | Permanent
}
```

`PipelineRunner` iterates selected nodes, creates a fresh `TransformContext` for each, passes it
through each step in sequence, and reports `OperationProgress` after each file completes.

**Destination ownership:** `TargetProvider` in `TransformContext` is populated by `CopyStep` and
`MoveStep` from their own `Config.DestinationPath` (resolved to an `IFileSystemProvider` at
pipeline execution time). It is not set from a global target path — there is no global target.
Each terminal step carries its own destination, which means a single pipeline could in principle
contain multiple Copy/Move steps writing to different locations.

**Example pipelines:**

| Use case | Steps |
|---|---|
| Simple copy | `[CopyStep]` |
| Flatten copy | `[FlattenStep → CopyStep]` |
| Rename | `[RenameStep("{artist} - {title}") → CopyStep]` |
| Transcode | `[ConvertStep(mp3, 320k) → CopyStep]` |
| Transcode + flatten + rename | `[FlattenStep → ConvertStep(mp3) → RenameStep("{artist} - {title}") → CopyStep]` |
| Archive move | `[FlattenStep → MoveStep]` |
| Delete | `[DeleteStep]` |

Pipelines saved as `.sc2pipe` (JSON). The simple presets (Copy, Move, Delete) appear as toolbar
buttons and internally create single-step pipelines.

### 5.4 Preview Mode

`PipelineRunner.PreviewAsync()` calls `step.Preview()` on each step for each selected node (no side
effects). Returns an `OperationPlan`:

```csharp
public record PlannedAction(
    string StepSummary,
    string SourcePath,
    string DestinationPath,
    long InputBytes,
    long EstimatedOutputBytes,
    PlanWarning? Warning);   // null = no issue

public enum PlanWarning { DestinationExists, NameConflict, PermissionIssue }

public class OperationPlan
{
    public IReadOnlyList<PlannedAction> Actions { get; }
    public long TotalInputBytes { get; }
    public long TotalEstimatedOutputBytes { get; }
    public int WarningCount { get; }
}
```

The Preview View shows actions grouped by warning status, sortable by source/dest path.

**Mandatory preview:** Pipelines containing `DeleteStep` or mirror-delete passes always show the
preview before execution. The user must explicitly confirm. This is not optional — delete operations
do not have a "just run" path. Copy and move operations can run directly or via preview at the
user's choice.

### 5.5 Fine-Grained Progress

```csharp
public record OperationProgress(
    string CurrentFile,
    long CurrentFileBytes,
    long CurrentFileTotalBytes,
    int FilesCompleted,
    int FilesTotal,
    long TotalBytesCompleted,
    long TotalBytes,
    TimeSpan Elapsed,
    TimeSpan EstimatedRemaining
);
```

`CopyStep` reads in configurable chunks (default 256KB, user-adjustable in settings). After each
chunk: increment `TotalBytesCompleted`, report progress. ETA = `(TotalBytes - TotalBytesCompleted)
/ (TotalBytesCompleted / Elapsed.TotalSeconds)`.

### 5.6 Directory Scanner

**Default: progressive scan.** The scanner shows top-level folders immediately, then streams
children in the background via `IAsyncEnumerable<FileSystemNode>`. The user can browse and start
selecting folders while deeper levels are still loading. A scan progress indicator shows how much
of the tree has been enumerated so far.

Benefits over the predecessor's full-pre-scan-then-display approach:
- First interaction is near-instant regardless of tree size
- Users can start selecting immediately while the scan continues
- Large libraries, network shares, and MTP sources don't block the UI

Tradeoffs:
- Filters and aggregate statistics (total size, file count) are approximate until the scan
  completes — the status bar shows "scanning..." alongside running totals
- Selecting a folder that hasn't been fully scanned yet triggers a prioritised scan of that
  subtree so its children appear quickly

**Full pre-scan (optional):** `ScanOptions.FullPreScan = true`. The entire tree is enumerated
before the tree is presented, with a progress dialog. Useful when the user wants accurate stats
before selecting anything.

**Lazy expansion (optional):** `ScanOptions.LazyExpand = true`. Children loaded on first TreeView
expand. Useful for very slow sources (MTP devices). Filters and aggregate stats are unavailable
for un-expanded nodes.

All enumeration runs on the threadpool. `DirectoryScanner` yields `FileSystemNode` objects via
`IAsyncEnumerable<FileSystemNode>` so the UI can show nodes as they arrive.

**Priority queue:** The scanner uses a priority queue internally. Background scan tasks (next sibling
subtrees) run at normal priority. When the user expands a tree node or checks a folder, a high-priority
task for that specific subtree is enqueued at the front, so it appears before background work continues.

### 5.7 Filesystem Watcher

`DirectoryWatcher` wraps `FileSystemWatcher` with:
- 300ms debounce (reset timer on each new event before firing)
- Event coalescing (a Create + Delete for the same path in the debounce window = no-op)
- Fires `DirectoryChanged(string affectedPath)` event

`DirectoryTreeViewModel` handles this by rescanning only the affected subtree and running
save-selections / rescan / restore-selections for that subtree (see §6.5).

### 5.8 Conversion Plugin Interface

```csharp
public interface IConversionPlugin
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<string> SupportedInputExtensions { get; }
    IReadOnlyList<string> SupportedOutputExtensions { get; }

    Task ConvertAsync(
        Stream input,  string inputExtension,
        Stream output, string outputExtension,
        ConversionOptions options,
        IProgress<double> progress,
        CancellationToken ct);
}
```

Plugins discovered from `plugins/<name>/` directories alongside the executable. Each folder
contains a `plugin.json` manifest and a DLL loaded via `AssemblyLoadContext` for isolation.

**FFmpeg plugin (separate optional download):**
- Shells out to an `ffmpeg` binary (user-provided or placed in the plugin folder)
- Supports: flac, wav, aiff, ogg → mp3, ogg, aac, opus, flac
- Kept separate from the main app to preserve its MIT license (FFmpeg is LGPL/GPL)
- Plugin itself can be MIT; it shells out to the user's own ffmpeg binary

---

## 6. Algorithms and Implementation Notes

This section documents the key algorithms and behaviours from the predecessor that must be
correctly reimplemented. It exists because the predecessor source is not in this repository.

### 6.1 Node Selection State

Each `FileSystemNode` carries two independent pieces of application state:

**`CheckState`** — user intent:
- `Checked` — user has explicitly selected this node
- `Unchecked` — user has explicitly deselected this node (or it was never selected)
- `Indeterminate` — folder where some but not all descendants are checked

**`FilterResult`** — filter chain output:
- `Included` — passes all enabled filters (or no filters are active)
- `Excluded` — caught by at least one active filter; carries the name of the first excluding filter

A parent directory dynamically becomes `Excluded` if all its loaded children and files are `Excluded`.
The UI models an `IsFilterIncluded` property (true if `Included`) which drives checkbox enabled state and visibility.

A file is **selected** (i.e. will be included in the next operation) if and only if:
```
IsSelected = CheckState == Checked && FilterResult == Included
```

**Visual colour coding** (applied in `FilterResultColorConverter`):

| State | Colour | Meaning |
|---|---|---|
| Selected (checked + included) | Default (black/white) | Will be processed |
| Unchecked + included | Muted (gray) | Visible but not selected |
| Excluded by filter | Accent (e.g. slate blue) | Only shown when ShowFilteredFiles = true |
| Hidden file | Secondary accent (e.g. brown) | Only shown when IncludeHidden = true |

Filtered files are hidden from the file list by default, controlled by `ShowFilteredFiles = true`.
Filtered nodes in the tree are similarly controlled by `ShowFilteredNodesInTree = true`. When visible, they appear with a distinct opacity/colour and are non-selectable (checkboxes are disabled).

### 6.2 Tri-State Checkbox Propagation

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

```csharp
using (node.BeginBatchUpdate())  // suspends PropertyChanged on subtree
{
    PropagateDownward(node, newState);
}
// BeginBatchUpdate().Dispose() fires a single reset notification
```

**Interaction with filters:** Filtering changes `FilterResult` but does not change `CheckState`.
When `ShowFilteredFiles = false`, filtered nodes are hidden in the tree but their `CheckState`
is preserved. If a filter is removed, previously-filtered nodes reappear with their prior state.

### 6.3 Mirror Filter

Determines whether a source node has a counterpart at `ComparisonPath + RelativePath` in a
comparison provider (typically the target side).

Current parameters:
- `CompareMode = NameOnly` — counterpart exists at same relative path
- `CompareMode = NameAndSize` — counterpart exists and size matches (name comparison remains case-insensitive)

The include/exclude behavior is governed by the chain-level `FilterMode` (`Only`, `Add`,
`Exclude`), not by a separate mirror-specific exclusion mode.

Implementation notes (current code path):
- If `comparisonProvider` is null, mirror matching returns `false`
- `comparePath` is built with `PathHelper.CombineForProvider(ComparisonPath, node.RelativePath)`
- If counterpart does not exist, returns `false`
- Directories are treated as matched when counterpart exists
- For `NameAndSize`, file match additionally checks target node `Size`

**ComparisonPath suggestion from pipeline:** `MainViewModel` pushes
`PipelineViewModel.FirstDestinationPath` into `FilterChainViewModel.PipelineDestinationPath`.
When opening `EditFilterDialog` for a mirror filter, the editor is pre-populated with that
suggested path.

### 6.4 Wildcard Pattern Matching

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

### 6.5 Tree Rescan with Selection Preservation

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

### 6.6 Selection File Formats

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
  "sourceRoot": "/home/user/Music",
  "savedAt": "2026-02-18T12:00:00Z",
  "selectedFiles": ["Rock/Beatles/Abbey Road/01 Come Together.flac"],
  "selectedFolders": ["Jazz/Miles Davis"]
}
```
- Used for crash-recovery autosave and session files
- Absolute paths stored for recovery (source root may have changed)

**Loading behaviour:**
- Both relative and absolute paths accepted
- Case-insensitive filename matching (for cross-platform portability)
- Unmatched paths are silently skipped (file may have been deleted or moved)
- `#NODE` / `selectedFolders` entries check all their descendants via `CheckState = Checked`

### 6.7 Sync Operations

The predecessor had four sync modes, each expressible as a filter + pipeline combination:

| Mode | Description | Implementation |
|---|---|---|
| **Update target** | Copy source files not present (or newer) in target | `MirrorFilter(ExcludeMatched, NameOnly)` + overwrite mode `IfNewer` + `[CopyStep]` |
| **Mirror target** | Update target + delete files in target not in source | Update pass, then second pass: enumerate target, apply `MirrorFilter` against source with `ExcludeMatched`, `[DeleteStep]` |
| **Merge** | Copy files differing in either direction (bidirectional) | Two update passes: source→target, then target→source |
| **Find orphans** | List target files with no match in source (no copying) | Enumerate target root, apply `MirrorFilter(ExcludeMatched)` against source, display result |

**Safety:** Mirror target's delete pass always shows a mandatory preview (see §5.4). The preview
clearly labels which files will be deleted and from where. The user must confirm before any
deletions execute.

**Destination resolution:** In the filter + pipeline combinations above, "target" means the
`DestinationPath` of the `CopyStep` or `MoveStep` in the pipeline. There is no separate global
target path field — the pipeline step is the authoritative source of the destination. The
`MirrorFilter`'s comparison path is auto-derived from that same step (see §6.3).

Overwrite strategy when a file exists at the destination (applies to copy and sync operations):
- `Skip` — never overwrite; skip if destination file exists
- `IfNewer` — overwrite only if source `ModifiedAt` > destination `ModifiedAt`
- `Always` — always overwrite

This is configured per-pipeline, not globally. The overwrite mode is carried in `TransformContext`.

### 6.8 Move Operation Strategy

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

### 6.9 Flatten Conflict Detection

When `FlattenStep` strips directory structure, files with the same name from different directories
collide at the destination. `FlattenStep` must detect this during preview and handle it at
execution time.

Default strategy: **auto-rename with counter suffix**
- `song.mp3` already exists → try `song (2).mp3`, `song (3).mp3`, etc.
- Record the rename in the `TransformContext` so `CopyStep` uses the renamed path
- Report the rename as a note in the log
- **The resolved name must appear in the preview.** The `OperationPlan` entry for a renamed file
  must show `song (2).mp3` as the destination path, not `song.mp3`. Users must not be surprised
  by "magic" renames they did not explicitly request.

`FlattenStep.Config` exposes a `ConflictStrategy` enum:
- `AutoRenameCounter` (default) — `name (N).ext`
- `AutoRenameSourcePath` — prefix with a sanitised source path: `Artist_Album_song.mp3`
- `Skip` — log a warning and skip conflicting files
- `Overwrite` — overwrite silently

### 6.10 Status Bar Statistics

The status bar shows live-updating counts derived from the current tree state:
- Files selected (CheckState = Checked, FilterResult = Included)
- Total selected size in bytes (human-readable: KB / MB / GB)
- Files filtered out (FilterResult = Excluded) — only when ShowFilteredFiles is on
- Current operation status (or "Ready")
- Scan status: "Scanning..." with a count when progressive scan is still running

These statistics require walking the tree. This must not block the UI thread. The predecessor ran
this on a dedicated background thread that continuously recalculated and pushed updates. In the
new app, `StatusBarViewModel` exposes observable properties updated via:
- Direct notification when individual `FileSystemNode.CheckState` changes
- A debounced full-recalculation triggered by filter chain changes

For large trees, maintain running totals that are incrementally updated on node state changes
rather than re-scanning the entire tree from scratch each time.

### 6.11 Safety Defaults for Destructive Operations

The predecessor had no safety net for destructive operations. SmartCopy2026 adds explicit safety
defaults that protect users from accidental data loss while remaining easy to override for
experienced users.

**Delete operations:**
- `DeleteStep` uses the platform trash/recycle bin by default (`DeleteMode.Trash`)
- A `DeleteMode.Permanent` option is available in settings and per-pipeline config
- When `DeleteMode.Permanent` is selected, the pipeline displays a warning badge in the UI
- All delete operations (both trash and permanent) require mandatory preview (see §5.4)

**Trash service (`TrashService`):**
- Windows: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile()` with `SendToRecycleBin`
- Linux: Move to `~/.local/share/Trash/` following the freedesktop.org trash specification
- macOS: `NSFileManager.trashItem` via P/Invoke
- **Timeout:** Wrap all trash operations in a 500ms timeout. If the operation does not complete
  within that window (common on network drives where trash is unsupported or slow), treat trash
  as unavailable and fall back to the permanent-delete confirmation dialog. Never let a slow
  trash call block the UI or the pipeline.
- Fallback: if trash is unavailable (e.g. network drive with no trash support), warn the user
  and require explicit confirmation for permanent delete

**Overwrite operations:**
- Default overwrite mode is `IfNewer` (not `Always`)
- When `Always` is selected, files that would be overwritten are highlighted in the preview
- The preview shows file size and date comparisons for overwrite targets

**Mirror target safety:**
- The delete pass in mirror-target sync always shows a mandatory preview
- The preview clearly separates "files to copy" from "files to delete" with distinct sections
- A count summary ("This will delete N files (X MB) from the target") is shown prominently

**Operation journal (lightweight):**
- After each operation completes, write a log file to `%APPDATA%/SmartCopy2026/logs/`
- Format: timestamped list of actions taken (copied, moved, deleted, skipped, failed)
- Not a full undo system — just a record for the user to review if something went wrong
- Auto-cleanup: logs older than 30 days are deleted on startup

---

## 7. UI Design

### Main Window Layout

The main content area uses a **3-column layout** — Filters | Folders | Files — all at the same
height and separated by draggable `GridSplitter`s. This places the filter controls in direct
visual proximity to the tree and file list they affect, making the data-flow left-to-right and
immediately legible to new users.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Source [/home/user/Music                              ▾] [📁]               │
│  ────────────────────────────────────────────────────────────────────────    │
│  ┌─────────────────┐ ║ ┌──────────────────┐ ║ ┌───────────────────────────┐ │
│  │ FILTERS         │ ║ │ ▶ □  Rock        │ ║ │ ☑  Name         Modified  │ │
│  │                 │ ║ │ ▶ ☑  Jazz        │ ║ │ ☑  Come Together.flac … │ │
│  │ ☑ Only .mp3 /   │ ║ │ ▶ ☐  Classical   │ ║ │ ☐  Something.flac      … │ │
│  │   .flac files   │ ║ │                  │ ║ │ ☑  cover.jpg           … │ │
│  │   Ext:*.mp3;…   │ ║ │                  │ ║ │ ☑  desktop.ini         … │ │
│  │                 │ ║ │                  │ ║ │                           │ │
│  │ ☑ Skip files    │ ║ │                  │ ║ │                           │ │
│  │   already on    │ ║ │                  │ ║ │                           │ │
│  │   target        │ ║ │                  │ ║ │                           │ │
│  │   Mirror: from  │ ║ │                  │ ║ │                           │ │
│  │   pipeline ▾    │ ║ │                  │ ║ │                           │ │
│  │ + Add filter    │ ║ │                  │ ║ │                           │ │
│  │─────────────────│ ║ │                  │ ║ │                           │ │
│  │ [Save ▾][Load ▾]│ ║ │                  │ ║ │                           │ │
│  └─────────────────┘ ║ └──────────────────┘ ║ └───────────────────────────┘ │
│  ──────────────────────────────────────────────────────────── [▶ Run      ] │
│  PIPELINE  [Load Preset ▾]                                     [👁 Preview  ] │
│  [⊞ Flatten] → [⚙ Convert  ] → [→ Copy To                  ] →  + Add step │
│              →  [mp3 320k  ]    [/mnt/phone/Music         📁]               │
│  ────────────────────────────────────────────────────────────────────────    │
│  142 files (2.3 GB) selected   17 filtered out  │  ████████░░ 78%  0:34 left │
│  Abbey Road/01 Come Together.flac                                            │
└──────────────────────────────────────────────────────────────────────────────┘
```

`║` = draggable GridSplitter between columns

**Filter card anatomy** (each filter in the Filters column):
```
┌──────────────────────────────────────────────────┐
│ ☑  Only .mp3 and .flac files        ≡  ✎  ✕    │
│    Extension: *.mp3; *.flac                       │
└──────────────────────────────────────────────────┘
```
- **Checkbox** (left) — enable/disable the filter in-place
- **Summary** (bold) — human-readable one-liner generated from filter config
- **Description** (dimmed subtitle) — raw technical spec for power users
- **Drag handle** `≡`, **edit pencil** `✎`, **remove** `✕` (right-aligned)
- The mode selector (`ONLY` / `ADD` / `EXCLUDE`) and detailed config live in the edit dialog, not the card face

### Filter UX Flow

#### Add Filter — Two-Level Drill-Down

Clicking "+ Add filter" opens a **`Popup`** anchored below the button (light-dismiss on
click-outside). The flyout uses two panels swapped by a boolean flag — no secondary window.

**Level 1 — Filter type selection:**
```
┌────────────────────────────────┐
│  Add Filter                    │
├────────────────────────────────┤
│  Extension    Filter by ext.   │
│  Wildcard     Name pattern     │
│  Date Range   Created/Modified │
│  Size Range   Min/Max bytes    │
│  Mirror       Skip on target   │
│  Attribute    Hidden/ReadOnly  │
└────────────────────────────────┘
```

**Level 2 — Preset picker for chosen type:**
```
┌────────────────────────────────┐
│  ← Extension                   │
├────────────────────────────────┤
│  ＋ New...                     │
├────────────────────────────────┤
│  Recently used                 │
│    My music (*.mp3;*.flac)     │
├────────────────────────────────┤
│  ★ Audio files                 │
│  ★ Images                      │
│  ★ Documents                   │
│  ★ Log files                   │
│    My custom preset            │
└────────────────────────────────┘
```

- **"＋ New..."** — closes flyout, opens `EditFilterDialog` with empty form for selected type
- **Preset row** — closes flyout, adds card immediately, triggers tree/file list update
- **"←"** — returns to Level 1; "★" = built-in (read-only); plain rows = user-saved presets
- "Recently used" shows the last 5 presets of this type from `AppSettings.FilterTypeMruPresetIds`

#### EditFilterDialog

A **modal `Window`** (`ShowDialog<bool?>`) opened via "＋ New..." or the edit pencil on any card.
The dialog dispatches to a type-specific editor view via `ContentControl` + `DataTemplate`.

```
┌─────────────────────────────────────────┐
│  Edit Filter                            │
├─────────────────────────────────────────┤
│  [ONLY ●] [ADD ○] [EXCLUDE ○]           │  ← mode radio group
├─────────────────────────────────────────┤
│  Name: [Only .mp3 and .flac       ]     │  ← auto-generated, user-overridable
├─────────────────────────────────────────┤
│  ┌── type-specific form ──────────────┐ │
│  │ Extension: [.mp3 ×][.flac ×] +Add  │ │  chips + input
│  │ Wildcard:  [*.tmp;*.bak          ]│ │  single text box
│  │ Date Range: ○Created ●Modified    │ │  radio + two CalendarDatePickers
│  │ Size Range: Min[1.5] Max[──] [MB▾] │ │  shared unit selector
│  │ Mirror:     [/mem/Mirror     ][…] │ │  browse button disabled in Phase 1
│  │             ○Name  ●Name+Size     │ │
│  │ Attribute:  ☐Hidden ☐RO ☐System  │ │
│  └────────────────────────────────── ┘ │
├─────────────────────────────────────────┤
│  ☐ Save as preset                       │
├─────────────────────────────────────────┤
│            [Cancel]      [OK ✓]         │  ← OK disabled when !IsValid
└─────────────────────────────────────────┘
```

"Save as preset" sets a flag on the dialog VM; after a successful dialog close,
`FilterChainView.axaml.cs` persists the preset via `FilterPresetStore`.

#### Filter Results in Tree / File List

- **`FilterResult.Excluded` + `ShowFilteredFiles=true`**: files remain visible; tree rows are dimmed
  (`Opacity = 0.4`) and excluded file names are styled (`SlateBlue`)
- **`FilterResult.Excluded` + `ShowFilteredFiles=false`**: excluded files are removed from
  `VisibleFiles`; tree remains navigable with excluded directories still shown dimmed
- All filter changes propagate within ~100 ms via a debounced `CancellationTokenSource`
- Drag handle `≡` on filter cards reorders the chain (Avalonia `DragDrop`); order affects
  chain evaluation sequence

### UI Improvements Over Predecessor

1. **Source and destination fields accept drag-and-drop** from Explorer/Finder
2. **Proper tri-state tree checkboxes** — `▣` for indeterminate
3. **Filter chain is visual** — each filter is a card; drag to reorder; toggle without removing
4. **Pipeline is visual** — steps shown as an arrow chain; presets as buttons
5. **Three-column layout** — Filters | Folders | Files in a single resizable row; all three columns
   have draggable splitters; column widths are persisted across sessions
6. **Filter cards are human-friendly** — each card shows a readable summary ("Only .mp3 and .flac
   files") above a dimmed technical subtitle; enable/disable via checkbox; edit via pencil icon
7. **Status bar** — selected count + size, filtered count, current operation
8. **Preview** — shows exactly what will happen before running
9. **Device picker** — MTP devices appear in the destination path picker on Copy/Move pipeline
   steps alongside local paths (the 📁 Browse button becomes a "Local folder... / Phone (MTP)..."
   split flyout when MTP devices are available)
10. **Log panel** — collapsible panel at the bottom showing timestamped operation log
11. **Keyboard-first** — every action reachable via keyboard; focus indicators on all controls
12. **Window state persistence** — size, position, maximised state, and all column widths saved to
    `%LOCALAPPDATA%/SmartCopy2026/window.json`; off-screen position safety check on restore

### Keyboard Navigation (Phase 1 baseline)

All critical actions must be keyboard-accessible from the initial shell:
- **Tab** cycles between the source field, tree, file list, filter chain, pipeline steps
  (including inline destination fields on Copy/Move step cards), and action buttons
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

### UI Shell Scope (Phase 1, Step 1)

The shell must be built first with placeholder/hardcoded data before any business logic is wired.
This validates the layout and UX before architecture decisions are locked in.

Shell checklist:
- [x] Main window with correct proportions, resizable split panes (3-column: Filters/Folders/Files)
- [x] Source field with browse button (no real browsing yet); target path removed — destination
      is owned by Copy/Move pipeline step cards
- [x] TreeView with 2–3 levels of hardcoded nodes, tri-state checkbox behaviour fully working
- [x] FileListView with hardcoded rows, all columns (Name/Size/Modified), column resizing, click-to-sort
- [x] Filter chain area: two placeholder filter cards with human-readable summary + technical subtitle,
      enable/disable checkbox, edit (pencil) button, remove button, inline "+ Add filter" ghost card,
      Save/Load buttons pinned to bottom of column
- [x] Pipeline area: horizontal scrollable step chain with → connectors; Load Preset ▾ menu
      (Standard: Copy/Move/Delete presets; My Pipelines; Save current pipeline); + Add step flyout
      (Path / Content / Terminal step categories); Run and Preview buttons stacked on the right;
      Copy/Move step cards show inline destination path TextBox + stub 📁 Browse button
- [x] Status bar: placeholder text (file count, size, filtered count, progress bar, time remaining, current file)
- [x] Window size, position, maximised state, and all three column widths persisted to
      `%LOCALAPPDATA%/SmartCopy2026/window.json`; restored on next open with off-screen safety guard
- [ ] Operation progress overlay: progress bars, pause/cancel buttons, status labels — no real operation
- [ ] Log panel: collapsible, scrollable, a few placeholder log entries
- [ ] Full keyboard navigation: Tab order, arrow keys in tree/list, Space to toggle, focus indicators
- [ ] Automation properties on all interactive controls (screen-reader baseline)

---

## 10. Data Models

### FileSystemNode

```csharp
public class FileSystemNode : INotifyPropertyChanged
{
    // Filesystem data (immutable after scan)
    public string Name { get; init; }
    public string FullPath { get; init; }
    public string RelativePath { get; init; }   // relative to scan root
    public bool IsDirectory { get; init; }
    public long Size { get; init; }             // 0 for directories
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public FileAttributes Attributes { get; init; }

    // Application state (mutable, raises PropertyChanged)
    public CheckState CheckState { get; set; }       // Checked | Unchecked | Indeterminate
    public FilterResult FilterResult { get; set; }   // Included | Excluded
    public string? ExcludedByFilter { get; set; }    // filter name if Excluded
    public string? Notes { get; set; }               // e.g. "Mirrored at /Mirror/foo"

    // Computed
    public bool IsSelected => CheckState == CheckState.Checked
                           && FilterResult == FilterResult.Included;

    // Tree structure
    public FileSystemNode? Parent { get; init; }
    public ObservableCollection<FileSystemNode> Children { get; }
}

public enum CheckState    { Checked, Unchecked, Indeterminate }
public enum FilterResult  { Included, Excluded }
public enum DeleteMode    { Trash, Permanent }
```

### FilterChainConfig (persistence)

```csharp
public sealed record FilterConfig(
    string FilterType,         // "Wildcard" | "Mirror" | "DateRange" | etc.
    bool IsEnabled,
    string Mode,               // "Only" | "Add" | "Exclude" (legacy "Include" is accepted on read)
    JsonObject Parameters,     // type-specific; see individual filter for keys
    string? CustomName = null
);

public sealed record FilterChainConfig(
    string Name,
    string? Description,
    List<FilterConfig> Filters
);
```

### PipelineConfig (persistence)

```csharp
public record TransformStepConfig(
    string StepType,           // "Copy" | "Flatten" | "Convert" | etc.
    JsonObject Parameters
);

public record PipelineConfig(
    string Name,
    string? Description,
    List<TransformStepConfig> Steps,
    string OverwriteMode,      // "Skip" | "IfNewer" | "Always"
    string DeleteMode          // "Trash" | "Permanent"
);
```

**Copy/Move step parameters** (stored in `TransformStepConfig.Parameters`):
```json
{ "destinationPath": "/mnt/phone/Music", "overwriteMode": "IfNewer" }
```
The destination path is part of the individual step's config, not a top-level pipeline
property. This allows a single pipeline to contain multiple Copy/Move steps writing to
different locations.

### AppSettings (JSON — `settings.json`)

```csharp
public class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? LastSourcePath { get; set; }
    public bool IncludeHidden { get; set; } = false;
    public bool ShowFilteredFiles { get; set; } = false;
    public bool AutoSelectOnSelectionRestore { get; set; } = true;
    public bool AllowOverwrite { get; set; } = false;
    public bool AllowDeleteReadOnly { get; set; } = false;
    public bool LazyExpandScan { get; set; } = false;
    public bool FullPreScan { get; set; } = false;
    public bool EnableFilesystemWatcher { get; set; } = true;
    public int CopyChunkSizeKb { get; set; } = 256;
    public string DefaultOverwriteMode { get; set; } = "IfNewer";
    public string DefaultDeleteMode { get; set; } = "Trash";
    public int LogRetentionDays { get; set; } = 30;
    public ColumnSettings FileListColumns { get; set; } = new();
    public List<string> RecentSources { get; set; } = [];
    // No global target path exists; Copy/Move steps own destinationPath.
    // RecentTargets is only for destination suggestions in step cards.
    public List<string> RecentTargets { get; set; } = [];
    public List<string> FavouritePaths { get; set; } = [];
    public List<string> RecentFilterChains { get; set; } = [];
    public List<string> RecentPipelines { get; set; } = [];
}
```

Window geometry is stored in a **separate** file (`%LOCALAPPDATA%/SmartCopy2026/window.json`)
rather than `settings.json`. This isolates fast-changing UI state from slower application
settings and avoids a read-modify-write race on startup:

```csharp
// Written/read by MainWindow.axaml.cs — not part of AppSettings
private sealed class WindowSettings
{
    public double  Width           { get; set; } = 1400;
    public double  Height          { get; set; } = 860;
    public double? X               { get; set; }   // null = use CenterScreen
    public double? Y               { get; set; }
    public bool    IsMaximized     { get; set; }
    public double? ColWidthFilters { get; set; }   // pixels; null = use star-ratio default
    public double? ColWidthFolders { get; set; }
    public double? ColWidthFiles   { get; set; }
}
```

**Safety rules applied on restore:**
- Width/height clamped to minimum 800×600
- Per-column minimums: Filters ≥ 150 px, Folders ≥ 150 px, Files ≥ 300 px
- Position only restored if the top-left corner + 100 px inset lands on a connected screen
  (prevents "lost window" after monitor configuration changes)
- All I/O wrapped in try/catch — corrupt file silently falls back to defaults

---

