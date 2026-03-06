using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class EditStepDialogViewModelTests
{
    [Fact]
    public void ForNew_DispatchesCopyEditor()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy, new AppSettings());

        Assert.IsType<CopyStepEditorViewModel>(vm.Editor);
    }

    [Fact]
    public void ForEdit_CopyStep_PrePopulatesDestination()
    {
        var existing = new PipelineStepViewModel(new CopyStep("/mem/out"));

        var vm = EditStepDialogViewModel.ForEdit(existing, new AppSettings());
        var editor = Assert.IsType<CopyStepEditorViewModel>(vm.Editor);

        Assert.Equal("/mem/out", editor.DestinationPath);
    }

    [Fact]
    public void IsValid_GatesOkCommand()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy, new AppSettings());
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
        var vm = EditStepDialogViewModel.ForNew(StepKind.Delete, new AppSettings());
        var editor = (DeleteStepEditorViewModel)vm.Editor;
        editor.DeleteMode = DeleteMode.Permanent;

        vm.OkCommand.Execute(null);

        var result = Assert.IsType<DeleteStep>(vm.ResultStep);
        Assert.Equal(DeleteMode.Permanent, result.Mode);
    }

    [Fact]
    public void FlattenConflictStrategy_RoundTripsThroughResult()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Flatten, new AppSettings());
        var editor = (FlattenStepEditorViewModel)vm.Editor;
        editor.ConflictStrategy = FlattenConflictStrategy.Overwrite;

        vm.OkCommand.Execute(null);

        var result = Assert.IsType<FlattenStep>(vm.ResultStep);
        Assert.Equal(FlattenConflictStrategy.Overwrite, result.ConflictStrategy);
    }

    [Fact]
    public void StepName_UnchangedAutoValue_DoesNotCreateCustomOverride()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy, new AppSettings());
        var editor = (CopyStepEditorViewModel)vm.Editor;
        editor.DestinationPath = "/mem/out";
        var autoName = vm.StepName;

        vm.OkCommand.Execute(null);

        Assert.Equal(autoName, vm.StepName);
        Assert.Null(vm.ResultCustomName);
    }

    [Fact]
    public void StepName_CustomValue_IsReturnedAsOverride()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy, new AppSettings());
        var editor = (CopyStepEditorViewModel)vm.Editor;
        editor.DestinationPath = "/mem/out";
        vm.StepName = "Backup Music";

        vm.OkCommand.Execute(null);

        Assert.Equal("Backup Music", vm.ResultCustomName);
    }

    [Fact]
    public void SaveAsPreset_DefaultsFalse()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Delete, new AppSettings());

        Assert.False(vm.SaveAsPreset);
    }

    [Fact]
    public void SaveAsPreset_CanBeToggled()
    {
        var vm = EditStepDialogViewModel.ForNew(StepKind.Copy, new AppSettings());

        vm.SaveAsPreset = true;

        Assert.True(vm.SaveAsPreset);
    }
}
