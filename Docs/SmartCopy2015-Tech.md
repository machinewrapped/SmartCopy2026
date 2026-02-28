# SmartCopy2015 — Technical Description: FileMover and FileDeleter Workers

---

## 1. Architectural Context

SmartCopy2015 is a Windows desktop utility for copying, moving, and deleting selected files between a source and target folder tree. The UI shows a `TreeView` (`directoryTree`) where each `TreeNode` has a `FolderData` object in its `.Tag` property. Files within each folder are represented as `FileData` objects. The user checks/unchecks nodes and files; checked-and-not-filtered-and-not-removed files are the "selected" set.

Long-running operations are encapsulated in a `Worker` subclass. The main form calls `PerformBackgroundOperation(worker)` (runs on a background thread with status-bar feedback).

---

## 2. The `Worker` Base Class

All workers share this abstract base. Key members:

### State machine
```
enum State { IDLE, ACTIVE, COMPLETED, ABORTED, ERROR }
```
`DoWork()` returns one of these states. `COMPLETED` is the normal return. `ABORTED` is returned when `CancellationPending` is true and the worker honours it.

### Pause/Resume
```
enum PauseState { RUNNING, PAUSE_REQUESTED, PAUSED, RESUME_REQUESTED }
```
- `RequestPause()` / `RequestResume()` are called from the UI thread.
- The worker checks `PauseRequested` in its inner loop and calls `DoPause()` which spins until `RESUME_REQUESTED` or cancellation.

### Progress reporting
`ReportProgress(double percent, bool forceReport = false)` is throttled: it only sends a `bgw.ReportProgress` call if at least 200ms have elapsed since the last report, or `forceReport` is true, or `percent >= 95`. Progress is passed as a `Worker.Report` snapshot object (not just an integer).

### `Report` snapshot (`Worker.Report`)
Created on the worker thread, consumed on the UI thread via `bgw.ReportProgress`. Contains:
- `percent`, `state`, `timeTaken`
- `status` / `substatus` strings (current operation text)
- `warnings[]` — accumulated warning strings, cleared after each snapshot
- `removedNodes[]` — `TreeNode`s to remove from the tree (UI thread does the actual remove)
- `selectedNodes[]` / `deselectedNodes[]` — nodes to check/uncheck

---

## 3. Supporting Types

### `FolderData`
Wraps a `DirectoryInfo`. Holds:
- A lazy-loaded `List<FileData>` (`GetFiles()`). If `ContainsDeletedFiles` is set, the next `GetFiles()` call rebuilds the list excluding deleted entries.
- A cached `List<FileData>` of selected (checked, not filtered, not removed, not deleted) files (`GetSelectedFiles(options)`). Cache is invalidated when options change or flags change.
- Four boolean flags via a `[Flags]` enum: `ContainsCheckedFiles`, `ContainsUncheckedFiles`, `ContainsRemovedFiles`, `ContainsDeletedFiles`.
- Static `FileIsMirroredAt(file, destination, options)` — checks whether a file exists at the destination, optionally ignoring extension (`ignoreExtension`) or size (`ignoreSize`).

### `FileData`
Wraps a `FileInfo`. Key flags (bit field):
- `Checked` — user has selected this file
- `Filtered` — excluded by options (hidden, extension filter)
- `Removed` — excluded by a mirror-remove operation (not physically deleted)
- `Deleted` — has been physically moved or deleted by a worker

`Selected` is the computed conjunction: `Checked & !Filtered & !Removed & !Deleted`.

Setting `Deleted = true` sets the parent `FolderData.ContainsDeletedFiles = true`, which causes the next `GetFiles()` call to rebuild the list.

### `Options` (`MainForm.cs` line 2073)
Plain data object passed into every worker constructor. Relevant fields:
| Field | Type | Meaning |
|---|---|---|
| `sourcePath` | `string` | Root of the source tree |
| `targetPath` | `string` | Root of the destination tree |
| `allowOverwrite` | `bool` | Overwrite existing files at destination |
| `allowDeleteReadOnly` | `bool` | Clear read-only attribute before delete/overwrite |
| `ignoreSize` | `bool` | Skip size check when comparing mirrored files |
| `ignoreExtension` | `bool` | Match by stem only when checking mirrors |

---

## 4. `FileMover` Worker

### Purpose
Moves user-selected files and folders from `options.sourcePath` to `options.targetPath`, preserving the relative directory structure.

### Construction
```csharp
new FileMover(TreeView tree, Options options)
```
- `canCancel = true`, `canPause = true`, `showStatusMessage = true`, `operationName = "Moving Files"`

