# SmartCopy2026
A tool for selectively and intelligently working with large directories

This is an evolution of SmartCopyTool (https://sourceforge.net/projects/smartcopytool/), originally written in 2012 to provide an alternative to Windows Explorer for managing large directories. File management in Windows has improved a lot since Windows XP/Vista, but it still lacks some features that made SmartCopyTool useful.

SmartCopy2026 is a complete rewrite of the application using modern .NET technologies and a more sophisticated UI framework. It is cross-platform and supports advanced workflows, which can be configured and saved as presets.

The principles that drive the design of SmartCopy2026 are:
- Composable pipelines
- Selective operations
- User control and configuration
- Safety and preview
- Cross-platform

## Features

**Filtering**
- Composable filter chain — combine as many filters as you need, in any order
- Filter types: file extension, wildcard pattern, date range, size range, file attributes
- Mirror filter — skip files already present at a destination (by name, name+size, or extension-agnostic)
- Enable/disable individual filters without removing them
- Save filter chains as named presets and load them instantly

**Transform pipeline**
- Build a sequential pipeline of steps to execute against your selection
- Step types: Copy, Move, Delete, Flatten (remove directory nesting), Rebase (change root path), Rename
- Preview (dry-run) every pipeline before committing — especially enforced for delete operations
- Free-space validation before copy/move steps execute
- Overwrite policy per step: skip, always overwrite, or overwrite if newer
- Safe deletes via trash/recycle bin by default; permanent delete available with confirmation
- Save pipelines as named presets; save the entire workflow (source + filters + pipeline) as a `.sc2workflow`

**Directory tree**
- Tri-state checkboxes with correct parent/child propagation
- Progressive scanning — tree populates as directories are read, with pause/cancel
- Live filesystem watcher — incremental updates when files change externally, without losing your selection
- Drag-and-drop or browse to set the source path; recent paths and bookmarks remembered

**Selection management**
- Save and restore selections in `.txt`, `.m3u`, `.m3u8`, or `.sc2sel` formats
- Relative paths make selections portable across machines
- Bulk operations: Select All, Clear All, Invert

**Usability**
- Full keyboard navigation — every action reachable without a mouse
- Structured operation log with per-file outcomes
- Pause and resume long-running operations

## How it works

The UI is organized as a left-to-right data flow:

1. **Filters** (left panel) — define which files are eligible. Filters narrow the view in the directory tree and file list. Excluded files are dimmed or hidden.
2. **Directory tree + file list** (centre) — browse the source directory and check the files you want to act on. Tri-state checkboxes propagate up and down the tree automatically.
3. **Transform pipeline** (bottom) — define what to do with your selection. Add steps, configure each one, then click Preview to see exactly what will happen before you click Run.

## Platform support

| Feature | Windows | Linux | macOS |
|---|---|---|---|
| Local filesystem | Yes | Yes | Yes |
| MTP devices (phones, cameras) | Yes | — | — |
| Filesystem watcher | Yes | Yes | Yes |
| Trash / recycle bin | Yes | Yes | Yes |
| Single-file self-contained publish | Yes | Yes | Yes |

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
dotnet publish SmartCopy.App -c Release --self-contained true -p:PublishSingleFile=true
```

## License
MIT
