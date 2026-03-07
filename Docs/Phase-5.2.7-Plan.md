# Phase 5.2.7 — Capability Gates and Cross-Filesystem Hardening

**Prepared:** 2026-03-07
**Status:** A–E complete — E.1 (MTP folder picker) pending

## Execution rules
- One sub-step per session. Do not start the next sub-step until the current one is manually validated.
- After each sub-step passes automated tests AND manual smoke: commit with message `feat: 5.2.7-X — <title>`.
- Start each session by reading this file and the Architecture doc.

## Context

Phase 1 and 2.1–2.6 established core UX, real filesystem integration, and incremental scanning.
Phase 5.2.7 adds adaptive behavior for heterogeneous filesystems: local (including network SMB paths), MTP devices, and platform trash (Recycle Bin / freedesktop / NSFileManager). The goal is that SmartCopy behaves correctly and safely regardless of where source/target files live, and surfaces meaningful capability-derived warnings to the user before destructive operations.

Current gaps at start of phase:
- [Resolved] `DeleteStep.ApplyAsync` calls `provider.DeleteAsync()` for both Trash and Permanent modes (no routing)
- [Resolved] `ProviderCapabilities` lacks `CanTrash`
- [Resolved] No `TrashService` abstraction exists anywhere
- `MoveStep` uses `ReferenceEquals` for same-provider check; two local providers on the same drive are treated as cross-provider (no atomic move)
- `LocalFileSystemProvider` ignores whether root is a UNC path (no SMB capability adjustments)
- No MTP provider
- No capability-derived messaging in PreviewViewModel or DeleteStepEditor

---

## Sub-step A — TrashService abstraction + delete routing

**Status:** [x] Complete — 2026-03-07
**Validation:** 365 tests passing; manual smoke confirmed Trash → Recycle Bin and Permanent delete on local Windows SSD.

**Goal:** DeleteStep routes to platform trash when mode=Trash and service is available; falls back to permanent delete when unavailable; journals actual outcome.

### Design decision
Trash is OS-level (Recycle Bin, freedesktop, NSFileManager), not provider-level. An MTP device or memory provider cannot trash. Therefore `ITrashService` is a separate service, NOT a method on `IFileSystemProvider`.

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

**Status:** [x] Complete — 2026-03-07
**Validation:** 367 tests passing; manual smoke confirmed same-volume atomic move (`C:\FolderA` → `C:\FolderB` shows `move` in journal, not copy+delete).

**Goal:** Two `LocalFileSystemProvider` instances on the same OS volume can perform atomic moves between them (previously blocked by `ReferenceEquals` check in `MoveStep`).

### Changes made
- `SmartCopy.Core/FileSystem/IFileSystemProvider.cs` — added `string? VolumeId { get; }`
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — `VolumeId = Path.GetPathRoot(RootPath)?.ToUpperInvariant()`; `null` for UNC
- `SmartCopy.Core/FileSystem/MemoryFileSystemProvider.cs` — optional `volumeId` ctor param (default `null`)
- `SmartCopy.Core/Pipeline/Steps/MoveStep.cs` — replaced `ReferenceEquals` with VolumeId equality; removed `sameProvider` param from `WalkAndMoveAsync`; fixed copy+delete fallback stream-before-delete `IOException` (stream was still open when `DeleteAsync` ran)
- `SmartCopy.Tests/TestInfrastructure/CapabilityOverrideProvider.cs` — added `VolumeId` passthrough
- `SmartCopy.Tests/TestInfrastructure/MemoryFileSystemFixtureBuilder.cs` — threaded `volumeId` through builder and factory
- `SmartCopy.Tests/Pipeline/MoveStepFallbackTests.cs` — updated 4 existing tests; added `DifferentVolumeIds_UsesCopyDeleteFallback` and `NullVolumeId_AlwaysCopyDeleteFallback`
- `SmartCopy.Tests/Pipeline/PipelineDirectoryTests.cs` — updated 5 atomic-move tests to set `volumeId: "MEM"`

---

## Sub-step C — SMB / Network path capability detection

**Status:** [x] Complete — 2026-03-07
**Validation:** 369 tests passing; manual smoke confirmed UNC source (`\\laptop\share`) — no watcher started, non-atomic move (copy+delete fallback observed).

