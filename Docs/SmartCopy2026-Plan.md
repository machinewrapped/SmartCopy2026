# SmartCopy2026 — Design & Implementation Plan (Revised)

**Prepared:** 2026-02-18
**Revised:** 2026-02-22 (Phase 1 is now strictly memory-first UX/architecture; local filesystem integration moved to standalone Phase 2)
**Author:** Simon Booth
**License:** MIT
**Predecessor:** SmartCopy 2015 (GPL v3, .NET 4.8 WinForms, SourceForge)

This document is the primary design reference for the SmartCopy2026 rewrite. The predecessor
source is not included in this repository; all relevant algorithms, data models, and design
decisions learned from analysing it are captured here.

---

## Table of Contents

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

### Rejected alternatives

- **WPF** — Windows-only; forfeits cross-platform goal
- **MAUI** — Mobile-first; poor for traditional tree/list desktop UIs
- **Electron** — large runtime/RAM overhead for a desktop file utility; Node.js I/O adds unnecessary complexity
- **Tauri/Rust** — Rust expertise required; web UI is poor for complex tree views; MTP via unsafe FFI
- **Kotlin + Compose Multiplatform** — JVM distribution overhead; no developer familiarity; MTP requires JNI

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

**Path matching semantics:** All internal path comparisons for filter matching use
case-insensitive comparison by default. On case-sensitive filesystems (Linux), this means
`Rock/` and `rock/` are treated as the same path for mirror/selection purposes. This is the
pragmatic choice — users copying between platforms expect case-insensitive matching. If a user
needs case-sensitive matching on Linux, a future setting can expose this.

### Avalonia tree performance note

Avalonia's `VirtualizingStackPanel` handles large lists efficiently, but propagating tri-state
checkbox state across a deep tree of 100k+ nodes requires deliberate care. The state propagation
algorithm must be efficient (see §6.2). This is an implementation concern, not a reason to switch
framework.

**TreeDataGrid:** Avalonia's `TreeDataGrid` control (stable in 11+) may outperform a hand-rolled
`TreeView` + column layout for the file list pane. Evaluate it during Step 1 shell construction;
it provides built-in column resizing and sort headers that would otherwise need custom controls.

### .NET 10 targeting

Use `net10.0` TFM. .NET 10 is LTS (supported until ~2028). If building on a machine with only .NET 9
installed, `global.json` can pin the SDK version. Self-contained publish (`--self-contained true
-p:PublishSingleFile=true`) bundles the runtime and ships a single executable per platform.

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

Filtered files are hidden from the tree/list by default. When `ShowFilteredFiles = true`, they
appear with a distinct colour and are non-selectable (user cannot check a filtered file).

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
suggested path. In Phase 1 memory-backed runs, startup seeds this to `/mem/target`.

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
│  │ Mirror:     [/mem/target     ][…] │ │  browse button disabled in Phase 1
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
| UX-4 (Step 6): Transform pipeline | In progress | Core pipeline (`TransformPipeline`, `PipelineRunner`) and built-in steps (`Copy/Move/Delete/Flatten`) implemented with tests | Wire Preview/Run in UI, enforce delete-confirm preview policy, add journal/progress integration |
| UX-5 (Step 7): Sync operations | Started (core skeleton) | `SyncWorkflow` has find-orphans and basic update/mirror builders | Implement full update/mirror semantics (`IfNewer`, orphan delete pass with confirmation) + UI entry points |
| Hardening-1 (Step 2): Memory provider foundation | Complete | `FileSystemNode`, `IFileSystemProvider`, `ProviderCapabilities`, `MemoryFileSystemProvider`, provider contract tests, and shared memory-first fixture builders are implemented | Reuse the shared fixture builder pattern for all new core workflow tests |
| Hardening-2 (Step 8): Selection save/load | In progress | `SelectionSerializer` (`.txt`, `.m3u`, `.sc2sel`) and `SelectionManager` implemented with round-trip tests | Wire File menu flows and add unmatched-path reporting behavior |
| Hardening-3 (Step 9): Settings persistence | In progress | `AppSettings` + `AppSettingsStore` implemented with corrupt-file fallback tests and cross-platform path resolution | Add schema migration path and startup/shutdown wiring for persisted defaults |
| Polish-1 (Step 10): Shell observability + status | Not started | Scope split out of overloaded Step 1/Step 4 | Implement after Step 7 end-to-end UX loop is proven |
| Polish-2 (Step 11): Keyboard + accessibility baseline | Not started | Scope split out of overloaded Step 1 | Implement after Step 10 |

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
- [x] Implement `FileSystemNode` full model contract from §10
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
- [x] Tri-state propagation algorithm from §6.2 in production nodes/view models
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

