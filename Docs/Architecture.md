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
    * MTP sources opt out of atomic directory deletion: deletes are executed file-first then folder-last, so a slow WPD recursive-directory request cannot leave the UI without progress. Final folder cleanup is non-recursive, so new device content cannot be removed after selection. The provider verifies the first successful delete in each pipeline execution; a small delete-device adapter lets this behaviour be unit-tested without connected hardware.
    * The UI schedules pipeline execution off Avalonia's dispatcher. Results are queued and drained on the UI thread, keeping the window responsive during provider I/O and large tree traversal.
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
*   **Principle**: Byte-transfer mechanics belong to a reusable *strategy*, selected per source→destination pair by a *policy*. Pipeline steps retain their operation-specific orchestration—for example, Copy's selection traversal and Move's source cleanup.
*   **Policy**: `ICopyStrategyPolicy` resolves `OperationalSettings` from the source and target classifications, volume relationship, and provider capabilities. The default policy applies the configured routing profile, disables unsupported optimisations, and chooses either a streaming or batched strategy. This is the extension point for alternative routing policies, such as future per-device learned profiles.
*   **Strategy contract**: `ICopyStrategy` copies a selection or transfers an individual file. It reports a result for each processed node, honours overwrite behaviour, and leaves operation-specific outcomes (such as Copy versus Move) to its caller. `MoveStep` can reuse the single-file transfer contract when it must fall back from an atomic move.
*   **Provider boundary**: A step opens an explicit, provider-owned `IBulkWriteSession` and passes it to the strategy. The session owns operation-local destination checks and writes, avoiding ambient provider state and allowing providers to implement their own bulk-write optimisations.
*   **Durability boundary**: The strategy requests staged or direct writes according to resolved settings and target capabilities; the provider implements that request using its own storage mechanism. Provider capabilities therefore remain the authority on what is safe and supported (for example, a target that cannot stage writes receives direct-write requests).
*   **Resolution seam**: Steps obtain the strategy through `IStepContext.ResolveCopyStrategyAsync(target, ct)`, with `PipelineRunner` supplying the job's policy. New transfer implementations, including a parallel-copy strategy, integrate at this seam.
*   **Implementation and tuning**: Buffer sizes, batching eligibility and flush behaviour, traversal optimisations, and performance measurement are implementation details. See `Docs/archive/optimisation/optimisation-strategies.md` and the strategy source for the current implementation.
*   **Signpost**: `SmartCopy.Core/Pipeline/Strategy/` (`ICopyStrategyPolicy`, `DefaultCopyStrategyPolicy`, `ICopyStrategy`, `StreamingCopyStrategy`, `BatchedCopyStrategy`); `SmartCopy.Core/FileSystem/` (`IBulkWriteSession`, `WriteDurability`).

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
