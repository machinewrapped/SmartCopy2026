using System;
using SmartCopy.UI.ViewModels.Dialogs;
using Xunit;

namespace SmartCopy.Tests.UI.ViewModels.Dialogs;

public sealed class SavePipelineDialogViewModelTests
{
    [Fact]
    public void CanOk_RequiresName()
    {
        var vm = new SavePipelineDialogViewModel();
        Assert.False(vm.OkCommand.CanExecute(null));

        vm.PipelineName = "  ";
        Assert.False(vm.OkCommand.CanExecute(null));

        vm.PipelineName = "ValidName";
        Assert.True(vm.OkCommand.CanExecute(null));
    }

    [Fact]
    public void OkCommand_TriggersOkRequested()
    {
        var vm = new SavePipelineDialogViewModel { PipelineName = "Valid" };
        var okTriggered = false;
        vm.OkRequested += () => okTriggered = true;

        vm.OkCommand.Execute(null);

        Assert.True(okTriggered);
    }

    [Fact]
    public void CancelCommand_TriggersCancelRequested()
    {
        var vm = new SavePipelineDialogViewModel();
        var cancelTriggered = false;
        vm.CancelRequested += () => cancelTriggered = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelTriggered);
    }

    [Fact]
    public void SelectingExistingName_UpdatesPipelineName()
    {
        var vm = new SavePipelineDialogViewModel();
        vm.ExistingNames.Add("My Pipeline");

        vm.SelectedExistingName = "My Pipeline";

        Assert.Equal("My Pipeline", vm.PipelineName);
    }

    [Theory]
    [InlineData("My Pipeline", "My Pipeline", true)]
    [InlineData("My Pipeline", "my pipeline", true)]
    [InlineData("My Pipeline", "My Pipeline  ", true)]
    [InlineData("My Pipeline", "Different", false)]
    public void IsOverwrite_IsCaseInsensitive(string existing, string input, bool expectedOverwrite)
    {
        var vm = new SavePipelineDialogViewModel();
        vm.ExistingNames.Add(existing);

        vm.PipelineName = input;

        Assert.Equal(expectedOverwrite, vm.IsOverwrite);
    }
}
