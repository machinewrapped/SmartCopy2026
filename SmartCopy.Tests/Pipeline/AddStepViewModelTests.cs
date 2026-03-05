using System.IO;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class AddStepViewModelTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (AddStepViewModel vm, AppSettings settings, string presetPath) CreateVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartCopy2026.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var presetPath = Path.Combine(dir, "step-presets.json");
        var store = new StepPresetStore(presetPath);
        var settings = new AppSettings();
        return (new AddStepViewModel(store, settings), settings, presetPath);
    }

    // -------------------------------------------------------------------------
    // Level 1 → Level 2 tests (existing behavior)
    // -------------------------------------------------------------------------

    [Fact]
    public void NavigateToCategory_ShowsLevel2AndItems()
    {
        var (vm, _, _) = CreateVm();

        vm.NavigateToCategoryCommand.Execute(StepCategory.Path);

        Assert.True(vm.IsLevel2Visible);
        Assert.Equal(StepCategory.Path, vm.SelectedCategory);
        Assert.True(vm.StepTypeItems.Count >= 3);
    }

    [Fact]
    public void GoBack_ReturnsToLevel1()
    {
        var (vm, _, _) = CreateVm();
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.IsLevel2Visible);
        Assert.Null(vm.SelectedCategory);
        Assert.Empty(vm.StepTypeItems);
    }

    [Fact]
    public void Categories_ContainExpectedStepKinds()
    {
        var (vm, _, _) = CreateVm();

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

        vm.NavigateToCategoryCommand.Execute(StepCategory.Selection);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.SelectAll);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.InvertSelection);
        Assert.Contains(vm.StepTypeItems, item => item.Kind == StepKind.ClearSelection);
    }

    // -------------------------------------------------------------------------
    // Level 2 → Level 3 bypass (no presets for type)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SelectStepType_ForCopyType_BypassesLevel3AndFiresEvent()
    {
        var (vm, _, _) = CreateVm();
        StepKind? picked = null;
        vm.StepTypeSelected += kind => picked = kind;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);

        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Copy);

        Assert.Equal(StepKind.Copy, picked);
        Assert.False(vm.IsLevel3Visible);
        Assert.False(vm.IsLevel2Visible);
    }

    // -------------------------------------------------------------------------
    // Level 2 → Level 3 navigation (type has presets)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SelectStepType_ForDeleteType_NavigatesToLevel3()
    {
        var (vm, _, _) = CreateVm();
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);

        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Delete);

        Assert.True(vm.IsLevel3Visible);
        Assert.True(vm.IsLevel2Visible);
        Assert.False(vm.IsLevel2VisibleOnly);
        Assert.True(vm.HasPresets || vm.HasRecentPresets);
    }

    [Fact]
    public async Task SelectStepType_ForFlattenType_NavigatesToLevel3()
    {
        var (vm, _, _) = CreateVm();
        vm.NavigateToCategoryCommand.Execute(StepCategory.Path);

        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Flatten);

        Assert.True(vm.IsLevel3Visible);
    }

    // -------------------------------------------------------------------------
    // Level 3 navigation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GoBackToLevel2_FromLevel3_ReturnsToLevel2()
    {
        var (vm, _, _) = CreateVm();
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Delete);
        Assert.True(vm.IsLevel3Visible);

        vm.GoBackToLevel2Command.Execute(null);

        Assert.False(vm.IsLevel3Visible);
        Assert.True(vm.IsLevel2VisibleOnly);
        Assert.Null(vm.SelectedStepType);
    }

    [Fact]
    public async Task RequestNewStep_FiresStepTypeSelected()
    {
        var (vm, _, _) = CreateVm();
        StepKind? picked = null;
        vm.StepTypeSelected += kind => picked = kind;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Delete);

        vm.RequestNewStepCommand.Execute(null);

        Assert.Equal(StepKind.Delete, picked);
        Assert.False(vm.IsLevel3Visible);
        Assert.False(vm.IsLevel2Visible);
    }

    [Fact]
    public async Task PickPreset_RaisesStepPresetPickedAndUpdatesSettings()
    {
        var (vm, settings, _) = CreateVm();
        StepPreset? pickedPreset = null;
        vm.StepPresetPicked += preset => pickedPreset = preset;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Delete);

        // Pick the first preset (a built-in)
        var firstPreset = vm.PresetsForType.First();
        vm.PickPresetCommand.Execute(firstPreset);

        Assert.NotNull(pickedPreset);
        Assert.Equal(firstPreset.Preset.Name, pickedPreset!.Name);
        Assert.False(vm.IsLevel3Visible);
        Assert.False(vm.IsLevel2Visible);

        // MRU should have been updated
        Assert.True(settings.StepTypeMruPresetIds.ContainsKey("Delete"));
        Assert.Contains(firstPreset.Preset.Id, settings.StepTypeMruPresetIds["Delete"]);
    }

    // -------------------------------------------------------------------------
    // MRU
    // -------------------------------------------------------------------------

    [Fact]
    public void UpdateMru_PrependsCapped()
    {
        var (vm, settings, _) = CreateVm();

        for (int i = 1; i <= 7; i++)
            vm.UpdateMru("Copy", $"id{i}");

        var mru = settings.StepTypeMruPresetIds["Copy"];
        Assert.Equal(5, mru.Count);
        Assert.Equal("id7", mru[0]);
        Assert.Equal("id6", mru[1]);
    }

    [Fact]
    public void UpdateMru_Deduplicates()
    {
        var (vm, settings, _) = CreateVm();
        vm.UpdateMru("Copy", "a");
        vm.UpdateMru("Copy", "b");
        vm.UpdateMru("Copy", "a");

        var mru = settings.StepTypeMruPresetIds["Copy"];
        Assert.Equal(2, mru.Count);
        Assert.Equal("a", mru[0]);
        Assert.Equal("b", mru[1]);
    }

    // -------------------------------------------------------------------------
    // Close
    // -------------------------------------------------------------------------

    [Fact]
    public void Close_RaisesCloseRequestedAndResetsState()
    {
        var (vm, _, _) = CreateVm();
        var closed = false;
        vm.CloseRequested += () => closed = true;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Content);

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
        Assert.False(vm.IsLevel2Visible);
        Assert.Null(vm.SelectedCategory);
        Assert.Empty(vm.StepTypeItems);
    }

    [Fact]
    public async Task Close_FromLevel3_ResetsAllState()
    {
        var (vm, _, _) = CreateVm();
        var closed = false;
        vm.CloseRequested += () => closed = true;
        vm.NavigateToCategoryCommand.Execute(StepCategory.Executable);
        await vm.SelectStepTypeCommand.ExecuteAsync(StepKind.Delete);

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
        Assert.False(vm.IsLevel3Visible);
        Assert.False(vm.IsLevel2Visible);
    }
}
