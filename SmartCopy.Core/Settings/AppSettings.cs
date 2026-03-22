using System.Text.Json.Serialization;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Core.Settings;

public sealed class AppSettings
{
    /// <summary>
    /// The file path this instance was loaded from / should be saved to.
    /// Not persisted to JSON. When null, <see cref="AppSettingsStore.SaveAsync"/> is a no-op.
    /// </summary>
    [JsonIgnore]
    public string? SettingsFilePath { get; set; }

    public int SchemaVersion { get; set; } = 1;
    public string? LastSourcePath { get; set; }
    public bool ShowHiddenFiles { get; set; }
    public bool ShowFilteredNodesInTree { get; set; } = true;
    public bool AllowDeleteReadOnly { get; set; }
    public bool LazyExpandScan { get; set; }
    public bool FullPreScan { get; set; }
    public bool FollowSymlinks { get; set; }
    public bool EnableFilesystemWatcher { get; set; } = true;
    public int CopyChunkSizeKb { get; set; } = 256;
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverwriteMode DefaultOverwriteMode { get; set; } = OverwriteMode.Skip;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeleteMode DefaultDeleteMode { get; set; } = DeleteMode.Trash;

    /// <summary>Reload the last saved workflow on startup.</summary>
    public bool RestoreLastWorkflow { get; set; } = false;
    /// <summary>Restore the last source path on startup (suppressed when <see cref="RestoreLastWorkflow"/> is true).</summary>
    public bool RestoreLastSourcePath { get; set; } = true;
    /// <summary>Skip the mandatory preview confirmation for delete pipelines.</summary>
    public bool AllowDeleteWithoutPreview { get; set; } = false;
    /// <summary>Skip the mandatory preview confirmation for overwrite pipelines.</summary>
    public bool AllowOverwriteWithoutPreview { get; set; } = false;
    /// <summary>Write session.sc2session next to the executable instead of in %APPDATA%.
    /// Lets each portable copy of the app remember its own last-used session.</summary>
    public bool SaveSessionLocally { get; set; } = false;

    /// <summary>Enable the in-memory file system provider for debug/testing.</summary>
    public bool EnableMemoryFileSystem { get; set; } = false;

    /// <summary>When enabled, the app will add artificial delay to the MemoryFileSystemProvider to simulate I/O.</summary>
    public bool AddArtificialDelay { get; set; } = false;

    /// <summary>When enabled, the MemoryFileSystemProvider will have limited capacity.</summary>
    public bool LimitMemoryFileSystemCapacity { get; set; } = false;

    public int LogRetentionDays { get; set; } = 30;
    public List<string> RecentSources { get; set; } = [];
    public List<string> RecentTargets { get; set; } = [];
    public List<string> RecentSelectionFiles { get; set; } = [];
    public List<string> FavouritePaths { get; set; } = [];
    public List<string> FavouriteSelectionFiles { get; set; } = [];
    public List<string> RecentFilterChains { get; set; } = [];
    public List<string> RecentPipelines { get; set; } = [];

    /// <summary>
    /// MRU preset IDs per filter type. Key = FilterType string (e.g. "Extension").
    /// At most 5 entries per type, most-recently-used first.
    /// </summary>
    public Dictionary<string, List<string>> FilterTypeMruPresetIds { get; set; } = [];

    /// <summary>
    /// MRU preset IDs per step type. Key = StepType string (e.g. "Delete").
    /// At most 5 entries per type, most-recently-used first.
    /// </summary>
    public Dictionary<string, List<string>> StepTypeMruPresetIds { get; set; } = [];

    public bool UseAbsolutePathsForSelectionSave { get; set; }
    public bool AutoOpenLogOnRun { get; set; } = true;

    /// <summary>Show full diagnostic output (exception stack traces, etc.) in the log panel.
    /// Useful for capturing details when reporting bugs.
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Copies all persisted properties from <paramref name="saved"/> into this instance,
    /// preserving <see cref="SettingsFilePath"/> which is not serialised to JSON.
    /// Use this after loading a file to update an existing shared instance (e.g. one already
    /// referenced by ViewModels) without replacing the object reference.
    /// </summary>
    public void MergeFrom(AppSettings saved)
    {
        SchemaVersion = saved.SchemaVersion;
        LastSourcePath = saved.LastSourcePath;
        ShowHiddenFiles = saved.ShowHiddenFiles;
        ShowFilteredNodesInTree = saved.ShowFilteredNodesInTree;
        AllowDeleteReadOnly = saved.AllowDeleteReadOnly;
        LazyExpandScan = saved.LazyExpandScan;
        FullPreScan = saved.FullPreScan;
        FollowSymlinks = saved.FollowSymlinks;
        EnableFilesystemWatcher = saved.EnableFilesystemWatcher;
        CopyChunkSizeKb = saved.CopyChunkSizeKb;
        DefaultOverwriteMode = saved.DefaultOverwriteMode;
        DefaultDeleteMode = saved.DefaultDeleteMode;
        RestoreLastWorkflow = saved.RestoreLastWorkflow;
        RestoreLastSourcePath = saved.RestoreLastSourcePath;
        AllowDeleteWithoutPreview = saved.AllowDeleteWithoutPreview;
        AllowOverwriteWithoutPreview = saved.AllowOverwriteWithoutPreview;
        SaveSessionLocally = saved.SaveSessionLocally;
        EnableMemoryFileSystem = saved.EnableMemoryFileSystem;
        AddArtificialDelay = saved.AddArtificialDelay;
        LimitMemoryFileSystemCapacity = saved.LimitMemoryFileSystemCapacity;
        LogRetentionDays = saved.LogRetentionDays;
        RecentSources = saved.RecentSources;
        RecentTargets = saved.RecentTargets;
        RecentSelectionFiles = saved.RecentSelectionFiles;
        FavouritePaths = saved.FavouritePaths;
        FavouriteSelectionFiles = saved.FavouriteSelectionFiles;
        RecentFilterChains = saved.RecentFilterChains;
        RecentPipelines = saved.RecentPipelines;
        FilterTypeMruPresetIds = saved.FilterTypeMruPresetIds;
        StepTypeMruPresetIds = saved.StepTypeMruPresetIds;
        UseAbsolutePathsForSelectionSave = saved.UseAbsolutePathsForSelectionSave;
        AutoOpenLogOnRun = saved.AutoOpenLogOnRun;
        VerboseLogging = saved.VerboseLogging;
    }
}

