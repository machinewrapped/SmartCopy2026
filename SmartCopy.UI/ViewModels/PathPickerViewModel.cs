using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels;

public partial class PathPickerViewModel : ViewModelBase
{
    private const int MaxRecentPaths = 10;

    private readonly AppSettings _settings;
    private readonly PathPickerMode _mode;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private SourceBookmarkItem? _selectedBookmark;

    public ObservableCollection<SourceBookmarkItem> Bookmarks { get; } = [];

    // Fired when the user explicitly commits a path (Enter key or combo selection)
    public event EventHandler<string>? PathCommitted;

    /// <summary>
    /// Called by the code-behind when a new provider (e.g. MTP) is created and must be
    /// registered before PathCommitted fires. Set by the parent ViewModel at construction.
    /// </summary>
    public Action<IFileSystemProvider>? RegisterProvider { get; set; }

    public PathPickerViewModel(AppSettings settings, PathPickerMode mode)
    {
        _settings = settings;
        _mode = mode;

        RefreshBookmarks();
    }

    /// <summary>Update bookmarks from app settings</summary>
    public void RefreshSettings()
    {
        RefreshBookmarks();
    }

    partial void OnSelectedBookmarkChanged(SourceBookmarkItem? value)
    {
        // Pitfall 1: Only populate the text field — don't apply side effects until commit
        if (value is not null)
        {
            Path = value.Path;
        }
    }

    [RelayCommand]
    private void RevertPath()
    {
        ValidationMessage = string.Empty;
        // The View is expected to handle reverting to its last known good state if necessary,
        // or the owner VM will handle it. PathPickerViewModel just manages the text and history.
        // If we needed to revert, we would need to store the `_lastCommittedPath` here, but for now
        // we leave that to the parent or just clear validation.
    }

    [RelayCommand]
    private void ApplyPath()
    {
        var normalized = PathHelper.NormalizeUserPath(Path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        RecordRecentPath(normalized);
        PathCommitted?.Invoke(this, normalized);
    }

    [RelayCommand]
    private async Task BookmarkCurrentPathAsync()
    {
        var normalized = PathHelper.NormalizeUserPath(Path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (GetFavouritesList().Any(existing => PathHelper.AreEquivalentUserPaths(existing, normalized)))
        {
            return;
        }

        GetFavouritesList().Insert(0, normalized);
        RefreshBookmarks();
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private async Task RemoveBookmarkAsync(SourceBookmarkItem? item)
    {
        if (item is null) return;

        var normalizedPath = PathHelper.NormalizeUserPath(item.Path);
        bool removed;

        if (item.IsBookmark)
        {
            removed = RemoveEquivalentPath(GetFavouritesList(), normalizedPath);
        }
        else
        {
            var recentList = GetRecentList();
            removed = RemoveEquivalentPath(recentList, normalizedPath);
        }

        if (removed)
        {
            RefreshBookmarks();
            await SaveSettingsAsync();
        }
    }

    public void RecordRecentPath(string path)
    {
        var normalizedPath = PathHelper.NormalizeUserPath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var recentList = GetRecentList();

        RemoveEquivalentPath(recentList, normalizedPath);
        recentList.Insert(0, normalizedPath);

        if (recentList.Count > MaxRecentPaths)
        {
            recentList.RemoveAt(MaxRecentPaths);
        }

        RefreshBookmarks();
        _ = SaveSettingsAsync(); // Fire and forget
    }

    private void RefreshBookmarks()
    {
        // Pitfall 2: Preserve the current text — Clear() nulls SelectedItem which
        // causes the editable ComboBox to wipe its Text binding.
        var currentPath = Path;

        Bookmarks.Clear();

        var addedPaths = new HashSet<string>(PathHelper.PathComparer);

        var recentList = GetRecentList();

        var normalizedFavourites = PathHelper.NormalizeDistinctUserPaths(GetFavouritesList());
        var normalizedRecent = PathHelper.NormalizeDistinctUserPaths(recentList);

        // Save normalized lists back
        SetFavouritesList(normalizedFavourites);
        SetRecentList(normalizedRecent);

        foreach (var p in normalizedFavourites)
        {
            if (addedPaths.Add(p))
                Bookmarks.Add(new SourceBookmarkItem(p, true));
        }

        foreach (var p in recentList)
        {
            if (addedPaths.Add(p))
                Bookmarks.Add(new SourceBookmarkItem(p, false));
        }

        Path = currentPath;
    }

    private List<string> GetFavouritesList() => _mode == PathPickerMode.SelectionFile
        ? _settings.FavouriteSelectionFiles
        : _settings.FavouritePaths;

    private void SetFavouritesList(List<string> normalized)
    {
        if (_mode == PathPickerMode.SelectionFile)
            _settings.FavouriteSelectionFiles = normalized;
        else
            _settings.FavouritePaths = normalized;
    }

    private List<string> GetRecentList() => _mode switch
    {
        PathPickerMode.Source        => _settings.RecentSources,
        PathPickerMode.SelectionFile => _settings.RecentSelectionFiles,
        _                            => _settings.RecentTargets,
    };

    private void SetRecentList(List<string> normalized)
    {
        switch (_mode)
        {
            case PathPickerMode.Source:        _settings.RecentSources        = normalized; break;
            case PathPickerMode.SelectionFile: _settings.RecentSelectionFiles = normalized; break;
            default:                           _settings.RecentTargets        = normalized; break;
        }
    }

    private static bool RemoveEquivalentPath(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = PathHelper.NormalizeUserPath(path);
        return list.RemoveAll(existing => PathHelper.AreEquivalentUserPaths(existing, normalizedPath)) > 0;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settingsStore = new AppSettingsStore();
            await settingsStore.SaveAsync(_settings);
        }
        catch
        {
            // Ignore settings save errors in viewmodels
        }
    }
}
