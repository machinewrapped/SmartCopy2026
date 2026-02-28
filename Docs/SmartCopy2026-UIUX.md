# SmartCopy2026 - UI/UX Design Reference

**Prepared:** 2026-02-24
**Source:** Extracted from `Docs/SmartCopy2026-Architecture.md` to keep UI/UX separated from technical architecture.

## Table of Contents

1. [UI Improvements Over Predecessor](#4-ui-improvements-over-predecessor)
2. [Main Window Layout](#2-main-window-layout)
3. [Filter UX Flow](#3-filter-ux-flow)
4. [Pipeline UX Flow](#4-pipeline-ux-flow)
5. [Keyboard Navigation](#5-keyboard-navigation)
6. [UI Shell Scope](#6-ui-shell-scope)

---

## 1. UI Improvements Over Predecessor

1. **Source and destination fields accept drag-and-drop** from Explorer/Finder
2. **Proper tri-state tree checkboxes** — `▣` for indeterminate
3. **Filter chain is visual** — each filter is a card; drag to reorder; toggle without removing
4. **Pipeline is visual** — steps shown as an arrow chain; presets as buttons
   and cards use human-readable summary + technical subtitle formatting
5. **Three-column layout** — Filters | Folders | Files in a single resizable row; all three columns
   have draggable splitters; column widths are persisted across sessions
6. **Filter cards are human-friendly** — each card shows a readable summary ("Only .mp3 and .flac
   files") above a dimmed technical subtitle; enable/disable via checkbox; edit via pencil icon
7. **Preview** — shows exactly what will happen before running
8. **Device picker** — MTP devices appear in the destination path picker on Copy/Move pipeline
   steps alongside local paths (the 📁 Browse button becomes a "Local folder... / Phone (MTP)..."
   split flyout when MTP devices are available)
9. **Keyboard-first** — every action reachable via keyboard; focus indicators on all controls

## 2. Main Window Layout

The main content area uses a **3-column layout** — Filters | Folders | Files — all at the same height and separated by draggable `GridSplitter`s. This places the filter controls in direct visual proximity to the tree and file list they affect, making the data-flow left-to-right and immediately legible to new users. The design now features a top menu and specific interactive elements reflecting the latest implementation.

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
├──────────────────────────────────────────────────────────────────────────────┤
│ 142 files selected · 2.3 GB · 17 filtered out          12/142 ████░ 34% 0:34 │
└──────────────────────────────────────────────────────────────────────────────┘
```

`║` = draggable GridSplitter between columns

**Status bar anatomy** (`StatusBarView`):

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ [SelectionView: always visible]          [OperationProgressView: when active] │
│ 142 files selected · 2.3 GB · 17 filt…  12/142 ████░░ 34%  0:34  [‖][✕]    │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **`SelectionView`** — always visible; updates live as the user checks nodes or changes filters. Shows `N files selected · size · M filtered out`. When nothing is selected: `No files selected`.
- **`OperationProgressView`** — docked to the right; only visible while an operation is running (`IsActive = true`). Shows `files completed / total`, a progress bar, ETA, Pause, and Cancel.
- Both are hosted by `StatusBarView`, which is bound to `MainViewModel.StatusBar` (`StatusBarViewModel`).

**Filter card anatomy** (each filter in the Filters column):
```text
┌──────────────────────────────────────────────────┐
│ ☑  Only Audio files                      ≡  ✎  ✕ │
│    Extension: *.mp3; *.flac; *.aac; *.ogg...     │
└──────────────────────────────────────────────────┘
```
- **Checkbox** (left) — enable/disable the filter in-place
- **Summary** (bold) — human-readable one-liner generated from filter config
- **Description** (dimmed subtitle) — raw technical spec for power users
- **Drag handle** `≡`, **edit pencil** `✎`, **remove** `✕` (right-aligned)
- The mode selector (`ONLY` / `ADD` / `EXCLUDE`) and detailed config live in the edit dialog, not the card face
- **Eye Toggle** (top right of FILTERS pane) to toggle visibility of excluded items.

---

## 3. Filter UX Flow

### Add Filter — Two-Level Drill-Down

Clicking "+ Add filter" opens a **`Popup`** anchored below the button (light-dismiss on click-outside). The flyout uses two panels swapped by a boolean flag — no secondary window.

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
- "Recently used" shows the last 5 presets of this type from `AppSettings.FilterTypeMruPresetIds`

### EditFilterDialog

A **modal `Window`** (`ShowDialog<bool?>`) opened via "＋ Configure..." or the edit pencil on any card.
The dialog dispatches to a type-specific editor view via `ContentControl` + `DataTemplate`.

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

"Save as preset" sets a flag on the dialog VM; after a successful dialog close, `FilterChainView.axaml.cs` persists the preset via `FilterPresetStore`.

### Filter Results in Tree / File List

- **`FilterResult.Excluded` + `ShowFilteredFiles=true` (Eye is open)**: files remain visible; tree rows are dimmed (`Opacity = 0.4`) and excluded file names are styled (`SlateBlue`)
- **`FilterResult.Excluded` + `ShowFilteredFiles=false`**: excluded files are removed from `VisibleFiles`; tree remains navigable with excluded directories still shown dimmed
- All filter changes propagate within ~100 ms via a debounced `CancellationTokenSource`
- Drag handle `≡` on filter cards reorders the chain (Avalonia `DragDrop`); order affects chain evaluation sequence

---

## 4. Pipeline UX Flow

### Pipeline Step Card Anatomy

Each step in the pipeline strip is a card connected by `→` arrows. Cards follow the same summary/subtitle pattern as filter cards:

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
- **Edit pencil** `✎` — opens `EditStepDialog` for full configuration (overwrite mode, conflict strategy, rename pattern, etc.)
- **Remove** `✕` — removes the step; pipeline revalidates after each removal

`DeleteStep` cards are intentionally quiet for `Trash` mode (single-line summary, no extra badge). When `DeleteMode.Permanent` is active, the card shows an amber warning badge.

### Add Step — Integrated Dropdown

Clicking `+ Add step` opens a **`Popup`** anchored above the button (light-dismiss on click-outside). 
This dropdown integrates loading and saving pipelines along with pipeline operations.

**Level 1 — Main Category Selection:**
```text
┌────────────────────────────────┐
│  Add Step                    ✕ │
├────────────────────────────────┤
│  Load preset                   │
│  Save current pipeline...      │
├────────────────────────────────┤
│  Content steps               ▸ │
│  Path steps                  ▸ │
│  Selection steps             ▸ │
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
│  Recently used                 │
│    Delete to Trash             │
├────────────────────────────────┤
│    Delete to Trash             │
│    Delete permanently          │
│    My custom delete            │
└────────────────────────────────┘
```

- `←` on Level 2 returns to Level 1; `←` on Level 3 returns to Level 1 (for top-level executable steps) or Level 2 (for categorized steps).
- If no presets exist for a step type (e.g. Copy, Move, Rename), Level 3 is bypassed and `EditStepDialog` opens directly — same bypass pattern as the filter flyout.
- **"＋ Configure..."** on Level 3 opens `EditStepDialog` with an empty form for the selected type.
- **Preset row** — closes flyout, adds step immediately from preset config.
- built-in (read-only); user-saved presets.
- "Recently used" shows the last 5 presets of this type from `AppSettings.StepTypeMruPresetIds`.

Clicking a category (Level 1) navigates to Level 2.
Clicking a top-level executable step or a step type from Level 2 that has presets navigates to Level 3.
Clicking a step type with no presets opens `EditStepDialog` directly.
This includes optional custom step naming in the same interaction.

**Execution eligibility rule:** A pipeline may contain zero or more executable steps.
`Run` stays disabled until the pipeline contains at least one executable step and all required fields for configured steps are valid. Adding Copy/Move does not replace existing executable steps. `DeleteStep` remains preview-mandatory and must be the final step when present.

When validation fails, the first blocking issue is shown as helper text under the pipeline strip, and the affected step card is highlighted with inline error text + tooltip (for keyboard and mouse users). Example: adding `Copy` after `Delete` marks the `Copy` step invalid with a message such as "Source no longer exists at this point in the pipeline."

### EditStepDialog

A **modal `Window`** (`ShowDialog<bool?>`) opened by `✎` on any step card, or automatically when adding a step type. Dispatches to a type-specific editor via `ContentControl` + `DataTemplate`.

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

"Save as preset" sets a flag on the dialog VM; after a successful dialog close, `PipelineView.axaml.cs` persists the preset via `StepPresetStore`. The step name is used as the preset name.

OK is disabled when:
- **Copy To / Move To**: destination path is empty
- **Rename**: pattern string is empty
- **Rebase**: neither strip-prefix nor add-prefix has a value

If the entered step name matches the auto-generated name, no custom-name override is persisted.

### Load Preset / Save Pipeline Integration

The `Load preset ▸` and `Save current pipeline...` options are integrated into the main `+ Add step` menu. To prevent destructive mistakes, `Load preset ▸` is only visible when the pipeline is empty, while `Save current pipeline...` is only visible when there are steps present.

Expanding `Load preset ▸` reveals user-saved `.sc2pipe` files from `%APPDATA%/SmartCopy2026/pipelines/`. Users can delete custom pipelines directly from this menu by clicking the inline Delete (🗑) icon next to the name.

**Save current pipeline...** — transforms the bottom of the flyout into an inline input form prompting for a name. If left blank, it falls back to a timestamped name.

Loading a preset replaces the entire current pipeline. A tooltip-notification at the bottom of the pipeline strip confirms the name of the loaded preset.

### Preview Dialog

`PreviewPipeline` opens the **Preview Dialog** (a modal `Window`, not a side panel), populated from `OperationPlan` produced by `PipelineRunner.PreviewAsync()`.

```text
┌──────────────────────────────────────────────────────────┐
│  Preview — 142 files · 2.3 GB                            │
├──────────────────────────────────────────────────────────┤
│  ✓ 138 ready    ⚠ 3 destination exists    ✕ 1 conflict  │
├──────────────────────────────────────────────────────────┤
│  ⚠ Destination Exists (3)                               │
│  ┌────────────────────────────┬─────────────┬─────────┐  │
│  │ Source                     │ Destination │    Size │  │
│  ├────────────────────────────┼─────────────┼─────────┤  │
│  │ Abbey Road/Come Together   │ same·newer  │  45 MB  │  │
│  │ Abbey Road/Something       │ same·same   │  38 MB  │  │
│  │ Jazz/Kind of Blue          │ same·older  │  92 MB  │  │
│  └────────────────────────────┴─────────────┴─────────┘  │
│                                                          │
│  ✕ Name Conflict (1)                                     │
│  ┌────────────────────────────┬─────────────┬─────────┐  │
│  │ Rock/track01.flac          │ track01(2)  │  28 MB  │  │
│  └────────────────────────────┴─────────────┴─────────┘  │
│                                                          │
│  ✓ Ready (138 files)                      [show/hide ▾]  │
├──────────────────────────────────────────────────────────┤
│                       [Cancel]    [▶ Run (142 files)]    │
└──────────────────────────────────────────────────────────┘
```

**Delete operation preview (confirm before delete):**

When the pipeline contains a `DeleteStep`, the `[▶ Run]` button is replaced by `[⚠ Run]` and executes `PreviewAsync` first, requiring explicit confirmation in the dialog before proceeding.
The dialog header and layout change to emphasize the destructive nature:

```text
┌──────────────────────────────────────────────────────────┐
│  ⚠ Delete Preview — confirmation required                │
├──────────────────────────────────────────────────────────┤
│  Files will be sent to the Recycle Bin.                  │
│  (To delete permanently, edit the Delete step.)          │
├──────────────────────────────────────────────────────────┤
│  47 files to delete · 1.2 GB                             │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ Abbey Road/01 Come Together.flac               45 MB │ │
│  │ Abbey Road/02 Something.flac                   38 MB │ │
│  │ ...                                                  │ │
│  └──────────────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────────────┤
│                   [Cancel]   [🗑 Delete 47 files to Bin] │
└──────────────────────────────────────────────────────────┘
```

For `DeleteMode.Permanent`, the header and confirm button intensify:
```text
│  ⚠ Permanent delete — files CANNOT be recovered.        │
...
│                   [Cancel]   [⚠ Permanently Delete 47]  │
```

If the pipeline has no executable step (for example, only `Flatten`/`Rename`/`Rebase`), the main run button is disabled until an executable step is added.

---

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
