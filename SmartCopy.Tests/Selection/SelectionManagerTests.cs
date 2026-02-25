using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Selection;

namespace SmartCopy.Tests.Selection;

public sealed class SelectionManagerTests
{
    [Fact]
    public void CaptureAndRestore_PreservesSelectionByRelativePath()
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = "/root",
            RelativePath = "root",
            IsDirectory = true,
        };

        var fileA = new FileSystemNode
        {
            Name = "a.mp3",
            FullPath = "/root/a.mp3",
            RelativePath = "root/a.mp3",
            IsDirectory = false,
            Parent = root,
            CheckState = CheckState.Checked,
        };
        var fileB = new FileSystemNode
        {
            Name = "b.mp3",
            FullPath = "/root/b.mp3",
            RelativePath = "root/b.mp3",
            IsDirectory = false,
            Parent = root,
            CheckState = CheckState.Unchecked,
        };

        root.Children.Add(fileA);
        root.Children.Add(fileB);

        var manager = new SelectionManager();
        var snapshot = manager.Capture([root]);

        fileA.CheckState = CheckState.Unchecked;
        manager.Restore([root], snapshot);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
    }

    [Fact]
    public void Restore_ReturnsMatchedCount()
    {
        var (root, fileA, _) = BuildTree();
        var snapshot = new SelectionSnapshot([fileA.RelativePath]);

        var result = new SelectionManager().Restore([root], snapshot);

        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.HasUnmatched);
    }

    [Fact]
    public void Restore_ReturnsUnmatchedPaths()
    {
        var (root, _, _) = BuildTree();
        var snapshot = new SelectionSnapshot(["does/not/exist.mp3", "also/missing.flac"]);

        var result = new SelectionManager().Restore([root], snapshot);

        Assert.Equal(0, result.MatchedCount);
        Assert.True(result.HasUnmatched);
        Assert.Equal(2, result.UnmatchedPaths.Count);
    }

    [Fact]
    public void Capture_AbsolutePaths_UsesFullPath()
    {
        var (root, fileA, _) = BuildTree();
        fileA.CheckState = CheckState.Checked;

        var snapshot = new SelectionManager().Capture([root], useAbsolutePaths: true);

        Assert.True(snapshot.Contains(fileA.FullPath));
        Assert.False(snapshot.Contains(fileA.RelativePath));
    }

    [Fact]
    public void Restore_WithAbsolutePaths_ReturnsCorrectUnmatched()
    {
        // Verify that when a snapshot is captured with absolute paths, Restore does not
        // falsely report matched nodes as unmatched (the original bug: matchedKeys always
        // stored relative paths, so absolute-path entries were never found in matchedKeys).
        var (root, fileA, fileB) = BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;

        var manager = new SelectionManager();
        var snapshot = manager.Capture([root], useAbsolutePaths: true);

        fileA.CheckState = CheckState.Unchecked;
        var result = manager.Restore([root], snapshot);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.HasUnmatched);
    }

    [Fact]
    public void SelectAll_SetsAllChecked()
    {
        var (root, fileA, fileB) = BuildTree();
        fileA.CheckState = CheckState.Unchecked;
        fileB.CheckState = CheckState.Unchecked;

        new SelectionManager().SelectAll([root]);

        Assert.Equal(CheckState.Checked, fileA.CheckState);
        Assert.Equal(CheckState.Checked, fileB.CheckState);
    }

    [Fact]
    public void ClearAll_SetsAllUnchecked()
    {
        var (root, fileA, fileB) = BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Checked;

        new SelectionManager().ClearAll([root]);

        Assert.Equal(CheckState.Unchecked, fileA.CheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState);
    }

    [Fact]
    public void InvertAll_TogglesCheckedToUnchecked()
    {
        var (root, fileA, fileB) = BuildTree();
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;

        new SelectionManager().InvertAll([root]);

        Assert.Equal(CheckState.Unchecked, fileA.CheckState);
        Assert.Equal(CheckState.Checked, fileB.CheckState);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static (FileSystemNode root, FileSystemNode fileA, FileSystemNode fileB) BuildTree()
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = "/root",
            RelativePath = "root",
            IsDirectory = true,
        };

        var fileA = new FileSystemNode
        {
            Name = "a.mp3",
            FullPath = "/root/a.mp3",
            RelativePath = "root/a.mp3",
            IsDirectory = false,
            Parent = root,
        };

        var fileB = new FileSystemNode
        {
            Name = "b.mp3",
            FullPath = "/root/b.mp3",
            RelativePath = "root/b.mp3",
            IsDirectory = false,
            Parent = root,
        };

        root.Files.Add(fileA);
        root.Files.Add(fileB);

        return (root, fileA, fileB);
    }
}
