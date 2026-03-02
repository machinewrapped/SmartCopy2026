# SmartCopy2026 - UI/UX Design Reference

**Prepared:** 2026-02-24

## Table of Contents

1. [UI Improvements Over Predecessor](#4-ui-improvements-over-predecessor)
2. [Main Window Layout](#2-main-window-layout)
3. [Filter UX Flow](#3-filter-ux-flow)
4. [Pipeline UX Flow](#4-pipeline-ux-flow)
5. [Keyboard Navigation](#5-keyboard-navigation)

---

## 1. UI Improvements Over Predecessor

1. **Source and destination fields accept drag-and-drop** from Explorer/Finder
2. **Tri-state tree checkboxes** — `▣` for mixed selection
3. **Filter chain is visual** — each filter is a card; drag to reorder; toggle without removing
4. **Filter cards are human-friendly** — each card shows a readable summary above a technical subtitle; enable/disable via checkbox; edit via pencil icon
5. **Pipeline is visual** — steps shown as an arrow chain; cards contain human-readable summary + technical subtitle
6. **Three-column layout** — Filters | Folders | Files in a single resizable row; draggable splitters; layout is persisted across sessions
7. **Preview** — see what will happen before running
8. **Device picker** — MTP devices appear in folder browser dialog, when supported
9. **Keyboard-first** — every action reachable via keyboard; focus indicators on all controls

## 2. Main Window Layout

`MainWindow.axaml` is a 6 row grid:
1. Menu bar
2. Source path field
3. Three-column area (FilterChain | DirectoryTree | FileList)
4. Pipeline edit/view/execution
5. Collapsible log panel
6. Status bar

The main content area uses a **3-column layout** — Filters | Folders | Files — all at the same height and separated by draggable `GridSplitter`s. This places the filter controls in visual proximity to the tree and file list they affect, making the data-flow readable left-to-right.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ File  Options  Selection  Workflows Help                                     │
├──────────────────────────────────────────────────────────────────────────────┤
│ Source [/mem/Music                                       ▾] [★] [📁 Browse]  │
├─────────────────┬─┬──────────────────┬─┬─────────────────────────────────────┤
│ FILTERS     [👁] │║│ ﹀ ▣ Music        │║│ ☐ Name            Size   Modified │
│                 │║│   ﹀ ☐ Alternative│║│                                    │
│ ☑ Only Audio... │║│     ☐ Algiers    │║│                                    │
│   Ext: *.mp3... │║│   ﹀ ▣ Curve      │║│                                    │
│                 │║│     ☑ 1991 Pubic…│║│                                    │
│  + Add filter   │║│     ☑ 1992 Doppe…│║│                                    │
│ ─────────────── │║│                  │║│                                    │
│ [Save ▾][Load ▾]│║│                  │║│                                    │
├─────────────────┴─┴──────────────────┴─┴─────────────────────────────────────┤
│ PIPELINE                                                   [▶ Run    ]       │
│                                                            [👁 Preview]       │
│ → Copy to Test ✎ ✕  → + Add step                                             │
│   Destination: /m…                                                           │
├─────────────────┴─┴──────────────────┴─┴─────────────────────────────────────┤
│ LOG PANEL                                                   [Clear] [Save]   │
├──────────────────────────────────────────────────────────────────────────────┤
│ 142 files selected · 2.3 GB · 17 filtered out          12/142 ████░ 34% 0:34 │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Status bar anatomy** (`StatusBarView`):

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ [SelectionView: always visible]          [OperationProgressView: when active] │
│ 142 files selected · 2.3 GB · 17 filt…  12/142 ████░░ 34%  0:34  [‖][✕]    │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Filter card anatomy**:
```text
┌──────────────────────────────────────────────────┐
│ ☑  Only Audio files                      ≡  ✎  ✕ │
│    Extension: *.mp3; *.flac; *.aac; *.ogg...     │
└──────────────────────────────────────────────────┘
```
- **Checkbox** (left) — enable/disable the filter in-place
- **Summary** (bold) — human-readable one-liner generated from filter config
- **Description** (dimmed subtitle) — technical details for power users
- **Drag handle** `≡`, **edit pencil** `✎`, **remove** `✕` (right-aligned)

---

## 3. Filter UX Flow

### Add Filter — Two-Level Drill-Down

Clicking "+ Add filter" opens a flyout anchored below the button (light-dismiss on click-outside).

**Level 1 — Filter type selection:**
```text
┌────────────────────────────────┐
│  Add Filter                  ✕ │
├────────────────────────────────┤
│  Extension    Filter by ext.   │
│  Wildcard     Name pattern     │
│  Date Range   Created/Modified │
│  Size Range   Min/Max bytes    │
│  Mirror       Skip on target   │
│  Attribute    Hidden/ReadOnly  │
└────────────────────────────────┘
```

**Level 2 — Preset picker for chosen type:**
```text
┌────────────────────────────────┐
│  ← Extension                   │
├────────────────────────────────┤
│  ＋ Configure...               │
├────────────────────────────────┤
│  Recently used                 │
│    My music (*.mp3;*.flac)     │
├────────────────────────────────┤
│    Audio files                 │
│    Images                      │
│    Documents                   │
│    Log files                   │
│    My custom preset            │
└────────────────────────────────┘
```

- **"＋ Configure..."** — closes flyout, opens `EditFilterDialog` with empty form for selected type
- **Preset row** — closes flyout, adds card immediately, triggers tree/file list update
- **"←"** — returns to Level 1; built-in (read-only); user-saved presets

### EditFilterDialog

A **modal `Window`** opened via "＋ Configure..." or the edit pencil on any card, shows type-specific editor view.

```text
┌─────────────────────────────────────────┐
│  Edit Filter                            │
├─────────────────────────────────────────┤
│  [ONLY ●] [ADD ○] [EXCLUDE ○]           │  ← mode radio group
├─────────────────────────────────────────┤
│  Name: [Only .mp3 and .flac       ]     │  ← auto-generated, user-overridable
├─────────────────────────────────────────┤
│  ┌── type-specific form ──────────────┐ │
│  │ Extension: [.mp3 ×][.flac ×] +Add  │ │  chips + input
│  │ Wildcard:  [*.tmp;*.bak          ]│ │  single text box
│  │ Date Range: ○Created ●Modified    │ │  radio + two CalendarDatePickers
│  │ Size Range: Min[1.5] Max[──] [MB▾] │ │  shared unit selector
│  │ Mirror:     [/mem/Mirror     ][…] │ │  browse button
│  │             ○Name  ●Name+Size     │ │
│  │ Attribute:  ☐Hidden ☐RO ☐System  │ │
│  └────────────────────────────────── ┘ │
├─────────────────────────────────────────┤
│  ☐ Save as preset                       │
├─────────────────────────────────────────┤
│            [Cancel]      [OK ✓]         │  ← OK disabled when !IsValid
└─────────────────────────────────────────┘
```

### Filter Results in Tree / File List

- **`FilterResult.Excluded` + `ShowFilteredFiles=true` (Eye is open)**: files remain visible; tree rows are dimmed and excluded file names are styled
- **`FilterResult.Excluded` + `ShowFilteredFiles=false`**: excluded nodes are removed from directory tree and file list.
- Filter changes propagate within ~100 ms via a debounced `CancellationTokenSource`
- Drag handle `≡` reorders the filter chain; order affects chain evaluation sequence

---

## 4. Pipeline UX Flow

### Pipeline Step Card Anatomy

Each step in the pipeline strip is a card. Cards follow the same summary/subtitle pattern as filter cards, with an additional (optional) warning when the step is invalid or destructive.

```text
┌──────────────────────────────────────────────────┐
│ → Copy to Test                          ✎  ✕    │
│   Destination: /mem/Test                        │
└──────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────┐
│ 🗑 Delete permanently                   ✎  ✕    │
│   This cannot be undone.                        │
│   ⚠ Permanent delete                           │
└──────────────────────────────────────────────────┘
```

- **Icon + summary** (top-left) — icon plus human-readable summary; auto-generated from step parameters unless user overrides it in `EditStepDialog`
- **Technical subtitle** — compact detail line (for example destination path, pattern, strategy)
- **Edit pencil** `✎` — opens `EditStepDialog`
- **Remove** `✕` — removes the step from the pipeline

`DeleteStep` cards show a warning badge when `DeleteMode.Permanent` is active.

### Add Step — Integrated Dropdown

Clicking `+ Add step` opens a flyout anchored above the button (light-dismiss on click-outside). 

**Level 1 — Main Category Selection:**
```text
┌────────────────────────────────┐
│  Add Step                    ✕ │
├────────────────────────────────┤
│  Selection steps             ▸ │
│  Path steps                  ▸ │
│  Content steps               ▸ │
├────────────────────────────────┤
│  Delete                        │
│  Delete source file            │
├────────────────────────────────┤
│  Move                          │
│  Move to destination           │
├────────────────────────────────┤
│  Copy                          │
│  Copy to destination           │
└────────────────────────────────┘
```

**Level 2 — Step type selection** (example: Path steps):
```text
┌────────────────────────────────┐
│  ← Path steps                  │
├────────────────────────────────┤
│  Flatten   Remove folders      │
│  Rebase    Change root path    │
│  Rename    Change filename     │
└────────────────────────────────┘
```

**Level 3 — Preset picker for chosen type** (example: Delete):
```text
┌────────────────────────────────┐
│  ← Delete                      │
├────────────────────────────────┤
│  ＋ Configure...               │
├────────────────────────────────┤
│    Delete to Trash             │
│    Delete permanently          │
│    My custom delete            │
└────────────────────────────────┘
```

- **"＋ Configure..."** opens `EditStepDialog`
- **Preset** — closes flyout and adds step immediately from preset config.

Clicking a category (Level 1) navigates to Level 2.
Clicking a step type from Level 2 that has presets navigates to Level 3.
Clicking a top-level executable step navigates to Level 3.
Clicking a step type with no presets opens `EditStepDialog` directly.

`Run` stays disabled until the pipeline contains at least one executable step and all steps are valid.

When validation fails, the failing step is highlighted with an explanation, e.g. `Copy` after `Delete` shows "Source no longer exists".

### EditStepDialog

Dispatches to a type-specific editor via `ContentControl` + `DataTemplate`.

```text
┌─────────────────────────────────────────┐
│  Edit Step: Copy To                     │
├─────────────────────────────────────────┤
│  Name: [Copy to Mirror              ]   │
│       (auto-generated, overridable)     │
├─────────────────────────────────────────┤
│  ┌── type-specific form ──────────────┐ │
│  │                                    │ │
│  │  [Copy To / Move To]               │ │
│  │  Destination:                      │ │
│  │  [/mnt/phone/Music          ][…]   │ │
│  │                                    │ │
│  │  Overwrite:                        │ │
│  │  ○ Skip  ● If Newer  ○ Always      │ │
│  │                                    │ │
│  │  [Flatten]                         │ │
│  │  Conflict strategy:                │ │
│  │  ● Auto-rename  (song (2).mp3)     │ │
│  │  ○ Prefix source path              │ │
│  │  ○ Skip conflicting files          │ │
│  │  ○ Overwrite silently              │ │
│  │                                    │ │
│  │  [Rename]                          │ │
│  │  Pattern: [{name}              ]   │ │
│  │  Tokens:  {name} {ext} {date}      │ │
│  │           {artist} {album}         │ │
│  │           {track:00} {title}       │ │
│  │  Preview: [artist - title.mp3   ]  │ │
│  │                                    │ │
│  │  [Delete]                          │ │
│  │  ● Send to Recycle Bin (safe)      │ │
│  │  ○ Delete permanently  ⚠          │ │
│  │                                    │ │
│  │  [Rebase]                          │ │
│  │  Strip prefix: [Music/          ]  │ │
│  │  Add prefix:   [Archive/        ]  │ │
│  │                                    │ │
│  │  [Convert]                         │ │
│  │  Output format: [mp3 ▾]            │ │
│  │  Quality:       [320k ▾]           │ │
│  │                                    │ │
│  └────────────────────────────────── ┘ │
├─────────────────────────────────────────┤
│  ☐ Save as preset                       │
├─────────────────────────────────────────┤
│            [Cancel]      [OK ✓]         │
└─────────────────────────────────────────┘
```

OK is disabled when the configuration is invalid.

Name is auto-generated from step parameters unless overridden by the user.

### Confirm before delete

When the pipeline contains a `DeleteStep`, the `[▶ Run]` button is replaced by `[⚠ Run]` and executes `PreviewAsync` first, requiring confirmation to proceed. For `DeleteMode.Permanent`, the warning intensifies:

```text
┌──────────────────────────────────────────────────────────┐
│  ⚠ Permanent delete — files CANNOT be recovered.        │
├──────────────────────────────────────────────────────────┤
│  Will delete 47 files · 1.2 GB                           │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Abbey Road/01 Come Together.flac               45 MB │ │
│ │ Abbey Road/02 Something.flac                   38 MB │ │
│ │ ...                                                  │ │
│ └──────────────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────────────┤
│                              [Cancel] [🗑 Run (47 files)] │
└──────────────────────────────────────────────────────────┘
```

## 5. Keyboard Navigation

All critical actions must be keyboard-accessible from the initial shell:
- **Tab** cycles between the source field, tree, file list, filter chain, pipeline steps
  and action buttons
- **Arrow keys** navigate the tree and file list
- **Space** toggles checkbox on the focused tree node or file list row
- **Enter** expands/collapses a tree node
- **Ctrl+A** selects all visible nodes in the current panel
- **Delete** or **Ctrl+D** removes a selected filter card or pipeline step
- **Ctrl+Shift+P** opens the pipeline preset menu (via Add Step)
- **F5** triggers a rescan
- **Escape** cancels a running operation (with confirmation) or closes a modal

Screen-reader labels and focus states are part of the UI shell, not a polish item. Avalonia supports `AutomationProperties.Name` — use it from the start.

---
