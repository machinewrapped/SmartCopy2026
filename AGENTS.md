# AGENTS.md

This file provides guidance to AI agents when working with code in this repository.

## Project Overview

SmartCopy2026 is a cross-platform file manager (Windows/Linux/macOS) rewritten from SmartCopy 2015 (WinForms/.NET 4.8). It intelligently copies large directories via composable filters and transform pipelines. Stack: C#/.NET 10, Avalonia UI 11, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection.

## Commands

```bash
# Build
dotnet build SmartCopy.App/SmartCopy.App.csproj

# Run (watch mode for development)
dotnet watch run --project SmartCopy.App/SmartCopy.App.csproj

# Run tests
# IMPORTANT: Codex (only) is unable to run the tests and must ask the user to run them manually.
dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj

# Run a single test
dotnet test SmartCopy.Tests/SmartCopy.Tests.csproj --filter "FullyQualifiedName~TestClassName"

# Publish self-contained single file
dotnet publish SmartCopy.App/SmartCopy.App.csproj -c Release --self-contained true -p:PublishSingleFile=true
```

## Solution Structure

Four projects in `SmartCopy2026.slnx`:

- **SmartCopy.Core** — Pure business logic, no UI references. Houses `FileSystemNode`, `CheckState`, `FilterResult`, `IFileSystemProvider`, `IFilter`, `FilterChain`, and `TransformPipeline`.
- **SmartCopy.App** — Avalonia application host. `Program.cs` bootstraps the DI container and Avalonia builder. `App.axaml.cs` wires DI to ViewModels.
- **SmartCopy.UI** — MVVM layer. ViewModels inherit `CommunityToolkit.Mvvm.ObservableObject`. Views are `.axaml` files.
- **SmartCopy.Tests** — xUnit + NSubstitute. Uses `MemoryFileSystemProvider` for fast, hermetic tests.

## Architecture

### Core model: FileSystemNode

Every file and folder is a `FileSystemNode` with two independent pieces of mutable state:

- `CheckState` — `Checked | Unchecked | Indeterminate` (user selection)
- `FilterResult` — `Included | Excluded` (computed by FilterChain)

A node is "selected" only when **both** `CheckState == Checked` AND `FilterResult == Included`.

**Tri-state propagation** (already implemented):
- Downward: setting a parent's `CheckState` recursively sets all descendants. Uses `BatchUpdate` context to fire a single PropertyChanged reset instead of one per node.
- Upward: after any check change, walks the parent chain recalculating Checked/Unchecked/Indeterminate.

### MVVM hierarchy

```
MainViewModel
├── DirectoryTreeViewModel   — RootNodes: ObservableCollection<FileSystemNode>
├── FileListViewModel        — Files: ObservableCollection<FileSystemNode>
├── FilterChainViewModel     — Filters: ObservableCollection<FilterViewModel>
├── PipelineViewModel        — Steps: ObservableCollection<PipelineStepViewModel>
├── OperationProgressViewModel
└── PreviewViewModel
```

ViewModels use `[ObservableProperty]` source-gen attributes (no runtime reflection).

### UI Layout

`MainWindow.axaml` is a 5-row grid:
1. Menu bar
2. Source/Target path fields
3. Three-column area (FilterChain | splitter | DirectoryTree | splitter | FileList)
4. PipelineView (step cards with → connectors)
5. OperationProgressView (only visible when `IsActive = true`)

### Value converters (SmartCopy.UI/Converters/)

| Converter | Maps |
|-----------|------|
| `CheckStateConverter` | `CheckState` ↔ `bool?` (null = Indeterminate) |
| `FileSizeConverter` | `long` bytes → "2.3 GB" |
| `FilterResultColorConverter` | `FilterResult.Excluded` → SlateBlue brush |

### Key abstractions (see Docs/SmartCopy2026-Architecture.md)

- **IFileSystemProvider** — unified interface for local disk, MTP devices, and in-memory (tests). Capabilities are declared via `ProviderCapabilities` flags.
- **IFilter / FilterChain** — composable filters (Wildcard, Mirror, DateRange, etc.). Each filter returns `Included` or `Excluded` per node.
- **TransformPipeline** — ordered steps: *path steps* (Flatten, Rebase, Rename), *content steps* (Convert), *terminal steps* (Copy, Move, Delete). Generates an `OperationPlan` for preview before execution.
- **Progressive scanning** — top-level folders appear immediately; children stream in via background priority queue. Tri-state state is preserved across rescans.

### Design constraints

- Pipelines containing a `DeleteStep` **must** generate a preview before execution — no silent deletes.
- Trash by default; permanent delete is an explicit opt-in.
- Filesystem watcher uses a 300 ms debounce before triggering rescan.

## Documentation Signposting

- **Architecture, Contracts, UI Behavior, and Data Schemas:** `Docs/SmartCopy2026-Architecture.md`
- **Execution Plan, Delivery Scope, and Checklists:** `Docs/SmartCopy2026-Plan.md`

## Implementation Status

We are deep into Phase 1 (Core Workflows, Memory-Backed). Live local filesystem integration is deferred to Phase 2. Current progress follows `Docs/SmartCopy2026-Plan.md §8`:

- **Complete:** UI baseline shell, Core Models (`FileSystemNode`, `MemoryFileSystemProvider`), Node Selection Logic (tri-state propagation).
- **Mostly Complete:** Filter Chain (Live UX is wired, full UI implementation near complete).
- **In Progress:** Transform pipeline (built-in steps implemented, UI wiring pending), Sync operations, Selection save/load, Settings persistence.
- **Not Started:** Shell observability updates, Accessibility baseline, live filesystem integration (Phase 2).
