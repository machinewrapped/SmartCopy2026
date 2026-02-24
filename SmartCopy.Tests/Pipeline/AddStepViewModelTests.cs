using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels.Pipeline;
using Xunit;

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
        var store = new StepPresetStore();
        var settings = new AppSettings();
        return (new AddStepViewModel(store, settings, presetPath), settings, presetPath);
    }

    // -------------------------------------------------------------------------
    // Menu Building Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_BuildsMenuTypeHierarchies()
    {
        var (vm, _, _) = CreateVm();
        await vm.Initialization;

        Assert.NotEmpty(vm.CopyMenuItems);
        Assert.NotEmpty(vm.MoveMenuItems);
        Assert.NotEmpty(vm.DeleteMenuItems);
        
        // Path menu is a category containing submenus
        Assert.NotEmpty(vm.PathMenuItems);
        Assert.Contains(vm.PathMenuItems, m => m.Header == "Flatten");
        Assert.Contains(vm.PathMenuItems, m => m.Header == "Rebase");
        Assert.Contains(vm.PathMenuItems, m => m.Header == "Rename");
        
        // Ensure the flatten submenu has items
        var flattenMenu = vm.PathMenuItems.First(m => m.Header == "Flatten");
        Assert.NotNull(flattenMenu.Items);
        Assert.NotEmpty(flattenMenu.Items);
    }

    [Fact]
    public async Task DeleteMenu_ContainsNewAndBuiltInPresets()
    {
        var (vm, _, _) = CreateVm();
        await vm.Initialization;

        var menu = vm.DeleteMenuItems;
        
        // First item is always '+ New...'
        Assert.Equal("＋ New...", menu[0].Header);
        
        // There should be builtin presets in the menu
        Assert.Contains(menu, m => m.Header.Contains("Delete to Trash"));
        Assert.Contains(menu, m => m.Header.Contains("Delete permanently"));
    }

    // -------------------------------------------------------------------------
    // Execution Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void RequestNewStep_FiresStepTypeSelected()
    {
        var (vm, _, _) = CreateVm();
        StepKind? picked = null;
        vm.StepTypeSelected += kind => picked = kind;

        vm.RequestNewStepCommand.Execute(StepKind.Delete);

        Assert.Equal(StepKind.Delete, picked);
    }

    [Fact]
    public async Task PickPreset_RaisesStepPresetPickedAndUpdatesSettings()
    {
        var (vm, settings, _) = CreateVm();
        await vm.Initialization;
        
        StepPreset? pickedPreset = null;
        vm.StepPresetPicked += preset => pickedPreset = preset;

        // Find a Delete preset menu item (skip '+ New...' and separators)
        var presetItem = vm.DeleteMenuItems.FirstOrDefault(m => m.CommandParameter is StepPresetItem);
        Assert.NotNull(presetItem);
        
        var stepPresetItem = (StepPresetItem)presetItem.CommandParameter!;

        vm.PickPresetCommand.Execute(stepPresetItem);

        Assert.NotNull(pickedPreset);
        Assert.Equal(stepPresetItem.Preset.Name, pickedPreset!.Name);

        // MRU should have been updated
        Assert.True(settings.StepTypeMruPresetIds.ContainsKey("Delete"));
        Assert.Contains(stepPresetItem.Preset.Id, settings.StepTypeMruPresetIds["Delete"]);
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

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
        Assert.False(vm.IsSavingPipeline);
        Assert.Empty(vm.NewPipelineName);
    }
}
