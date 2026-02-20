# SmartCopy2026 — Plan Review & Insights

**Reviewer:** "Antigravity" Agent
**Date:** 2026-02-19

## 1. Executive Summary

The **SmartCopy2026** plan is exceptionally well-structured, addressing the limitations of the predecessor while adopting a modern, cross-platform architecture. The choice of **Avalonia UI** and **.NET 10** ensures longevity and cross-platform reach. The separation of concerns (Core / App / UI) and the use of the **Pipeline** and **Filter Chain** patterns provide a level of flexibility often missing in ad-hoc utility rewrites.

The plan is **highly feasible** for a solo developer aided by AI agents, provided that strict discipline is maintained regarding the scope of Phase 1. The "Shell First" approach (§8 Step 1) is particularly commendable as it mitigates the risk of backend over-engineering before UX validation.

## 2. Architectural Insights

### 2.1. FileSystem Abstraction (`IFileSystemProvider`)
*   **Insight:** This is the make-or-break abstraction. MTP (Media Transfer Protocol) is notoriously sensitive to concurrency and timeouts.
*   **Recommendation:**
    *   Ensure `IFileSystemProvider` includes a `Capabilities` property (e.g., `CanSeek`, `CanTimeout`, `MaxPathLength`). MTP streams often cannot seek.
    *   Consider a **"Connection Resilience"** wrapper. MTP devices often disconnect briefly. A retry policy within the provider (invisible to the pipeline) could save frustration.

### 2.2. Tree Virtualization & State Propagation (§3 / §6.2)
The plan correctly identifies performance risks with 100k+ nodes.
*   **Risk:** Recursively firing `PropertyChanged` events for every node during a "Select All" operation will freeze the UI, even with virtualization, because the data model updates still run on the UI thread (usually) or trigger bindings.
*   **Recommendation:**
    *   Implement a **`BatchUpdate` context**. When checking a parent folder, suspend `PropertyChanged` notifications for the subtree, update the raw state, and then fire a single "Reset" or specific event, or rely on the parent wanting to refresh visual children.
    *   Consider separating the **Selection State** into a `HashSet<string RelativePath>` in the ViewModel/Manager, rather than a property on every `FileSystemNode` object. This makes "saving selection" instant (it's just the set) and "restoring selection" fast (set lookups). However, the current `FileSystemNode.IsChecked` binding is easier for UI. If you stick to the node-property approach, ensure strictly O(N) propagation without layout trashing.

### 2.3. The Transform Pipeline (§5.3)
*   **Insight:** The pipeline concept is powerful. The distinction between `PathStep` and `ContentStep` is smart.
*   **Edge Case:** "Move" operations across providers (e.g., Local -> MTP) are actually "Copy + Delete".
*   **Recommendation:**
    *   Ensure `MoveStep` explicitly queries the providers. If `SourceProvider == TargetProvider`, try the atomic move. If different, functionality must degrade to `CopyStep` -> `DeleteStep`. The plan hints at this in §6.8, but it should be a formal part of the pipeline logic to avoid "half-moved" states on failure.

## 3. Implementation Specifics

### 3.1. Progressive Scanning (§5.6)
*   **UX Pattern:** The "prioritized scan" (scanning the folder the user just expanded) is excellent.
*   **Recommendation:** Use a **Priority Queue** for the scanner. Default tasks are "scan next sibling". User action adds a "scan this specific node" task with High Priority to the front of the queue.

### 3.2. Conflict Resolution (§6.9)
*   **Recommendation:** During `Preview`, if `FlattenStep` detects a collision that will result in `AutoRename`, the `OperationPlan` should explicitly show the *new* name (e.g., `song (2).mp3`). Users hate "magic" renaming that they didn't expect.

### 3.3. Testing Strategy
*   **Recommendation:** Create a `MemoryFileSystemProvider` early (Phase 1). This allows you to test 90% of the logic (Scanning, Filtering, Pipelines) without touching the slow physical disk or worrying about cleanup. It makes unit tests millisecond-fast.

## 4. Phase 1 Scope Adjustments

*   **Keyboard Navigation:** The plan moves this to Phase 1 (§8 Step 1), which is excellent. Accessibility implies usability.
*   **Logging:** The plan mentions a "lightweight journal". I recommend using **Serilog** configured to write to a fast rolling file. Don't reinvent logging. It allows structured data (JSON logs) which makes "Operation Journal" parsing trivial later.

## 5. Security & Safety

*   **Plugin Trust (§11.2):** For a "solo + AI" open source project, **Sandboxing** is too hard.
    *   **Decision:** Accept that plugins equate to "running arbitrary code". The best mitigation is simply **user confirmation** on first load, as noted. Do not over-engineer a trust system in v1.
*   **Delete Safety:**
    *   **Trap:** `TrashService` might hang on network drives.
    *   **Fix:** Wrap trash checks in a short timeout (e.g., 500ms). If it takes longer, assume "Trash Unavailable" and prompt for permanent delete.

## 6. Technology Stack Validation

*   **.NET 10 (LTS):** Given the current date (Feb 2026), .NET 10 is the correct target for a new long-term project.
*   **Avalonia 11+:** Excellent choice. The `TreeDataGrid` (if available/stable) might be more performant than a standard `TreeView` + `Grid` hybrid for the file columns.

## 7. Conclusion

This plan is **Approved for Implementation**. The level of detail in the "Algorithms" section (§6) reduces the "unknowns" significantly.

**Suggested Immediate Next Action:**
Begin **Phase 1, Step 1 (Project Scaffold)**. Initialize the git repo, solution structure, and the basic Avalonia shell.
