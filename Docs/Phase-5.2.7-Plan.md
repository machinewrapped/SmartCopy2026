# Phase 5.2.7 — Capability Gates and Cross-Filesystem Hardening

**Prepared:** 2026-03-07
**Status:** A–D complete

## Execution rules
- One sub-step per session. Do not start the next sub-step until the current one is manually validated.
- After each sub-step passes automated tests AND manual smoke: commit with message `feat: 5.2.7-X — <title>`.
- Start each session by reading this file and the Architecture doc.

## Context

Phase 1 and 2.1–2.6 established core UX, real filesystem integration, and incremental scanning.
Phase 5.2.7 adds adaptive behavior for heterogeneous filesystems: local (including network SMB paths), and platform trash (Recycle Bin / freedesktop / NSFileManager). The goal is that SmartCopy behaves correctly and safely regardless of where source/target files live, and surfaces meaningful capability-derived warnings to the user before destructive operations.

Gaps at start of phase:
- [Resolved] `DeleteStep.ApplyAsync` calls `provider.DeleteAsync()` for both Trash and Permanent modes (no routing)
- [Resolved] `ProviderCapabilities` lacks `CanTrash`
- [Resolved] No `TrashService` abstraction exists anywhere
- [Resolved] `MoveStep` uses `ReferenceEquals` for same-provider check; two local providers on the same drive are treated as cross-provider (no atomic move)
- [Resolved] `LocalFileSystemProvider` ignores whether root is a UNC path (no SMB capability adjustments)
- [Resolved] No capability-derived messaging in PreviewViewModel or DeleteStepEditor

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

## Verification

### Automated
```bash
dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj
```

### Manual smoke
- **Trash (local):** delete pipeline with Trash mode → files in Recycle Bin
- **Trash fallback (network):** UNC source, Trash mode → preview warning + permanent delete
- **Same-volume atomic move:** `C:\Source` → `C:\Dest` → journal shows move (not copy+delete)
- **SMB scan + copy:** `\\server\share` → no watcher, trash warning for delete steps

### Plan document update
Update `Docs/SmartCopy2026-Plan.md` section 5.2.7 checkboxes after each sub-step is validated.
Update `Docs/Architecture.md` for `ITrashService`, `VolumeId`, and SMB capability detection.