**Goal:** `LocalFileSystemProvider` detects UNC roots and adjusts capabilities accordingly.
No separate SMB provider class needed — `System.IO` handles UNC paths natively.

### Design decision
Provider type boundaries track *implementation* divergence, not conceptual ownership. UNC paths differ only in capabilities (not in `System.IO` API surface), so they remain within `LocalFileSystemProvider`. Future rename to `SystemIOFileSystemProvider` is noted as a cleanup item.

### Changes made
- `SmartCopy.Core/FileSystem/LocalFileSystemProvider.cs` — detect UNC via `Uri.TryCreate + uri.IsUnc` (public BCL API); cache `_capabilities` as `readonly` field (single allocation); `VolumeId` reuses `_isNetworkPath`. CanWatch/CanTrash/CanAtomicMove = `!_isNetworkPath`.
- `SmartCopy.Tests/FileSystem/PathHandlingTests.cs` — two new tests: local root has full capabilities; UNC root has degraded capabilities and null VolumeId.

### Bug fix (same session)
`MoveStep` copy+delete fallback left empty source directories behind. Fixed: after piecewise-moving a fully-selected subtree, the source directory is deleted bottom-up. Guard: `allMoved && !IsNodeFailed(child) && CanMoveEntireSubtree(child)`. Existing tests updated to assert source dirs are gone; partial-selection test asserts dir is preserved.

---

## Sub-step D — Capability-derived safety messaging in UI

**Status:** [x] Complete — 2026-03-07
**Validation:** 368 tests passing; manual smoke confirmed yellow banner in preview for UNC Trash-mode delete; "⚠ Trash unavailable" badge on step card; Run/Preview buttons disabled until mode changed to Permanent.

**Goal:** Preview and step editors surface warnings when an operation will degrade due to provider capabilities (e.g., trash falls back to permanent delete, cross-volume copy+delete).

### Changes made
- `SmartCopy.Core/Pipeline/OperationPlan.cs` — added `IReadOnlyList<string> Warnings { get; init; }`
- `SmartCopy.Core/Pipeline/PipelineRunner.cs` — collects warnings post-preview: Trash+!CanTrash → "Trash not available…"; cross-volume move → "Destination is on another drive…"
- `SmartCopy.UI/ViewModels/PreviewViewModel.cs` — `Warnings`/`HasWarnings` populated from plan in `LoadFrom`
- `SmartCopy.UI/Views/PreviewView.axaml` — yellow banner above delete confirmation banner; collapses when empty
- `SmartCopy.UI/ViewModels/Pipeline/DeleteStepEditorViewModel.cs` — `CapabilityWarning` computed from mode × CanTrash; `SetSourceCapabilities()` method; defaults to full capabilities
- `SmartCopy.UI/Views/Pipeline/StepEditors/DeleteStepEditor.axaml` — `CapabilityWarning` TextBlock with IsNotNullOrEmpty converter
- `SmartCopy.UI/ViewModels/PipelineStepViewModel.cs` — `TrashUnavailable` observable drives `ShowDeleteBadge`/`DeleteBadge` ("⚠ Trash unavailable")
- `SmartCopy.UI/ViewModels/PipelineViewModel.cs` — `_capabilityBlocked` field gates `CanRun`; `SourceCapabilities` property; `SetSourceCapabilities()`; capability check in `Revalidate()` sets `TrashUnavailable` + `BlockingValidationMessage`
- `SmartCopy.UI/ViewModels/MainViewModel.cs` — pushes `SourceProvider.Capabilities` to `Pipeline` after each successful source path change
- `SmartCopy.UI/ViewModels/Pipeline/EditStepDialogViewModel.cs` — `ForNew`/`ForEdit` accept optional `ProviderCapabilities?`; passed to `DeleteStepEditorViewModel` if applicable
- `SmartCopy.UI/Views/PipelineView.axaml.cs` — passes `_currentViewModel.SourceCapabilities` to dialog factories

---

## Sub-step E — MTP provider + device picker (Windows-only)

**Status:** [x] Complete — 2026-03-07
**Manual validation gate:** connect Android device, select via MTP picker, copy files to local destination — round-trip completes without error. Confirmed working; root-only selection identified as follow-up (see E.1 below).

**Goal:** Users can select a connected MTP device as source; SmartCopy scans and copies from it via
the existing pipeline.

