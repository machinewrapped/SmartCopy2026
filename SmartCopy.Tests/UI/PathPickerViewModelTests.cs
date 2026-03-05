using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

/// <summary>
/// PathPickerViewModel tests cover in-memory state only.
/// AppSettingsStore fire-and-forget saves are a benign side-effect — exceptions are swallowed by the VM.
/// </summary>
public sealed class PathPickerViewModelTests
{
    // Helper: returns a well-formed absolute path that survives PathHelper.NormalizeUserPath on all platforms.
    private static string P(string suffix) =>
        OperatingSystem.IsWindows() ? $@"C:\{suffix}" : $"/{suffix}";

    private static PathPickerViewModel CreateVm(AppSettings? settings = null) =>
        new(settings ?? new AppSettings(), PathPickerMode.Source);

    // ── Pitfall 1: selecting a bookmark must not commit the path ──────────────

    [Fact]
    public void SelectingBookmark_UpdatesPath_DoesNotFirePathCommitted()
    {
        var settings = new AppSettings { RecentSources = [P("music")] };
        var vm = CreateVm(settings);
        var committed = false;
        vm.PathCommitted += (_, _) => committed = true;

        vm.SelectedBookmark = vm.Bookmarks.First();

        Assert.Equal(P("music"), vm.Path);
        Assert.False(committed);
    }

    // ── Pitfall 2: RefreshBookmarks must not clear the current Path ───────────

    [Fact]
    public void RefreshSettings_PreservesCurrentPath()
    {
        var vm = CreateVm();
        vm.Path = P("some/typed/path");

        vm.RefreshSettings();

        Assert.Equal(P("some/typed/path"), vm.Path);
    }

    // ── ApplyPath / PathCommitted ─────────────────────────────────────────────

    [Fact]
    public void ApplyPath_WithBlankPath_DoesNotFirePathCommitted()
    {
        var vm = CreateVm();
        vm.Path = "   ";
        var committed = false;
        vm.PathCommitted += (_, _) => committed = true;

        vm.ApplyPathCommand.Execute(null);

        Assert.False(committed);
    }

    [Fact]
    public void ApplyPath_FiresPathCommittedWithNormalizedPath()
    {
        var vm = CreateVm();
        vm.Path = P("music");
        string? received = null;
        vm.PathCommitted += (_, p) => received = p;

        vm.ApplyPathCommand.Execute(null);

        Assert.NotNull(received);
    }

    // ── RecordRecentPath ──────────────────────────────────────────────────────

    [Fact]
    public void RecordRecentPath_AddsPathToFrontOfList()
    {
        var settings = new AppSettings { RecentSources = [P("older")] };
        var vm = CreateVm(settings);

        vm.RecordRecentPath(P("newer"));

        Assert.Equal(P("newer"), settings.RecentSources[0]);
    }

    [Fact]
    public void RecordRecentPath_MovesExistingEntryToFront()
    {
        var settings = new AppSettings { RecentSources = [P("a"), P("b"), P("c")] };
        var vm = CreateVm(settings);

        vm.RecordRecentPath(P("c"));

        Assert.Equal(P("c"), settings.RecentSources[0]);
        Assert.Equal(3, settings.RecentSources.Count); // no duplicate added
    }

    [Fact]
    public void RecordRecentPath_CapsAtTenEntries()
    {
        var settings = new AppSettings
        {
            RecentSources = [P("1"), P("2"), P("3"), P("4"), P("5"),
                             P("6"), P("7"), P("8"), P("9"), P("10")]
        };
        var vm = CreateVm(settings);

        vm.RecordRecentPath(P("11"));

        Assert.Equal(10, settings.RecentSources.Count);
        Assert.Equal(P("11"), settings.RecentSources[0]);
        Assert.DoesNotContain(P("10"), settings.RecentSources);
    }

    // ── Bookmarks collection ──────────────────────────────────────────────────

    [Fact]
    public void Bookmarks_ShowsFavouritesBeforeRecent()
    {
        var settings = new AppSettings
        {
            FavouritePaths = [P("fav")],
            RecentSources  = [P("recent")],
        };
        var vm = CreateVm(settings);

        Assert.True(vm.Bookmarks[0].IsBookmark);
        Assert.False(vm.Bookmarks[1].IsBookmark);
    }

    [Fact]
    public void Bookmarks_DeduplicatesPathsAcrossFavouritesAndRecent()
    {
        var settings = new AppSettings
        {
            FavouritePaths = [P("shared")],
            RecentSources  = [P("shared")],
        };
        var vm = CreateVm(settings);

        Assert.Single(vm.Bookmarks);
    }

    // ── BookmarkCurrentPath ───────────────────────────────────────────────────

    [Fact]
    public async Task BookmarkCurrentPath_AddsFavourite()
    {
        var vm = CreateVm();
        vm.Path = P("music");

        await ((IAsyncRelayCommand)vm.BookmarkCurrentPathCommand).ExecuteAsync(null);

        Assert.Contains(vm.Bookmarks, b => b.IsBookmark && b.Path == P("music"));
    }

    [Fact]
    public async Task BookmarkCurrentPath_DoesNotDuplicateExistingFavourite()
    {
        var settings = new AppSettings { FavouritePaths = [P("music")] };
        var vm = CreateVm(settings);
        vm.Path = P("music");

        await ((IAsyncRelayCommand)vm.BookmarkCurrentPathCommand).ExecuteAsync(null);

        Assert.Single(vm.Bookmarks, b => b.IsBookmark);
    }

    // ── RemoveBookmark ────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveBookmark_RemovesFavouriteEntry()
    {
        var settings = new AppSettings { FavouritePaths = [P("fav")] };
        var vm = CreateVm(settings);
        var item = vm.Bookmarks.Single(b => b.IsBookmark);

        await ((IAsyncRelayCommand<SourceBookmarkItem?>)vm.RemoveBookmarkCommand).ExecuteAsync(item);

        Assert.DoesNotContain(vm.Bookmarks, b => b.IsBookmark && b.Path == P("fav"));
    }

    [Fact]
    public async Task RemoveBookmark_RemovesRecentEntry()
    {
        var settings = new AppSettings { RecentSources = [P("recent")] };
        var vm = CreateVm(settings);
        var item = vm.Bookmarks.Single(b => !b.IsBookmark);

        await ((IAsyncRelayCommand<SourceBookmarkItem?>)vm.RemoveBookmarkCommand).ExecuteAsync(item);

        Assert.Empty(vm.Bookmarks);
    }
}
