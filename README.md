# SmartCopy 2[026]
A tool for working with large directories that aims to combine the steerability and configurability of a GUI with the power and flexibility of command line tools.

This is an evolution of SmartCopyTool (https://sourceforge.net/projects/smartcopytool/), itself an evolution of a tool I wrote around 2 decades ago to provide an alternative to Windows Explorer for managing large directories. File management in Windows has improved a lot since those days, but it still lacks some features that made SmartCopyTool useful.

SmartCopy2026 is a complete rewrite, using modern .NET technologies and a more sophisticated UI framework. It is cross-platform and supports advanced workflows, which can be configured and saved as presets.

<img width="2850" height="1530" alt="SmartCopy 2026" src="https://github.com/user-attachments/assets/428f3724-62ed-41cd-8210-2429dab03356" />

The principles that drive the design of SmartCopy2026 are:
- Composable pipelines
- Selective operations
- User control and configuration
- Safety and preview

## Features

**Directory tree view**
- View the contents of a directory as a hierarchy, and select which files and folders to include in the pipeline
- Progressive scanning allows you to interact with the tree whilst deeper levels are still being scanned
- Filesystem watcher — detects exteral changes to the directory, on supported file systems
- Bookmark frequently used paths to quickly select them as the source or destination of an operation
- Save and restore selections in `.txt`, `.m3u` or `.sc2sel` format

**Filtering**
- Composable filter chain — combine filters to determine which files are included in the working set
- Filter types: file extension, wildcard pattern, date range, size range, file attributes
- Mirror filter — include or exclude files that exist in another location
- Save filter chains as named presets

**Transform pipeline**
- Build a sequence of actions to perform - copy, move, delete
- Selected, filtered file list is used as the source for the operation
- Apply transforms such as flattening the directory hierarchy or renaming files
- Preview before executing to see exactly what will happen to each folder or file
- Safe delete via trash/recycle bin (if supported on the file system)
- Save pipelines as presets, or complete workflows

## How it works

The UI is organized as a left-to-right data flow:

1. **Filters** (left panel) — Filters narrow the directory tree and file list, defining which files are selectable.
2. **Directory tree + file list** (centre) — browse the source directory and select files and folders to act on.
3. **Transform pipeline** (bottom) — define a sequence of actions to perform with the selected files.

## Platform support

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Local filesystem | Yes | Yes | Yes |
| MTP devices (phones, cameras) | Yes | — | — |
| Filesystem watcher | Yes | Yes | Yes |
| Trash / recycle bin | Yes | Yes | Yes |

## Building and running

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Build
dotnet build

# Run (watch mode for development)
dotnet watch run --project SmartCopy.App

# Run tests
dotnet test

# Publish self-contained single-file executable
dotnet publish SmartCopy.App/SmartCopy.App.csproj -p:PublishProfile=win-x64
```

## License
MIT
