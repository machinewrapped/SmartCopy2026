using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Core.Settings;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class UnmirroredFolderFilterTests
{
    [Theory]
    [InlineData(FilterMode.Only, FilterResult.Excluded, FilterResult.Included)]
    [InlineData(FilterMode.Exclude, FilterResult.Included, FilterResult.Excluded)]
    public async Task FilterChain_UsesContainingFolderRatherThanFile(FilterMode mode, FilterResult existing, FilterResult missing)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/source/existing/new.mp3", size: 1)
            .WithSimulatedFile("/source/existing/nested/song.mp3", size: 1)
            .WithDirectory("/mirror/existing"));
        var root = await provider.BuildDirectoryTree("/source");
        var filter = new MirrorFilter("mem://mirror", MirrorCompareMode.NameOnly, mode, unmirroredFolder: true);

        await new FilterChain([filter]).ApplyToTreeAsync(root, TestAppContext.FromProvider(provider));

        Assert.Equal(existing, root.FindNodeByPathSegments(["existing", "new.mp3"])!.FilterResult);
        Assert.Equal(missing, root.FindNodeByPathSegments(["existing", "nested", "song.mp3"])!.FilterResult);
    }

    [Fact]
    public async Task EmptyFoldersAndRootFiles_AreComparedToTheirCorrespondingFolders()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/source/root.mp3", size: 1)
            .WithDirectory("/source/empty")
            .WithDirectory("/mirror"));
        var root = await provider.BuildDirectoryTree("/source");
        var filter = new MirrorFilter("mem://mirror", MirrorCompareMode.NameOnly, FilterMode.Only, unmirroredFolder: true);
        var context = TestAppContext.FromProvider(provider);

        Assert.False(await filter.MatchesAsync(root.FindNodeByPathSegments(["root.mp3"])!, context));
        Assert.True(await filter.MatchesAsync(root.FindNodeByPathSegments(["empty"])!, context));
    }

    [Theory]
    [InlineData(FilterMode.Only, FilterResult.Included, FilterResult.Excluded)]
    [InlineData(FilterMode.Exclude, FilterResult.Excluded, FilterResult.Included)]
    public async Task MirrorFolder_MatchesExistingFolders(FilterMode mode, FilterResult existing, FilterResult missing)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/source/existing/new.mp3", size: 1)
            .WithSimulatedFile("/source/missing/song.mp3", size: 1)
            .WithDirectory("/mirror/existing"));
        var root = await provider.BuildDirectoryTree("/source");
        var filter = new MirrorFilter("mem://mirror", MirrorCompareMode.NameOnly, mode, mirrorFolder: true);

        await new FilterChain([filter]).ApplyToTreeAsync(root, TestAppContext.FromProvider(provider));

        Assert.Equal(existing, root.FindNodeByPathSegments(["existing", "new.mp3"])!.FilterResult);
        Assert.Equal(missing, root.FindNodeByPathSegments(["missing", "song.mp3"])!.FilterResult);
        var restored = FilterFactory.FromConfig(filter.Config);
        Assert.Equal("MirrorFolder", restored.Config.FilterType);
        var editor = FilterEditorViewModelFactory.CreateFrom(restored, new AppSettings());
        Assert.Equal("MirrorFolder", editor.BuildFilter().Config.FilterType);
    }

    [Fact]
    public void ConfigAndEditor_RoundTripFolderTypeAndAutomaticPath()
    {
        var original = new MirrorFilter("", MirrorCompareMode.NameOnly, FilterMode.Exclude, useAutomaticPath: true, unmirroredFolder: true);
        var restored = Assert.IsType<MirrorFilter>(FilterFactory.FromConfig(original.Config));
        var editor = Assert.IsType<MirrorFilterEditorViewModel>(FilterEditorViewModelFactory.CreateFrom(restored, new AppSettings()));

        Assert.True(restored.UnmirroredFolder);
        Assert.True(restored.UseAutomaticPath);
        Assert.Equal(FilterMode.Exclude, restored.Mode);
        Assert.False(editor.ShowCompareMode);
        Assert.Equal("UnmirroredFolder", editor.BuildFilter().Config.FilterType);
    }

    [Theory]
    [InlineData(FilterMode.Only, true)]
    [InlineData(FilterMode.Exclude, false)]
    [InlineData(FilterMode.Add, false)]
    public async Task AutomaticPathUnavailable_IsNeutral(FilterMode mode, bool expected)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f.WithSimulatedFile("/source/song.mp3", size: 1));
        var root = await provider.BuildDirectoryTree("/source");
        var filter = new MirrorFilter("", MirrorCompareMode.NameOnly, mode, useAutomaticPath: true, unmirroredFolder: true);

        Assert.Equal(expected, await filter.MatchesAsync(root.FindNodeByPathSegments(["song.mp3"])!, TestAppContext.FromProvider(provider)));
    }
}