### NuGet dependency
`MediaDevices` v1.10.0 added to `SmartCopy.Core.csproj` (Windows WPD API wrapper, Windows TFM only).
Note: plan originally cited v3.3.0 which does not exist on NuGet; v1.10.0 is the actual latest.

### Build strategy (revised from plan)
Multi-target Core, UI, and App: `net10.0;net10.0-windows10.0.17763.0`. App was originally changed to Windows-only but restored to multi-target so Linux/macOS builds remain possible (MTP button hidden via `OnPlatform`, `#if WINDOWS` code compiled out). Tests remain `net10.0` only.

### What was built
- `SmartCopy.Core/FileSystem/MtpFileSystemProvider.cs` — full `IFileSystemProvider` impl (`#if WINDOWS`); `RootPath = "mtp://{device.FriendlyName}/"` with `device.Model` fallback; correct 1.10.0 API: `EnumerateDirectories()`/`EnumerateFiles()`, `fileInfo.OpenRead()`, `UploadFile(Stream, path)`, `Length` cast from `ulong`
- `SmartCopy.UI/ViewModels/Dialogs/MtpDevicePickerViewModel.cs` — lists `MediaDevice.GetDevices()` (`#if WINDOWS`, excluded from net10.0 build)
- `SmartCopy.UI/Views/Dialogs/MtpDevicePickerDialog.axaml` + `.axaml.cs` — simple device list; no `x:DataType` (avoids Avalonia name-generator failure on net10.0 slice); code-behind always compiles, `#if WINDOWS` inside handler only
- `SmartCopy.Core/Settings/IAppContext` + `SmartCopyAppContext` — `Register(IFileSystemProvider)` method; `MainViewModel` now passes shared `_providerRegistry` to `_appContext` so both resolve the same set of providers
- `PathPickerViewModel.RegisterProvider` callback wired in `MainViewModel` (source), `CopyStepEditorViewModel`, and `MoveStepEditorViewModel` (destinations) — MTP available in every path picker
- `EditStepDialogViewModel.ForNew/ForEdit`, `StepEditorViewModelFactory.Create`, `CopyStepEditorViewModel`, `MoveStepEditorViewModel` — all changed from `AppSettings` to `IAppContext`
- 📱 button added to `PathPickerControl` (col 3, `IsVisible="{OnPlatform Windows=True, Default=False}"`)
- `PathHelper.NormalizeUserPath` — early-return for `://` scheme paths so `mtp://...` is never passed to `Path.GetFullPath` (which would resolve it against CWD)
- `.vscode/launch.json` — two configurations: Windows (net10.0-windows, MTP enabled) and Cross-platform (net10.0, no MTP)

### Tests
- 369 tests passing
- `PathHelperTests.NormalizeUserPath_UriSchemePath_ReturnedAsIs` added to cover the `://` fix
- No MTP unit tests (MediaDevice is not mockable without a real COM object); manual validation is the gate

---

## Sub-step E.1 — MTP folder picker / browse before scan (follow-up)

**Status:** [ ] Pending
**Priority:** High — without this, selecting a device root on a phone with a 1 TB MicroSD card triggers a full recursive scan that could take extremely long.

**Goal:** After selecting a device in `MtpDevicePickerDialog`, the user can navigate the device tree and select a sub-folder as the scan root, rather than being forced to use the device root.

### Proposed design
Extend `MtpDevicePickerDialog` with a two-panel layout:
1. **Device list** (left / top) — existing `ListBox` of `DeviceNames`
2. **Folder browser** (right / bottom) — a `TreeView` or drill-down `ListBox` that populates lazily from `MediaDevice.GetDirectoryInfo(path).EnumerateDirectories()` as the user navigates

The selected path is the confirmed output (not necessarily the root). `MtpDevicePickerViewModel` would expose:
- `ObservableCollection<MtpFolderNode> RootFolders` — populated when a device is selected
- `MtpFolderNode? SelectedFolder` — tracks current selection
- `string SelectedPath` — `SelectedFolder?.FullName ?? "/"` — what gets committed to the path picker

`MtpFolderNode` wraps a `MediaDirectoryInfo` with lazy `Children` loading (expand-on-demand).

### Notes
- Keep folder enumeration off the UI thread (`Task.Run` + `Dispatcher.UIThread.Post`)
- Show a loading indicator while children are being fetched
- "Select this folder" button confirms; double-click navigates into; breadcrumb or back button for navigation

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
