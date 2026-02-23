using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class EditStepDialogViewModelTests
{
    [Fact]
    public void ForNew_DispatchesCopyEditor()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy);

        Assert.IsType<CopyStepEditorViewModel>(vm.Editor);
    }

    [Fact]
    public void ForEdit_CopyStep_PrePopulatesDestination()
    {
        var existing = new PipelineStepViewModel(new CopyStep("/mem/out"));

        var vm = EditStepDialogViewModel.ForEdit(existing);
        var editor = Assert.IsType<CopyStepEditorViewModel>(vm.Editor);

        Assert.Equal("/mem/out", editor.DestinationPath);
    }

    [Fact]
    public void IsValid_GatesOkCommand()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy);
        var editor = (CopyStepEditorViewModel)vm.Editor;

        Assert.False(vm.IsValid);
        Assert.False(vm.OkCommand.CanExecute(null));

        editor.DestinationPath = "/mem/out";

        Assert.True(vm.IsValid);
        Assert.True(vm.OkCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteModeToggle_IsReflectedInResultStep()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Delete);
        var editor = (DeleteStepEditorViewModel)vm.Editor;
        editor.DeleteMode = DeleteMode.Permanent;

        vm.OkCommand.Execute(null);

        var result = Assert.IsType<DeleteStep>(vm.ResultStep);
        Assert.Equal(DeleteMode.Permanent, result.Mode);
    }

    [Fact]
    public void FlattenConflictStrategy_RoundTripsThroughResult()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Flatten);
        var editor = (FlattenStepEditorViewModel)vm.Editor;
        editor.ConflictStrategy = FlattenConflictStrategy.Overwrite;

        vm.OkCommand.Execute(null);

        var result = Assert.IsType<FlattenStep>(vm.ResultStep);
        Assert.Equal(FlattenConflictStrategy.Overwrite, result.ConflictStrategy);
    }
}