### `DoWork()`
1. Counts selected nodes (`numNodesToMove`) and total bytes (`bytesToMove`) via helpers.
2. If `numNodesToMove == 0`, returns `COMPLETED` immediately.
3. Calls `RecursiveMoveFolder(tree.Nodes[0], src, dst)`.

### `RecursiveMoveFolder(node, src, dst)`
This is the core recursive method. For each node it does:

**Step 1 — Pause/cancel check** at the top.

**Step 2 — Attempt whole-subtree move** via `CanMoveEntireNode`:
- If the node passes the check AND the target directory does not yet exist, it tries `Directory.Move(folder.FullName, targetName)`.
- `Directory.Move` is a rename at the OS level and is instantaneous on the same volume. If it succeeds: updates `numMoved`, `bytesMoved`, queues the node for removal via `FlagForRemoval`, reports progress, returns `COMPLETED`.
- If `Directory.Move` throws (most likely because src and dst are on different volumes), execution falls through to the file-by-file path.

**Step 3 — Recurse into children** (if whole-subtree move failed). Iterates child nodes using a cached `next` pointer to avoid invalidation when nodes are removed. Propagates `ABORTED` or error states upward immediately.

**Step 4 — Move individual files** from `folder.GetSelectedFiles(options)`:
- For each `FileData`:
  - Sets status to the file's full path.
  - Reports byte-based progress (with `forceReport = true` for files > 50 MB).
  - Pause/cancel check per file.
  - Computes `targetFilename` via `RerootPath`.
  - Self-move guard: warns and skips if source == destination.
  - If `allowOverwrite` and the target exists: logs warning, clears read-only if needed, deletes the target.
  - If target does not exist: calls `File.Move(source, target)`, sets `file.Deleted = true`.
  - Exceptions are caught per-file; `file.Notes` is set to the exception message, a warning is logged, but the loop continues.

### `CanMoveEntireNode(node, target)`
Returns `true` only if **all** of the following hold:
1. `node.Checked == true` (the folder node itself is checked)
2. `folder.HasUnselectedFiles == false` (the entire folder is selected)
3. The target path does not start with the source folder path (prevents moving a folder into itself)
4. All child nodes also pass `CanMoveEntireNode` recursively (every subdirectory in the tree is also fully selected)

The intent: only use `Directory.Move` when every single file and subfolder in the subtree is being moved — otherwise the source directory must be left intact for the unselected files.

### Progress calculation
Progress is byte-based: `(bytesMoved / bytesToMove) * 100`. `bytesMoved` is incremented before `File.Move` is called (optimistic accounting). For whole-subtree moves, `CountSelectedBytes` is used to advance `bytesMoved` in bulk.

---

## 5. `FileDeleter` Worker

### Purpose
Deletes all user-selected files and folders from `options.sourcePath`.

### Construction
```csharp
new FileDeleter(TreeView tree, Options options)
```
- `canCancel = true`, `canPause = true`
- `operationName = "Deleting Files"`

### `DoWork()`
1. Counts selected nodes (`numNodesToDelete`).
2. If zero, returns `COMPLETED`.
3. Calls `DeleteSelectedFolders(tree.Nodes[0])`.

### `DeleteSelectedFolders(node)`
Recursive method. Processing order within each node is **children first, then parent's files, then parent directory**:

**Step 1 — Pause/cancel check.**

**Step 2 — Recurse into children.** The child node list is copied into an array before iteration (to avoid mutation during traversal). Each child is processed via `DeleteSelectedFolders`.

**Step 3 — Delete individual files** (if `folder.ContainsCheckedFiles`):
- Calls `folder.GetSelectedFiles(options)` to get the filtered, checked file list.
- For each `FileData`:
  - Sets status to `"Deleting {fullPath}"`.
  - Reports progress (folder-count-based with per-file granularity within each folder).
  - Pause/cancel check.
  - If `allowDeleteReadOnly` and `file.ReadOnly`: clears the read-only attribute.
  - Calls `File.Delete(file.FullName)`, sets `file.Deleted = true`.
  - Exceptions are caught; `file.Notes = "Could not delete"`, warning is logged, loop continues.

**Step 4 — Delete the directory** (if `node.Checked`):
- Attempts `Directory.Delete(folder.FullName)` — non-recursive. This will only succeed if the directory is physically empty (all non-selected files must have already been absent or the directory was fully selected). On success, `FlagForRemoval(node)`. On exception, warning is logged.

### Progress calculation
Progress is folder-count-based with per-file subdivision:
```
progress = (numDeleted * 100) / numNodesToDelete
         + (fileIndex * 100) / (numSelected * numNodesToDelete)
```
---
