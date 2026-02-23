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
                new PlannedAction("Copy", "/a", "/out/a", 10, 10, null),
                new PlannedAction("Copy", "/b", "/out/b", 10, 10, PlanWarning.DestinationExists),
                new PlannedAction("Move", "/c", "/out/c", 10, 10, PlanWarning.NameConflict),
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

        Assert.Equal(3, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Ready"));
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Destination Exists"));
        Assert.Contains(vm.Groups, g => g.Title.StartsWith("Name Conflict"));
    }

    [Fact]
    public void LoadFrom_SetsWarningCounts()
    {
        var vm = new PreviewViewModel();

        vm.LoadFrom(BuildPlan(), isDeletePipeline: false, deleteMode: DeleteMode.Trash);

        Assert.Equal(3, vm.TotalActionCount);
        Assert.Equal(30, vm.TotalEstimatedOutputBytes);
    }

    [Fact]
    public void IsDeletePipeline_SetsDeleteMode()
    {
        var vm = new PreviewViewModel();

        vm.LoadFrom(BuildPlan(), isDeletePipeline: true, deleteMode: DeleteMode.Permanent);

        Assert.True(vm.IsDeletePipeline);
        Assert.Contains("Permanently Delete", vm.ConfirmButtonText);
    }

    [Fact]
    public void CanRun_IsGatedByConfirmation_ForDeletePipelines()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(BuildPlan(), isDeletePipeline: true, deleteMode: DeleteMode.Trash);

        Assert.False(vm.CanRun);
        vm.IsConfirmed = true;
        Assert.True(vm.CanRun);
    }

    [Fact]
    public void ReadyGroup_CountMatches()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(BuildPlan(), isDeletePipeline: false, deleteMode: DeleteMode.Trash);

        var readyGroup = Assert.Single(vm.Groups, g => g.IsReadyGroup);
        Assert.Equal(1, readyGroup.Count);
    }
}
