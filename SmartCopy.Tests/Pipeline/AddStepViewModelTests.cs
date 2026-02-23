using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class AddStepViewModelTests
{
    [Fact]
    public void NavigateToCategory_ShowsLevel2AndItems()
    {
        var vm = new AddStepViewModel();

        vm.NavigateToCategoryCommand.Execute(StepCategory.Path);

        Assert.True(vm.IsLevel2Visible);
        Assert.Equal(StepCategory.Path, vm.SelectedCategory);
        Assert.True(vm.StepTypeItems.Count >= 3);
    }

    [Fact]
    public void GoBack_ReturnsToLevel1()
    {
        var vm = new AddStepViewModel();
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.IsLevel2Visible);
        Assert.Null(vm.SelectedCategory);
        Assert.Empty(vm.StepTypeItems);
    }

    [Fact]
    public void Categories_ContainExpectedStepKinds()
    {
        var vm = new AddStepViewModel();

        vm.NavigateToCategoryCommand.Execute(StepCategory.Path);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Flatten);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Rebase);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Rename);

        vm.NavigateToCategoryCommand.Execute(StepCategory.Content);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Convert);

        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Copy);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Move);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.Delete);
    }

    [Fact]
    public void SelectStepType_RaisesEvent()
    {
        var vm = new AddStepViewModel();
        StepKind? picked = null;
        vm.StepTypeSelected += kind => picked = kind;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Path);

        vm.SelectStepTypeCommand.Execute(StepKind.Flatten);

        Assert.Equal(StepKind.Flatten, picked);
        Assert.False(vm.IsLevel2Visible);
    }

    [Fact]
    public void SelectingMultipleExecutableTypes_DoesNotReplacePriorSelection()
    {
        var vm = new AddStepViewModel();
        var picks = new List<StepKind>();
        vm.StepTypeSelected += picks.Add;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);

        vm.SelectStepTypeCommand.Execute(StepKind.Copy);
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        vm.SelectStepTypeCommand.Execute(StepKind.Move);

        Assert.Equal(2, picks.Count);
        Assert.Equal(StepKind.Copy, picks[0]);
        Assert.Equal(StepKind.Move, picks[1]);
    }
}
