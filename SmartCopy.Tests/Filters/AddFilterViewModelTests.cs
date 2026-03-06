using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Filters;

public sealed class AddFilterViewModelTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a VM backed by a fresh AppSettings and a temp-dir preset store path.
    /// Returns both so tests can inspect settings mutations via the same instance.
    /// </summary>
    private static (AddFilterViewModel vm, AppSettings settings) CreateVm()
    {
        var appContext = new TestAppContext();
        var vm = new AddFilterViewModel(appContext);
        return (vm, appContext.Settings);
    }

    private static FilterTypeItem ExtensionTypeItem(AddFilterViewModel vm) =>
        vm.FilterTypes.First(t => t.TypeKey == "Extension");

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FilterTypes_HasSixEntries()
    {
        var (vm, _) = CreateVm();

        Assert.Equal(6, vm.FilterTypes.Count);
    }

    [Fact]
    public async Task SelectFilterType_NavigatesToLevel2()
    {
        var (vm, _) = CreateVm();
        var item = ExtensionTypeItem(vm);

        await ((IAsyncRelayCommand<FilterTypeItem>)vm.SelectFilterTypeCommand).ExecuteAsync(item);

        Assert.True(vm.IsLevel2Visible);
        Assert.Equal(item, vm.SelectedType);
    }

    [Fact]
    public async Task GoBack_ReturnsToLevel1()
    {
        var (vm, _) = CreateVm();
        var item = ExtensionTypeItem(vm);

        await ((IAsyncRelayCommand<FilterTypeItem>)vm.SelectFilterTypeCommand).ExecuteAsync(item);
        Assert.True(vm.IsLevel2Visible);

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.IsLevel2Visible);
        Assert.Null(vm.SelectedType);
    }

    [Fact]
    public void UpdateMru_PrependsCapped()
    {
        var (vm, settings) = CreateVm();

        // Insert 6 distinct IDs — list must cap at 5, most-recent first
        for (var i = 1; i <= 6; i++)
        {
            vm.UpdateMru("Extension", $"id-{i}");
        }

        Assert.True(settings.FilterTypeMruPresetIds.TryGetValue("Extension", out var list));
        Assert.Equal(5, list.Count);
        // Most-recently inserted (id-6) should be first
        Assert.Equal("id-6", list[0]);
    }

    [Fact]
    public void UpdateMru_Deduplicates()
    {
        var (vm, settings) = CreateVm();

        vm.UpdateMru("Extension", "abc");
        vm.UpdateMru("Extension", "def");
        vm.UpdateMru("Extension", "abc"); // add abc again — should de-dupe and move to front

        Assert.True(settings.FilterTypeMruPresetIds.TryGetValue("Extension", out var list));
        Assert.Equal(2, list.Count);
        Assert.Equal("abc", list[0]); // most-recently used is first
        Assert.Equal("def", list[1]);
    }
}
