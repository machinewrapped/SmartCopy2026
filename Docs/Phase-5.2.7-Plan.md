# Phase 5.2.7 — Capability Gates and Cross-Filesystem Hardening

**Prepared:** 2026-03-07
**Status:** In progress — A complete, B–E pending

## Execution rules
- One sub-step per session. Do not start the next sub-step until the current one is manually validated.
- After each sub-step passes automated tests AND manual smoke: commit with message `feat: 5.2.7-X — <title>`.
- Start each session by reading this file and the Architecture doc.

## Context

Phase 1 and 2.1–2.6 established core UX, real filesystem integration, and incremental rescanning.
Phase 5.2.7 adds adaptive behavior for heterogeneous filesystems: local (including network SMB paths),
MTP devices, and platform trash (Recycle Bin / freedesktop / NSFileManager). The goal is that
SmartCopy behaves correctly and safely regardless of where source/target files live, and surfaces
meaningful capability-derived warnings to the user before destructive operations.

Current gaps at start of phase:
- `DeleteStep.ApplyAsync` calls `provider.DeleteAsync()` for both Trash and Permanent modes (no routing)
- `ProviderCapabilities` lacks `CanTrash`
- No `TrashService` abstraction exists anywhere
- `MoveStep` uses `ReferenceEquals` for same-provider check; two local providers on the same drive
  are treated as cross-provider (no atomic move)
- `LocalFileSystemProvider` ignores whether root is a UNC path (no SMB capability adjustments)
- No MTP provider
- No capability-derived messaging in PreviewViewModel or DeleteStepEditor

---

## Sub-step A — TrashService abstraction + delete routing

**Status:** [x] Complete — 2026-03-07
**Validation:** 365 tests passing; manual smoke confirmed Trash → Recycle Bin and Permanent delete on local Windows SSD.

**Goal:** DeleteStep routes to platform trash when mode=Trash and service is available; falls back to
permanent delete when unavailable; journals actual outcome.

### Design decision
Trash is OS-level (Recycle Bin, freedesktop, NSFileManager), not provider-level. An MTP device
or memory provider cannot trash. Therefore `ITrashService` is a separate service, NOT a method
on `IFileSystemProvider`.

### Files to create
- `SmartCopy.Core/Trash/ITrashService.cs`
- `SmartCopy.Core/Trash/WindowsTrashService.cs` — `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory` with `SendToRecycleBin`; `IsAvailable = OperatingSystem.IsWindows()`
- `SmartCopy.Core/Trash/FreedesktopTrashService.cs` — freedesktop trash spec (`~/.local/share/Trash/`); `IsAvailable = OperatingSystem.IsLinux()`
- `SmartCopy.Core/Trash/MacOsTrashService.cs` — P/Invoke `NSFileManager.trashItem`; `IsAvailable = OperatingSystem.IsMacOS()`
- `SmartCopy.Core/Trash/NullTrashService.cs` — `IsAvailable = false`; used in tests

### Files to modify
- `SmartCopy.Core/FileSystem/ProviderCapabilities.cs` — add `bool CanTrash`
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — `CanTrash = true` for local paths, `false` for UNC (see Sub-step C)
- `SmartCopy.Core/FileSystem/MemoryFileSystemProvider.cs` — `CanTrash = false`
- `SmartCopy.Core/Pipeline/IStepContext.cs` — add `ITrashService TrashService { get; }`
- `SmartCopy.Core/Pipeline/PipelineRunner.cs` — inject + expose `ITrashService` in `StepContext`
- `SmartCopy.Core/Pipeline/PipelineJob.cs` — add `ITrashService TrashService { get; init; }`
- `SmartCopy.Core/Pipeline/Steps/DeleteStep.cs` — route ApplyAsync through TrashService when CanTrash=true; fallback to DeleteAsync; yield actual SourceResult
- `SmartCopy.App/AppServiceProvider.cs` — register platform-specific ITrashService
- `SmartCopy.UI/ViewModels/MainViewModel.cs` — pass ITrashService when constructing PipelineJob

### Tests
- `SmartCopy.Tests/Pipeline/DeleteStepTrashTests.cs`
  - Trash mode + CanTrash=true → TrashService called, result is Trashed
  - Trash mode + CanTrash=false → DeleteAsync called, result is Deleted (fallback)
  - Permanent mode → DeleteAsync called, TrashService NOT called
  - Read-only file + AllowDeleteReadOnly=false → Skipped
- Use existing `CapabilityOverrideProvider` to toggle CanTrash; mock `ITrashService` with NSubstitute

---

## Sub-step B — Atomic move volume rationalization

**Status:** [ ] Pending
**Manual validation gate:** move files from `C:\FolderA` to `C:\FolderB` using two separate providers — journal should show `move` (not copy+delete).

**Goal:** Two `LocalFileSystemProvider` instances on the same OS volume can perform atomic moves
between them (currently blocked by `ReferenceEquals` check in `MoveStep`).

### Root cause
`MoveStep.ApplyAsync` uses `ReferenceEquals(targetProvider, context.SourceProvider)`.
Two providers rooted at `C:\Source` and `C:\Destination` are different instances — always
copy+delete even though `File.Move()` would succeed atomically.

### Files to modify
- `SmartCopy.Core/FileSystem/IFileSystemProvider.cs` — add `string? VolumeId { get; }` (drive root for local, null for memory/MTP)
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — `VolumeId = Path.GetPathRoot(RootPath)?.ToUpperInvariant()` for local; `null` for UNC
- `SmartCopy.Core/FileSystem/MemoryFileSystemProvider.cs` — `VolumeId = null`
- `SmartCopy.Core/Pipeline/Steps/MoveStep.cs` — replace `ReferenceEquals` with VolumeId equality:
  ```csharp
  var sameVolume = context.SourceProvider.VolumeId is { } vid && targetProvider.VolumeId == vid;
  var canAtomicMove = sameVolume && targetProvider.Capabilities.CanAtomicMove;
  ```

