# MTP provider + device picker (Windows-only)

**Goal:** Users can select a connected MTP device as source; SmartCopy scans and copies from it via the existing pipeline.

**Status:** On Hold — 2026-03-07
Work commenced on the mpt-support branch and good progress was made, but it proved more complicated than anticipated, so it has been deferred to a post-launch feature update.

**TODO** verify the belief that MediaDevices is a Windows-only module. Neither the NuGet page nor the Github page mention any platform restrictions, this assumption may have been out-dated.

https://www.nuget.org/packages/MediaDevices/#supportedframeworks-body-tab
https://github.com/Bassman2/MediaDevices

**Manual validation gate:** connect Android device, select via MTP picker, copy files to local destination — round-trip completes without error.

---

## Completed Work (mtp-support branch)

### NuGet dependency
`MediaDevices` v1.10.0 added to `SmartCopy.Core.csproj` (Windows WPD API wrapper, Windows TFM only).

### Build strategy (revised from plan)
Multi-target Core, UI, and App: `net10.0;net10.0-windows10.0.17763.0` so Linux/macOS builds remain possible (MTP button hidden via `OnPlatform`, `#if WINDOWS` code compiled out). Tests remain `net10.0` only.

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

## Remaining Work

### MTP folder picker / browse before scan (follow-up)

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

