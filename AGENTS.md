# AGENTS.md

This file provides guidance to AI agents when working with code in this repository.

## Project Overview

SmartCopy2026 is a cross-platform file manager (Windows/Linux/macOS) rewritten from SmartCopy 2015 (WinForms/.NET 4.8). It intelligently copies large directories via composable filters and transform pipelines. Stack: C#/.NET 10, Avalonia UI 11, CommunityToolkit.Mvvm.

## Project Status
Current progress and outstanding tasks are recorded in `Docs/SmartCopy2026-Plan.md`. Refer to the plan document to get up to speed, update it when a deliverable is completed and validated.

## System Architecture
Architectural overview and design principles are detailed in `Docs/Architecture.md`. Consult the architecture when designing a solution to ensure that it follows established principles, and update the document after refactoring or redesign changes the architecture.

## UI/UX Design Documentation

Canonical UI and interaction designs can be found in `Docs/UIUX.md`. Refer to this document for UI consistency, update it when UI/UX decisions are made.

## Commands

```bash
# Build
dotnet build | Out-String

# Run (watch mode for development)
dotnet watch run

# Run tests
# IMPORTANT: Codex (only) is unable to run the tests and must ask the user to run them manually.
dotnet test | Out-String

# Run a single test
dotnet test --filter "FullyQualifiedName~TestClassName" | Out-String

# Publish self-contained single file
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true | Out-String
```

## Solution Structure

Four projects in `SmartCopy2026.slnx`:

- **SmartCopy.Core** — Pure business logic, no UI references.
- **SmartCopy.App** — Avalonia application host.
- **SmartCopy.UI** — MVVM layer.
- **SmartCopy.Tests** — xUnit + NSubstitute. Uses `MemoryFileSystemProvider` for fast, hermetic tests.

## Key abstractions

- **IFileSystemProvider** — unified interface for local and network drives, MTP devices and in-memory tests. `ProviderCapabilities` define the capabilities of each.
- **FilterChain** — composable `IFilter` chain to filter the filesystem view
- **TransformPipeline** — ordered sequence of `IPipelineStep` to apply to selected filesystem nodes.

## C# Coding Conventions

### Implicit Usings
All projects have `<ImplicitUsings>enable</ImplicitUsings>` in their `.csproj` files. The SDK automatically includes common namespaces (`System`, `System.IO`, `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`, etc.). Do **not** add explicit `using` directives for these.

## UI Guidelines & Anti-Patterns

### Avalonia DataBinding
- **Avoid `$parent` DataContext binding in DataTemplates or Flyouts/Menus**: Binding to `$parent[UserControl].((MyViewModel)DataContext).MyCommand` often fails or behaves unreliably, especially inside popups or menu items because they exist in a different visual tree root.
- **Instead, use Code-Behind Events**: For nested list items, menus, or flyouts where you need to access a parent ViewModel's command, use a `Click` (or similar) event handler in the `.axaml.cs` code-behind.
  - In the handler, set `e.Handled = true` to prevent unwanted event bubbling (e.g., stopping a menu item from triggering its own action when a nested button is clicked).
  - Extract the command parameter from the `sender` and execute the command manually by casting the View's `DataContext` to the appropriate ViewModel type.

### Keyboard Handling — Tunnel vs. Bubble
- **`KeyDown="handler"` in AXAML registers a bubbling handler.** Controls like `TreeView` and `ListBox` consume certain keys (e.g. arrow keys) during the *tunnel* phase, marking them handled before the event bubbles. A bubbling handler will never fire for those keys.
- **Fix:** In the code-behind constructor, use `AddHandler` with `RoutingStrategies.Tunnel` instead:
  ```csharp
  DirectoryTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
  ```
  This fires before the control's built-in handling. Mark `e.Handled = true` to suppress the default behaviour where needed. See `MainWindow.axaml.cs` (window-level shortcuts) and `DirectoryTreeView.axaml.cs` (`Alt+Arrow` recursive expand/collapse) for reference.