Modify `MainViewModel`: construct `AppSettings` + `FilterPresetStore`; pass to `FilterChainViewModel`; keep `PipelineDestinationPath` synchronized with pipeline destination and seed it to `/mem/target` during `InitializeAsync`.

Tests (`SmartCopy.Tests/Filters/FilterChainViewModelTests.cs`): `BuildLiveChain`; add/remove fire `ChainChanged`; `IsEnabled` toggle fires `ChainChanged`; `ReplaceFilter` updates VM.

#### Sub-step 4f — Live wiring: filter application to tree + file list

Modify `MainViewModel`: subscribe `FilterChain.ChainChanged` → `ApplyFiltersAsync()` (CancellationTokenSource debounce; passes same `MemoryFileSystemProvider` as `comparisonProvider`).

Modify `MockMemoryFileSystemFactory`: add `/mem/target` subtree with representative files.

Modify `DirectoryTreeViewModel`: add `ApplyFiltersAsync(FilterChain, IFileSystemProvider?, CancellationToken)` delegating to `chain.ApplyToTreeAsync(RootNodes, …)`.

Modify `FileListViewModel`: remove stub `FilterResult` logic; add `UpdateChain`, `ReapplyFiltersAsync`; expose `VisibleFiles` (respects `ShowFilteredFiles`).

New: `SmartCopy.UI/Converters/FilterResultOpacityConverter.cs` — `Excluded → 0.4`, `Included → 1.0`.

Modify `DirectoryTreeView.axaml`: add `Opacity` style binding via `FilterResultOpacityConverter`. Wire `FileListView.axaml` `DataGrid.ItemsSource` to `VisibleFiles`.

Tests (`SmartCopy.Tests/Filters/FilterLiveWiringTests.cs`) against `MemoryFileSystemProvider`: extension filter excludes non-matching files; `Only`+`Exclude` chain behavior; `ShowFilteredFiles` toggle; disabled filter resets excluded nodes; `ReapplyFiltersAsync` updates existing loaded file nodes.

#### Acceptance criteria
- [x] `Only`/`Add`/`Exclude` semantics match §5.2
- [x] Disabled filters have zero effect on `FilterResult`
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

Deliverables:
- [x] `ITransformStep`, `TransformPipeline`, `TransformContext`, `PipelineRunner`
- [x] `CopyStep`, `MoveStep`, `DeleteStep`, `FlattenStep`
- [ ] Preview generation (`OperationPlan`) and preview UI wiring (core complete; UI wiring pending)
- [ ] Progress overlay wired to real operation events
- [ ] Operation journal written to `%APPDATA%/SmartCopy2026/logs/`

Acceptance criteria:
- [x] Exactly one terminal step required and validated
- [ ] Delete pipelines always require explicit preview confirmation
- [ ] Overwrite and delete modes are honored per context/config

Verification:
- [ ] Unit tests for copy/move/delete/flatten behavior and conflict handling
- [ ] Integration test: scan -> select -> filter -> preview -> execute -> verify outputs

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
- [ ] Persisted UI and workflow defaults (sort, scan options, recents)

Acceptance criteria:
- [x] Missing/corrupt settings file falls back to defaults without crash
- [ ] Forward-compatible migration path exists for schema changes

Verification:
- [ ] Unit tests for serialization/migration/error fallback
- [ ] Manual smoke test across restart on Windows

### Step 9 — Shell Observability and Status Feedback (UX Polish Track)

Deliverables:
- [ ] Collapsible log panel with placeholder entries in the shell layout
- [ ] Status bar live counts/size from §6.10
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
- [ ] Incremental subtree rescan with selection preservation (§6.5)
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
- [ ] Drag-and-drop and bookmarks/favorites for source field and pipeline destination fields

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
    public string? Notes { get; set; }               // e.g. "Mirrored at /target/foo"

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
4. Network trash behavior is defined in §6.11 and should be implemented as a hard safety rule.
