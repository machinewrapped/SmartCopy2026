using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

public sealed class SelectionViewModelTests
{
    [Fact]
    public void UpdateStats_ZeroFiles_ShowsNoFilesSelected()
    {
        var vm = new SelectionViewModel();

        vm.UpdateStats(0, 0, 0);

        Assert.Equal("No files selected", vm.StatusText);
    }

    [Fact]
    public void UpdateStats_WithFiles_ShowsCountAndSize()
    {
        var vm = new SelectionViewModel();

        vm.UpdateStats(3, 1536, 0);

        Assert.Contains("3 files selected", vm.StatusText);
        Assert.Contains("1.5 KB", vm.StatusText);
    }

    [Fact]
    public void UpdateStats_WithFilteredOut_ShowsFilteredCount()
    {
        var vm = new SelectionViewModel();

        vm.UpdateStats(2, 2048, 5);

        Assert.Contains("2 files selected", vm.StatusText);
        Assert.Contains("5 files filtered out", vm.StatusText);
    }
}
