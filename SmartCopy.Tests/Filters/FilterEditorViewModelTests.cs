using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class FilterEditorViewModelTests
{
    // -------------------------------------------------------------------------
    // ExtensionFilterEditorViewModel tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AddExtension_NormalizesAndAdds()
    {
        var vm = new ExtensionFilterEditorViewModel();
        vm.InputText = ".MP3";

        vm.AddExtensionCommand.Execute(null);

        Assert.Contains("mp3", vm.Extensions);
    }

    [Fact]
    public void AddExtension_Deduplicates()
    {
        var vm = new ExtensionFilterEditorViewModel();

        vm.InputText = "mp3";
        vm.AddExtensionCommand.Execute(null);

        vm.InputText = "mp3";
        vm.AddExtensionCommand.Execute(null);

        Assert.Single(vm.Extensions);
    }

    [Fact]
    public void AddExtension_SemicolonSeparated()
    {
        var vm = new ExtensionFilterEditorViewModel();
        vm.InputText = "mp3;flac";

        vm.AddExtensionCommand.Execute(null);

        Assert.Equal(2, vm.Extensions.Count);
        Assert.Contains("mp3", vm.Extensions);
        Assert.Contains("flac", vm.Extensions);
    }

    [Fact]
    public void BuildFilter_ExtensionFilter_CorrectExtensions()
    {
        var vm = new ExtensionFilterEditorViewModel();

        vm.InputText = "mp3";
        vm.AddExtensionCommand.Execute(null);
        vm.InputText = "flac";
        vm.AddExtensionCommand.Execute(null);

        var filter = (ExtensionFilter)vm.BuildFilter();

        Assert.Contains("mp3", filter.Extensions);
        Assert.Contains("flac", filter.Extensions);
    }

    // -------------------------------------------------------------------------
    // SizeRangeFilterEditorViewModel tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SizeRange_GbConversion_CorrectBytes()
    {
        var vm = new SizeRangeFilterEditorViewModel
        {
            MinValue = 1.5,
            MinUnit = SizeUnit.GB,
        };

        var filter = (SizeRangeFilter)vm.BuildFilter();

        // 1.5 * 1024^3 = 1,610,612,736
        Assert.Equal(1_610_612_736L, filter.MinBytes);
    }

    // -------------------------------------------------------------------------
    // Round-trip via factory
    // -------------------------------------------------------------------------

    [Fact]
    public void LoadFrom_Extension_ThenBuildFilter_RoundTrip()
    {
        var original = new ExtensionFilter(["mp3", "flac"], FilterMode.Only);

        var editor = (ExtensionFilterEditorViewModel)FilterEditorViewModelFactory.CreateFrom(original, new AppSettings());
        var rebuilt = (ExtensionFilter)editor.BuildFilter();

        Assert.Equal(2, rebuilt.Extensions.Count);
        Assert.Contains("mp3", rebuilt.Extensions);
        Assert.Contains("flac", rebuilt.Extensions);
    }
}
