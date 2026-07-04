# SmartCopy2026 - Architecture Reference

This is a concise reference covering the conceptual architecture, core principles, and technical contracts of SmartCopy2026. For implementation details, search the codebase directly.

## 1. High-Level Structure

SmartCopy2026 follows a strict separation of concerns, heavily utilizing Dependency Injection.

*   **`SmartCopy.Core`**: The domain logic. It has NO dependencies on Avalonia or UI frameworks. Contains all filesystem abstractions, the pipeline runner, filters, and directory models.
*   **`SmartCopy.App`**: The Avalonia application host and Dependency Injection root.
*   **`SmartCopy.UI`**: Avalonia Views and ViewModels following the MVVM pattern. ViewModels do not execute side-effects directly; they communicate with Core services and use `DialogService`/`NavigationService` for interaction.

## 2. Core Concepts

### 2.1 File System Abstraction (`IFileSystemProvider`)
*   **Principle**: The pipeline and all algorithms are completely decoupled from physical file systems.
*   **Details**: Target file systems are abstracted behind `IFileSystemProvider`. Implementations exist for Local (NTFS/POSIX), Windows MTP (phones/cameras), and in-memory instances (for fast, isolated unit testing).
*   **Signpost**: Search for `IFileSystemProvider.cs` and `ProviderCapabilities` to understand constraints (e.g., missing atomic move on MTP, or unseekable streams).

### 2.2 Directory Tree and Selection State (`DirectoryTreeNode`)
*   **Principle**: A unified node model represents files and folders. Tree state is evaluated dynamically using User Intent and Filter Evaluation.
*   **Details**:
    *   `CheckState`: Represents explicit user interactions. Evaluates as Checked/Unchecked/Indeterminate. Downward propagation affects descendants; upward propagation calculates indeterminate states.
    *   `FilterResult`: The outcome of the filter chain. Evaluates as Included/Excluded/Mixed.
    *   A node is considered *selected for an operation* if and only if it is checked *and* included by the filter.
*   **Signpost**: Search for `DirectoryTreeNode.cs` and see tree traversal mechanics in logic services.

### 2.3 Filter Chain
*   **Principle**: Filters process nodes iteratively via set theory logic.
*   **Details**: The chain evaluates against a base state where everything is included. Filters evaluate sequentially as `Only` (intersection), `Exclude` (subtraction), or `Add` (union).
*   **Signpost**: Look at `IFilter.cs`, `FilterChain.cs`, and the specific filter implementations.

### 2.4 Transform Pipeline
*   **Principle**: File operations are orchestrated as a declarative pipeline of sequential steps applied to the selected directory tree.
*   **Details**:
    *   **Steps** execute filesystem operations (`CopyStep`, `MoveStep`, `DeleteStep`) or metadata mutations (`FlattenStep`).
    *   **Validation** is strict. Preconditions and postconditions prevent execution of invalid pipeline paths (e.g., executing a Move followed by an Overwrite of the unavailable source).
    *   **Previews** safely preview complex actions, automatic prior to executing destructive effects (Delete, Overwrite).

    * **Example pipelines:**
    | Use case | Steps |
    |---|---|
    | Simple copy | `[CopyStep]` |
    | Copy everything | `[SelectAllStep → CopyStep]` |
    | Flatten copy | `[FlattenStep → CopyStep]` |
    | Archive move | `[FlattenStep → MoveStep]` |
    | Copy then move | `[CopyStep("/mem/Backup") → MoveStep("/mem/Archive")]` |
    | Split Backup | `[CopyStep("destination1") → InvertSelectionStep → CopyStep("destination2")]` |

*   **Signpost**: Search `IPipelineStep.cs`, `PipelineRunner.cs`, and `PipelineValidator.cs`.

