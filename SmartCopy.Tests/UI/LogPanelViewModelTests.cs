using Microsoft.Extensions.Logging;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

public sealed class LogPanelViewModelTests
{
    [Fact]
    public void AddEntry_IncrementsEntryCount()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("first");
        vm.AddEntry("second");

        Assert.Equal(2, vm.EntryCount);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_SetsCorrectLevel()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("info message", LogLevel.Information);
        vm.AddEntry("warn message", LogLevel.Warning);
        vm.AddEntry("error message", LogLevel.Error);

        Assert.Equal(LogLevel.Information, vm.Entries[0].Level);
        Assert.Equal(LogLevel.Warning, vm.Entries[1].Level);
        Assert.Equal(LogLevel.Error, vm.Entries[2].Level);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var vm = new LogPanelViewModel();
        vm.AddEntry("a");
        vm.AddEntry("b");

        vm.ClearCommand.Execute(null);

        Assert.Equal(0, vm.EntryCount);
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void IsExpanded_DefaultsFalse()
    {
        var vm = new LogPanelViewModel();

        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void FilterLevel_DefaultsToNull()
    {
        var vm = new LogPanelViewModel();

        Assert.Null(vm.FilterLevel);
        Assert.False(vm.IsWarningFilterActive);
        Assert.False(vm.IsErrorFilterActive);
    }

    [Fact]
    public void WarningCount_IncrementsOnlyForWarnings()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("info", LogLevel.Information);
        vm.AddEntry("warn", LogLevel.Warning);
        vm.AddEntry("warn2", LogLevel.Warning);
        vm.AddEntry("err", LogLevel.Error);

        Assert.Equal(2, vm.WarningCount);
    }

    [Fact]
    public void ErrorCount_IncrementsOnlyForErrors()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("info", LogLevel.Information);
        vm.AddEntry("warn", LogLevel.Warning);
        vm.AddEntry("err", LogLevel.Error);
        vm.AddEntry("err2", LogLevel.Error);

        Assert.Equal(2, vm.ErrorCount);
    }

    [Fact]
    public void Clear_ResetsCounts()
    {
        var vm = new LogPanelViewModel();
        vm.AddEntry("warn", LogLevel.Warning);
        vm.AddEntry("err", LogLevel.Error);

        vm.ClearCommand.Execute(null);

        Assert.Equal(0, vm.WarningCount);
        Assert.Equal(0, vm.ErrorCount);
    }

    [Fact]
    public void ToggleWarningFilter_SetsFilterLevel()
    {
        var vm = new LogPanelViewModel();

        vm.ToggleWarningFilterCommand.Execute(null);

        Assert.Equal(LogLevel.Warning, vm.FilterLevel);
        Assert.True(vm.IsWarningFilterActive);
        Assert.False(vm.IsErrorFilterActive);
    }

    [Fact]
    public void ToggleWarningFilter_WhenActive_ClearsFilterLevel()
    {
        var vm = new LogPanelViewModel();
        vm.ToggleWarningFilterCommand.Execute(null);

        vm.ToggleWarningFilterCommand.Execute(null);

        Assert.Null(vm.FilterLevel);
        Assert.False(vm.IsWarningFilterActive);
    }

    [Fact]
    public void ToggleErrorFilter_IsExclusive_ClearsWarningFilter()
    {
        var vm = new LogPanelViewModel();
        vm.ToggleWarningFilterCommand.Execute(null);

        vm.ToggleErrorFilterCommand.Execute(null);

        Assert.Equal(LogLevel.Error, vm.FilterLevel);
        Assert.True(vm.IsErrorFilterActive);
        Assert.False(vm.IsWarningFilterActive);
    }
}
