# SmartCopy2026 — Design & Implementation Plan (Revised)

**Prepared:** 2026-02-18
**Revised:** 2026-02-19 (incorporated Gemini review feedback)
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
- **Electron** — 150MB+ runtime; 200MB+ RAM for a file utility is embarrassing; Node.js I/O adds unnecessary overhead
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
    FilterMode Mode { get; }        // Include | Exclude
    bool IsEnabled { get; set; }
    FilterConfig Config { get; }    // serialisable

    /// <summary>
    /// Returns true if this filter matches the node.
    /// For Exclude filters: matched nodes are hidden.
    /// For Include filters: only matched nodes are shown.
    /// comparisonProvider is non-null when filter needs to check a second location (MirrorFilter).
    /// </summary>
    bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider);
}

public class FilterChain
{
    public IReadOnlyList<IFilter> Filters { get; }

    // Applies all enabled filters in order. A node survives if it passes all Include
    // filters and is not caught by any Exclude filter.
    public IEnumerable<FileSystemNode> Apply(
        IEnumerable<FileSystemNode> nodes,
        IFileSystemProvider? comparisonProvider = null);

    public FilterChainConfig ToConfig();
    public static FilterChain FromConfig(FilterChainConfig config);
}
```

Filter evaluation order matters. Later filters in the chain narrow the result further. Disabled
filters are skipped entirely (their nodes are treated as if the filter does not exist).

**Filter types:**

| Filter | Key config | Notes |
|---|---|---|
| `WildcardFilter` | `Pattern` (`;`-separated) | Matches on filename only; `*` and `?` wildcards |
| `ExtensionFilter` | `Extensions` list | Case-insensitive; normalised without leading dot |
| `MirrorFilter` | `ComparisonPath`, `CompareMode`, `ExcludeMode` | See §6.3 for full algorithm |
| `DateRangeFilter` | `Field` (Created/Modified), `Min`, `Max` | Either bound can be null (open range) |
| `SizeRangeFilter` | `MinBytes`, `MaxBytes` | Either bound can be null |
| `DuplicateFilter` | `Scope` (WithinSource/VsTarget) | Excludes all but first occurrence |
| `PathDepthFilter` | `MinDepth`, `MaxDepth` | Depth 0 = root; either bound nullable |
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

Determines whether a file in the source has a counterpart in a comparison location (typically the
target). The filter has two orthogonal parameters:

**CompareMode** — what constitutes a "match":
- `NameOnly` — same filename (case-insensitive) at the same relative path
- `NameAndSize` — same filename AND same byte count
- `ExtensionAgnostic` — same filename stem (no extension), any extension, same relative path
  (useful when source is .flac and target has been converted to .mp3)

**ExcludeMode** — which files to exclude from the view:
- `ExcludeMatched` (most common) — hide files that exist in the comparison location.
  Use case: "show me only files not yet copied to the target"
- `ExcludeUnmatched` — hide files that do NOT exist in the comparison location.
  Use case: "show me only files that are already mirrored"

Implementation notes:
- Build a lookup set from the comparison provider at filter application time (not per-file)
- For `NameOnly`/`NameAndSize`: key = `relativePath.ToLowerInvariant()`
- For `ExtensionAgnostic`: key = `Path.ChangeExtension(relativePath, "").ToLowerInvariant()`
- Rebuild the lookup when the comparison path changes (not on every `Matches()` call)

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

```
┌──────────────────────────────────────────────────────────────────────┐
│  Source [/home/user/Music               ▾] [📁]                      │
│  Target [/mnt/phone/Music               ▾] [📁] [📱 Phone (MTP)]     │
│  ──────────────────────────────────────────────────────────────────  │
│  ┌──────────────────────┐  ┌───────────────────────────────────────┐ │
│  │ ▶ □  Rock            │  │ ☑  Name               Size  Modified  │ │
│  │   ▼ ☑  Classic Rock  │  │ ☑  Come Together.flac 48MB 2024-03-01│ │
│  │      ☑  Beatles      │  │ ☐  Something.flac     32MB 2024-03-01│ │
│  │      ☐  Rolling St.  │  │ ☑  cover.jpg         420KB 2024-03-01│ │
│  │   ▶ ▣  Metal         │  │                                       │ │
│  │ ▶ ☑  Jazz            │  │                                       │ │
│  │ ▶ ☐  Classical       │  │                                       │ │
│  └──────────────────────┘  └───────────────────────────────────────┘ │
│  ──────────────────────────────────────────────────────────────────  │
│  FILTERS  [+ Add ▾]  [Save ▾]  [Load ▾]                              │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ ≡  Extension: *.mp3;*.flac   [INCLUDE ▾]   [●]  [✕]        │    │
│  │ ≡  Mirror: /target  name+size  [EXCLUDE matched ▾]  [●] [✕] │    │
│  └──────────────────────────────────────────────────────────────┘    │
│  ──────────────────────────────────────────────────────────────────  │
│  PIPELINE  [Copy ▾]  [Move ▾]  [Delete]  [Custom ▾]  [▶ Run]  [👁 Preview] │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │  [⊞ Flatten]  →  [⚙ Convert: mp3 320k]  →  [→ Copy]  [✕]  │    │
│  └──────────────────────────────────────────────────────────────┘    │
│  ──────────────────────────────────────────────────────────────────  │
│  142 files (2.3 GB) selected   17 filtered out  │  ████████░░ 78%  0:34 left │
│  Abbey Road/01 Come Together.flac                                     │
└──────────────────────────────────────────────────────────────────────┘
```

`≡` = drag handle for reordering  `[●]` = enable/disable toggle

### UI Improvements Over Predecessor

1. **Source and target accept drag-and-drop** from Explorer/Finder
2. **Proper tri-state tree checkboxes** — `▣` for indeterminate
3. **Filter chain is visual** — each filter is a card; drag to reorder; toggle without removing
4. **Pipeline is visual** — steps shown as an arrow chain; presets as buttons
5. **Split pane is resizable** — drag the divider between tree and file list
6. **Status bar** — selected count + size, filtered count, current operation
7. **Preview** — shows exactly what will happen before running
8. **Device picker** — MTP devices appear in the target path dropdown alongside local paths
9. **Log panel** — collapsible panel at the bottom showing timestamped operation log
10. **Keyboard-first** — every action reachable via keyboard; focus indicators on all controls

### Keyboard Navigation (Phase 1 baseline)

All critical actions must be keyboard-accessible from the initial shell:
- **Tab** cycles between source/target fields, tree, file list, filter chain, pipeline, buttons
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
- [ ] Main window with correct proportions, resizable split pane
- [ ] Source/target fields with browse button (no real browsing yet — just editable text)
- [ ] TreeView with 2–3 levels of hardcoded nodes, tri-state checkbox behaviour fully working
- [ ] FileListView with 5–6 hardcoded rows, all columns, column resizing, click-to-sort
- [ ] Filter chain area: two placeholder `FilterCard` controls, drag-to-reorder working
- [ ] Pipeline area: two placeholder `PipelineStepCard` controls with arrow connectors
- [ ] Status bar: placeholder text updating on checkbox interactions
- [ ] Operation progress overlay: progress bars, pause/cancel buttons, status labels — no real operation
- [ ] Log panel: collapsible, scrollable, a few placeholder log entries
- [ ] Menu skeleton: File, Edit, Filters, Pipeline, Tools, Help (items present, no handlers)
- [ ] Full keyboard navigation: Tab order, arrow keys in tree/list, Space to toggle, focus indicators
- [ ] Automation properties on all interactive controls (screen-reader baseline)

---

## 8. Phase 1 — Core Workflows

*Goal: a cross-platform file manager that nails the core workflows — browse, select, filter,
copy, sync — with a clean architecture that makes everything else easy to add.*

Work in this order. Each step should leave the app in a buildable, runnable state.

### Step 1 — Project Scaffold + UI Shell

- [ ] Create solution: `SmartCopy.Core`, `SmartCopy.App`, `SmartCopy.UI`, `SmartCopy.Tests`
- [ ] `SmartCopy.Core` targets `net10.0`; `SmartCopy.App` targets Avalonia with `net10.0`
- [ ] Wire up DI in `AppServiceProvider.cs`
- [ ] **Build the complete UI shell** (see §7 UI Shell Scope above, including keyboard nav)
- [ ] All ViewModels are stubs with hardcoded/fake data — no real filesystem calls yet
- [ ] App launches, layout looks right, resizing works, checkboxes propagate, drag-to-reorder works
- [ ] CI pipeline: build + test on push (GitHub Actions, Windows + Linux runners)

### Step 2 — Core Models + Local Filesystem Provider

- [ ] `FileSystemNode` with all properties and observable `Children`
- [ ] `IFileSystemProvider` interface (including `ProviderCapabilities`)
- [ ] `LocalFileSystemProvider` implementation
- [ ] **`MemoryFileSystemProvider`** — in-memory implementation of `IFileSystemProvider` for use
      in all unit and integration tests; avoids slow disk I/O and manual temp-directory cleanup;
      enables millisecond-fast tests for scanning, filtering, and pipeline logic
- [ ] `TrashService` with platform-specific implementations and 500ms timeout
- [ ] Unit tests: `LocalFileSystemProvider` against a temp directory fixture

### Step 3 — Directory Scanner

- [ ] `DirectoryScanner` with `IAsyncEnumerable<FileSystemNode>` yield
- [ ] Progressive scan: top-level folders first, stream children in background
- [ ] `ScanOptions`: `IncludeHidden`, `FullPreScan`, `LazyExpand`, `MaxDepth`
- [ ] Scan progress reporting via `IProgress<ScanProgress>`
- [ ] Wire into `DirectoryTreeViewModel`: replace hardcoded nodes with real scan
- [ ] Unit tests: scan a temp directory structure, verify node counts and depths

### Step 4 — Node Selection Logic

- [ ] Tri-state checkbox propagation (downward set + upward recalculate) — see §6.2
- [ ] `IsSelected` computed property on `FileSystemNode`
- [ ] Status bar live-updating selected count and size — see §6.10
- [ ] Unit tests: propagation rules for checked/unchecked/indeterminate

### Step 5 — Filter Chain

- [ ] `IFilter`, `FilterChain`, `FilterConfig`
- [ ] All filter types: `WildcardFilter`, `ExtensionFilter`, `MirrorFilter`, `DateRangeFilter`,
  `SizeRangeFilter`, `AttributeFilter`
- [ ] Wire `FilterChain` into tree + file list view: excluded nodes hidden or coloured
- [ ] `FilterChainViewModel` wired to real `FilterChain`
- [ ] Unit tests for each filter type and filter chain composition

### Step 6 — Transform Pipeline (Built-in Steps)

- [ ] `ITransformStep`, `TransformPipeline`, `TransformContext`, `PipelineRunner`
- [ ] `CopyStep` with 256KB chunk streaming + `OperationProgress` reporting
- [ ] `MoveStep` with whole-directory-first strategy — see §6.8
- [ ] `DeleteStep` with trash-by-default + ReadOnly handling — see §6.11
- [ ] `FlattenStep` with conflict detection — see §6.9
- [ ] `PreviewOperation`: dry-run producing `OperationPlan`
- [ ] Mandatory preview for delete pipelines — see §5.4
- [ ] Wire progress overlay to real `PipelineRunner` — replace placeholder
- [ ] Wire Preview button to real `PreviewViewModel`
- [ ] Operation journal: log completed actions via Serilog rolling-file sink to `%APPDATA%/SmartCopy2026/logs/` — see §6.11
- [ ] Unit tests: copy, move, delete, flatten against temp directories
- [ ] Integration test: end-to-end copy workflow (scan → select → filter → copy → verify)

### Step 7 — Sync Operations

- [ ] Update target (MirrorFilter + CopyStep + IfNewer overwrite mode) — see §6.7
- [ ] Mirror target (Update + delete orphans in second pass) with mandatory preview for deletes
- [ ] Find orphans (report only, no copy)
- [ ] Expose via Pipeline presets and/or dedicated menu items
- [ ] Integration test: sync a pre-built fixture, verify correct files copied/deleted

### Step 8 — Selection Save/Load

- [ ] `SelectionSerializer`: write and read `.txt`, `.m3u`, `.sc2sel` — see §6.6
- [ ] `SelectionManager`: in-memory snapshot for rescan preservation — see §6.5
- [ ] Wire to File menu: Save Selection, Load Selection, Restore Selection
- [ ] Unit tests: round-trip all three formats; verify unmatched paths are skipped gracefully

### Step 9 — Settings Persistence

- [ ] `AppSettings` class with all properties
- [ ] Load on startup, save on clean exit
- [ ] Settings file: `%APPDATA%/SmartCopy2026/settings.json` on Windows,
  `~/.config/SmartCopy2026/settings.json` on Linux/macOS
- [ ] Window size/position, column widths, sort column/order, recent paths, scan options

### Step 10 — Filesystem Watcher

- [ ] `DirectoryWatcher` with 300ms debounce and event coalescing
- [ ] Incremental rescan of affected subtree with selection preservation — see §6.5
- [ ] Enable/disable via Settings (watcher disabled on MTP sources — not applicable)

---

## 9. Remaining Phases

### Phase 2 — Modern Features

- [ ] Filter chain save/load (`.sc2filter`) + preset library UI
- [ ] Pipeline save/load (`.sc2pipe`) + preset library UI
- [ ] MTP provider (`MtpFileSystemProvider`) — Windows only, via MediaDevices NuGet package
- [ ] Device detection: enumerate WPD devices, show in source/target dropdown
- [ ] Lazy-expand scan mode
- [ ] `DuplicateFilter` + `PathDepthFilter`
- [ ] Drag-and-drop for source/target paths
- [ ] Bookmarks/favourites for source/target paths

### Phase 3 — Advanced Pipeline Steps

- [ ] `RenameStep` with pattern tokens: `{name}`, `{ext}`, `{date}`, `{artist}`, `{album}`,
  `{track:00}`, `{title}` — token values from filename heuristics or embedded metadata
- [ ] `RebaseStep` (add/remove leading directory levels)
- [ ] `ConvertStep` + `IConversionPlugin` interface + `PluginLoader`
- [ ] Plugin settings UI per plugin (settings panel injected into `PipelineStepCard`)
- [ ] FFmpeg plugin (separate download): flac/wav/aiff → mp3/ogg/aac/opus
- [ ] Transcode-to-MTP pipeline (convert on the fly, stream to device)
- [ ] Conversion preview: estimated output size (based on bitrate × duration if available)

### Phase 4 — Polish and Extensibility

- [ ] Session files (`.sc2session`): source path, target path, filter chain, pipeline,
  selection snapshot — full round-trip restore
- [ ] Dark/light theme toggle (Avalonia theming)
- [ ] Localisation-ready string resources (i18n infrastructure; English-only initially)
- [ ] Automatic update check (GitHub Releases API)
- [ ] "Merge multiple sources to one target" — multiple source roots, single target tree
- [ ] Plugin SDK documentation for community-authored conversion plugins

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
public record FilterConfig(
    string FilterType,         // "Wildcard" | "Mirror" | "DateRange" | etc.
    bool IsEnabled,
    string Mode,               // "Include" | "Exclude"
    JsonObject Parameters      // type-specific; see individual filter for keys
);

public record FilterChainConfig(
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

### AppSettings (JSON — `settings.json`)

```csharp
public class AppSettings
{
    public string? LastSourcePath { get; set; }
    public string? LastTargetPath { get; set; }
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
    public WindowState WindowState { get; set; } = new();
    public ColumnSettings FileListColumns { get; set; } = new();
    public List<string> RecentSources { get; set; } = [];
    public List<string> RecentTargets { get; set; } = [];
    public List<string> FavouritePaths { get; set; } = [];
    public List<string> RecentFilterChains { get; set; } = [];
    public List<string> RecentPipelines { get; set; } = [];
}
```

---

## 11. Open Questions

1. **Packaging and distribution** — self-contained single-file publish works for all platforms.
   Consider: Windows `winget` manifest, macOS `.dmg` wrapping the binary, Linux `.AppImage`.
   No installer strictly required for v1.

2. **Plugin trust model** — options for third-party plugins:
   - Prompt user on first load ("This plugin is from an unknown source. Allow?")
   - Code-signing requirement (complex to enforce; blocks community plugins)
   - Hash-pinning in a manifest (requires a central registry)
   Recommend: prompt on first load for v1; revisit if a plugin ecosystem develops.

3. **Session files and selection snapshot size** — `.sc2session` includes a full selection
   snapshot. For very large trees this could be several MB. Accepted as a reasonable trade-off
   for reliable crash recovery. Compress with `GZipStream` if size becomes a practical concern.

4. **Trash on network drives** — some network paths don't support a trash/recycle bin. The
   `TrashService` should detect this and fall back to a confirmation dialog for permanent delete.
   Resolved: yes, detect and warn (see §6.11).