### Tests
Extend `SmartCopy.Tests/Pipeline/MoveStepFallbackTests.cs`:
- Two local providers, same drive root → atomic move
- Two local providers, different drive roots → copy+delete fallback
- Memory provider → always copy+delete (VolumeId = null)

---

## Sub-step C — SMB / Network path capability detection

**Status:** [ ] Pending
**Manual validation gate:** point source at `\\server\share` — no watcher starts, Trash mode preview shows permanent delete warning.

**Goal:** `LocalFileSystemProvider` detects UNC roots and adjusts capabilities accordingly.
No separate SMB provider class needed — `System.IO` handles UNC paths natively.

### Files to modify
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs`
  - Detect UNC in constructor: `_isNetworkPath = RootPath.StartsWith(@"\\") || RootPath.StartsWith("//")`
  - `CanWatch = !_isNetworkPath` (FileSystemWatcher unreliable over SMB)
  - `CanTrash = !_isNetworkPath` (can't send network files to local Recycle Bin)
  - `CanAtomicMove = !_isNetworkPath`
  - `VolumeId = null` if network, else drive root

### Tests
Extend `SmartCopy.Tests/FileSystem/PathHandlingTests.cs`:
- UNC root → CanWatch/CanTrash/CanAtomicMove = false, VolumeId = null
- Local root → all true, VolumeId = drive root string

---

## Sub-step D — Capability-derived safety messaging in UI

**Status:** [ ] Pending
**Manual validation gate:** preview a Trash-mode delete with a UNC source — yellow warning banner appears in preview dialog.

**Goal:** Preview and step editors surface warnings when an operation will degrade due to provider
capabilities (e.g., trash falls back to permanent delete, cross-volume copy+delete).

### Messaging cases
| Scenario | Location | Message |
|---|---|---|
| Trash mode, CanTrash=false | PreviewViewModel header | "Trash not available for this path — files will be permanently deleted" |
| Cross-volume move | PreviewViewModel header | "Cross-volume move: files will be copied then deleted" |
| DeleteStep editor, Trash + network source | DeleteStepEditor | "Network paths cannot be sent to Trash — permanent delete will be used" |

### Files to modify
- `SmartCopy.Core/Pipeline/OperationPlan.cs` — add `IReadOnlyList<string> Warnings { get; init; }`
- `SmartCopy.Core/Pipeline/PipelineRunner.cs` — collect and populate `OperationPlan.Warnings` during PreviewAsync
- `SmartCopy.UI/ViewModels/PreviewViewModel.cs` — expose Warnings; show yellow banner
- `SmartCopy.UI/Views/PreviewView.axaml` — yellow warning banner, visible when Warnings.Count > 0
- `SmartCopy.UI/ViewModels/Pipeline/StepEditors/DeleteStepEditorViewModel.cs` — `string? CapabilityWarning` from source provider capabilities
- `SmartCopy.UI/Views/Pipeline/StepEditors/DeleteStepEditor.axaml` — warning TextBlock bound to CapabilityWarning

---

## Sub-step E — MTP provider + device picker (Windows-only)

**Status:** [ ] Pending
**Manual validation gate:** connect Android device, select via MTP picker, copy files to local destination — round-trip completes without error.

**Goal:** Users can select a connected MTP device as source; SmartCopy scans and copies from it via
the existing pipeline.

### NuGet dependency
Add `MediaDevices` to `SmartCopy.Core.csproj` (Windows WPD API wrapper).

### Files to create
- `SmartCopy.Core/FileSystem/MtpFileSystemProvider.cs`
  - `RootPath = "mtp://{device.FriendlyName}/"`
  - `VolumeId = null`
  - `Capabilities`: all false (CanSeek, CanAtomicMove, CanWatch, CanTrash = false)
  - `MoveAsync` throws `NotSupportedException` (MoveStep will copy+delete via fallback)
- `SmartCopy.UI/ViewModels/Dialogs/MtpDevicePickerViewModel.cs` — lists `MediaDevice.GetDevices()`
- `SmartCopy.UI/Views/Dialogs/MtpDevicePickerDialog.axaml` + `.axaml.cs` — ListBox of device names

### Files to modify
- `SmartCopy.Core/FileSystem/FileSystemProviderRegistry.cs` — add `RegisterMtpDevice(MediaDevice)`
- `SmartCopy.UI/ViewModels/MainViewModel.cs` — add `OpenMtpDevicePicker` command
- `SmartCopy.UI/Views/MainWindow.axaml` — "MTP Device..." source option, Windows-only

### Tests
- `SmartCopy.Tests/FileSystem/MtpProviderTests.cs` — mock `MediaDevice` via NSubstitute; test capabilities, MoveAsync throws, path operations

---

## Verification

### Automated
```bash
dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj
```

### Manual smoke
- **Trash (local):** delete pipeline with Trash mode → files in Recycle Bin
- **Trash fallback (network):** UNC source, Trash mode → preview warning + permanent delete
- **Same-volume atomic move:** `C:\Source` → `C:\Dest` → journal shows move (not copy+delete)
- **MTP copy:** Android/camera as source → copy to local destination
- **SMB scan + copy:** `\\server\share` → no watcher, trash warning for delete steps

### Plan document update
Update `Docs/SmartCopy2026-Plan.md` section 5.2.7 checkboxes after each sub-step is validated.
Update `Docs/Architecture.md` for `ITrashService`, `VolumeId`, MTP provider, and SMB capability detection.
