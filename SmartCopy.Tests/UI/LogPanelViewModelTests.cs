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

        vm.AddEntry("info message", LogLevel.Info);
        vm.AddEntry("warn message", LogLevel.Warning);
        vm.AddEntry("error message", LogLevel.Error);

        Assert.Equal(LogLevel.Info, vm.Entries[0].Level);
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
    public void MinimumLevel_DefaultsToInfo()
    {
        var vm = new LogPanelViewModel();

        Assert.Equal(LogLevel.Info, vm.MinimumLevel);
    }

    [Fact]
    public void WarningCount_IncrementsOnlyForWarnings()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("info", LogLevel.Info);
        vm.AddEntry("warn", LogLevel.Warning);
        vm.AddEntry("warn2", LogLevel.Warning);
        vm.AddEntry("err", LogLevel.Error);

        Assert.Equal(2, vm.WarningCount);
    }

    [Fact]
    public void ErrorCount_IncrementsOnlyForErrors()
    {
        var vm = new LogPanelViewModel();

        vm.AddEntry("info", LogLevel.Info);
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
}
