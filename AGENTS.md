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

## Implementation Status
Current progress and tasks to be completed are recorded in `Docs/SmartCopy2026-Plan.md`. 

It is focussed on planning and tracking deliverables. Refer to it when you need to get up to speed, update it when progress is made.

## Documentation Signposting

## System Architecture
Canonical architecture reference lives in: `Docs/SmartCopy2026-Architecture.md#1-architecture-design`

When architecture or contract details change, update the architecture reference, then update the plan.

Detailed technical contracts (providers, filters, pipeline, preview/progress, scanner/watcher, plugin interface) live in:
- `Docs/SmartCopy2026-Architecture.md#2-key-technical-designs`

Canonical algorithm/invariant reference (selection state, tri-state propagation, mirror matching, wildcard matching, safety defaults) lives in:
- `Docs/SmartCopy2026-Architecture.md#3-algorithms-and-implementation-notes`

This document should be updated when changes are made that affect design and implementation.

---

## UI Design

Canonical UI and interaction designs can be found in `Docs/SmartCopy2026-UIUX.md`. 

Refer to this document for UI consistency and keep it updated when manual testing leads to requests for UI/UX changes.


## Solution Structure

Four projects in `SmartCopy2026.slnx`:

- **SmartCopy.Core** — Pure business logic, no UI references. Houses `IFileSystemProvider`, `FileSystemNode`, `CheckState`, `FilterResult`, `IFilter`, `FilterChain`, and `TransformPipeline`.
- **SmartCopy.App** — Avalonia application host.
- **SmartCopy.UI** — MVVM layer. ViewModels inherit `CommunityToolkit.Mvvm.ObservableObject`. Views are `.axaml` files.
- **SmartCopy.Tests** — xUnit + NSubstitute. Uses `MemoryFileSystemProvider` for fast, hermetic tests.

## Architecture

### Core model: FileSystemNode

Every file and folder is a `FileSystemNode` with two independent pieces of mutable state:

- `CheckState` — `Checked | Unchecked | Indeterminate` (user selection)
- `FilterResult` — `Included | Excluded` (computed by FilterChain)

A node is "selected" only when **both** `CheckState == Checked` AND `FilterResult == Included`.

### MVVM hierarchy

```
MainViewModel
├── DirectoryTreeViewModel   — RootNodes: ObservableCollection<FileSystemNode>
├── FileListViewModel        — Files: ObservableCollection<FileSystemNode>
├── FilterChainViewModel     — Filters: ObservableCollection<FilterViewModel>
├── PipelineViewModel        — Steps: ObservableCollection<PipelineStepViewModel>
└── StatusBarViewModel
└──── SelectionViewModel
└──── OperationProgressViewModel
```

ViewModels use `[ObservableProperty]` source-gen attributes (no runtime reflection).

### UI Layout

`MainWindow.axaml` is a 5-row grid:
1. Menu bar
2. Source path field
3. Three-column area (FilterChain | splitter | DirectoryTree | splitter | FileList)
4. PipelineView (step cards with → connectors)
5. StatusBarViewModel (live updates)

### Key abstractions (see Docs/SmartCopy2026-Architecture.md)

- **IFileSystemProvider** — unified interface for local disk, MTP devices, and in-memory (tests). Capabilities are declared via `ProviderCapabilities` flags.
- **IFilter / FilterChain** — composable filters (Wildcard, Mirror, DateRange, etc.). Each filter returns `Included` or `Excluded` per node.
- **TransformPipeline** — ordered steps: *path steps* (Flatten, Rebase, Rename), *content steps* (Convert), *executable steps* (Copy, Move, Delete). Generates an `OperationPlan` for preview before execution.

## UI Guidelines & Anti-Patterns

### Avalonia DataBinding
- **Avoid `$parent` DataContext binding in DataTemplates or Flyouts/Menus**: Binding to `$parent[UserControl].((MyViewModel)DataContext).MyCommand` often fails or behaves unreliably, especially inside popups or menu items because they exist in a different visual tree root.
- **Instead, use Code-Behind Events**: For nested list items, menus, or flyouts where you need to access a parent ViewModel's command, use a `Click` (or similar) event handler in the `.axaml.cs` code-behind.
  - In the handler, set `e.Handled = true` to prevent unwanted event bubbling (e.g., stopping a menu item from triggering its own action when a nested button is clicked).
  - Extract the command parameter from the `sender` and execute the command manually by casting the View's `DataContext` to the appropriate ViewModel type.