#### 2.4.1 Copy Strategy (policy + strategy)
*   **Principle**: The byte-transfer mechanics of a copy are owned by a single, reusable *strategy*, selected per source→destination pair by a *policy*. Steps delegate the transfer; they keep only their own orchestration (CopyStep's flat enumeration vs MoveStep's recursive atomic-subtree walk and source cleanup).
*   **Details**:
    *   `ICopyStrategyPolicy.Resolve(CopyStrategyInputs)` clamps `OperationalSettings` to provider capabilities, then — when `OperationalSettings.DestinationRoutingEnabled` is set — picks the copy buffer from the drive pair (source/target `DriveClassification`, same-volume, and optional source/target volume IDs). `DefaultCopyStrategyPolicy` applies the `OperationalSettings.CopyBufferRouting` profile (see `Docs/optimisation-strategies.md` §2 Current Policy): same-volume HDD 256 KiB, cross-volume HDD/Unknown 512 KiB, SSD/USB 1 MiB by default. The policy then selects the concrete strategy (`BatchedCopyStrategy` when `BatchBufferBytes > 0`, else `StreamingCopyStrategy`).
    *   `ICopyStrategy` exposes `CopySelectionAsync` (whole flat selection, batching-aware; caller supplies the success label so Copy vs Move stays caller-side) and `TransferFileAsync` (single-file bytes, throws on IO error). `MoveStep`'s non-atomic fallback reuses `TransferFileAsync` then deletes the source.
    *   `BatchedCopyStrategy` walks the selection **depth-first** — each directory's own files are smallest-first by default (`BatchOrderByFileSize`, for optimal buffer packing), then each child subtree completed in full before the next sibling. Disabling the ordering preserves the directory tree's natural file order for benchmark isolation. This deliberate traversal (not `GetSelectedDescendants()`'s incidental traversal) gives the resume property: accumulation and flush write in the same order, so an interrupted copy leaves a clean depth-first prefix on disk. A **512 KiB batch-eligibility ceiling** (`OperationalSettings.BatchEligibilityCeilingBytes`, capped to the buffer) keeps ≥2 files per flush — larger files bypass batching and stream individually. Batches are intentionally *not* confined to a directory: the depth-first write order already makes interruptions clean, so confinement would only cost packing efficiency (see `Docs/optimisation-strategies.md` Phase 3).
        *   **Traversal selection & pruning** (`EnumerateForBatching`): selection is enforced per node via `IsSelected` (`Checked && Included && !IsMarkedForRemoval`), so a partially-selected directory — `CheckState=Indeterminate` *or* `FilterResult=Mixed` — is still recursed into to reach its selected descendants; the directory is yielded as a no-op traversal marker only when itself selected. As an optimisation the walk prunes any child subtree that *cannot* contain a selected node: `CheckState == Unchecked`, `FilterResult == Excluded`, or `IsMarkedForRemoval`. Each holds for the **whole** subtree (`CheckState`/`FilterResult` are computed bottom-up by `RecalculateCheckState`/`RecalculateParentExclusion`; `MarkForRemoval` recurses). The polarity is load-bearing: prune on `== Unchecked`/`== Excluded`, **never** `!= Checked`/`!= Included` — an Indeterminate or Mixed directory still holds selected descendants, so pruning it would silently drop files.
        *   **Per-file timing is producer-attributed, not yield-inferred.** `PipelineRunner` stamps `TransformResult.ExecutionDuration` from yield cadence *only when the strategy leaves it null* — valid for the streaming path (one yield brackets one file's read+write) but wrong for batching, where reads yield nothing and the whole batch's read cost would land on the first file flushed. So `BatchedCopyStrategy` sets `ExecutionDuration` itself: it records each file's destination-check (`ExistsAsync`) + read time exactly, times the write flush as a unit, then adds an even share of that shared write phase to each file. The check is included so the per-file figure carries the same ceremony the streaming path's cadence already charges — otherwise bucket comparisons would bill the streaming control for a stat the batched variants hide. The write-phase split — not bytes-proportional — is deliberate: in the batch-eligible (small-file) regime per-file write cost is dominated by filesystem overhead, so a 1-byte and a 100-byte file cost essentially the same; apportioning by bytes would falsely report the larger as 100× slower. Per-file durations sum to the batch's elapsed time, so aggregates and runtime throughput stay conserved. This is what makes bucket-level throughput meaningful for batched variants (see `Docs/optimisation-strategies.md` §4.2.2).
    *   Steps obtain the strategy via the `IStepContext.ResolveCopyStrategyAsync(target, ct)` seam (a default interface method); `PipelineRunner.StepContext` overrides the policy from `PipelineJob.CopyStrategyPolicy`. This is the seam where future per-device learned profiles (a different `ICopyStrategyPolicy` using the forwarded volume IDs/classifications and falling back to `DefaultCopyStrategyPolicy`) and parallel copy (a new `ICopyStrategy`) plug in.
*   **Write durability is an intent, not a provider-internal heuristic.** Per file, the strategy decides `WriteDurability.Staged` (crash-safe) vs `Direct` (no staging) from the tiny-file threshold and the target's `AllowStagedWrite` capability, and carries it on `OperationalSettings` (two variants precomputed per strategy to avoid per-file allocation). The provider *executes* the intent with its own mechanism — `LocalFileSystemProvider` stages via temp file + atomic rename or writes direct; `MemoryFileSystemProvider`'s single buffered insert is atomic regardless; MTP reports `AllowStagedWrite: false` and is always asked for `Direct`. The provider-agnostic byte pump lives in `StreamCopyEngine`; the directory-creation cache is the reusable `FreshDirectoryTracker`.
*   **Signpost**: `SmartCopy.Core/Pipeline/Strategy/` (`ICopyStrategyPolicy`, `DefaultCopyStrategyPolicy`, `ICopyStrategy`, `StreamingCopyStrategy`, `BatchedCopyStrategy`); `SmartCopy.Core/FileSystem/` (`StreamCopyEngine`, `FreshDirectoryTracker`, `WriteDurability`).

### 2.5 Scanning & Watching
*   **Principle**: Progressively loaded, non-blocking asynchronous directories.
*   **Details**: `DirectoryScanner` streams folders directly in, presenting them immediately to maintain responsive UI while recursively background-scanning. 

`DirectoryWatcher` monitors the filesystem for changes and performs off-tree scans.
*   It queues ready-to-apply batches of changes.
*   `MainViewModel` decides when these batches are drained and applied to the directory tree.
*   Deletions are handled by marking nodes with `MarkForRemoval`.
*   Creations and changes arrive as pre-built nodes that can be patched into the tree.
*   Watcher behavior is best-effort; failures are logged, and the user can manually rescan if needed.
*   **Signpost**: Examine `DirectoryScanner.cs` and `DirectoryWatcher.cs`.

### 2.6 Workflows
*   **Principle**: Centralized orchestrator to simplify complex recurring user flows.
*   **Details**: `WorkflowConfig` instances encapsulate source paths, target destinations, active filter chains, and pipeline pipelines into unified, single-click presets.
*   **Signpost**: Find `WorkflowConfig.cs` and `WorkflowPresetStore.cs`.

### 2.7 Context & Data Storage (`IAppContext` and `IPathResolver`)
*   **Principle**: Application settings, data store paths, and file system provider resolution are isolated behind abstractions to enable zero-friction unit testing and dependency injection.
*   **Details**: `IAppContext` serves as the central bundle containing global `AppSettings`, an `IAppDataStore`, and implementing `IPathResolver`. 
    * The `IAppDataStore` virtualizes physical paths (like user directories or Temp folders) so that preset stores (like `FilterPresetStore`) can request directories by name (e.g., "FilterPresets" or "Pipelines") without hardcoding OS-level concepts. 
    * The `IPathResolver` translates string paths to `IFileSystemProvider` instances, supplying the needed context for pipelines and filters. UI ViewModels simply take an `IAppContext` to instantiate their requisite stores, or supply resolution logic to components. In test environments, tests configure and inject `TestAppContext` (which also implements `IPathResolver`) seamlessly simulating different file systems.
*   **Signpost**: Examine `IAppContext.cs`, `IPathResolver.cs`, `IAppDataStore.cs`, and `TestAppContext.cs` (for test-specific file system and path isolation).

## 3. Rules & Constraints

1.  **Cross-provider moves** (e.g., Local to MTP) cannot be atomic. Strategies must gracefully degrade to Copy-then-Delete.
2.  **UI Data Binding**: Never bind expensive domain operations or remote path validations to an editable ComboBox's `SelectedItem` property (e.g. 'Source Path'). This triggers unwanted immediate invocations on keystrokes. Defer action execution using distinct confirm transitions ('Enter' or closed dropdown).
3.  **Path Handling**: Paths are internally represented as `string[]` path segments relative to the provider root. Use provider-specific `IFileSystemProvider.JoinPath()` to convert to canonical paths when the filesystem is known.

## 4.Pitfalls

### Editable ComboBox + ObservableCollection Pitfall (Source Path Selector)

The PathPickerControl uses an **editable `ComboBox`** bound to `Bookmarks` (items) and `Path` (text). Two pitfalls must be avoided whenever this pattern is used:

**Pitfall 1 — Do not apply side effects from `SelectedItem` changes.**
An editable ComboBox fires `SelectedItem` changes on every arrow-key press (including when the dropdown is closed). If the ViewModel reacts to `SelectedItem` by triggering an expensive operation (e.g. directory scan), unintended states are committed and the UI becomes sluggish.

**Correct pattern:** `OnSelectedBookmarkChanged` should only populate the text field. The actual application must be deferred to an explicit commit action:
- **Keyboard:** Enter key fires `ApplyPathCommand` (wired via tunnel handler in code-behind)
- **Mouse:** A `SelectionChanged` handler sets `_applyOnDropDownClose = true` when the dropdown is open. `DropDownClosed` checks this flag and applies via `Dispatcher.UIThread.Post`.

**Pitfall 2 — `ObservableCollection.Clear()` wipes the ComboBox `Text` binding.**
The ComboBox nulls its `SelectedItem` (e.g. during `RefreshBookmarks`), which causes Avalonia to wipe the `Text` property. The two-way binding propagates that blank back to the ViewModel, erasing the user's input.

**Correct pattern:** Save contents before clearing and restore it after repopulation:
