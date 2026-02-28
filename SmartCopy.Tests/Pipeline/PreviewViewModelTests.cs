using SmartCopy.Core.Pipeline;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PreviewViewModelTests
{
    private static OperationPlan BuildPlan()
    {
        return new OperationPlan
        {
            Actions =
            [
                new PlannedAction(
                    SourcePath: "/a",
                    SourcePathResult: SourcePathResult.Copied,
                    DestinationPath: "/out/a",
                    DestinationPathResult: DestinationPathResult.Created,
                    NumberOfFilesAffected: 1,
                    NumberOfFoldersAffected: 0,
                    InputBytes: 10,
                    OutputBytes: 10),
                new PlannedAction(
                    SourcePath: "/b",
                    SourcePathResult: SourcePathResult.Copied,
                    DestinationPath: "/out/b",
                    DestinationPathResult: DestinationPathResult.Overwritten,
                    NumberOfFilesAffected: 1,
                    NumberOfFoldersAffected: 0,
                    InputBytes: 10,
                    OutputBytes: 10),
                new PlannedAction(
                    SourcePath: "/c",
                    SourcePathResult: SourcePathResult.Moved,
                    DestinationPath: "/out/c",
                    DestinationPathResult: DestinationPathResult.Created,
                    NumberOfFilesAffected: 1,
                    NumberOfFoldersAffected: 0,
                    InputBytes: 10,
                    OutputBytes: 10),
                new PlannedAction(
                    SourcePath: "/d",
                    SourcePathResult: SourcePathResult.Deleted,
                    DestinationPath: null,
                    DestinationPathResult: DestinationPathResult.None,
                    NumberOfFilesAffected: 1,
                    NumberOfFoldersAffected: 0,
                    InputBytes: 10,
                    OutputBytes: 10),
            ],
            TotalInputBytes = 30,
            TotalEstimatedOutputBytes = 30,
        };
    }

    [Fact]
    public void LoadFrom_GroupsActionsByWarning()
    {
        var vm = new PreviewViewModel();

        vm.LoadFrom(BuildPlan(), isDeletePipeline: false, deleteMode: DeleteMode.Trash);

        Assert.Equal(4, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Will delete"));
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Will overwrite"));
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Will copy"));
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Will move"));
    }

    [Fact]
    public void LoadFrom_SetsWarningCounts()
    {
        var vm = new PreviewViewModel();

        vm.LoadFrom(BuildPlan(), isDeletePipeline: false, deleteMode: DeleteMode.Trash);

        Assert.Equal(4, vm.TotalActionCount);
        Assert.Equal(30, vm.TotalEstimatedOutputBytes);
    }

    [Fact]
    public void IsDeletePipeline_SetsDeleteMode()
    {
        var vm = new PreviewViewModel();

        vm.LoadFrom(BuildPlan(), isDeletePipeline: true, deleteMode: DeleteMode.Permanent);

        Assert.True(vm.IsDeletePipeline);
        Assert.Contains("⚠ Run", vm.ConfirmButtonText);
    }

    [Fact]
    public void CopyGroup_CountMatches()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(BuildPlan(), isDeletePipeline: false, deleteMode: DeleteMode.Trash);

        var copyGroup = Assert.Single(vm.Groups, g => g.Title.StartsWith("Will copy"));
        Assert.Equal(1, copyGroup.Count);
    }
}
