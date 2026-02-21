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
}

