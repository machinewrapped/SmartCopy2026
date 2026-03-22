using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Selection;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Selection;

public sealed class SelectionManagerTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static async Task<(DirectoryNode root, DirectoryTreeNode fileA, DirectoryTreeNode fileB)> BuildTree()
    {
        DirectoryNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithFile("/root/a.mp3", "x"u8)
            .WithFile("/root/b.mp3", "y"u8));

        var fileA = root.FindNodeByPathSegments(["root", "a.mp3"]);
        Assert.NotNull(fileA);

        var fileB = root.FindNodeByPathSegments(["root", "b.mp3"]);
        Assert.NotNull(fileB);

        return (root, fileA, fileB);
    }

    [Fact]
    public async Task CaptureAndRestore_PreservesSelectionByRelativePath()
    {
        var (root, fileA, fileB) = await BuildTree();

        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;

        var manager = new SelectionManager();
        var snapshot = manager.Capture(root);

        fileA.CheckState = CheckState.Unchecked;
        manager.Restore(root, snapshot);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
    }

    [Fact]
    public async Task Restore_ReturnsMatchedCount()
    {
        var (root, fileA, _) = await BuildTree();
        var snapshot = new SelectionSnapshot([fileA.CanonicalRelativePath]);

        var result = new SelectionManager().Restore(root, snapshot);

        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.HasUnmatched);
    }

    [Fact]
    public async Task Restore_ReturnsUnmatchedPaths()
    {
        var (root, _, _) = await BuildTree();
        var snapshot = new SelectionSnapshot(["does/not/exist.mp3", "also/missing.flac"]);

        var result = new SelectionManager().Restore(root, snapshot);

        Assert.Equal(0, result.MatchedCount);
        Assert.True(result.HasUnmatched);
        Assert.Equal(2, result.UnmatchedPaths.Count);
    }

    [Fact]
    public async Task Capture_AbsolutePaths_UsesFullPath()
    {
        var (root, fileA, _) = await BuildTree();
        fileA.CheckState = CheckState.Checked;

        var snapshot = new SelectionManager().Capture(root, useAbsolutePaths: true);

        Assert.True(snapshot.Contains(fileA.FullPath));
        Assert.False(snapshot.Contains(fileA.CanonicalRelativePath));
    }

    [Fact]
    public async Task Restore_WithAbsolutePaths_ReturnsCorrectUnmatched()
    {
        // Verify that when a snapshot is captured with absolute paths, Restore does not
        // falsely report matched nodes as unmatched (the original bug: matchedKeys always
        // stored relative paths, so absolute-path entries were never found in matchedKeys).
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;

        var manager = new SelectionManager();
        var snapshot = manager.Capture(root, useAbsolutePaths: true);

        fileA.CheckState = CheckState.Unchecked;
        var result = manager.Restore(root, snapshot);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.HasUnmatched);
    }

    [Fact]
    public async Task SelectAll_SetsAllChecked()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Unchecked;
        fileB.CheckState = CheckState.Unchecked;

        new SelectionManager().SelectAll(root);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Checked, fileB.CheckState);
    }

    [Fact]
    public async Task ClearAll_SetsAllUnchecked()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Checked;

        new SelectionManager().ClearAll(root);

        Assert.Equal(CheckState.Unchecked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
    }

    [Fact]
    public async Task InvertAll_TogglesCheckedToUnchecked()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;

        new SelectionManager().InvertAll(root);

        Assert.Equal(CheckState.Unchecked, fileA.CheckState);
        Assert.Equal(CheckState.Checked, fileB.CheckState);
    }

    // ── RemoveFromSnapshot ────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveFromSnapshot_UnchecksMatchedNodes_LeavesOthersUnchanged()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Checked;

        var snapshot = new SelectionSnapshot([fileA.CanonicalRelativePath]);
        new SelectionManager().RemoveFromSnapshot(root, snapshot);

        Assert.Equal(CheckState.Unchecked, fileA.CheckState);
        Assert.Equal(CheckState.Checked,   fileB.CheckState);
    }

    [Fact]
    public async Task RemoveFromSnapshot_ReturnsMatchedCount()
    {
        var (root, fileA, _) = await BuildTree();
        fileA.CheckState = CheckState.Checked;

        var snapshot = new SelectionSnapshot([fileA.CanonicalRelativePath]);
        var result = new SelectionManager().RemoveFromSnapshot(root, snapshot);

        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.HasUnmatched);
    }

    [Fact]
    public async Task RemoveFromSnapshot_ReturnsUnmatchedPaths()
    {
        var (root, _, _) = await BuildTree();
        var snapshot = new SelectionSnapshot(["does/not/exist.mp3"]);

        var result = new SelectionManager().RemoveFromSnapshot(root, snapshot);

        Assert.Equal(0, result.MatchedCount);
        Assert.True(result.HasUnmatched);
    }

    // ── ExpandSelectedFolders ─────────────────────────────────────────────────

    [Fact]
    public async Task ExpandSelectedFolders_ExpandsDirsWithCheckStateNotUnchecked()
    {
        var (root, fileA, _) = await BuildTree();
        fileA.CheckState = CheckState.Checked;

        // "root/a.mp3" being checked makes the "root" directory Indeterminate (or Checked)
        var subDir = (DirectoryNode?)root.FindNodeByPathSegments(["root"]);
        Assert.NotNull(subDir);
        subDir.IsExpanded = false;

        new SelectionManager().ExpandSelectedFolders(root);

        Assert.True(subDir.IsExpanded);
    }

    [Fact]
    public async Task ExpandSelectedFolders_DoesNotExpandUncheckedDirs()
    {
        var (root, _, _) = await BuildTree();

        var subDir = (DirectoryNode?)root.FindNodeByPathSegments(["root"]);
        Assert.NotNull(subDir);
        Assert.Equal(CheckState.Unchecked, subDir.CheckState);
        subDir.IsExpanded = false;

        new SelectionManager().ExpandSelectedFolders(root);

        Assert.False(subDir.IsExpanded);
    }

    // ── SelectAllFilesInSelectedFolders ───────────────────────────────────────

    [Fact]
    public async Task SelectAllFilesInSelectedFolders_ChecksFilterIncludedFilesInSelectedDirs()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;   // makes parent Indeterminate
        fileB.CheckState = CheckState.Unchecked; // should be checked by the command

        new SelectionManager().SelectAllFilesInSelectedFolders(root);

        Assert.Equal(CheckState.Checked, fileB.CheckState);
    }

    [Fact]
    public async Task SelectAllFilesInSelectedFolders_SkipsExcludedFiles()
    {
        var (root, fileA, fileB) = await BuildTree();
        fileA.CheckState = CheckState.Checked;   // makes parent non-Unchecked
        fileB.CheckState = CheckState.Unchecked;
        fileB.FilterResult = FilterResult.Excluded;

        new SelectionManager().SelectAllFilesInSelectedFolders(root);

        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
    }
}
