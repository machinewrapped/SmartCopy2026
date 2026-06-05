# AGENTS.md
This file provides guidance to AI agents when working with code in this repository.

## Project Overview
SmartCopy2026 is a cross-platform file manager (Windows/Linux/macOS) rewritten from SmartCopy 2015 (WinForms/.NET 4.8). It intelligently copies large directories via composable filters and transform pipelines. Stack: C#/.NET 10, Avalonia UI 11, CommunityToolkit.Mvvm.

## Project Status
Implementation of `Docs/SmartCopy2026-Plan.md` is complete. The app is launched and live on multiple sites.

## System Architecture
Architectural overview and design principles are detailed in `Docs/Architecture.md`. When adding new features, new abstractions, or making structural changes, consult `Docs/Architecture.md` to ensure alignment with established principles. Update the document after refactoring or redesign changes the architecture.

If existing code contradicts `Docs/Architecture.md`, treat the document as the source of truth and flag the discrepancy in a comment before making changes.

## UI/UX Design Documentation
Canonical UI and interaction designs can be found in `Docs/UIUX.md`. Refer to this document for UI consistency, update it when UI/UX decisions are made. If a mismatch is found between the documentation and the code, surface the discrepancy and ask the user to decide which should be updated.

## Commands

```bash
# Build
dotnet build | Out-String

# Run (watch mode for development)
dotnet watch run

# Run tests
# IMPORTANT: Codex can run the tests, but in this environment it must do so outside the sandbox.
dotnet test | Out-String

# Run a single test
dotnet test --filter "FullyQualifiedName~TestClassName" | Out-String

# Publish self-contained single file
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true | Out-String

# Run benchmark suite (results written to .benchmarks/)
# CRITICAL RULE: NEVER launch benchmarks without EXPLICIT user permission.
# Benchmarks require a stable system state (no background tasks, controlled temperature).
# Always prepare the configuration and ask the user for permission before running this command.
dotnet run --project .\SmartCopy.Benchmarks

# Analyze benchmark results without re-running copies
dotnet run --project .\SmartCopy.Benchmarks --mode analyze
```

## Error Handling

**Thou Shalt Not Crash** The app must never crash to desktop. All operations (pipeline execution, preview generation, scanning) must catch exceptions at the top level and surface errors through the UI (log panel, banners, validation messages). Never let an exception propagate unhandled.

## Solution Structure

Five projects in `SmartCopy2026.slnx`:

- **SmartCopy.Core** — Pure business logic, no UI references.
- **SmartCopy.App** — Avalonia application host.
- **SmartCopy.UI** — MVVM layer.
- **SmartCopy.Tests** — xUnit + NSubstitute. Uses `MemoryFileSystemProvider` for fast, hermetic tests.
- **SmartCopy.Benchmarks** — Standalone console app for performance testing against real dataset fixtures. Results are written to `.benchmarks/`. Run with `--mode analyze` to process existing results without re-running copies.

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
- **Instead, use Code-Behind Events**: For nested list items, menus, or flyouts where you need to access a parent ViewModel's command, use a `Click` (or similar) event handler in the `.axaml.cs` code-behind:
  1. Cast `((Control)sender).DataContext` (or the view's `DataContext`) to the appropriate ViewModel type.
  2. Extract the command parameter from `sender`.
  3. Invoke the command.
  4. Set `e.Handled = true` last to suppress unwanted event bubbling (e.g., stopping a menu item from triggering its own action when a nested button is clicked).

### Keyboard Handling — Tunnel vs. Bubble
- **`KeyDown="handler"` in AXAML registers a bubbling handler.** Controls like `TreeView` and `ListBox` consume certain keys (e.g. arrow keys) during the *tunnel* phase, marking them handled before the event bubbles. A bubbling handler will never fire for those keys.
- **Fix:** In the code-behind constructor, use `AddHandler` with `RoutingStrategies.Tunnel` instead:
  ```csharp
  DirectoryTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
  ```
  This fires before the control's built-in handling. Mark `e.Handled = true` to suppress the default behaviour where needed. See `MainWindow.axaml.cs` (window-level shortcuts) and `DirectoryTreeView.axaml.cs` (`Alt+Arrow` recursive expand/collapse) for reference.

## Cross-Platform Conventions
The app targets Windows, Linux, and macOS. Always follow these rules:
- **Path operations must go through `IFileSystemProvider`**: Use `GetRelativePath`, `SplitPath`, `JoinPath`, and `GetFileName` rather than `System.IO.Path` helpers or hardcoded separators (`\` or `/`). 
  - `System.IO.Path` is only appropriate when working outside the provider abstraction (e.g. config files or `SmartCopy.App` bootstrap, or within `LocalFilesystemProvider` itself).
- Never use case-insensitive string comparisons for file paths; Linux file systems are case-sensitive.

## Dependency Management
Do not add new NuGet dependencies without explicit user approval. If a task requires a new package, propose it and wait for confirmation before editing any `.csproj` file.

## Task Completion Requirements

**Always Verify Compilation:** "The code compiles" is a strict pre-requisite for declaring any coding task done. Before presenting a solution or asking for user review on code changes, you must run `dotnet build` and ensure there are 0 errors. Never declare a task complete if the code is in a broken state.
