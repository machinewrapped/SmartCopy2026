using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class EditFilterDialogViewModelTests
{
    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ForNew_ExtensionType_EditorIsExtensionEditor()
    {
        var vm = EditFilterDialogViewModel.ForNew("Extension");

        Assert.IsType<ExtensionFilterEditorViewModel>(vm.Editor);
    }

    [Fact]
    public void ForNew_IsValid_FalseWhenNoExtensions()
    {
        var vm = EditFilterDialogViewModel.ForNew("Extension");
        var editor = (ExtensionFilterEditorViewModel)vm.Editor;

        // No extensions added yet
        Assert.Empty(editor.Extensions);
        Assert.False(vm.IsValid);
        Assert.False(vm.OkCommand.CanExecute(null));
    }

    [Fact]
    public void ForEdit_PrePopulatesExtensions()
    {
        var existingFilter = new ExtensionFilter(["mp3", "flac"], FilterMode.Only);

        var vm = EditFilterDialogViewModel.ForEdit(existingFilter);
        var editor = (ExtensionFilterEditorViewModel)vm.Editor;

        Assert.Equal(2, editor.Extensions.Count);
        Assert.Contains("mp3", editor.Extensions);
        Assert.Contains("flac", editor.Extensions);
    }

    [Fact]
    public void Ok_SetsResultFilter()
    {
        var vm = EditFilterDialogViewModel.ForNew("Extension");
        var editor = (ExtensionFilterEditorViewModel)vm.Editor;

        editor.InputText = "wav";
        editor.AddExtensionCommand.Execute(null);

        Assert.True(vm.IsValid);
        vm.OkCommand.Execute(null);

        Assert.NotNull(vm.ResultFilter);
        Assert.IsType<ExtensionFilter>(vm.ResultFilter);
    }

    [Fact]
    public void ModeIsAdd_SetToTrue_ChangesModeOnEditor()
    {
        var vm = EditFilterDialogViewModel.ForNew("Extension");

        vm.ModeIsAdd = true;

        Assert.Equal(FilterMode.Add, vm.Mode);
        Assert.Equal(FilterMode.Add, vm.Editor.Mode);
    }

    [Fact]
    public void ModeIsExclude_SetToTrue_ChangesModeOnEditor()
    {
        var vm = EditFilterDialogViewModel.ForNew("Extension");

        vm.ModeIsExclude = true;

        Assert.Equal(FilterMode.Exclude, vm.Mode);
        Assert.Equal(FilterMode.Exclude, vm.Editor.Mode);
    }

    [Fact]
    public void ForNew_MirrorType_SetsComparisonPathSuggestion()
    {
        var vm = EditFilterDialogViewModel.ForNew("Mirror", "/mem/Mirror");
        var editor = (MirrorFilterEditorViewModel)vm.Editor;

        Assert.IsType<MirrorFilterEditorViewModel>(editor);
        Assert.Equal("/mem/Mirror", editor.ComparisonPath);
    }
}
