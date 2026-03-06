using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Filters;

public sealed class FilterChainViewModelTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ExtensionFilter MakeExtensionFilter(string ext = "mp3") =>
        new ExtensionFilter([ext], FilterMode.Only);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildLiveChain_ContainsAllFilterInstances()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        var filterA = MakeExtensionFilter("mp3");
        var filterB = MakeExtensionFilter("flac");

        vm.AddFilterFromResult(filterA);
        vm.AddFilterFromResult(filterB);

        var chain = vm.BuildLiveChain();

        Assert.Equal(2, chain.Filters.Count);
        Assert.Contains(filterA, chain.Filters);
        Assert.Contains(filterB, chain.Filters);
    }

    [Fact]
    public void AddFilterFromResult_FiresChainChanged()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        var raised = false;
        vm.ChainChanged += (_, _) => raised = true;

        vm.AddFilterFromResult(MakeExtensionFilter());

        Assert.True(raised);
    }

    [Fact]
    public void RemoveFilter_FiresChainChanged()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        vm.AddFilterFromResult(MakeExtensionFilter("mp3"));

        var filterVm = vm.Filters[0];
        var raised = false;
        vm.ChainChanged += (_, _) => raised = true;

        vm.RemoveFilterCommand.Execute(filterVm);

        Assert.True(raised);
        Assert.Empty(vm.Filters);
    }

    [Fact]
    public void IsEnabledToggle_FiresChainChanged()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        vm.AddFilterFromResult(MakeExtensionFilter());

        var filterVm = vm.Filters[0];
        var raised = false;
        vm.ChainChanged += (_, _) => raised = true;

        filterVm.IsEnabled = false;

        Assert.True(raised);
    }

    [Fact]
    public void ReplaceFilter_UpdatesBackingFilterAndProperties()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        var original = MakeExtensionFilter("mp3");
        vm.AddFilterFromResult(original);

        var filterVm = vm.Filters[0];
        var replacement = new ExtensionFilter(["flac"], FilterMode.Exclude);

        vm.ReplaceFilter(filterVm, replacement);

        Assert.Same(replacement, filterVm.BackingFilter);
        Assert.Equal("EXCLUDE", filterVm.Mode);
    }

    [Fact]
    public void MoveFilter_ReordersCollection()
    {
        var vm = new FilterChainViewModel(new TestAppContext());
        var filterA = MakeExtensionFilter("mp3");
        var filterB = MakeExtensionFilter("flac");
        var filterC = MakeExtensionFilter("wav");

        vm.AddFilterFromResult(filterA);
        vm.AddFilterFromResult(filterB);
        vm.AddFilterFromResult(filterC);

        // Move index 0 (A) to index 2 → expected order: B, C, A
        vm.MoveFilter(0, 2);

        Assert.Same(filterB, vm.Filters[0].BackingFilter);
        Assert.Same(filterC, vm.Filters[1].BackingFilter);
        Assert.Same(filterA, vm.Filters[2].BackingFilter);
    }
}
