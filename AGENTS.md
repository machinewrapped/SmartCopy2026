# AGENTS.md

This file provides guidance to AI agents when working with code in this repository.

## Project Overview

SmartCopy2026 is a cross-platform file manager (Windows/Linux/macOS) rewritten from SmartCopy 2015 (WinForms/.NET 4.8). It intelligently copies large directories via composable filters and transform pipelines. Stack: C#/.NET 10, Avalonia UI 11, CommunityToolkit.Mvvm.

## Project Status
Current progress and outstanding tasks are recorded in `Docs/SmartCopy2026-Plan.md`. 

Refer to this document when you need to get up to speed and update it when a deliverable is completed and validated.

## System Architecture
Architectural overview and design principles: `Docs/Architecture.md` This document should be reviewed when designing a solution and updated after changes affect the application's architecture.

## UI/UX Design Documentation

Canonical UI and interaction designs can be found in `Docs/UI+UX.md`. Refer to this document for UI consistency, update it after UI/UX decisions are made.

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

- **IFileSystemProvider** — unified interface for local disk, MTP devices, and in-memory (tests). Capabilities are declared via `ProviderCapabilities` flags.
- **FilterChain** — composable `IFilter` chain for filesystem view (Wildcard, Mirror, DateRange, etc.)
- **TransformPipeline** — ordered sequence of `IPipelineStep` to apply to the selected filesystem nodes.

## C# Coding Conventions

### Implicit Usings
All projects have `<ImplicitUsings>enable</ImplicitUsings>` in their `.csproj` files. The SDK automatically includes common namespaces (`System`, `System.IO`, `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`, etc.). Do **not** add explicit `using` directives for these.

## UI Guidelines & Anti-Patterns

### Avalonia DataBinding
- **Avoid `$parent` DataContext binding in DataTemplates or Flyouts/Menus**: Binding to `$parent[UserControl].((MyViewModel)DataContext).MyCommand` often fails or behaves unreliably, especially inside popups or menu items because they exist in a different visual tree root.
- **Instead, use Code-Behind Events**: For nested list items, menus, or flyouts where you need to access a parent ViewModel's command, use a `Click` (or similar) event handler in the `.axaml.cs` code-behind.
  - In the handler, set `e.Handled = true` to prevent unwanted event bubbling (e.g., stopping a menu item from triggering its own action when a nested button is clicked).
  - Extract the command parameter from the `sender` and execute the command manually by casting the View's `DataContext` to the appropriate ViewModel type.

