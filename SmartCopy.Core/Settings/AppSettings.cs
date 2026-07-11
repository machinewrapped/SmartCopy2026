using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using SmartCopy.Core.FileSystem;
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
    public int CopyChunkSizeKb { get; set; } = OperationalSettings.DefaultCopyBufferSizeBytes / 1024;

    // Performance optimisations — tunable in DEBUG builds, always serialised.
    public CopyOptimisationPlatformPolicy CopyOptimisationPlatformPolicy { get; set; } = new();

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
    public bool WriteOperationJournal { get; set; } = false;

    /// <summary>
    /// Show full diagnostic output (exception stack traces, etc.) in the log panel.
    /// Useful for capturing details when reporting bugs.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    public OperationalSettings CreateOperationalSettings() => CreateOperationalSettings(GetCurrentPlatform());

    internal OperationalSettings CreateOperationalSettings(OSPlatform platform)
    {
        var policy = GetCopyOptimisationPolicy(platform);
        var settings = new OperationalSettings
        {
            CopyBufferSizeBytes = PositiveKbToIntBytesOrDefault(
                CopyChunkSizeKb,
                OperationalSettings.DefaultCopyBufferSizeBytes),

            CopyBufferRouting = new CopyBufferRoutingSettings
            {
                SsdBytes = PositiveKbToIntBytesOrDefault(
                    policy.CopyRoutingSsdBufferKb,
                    CopyBufferRoutingSettings.DefaultSsdBytes),
                UsbBytes = PositiveKbToIntBytesOrDefault(
                    policy.CopyRoutingUsbBufferKb,
                    CopyBufferRoutingSettings.DefaultUsbBytes),
                HddBytes = PositiveKbToIntBytesOrDefault(
                    policy.CopyRoutingHddBufferKb,
                    CopyBufferRoutingSettings.DefaultHddBytes),
                SameVolumeHddBytes = PositiveKbToIntBytesOrDefault(
                    policy.CopyRoutingSameVolumeHddBufferKb,
                    CopyBufferRoutingSettings.DefaultSameVolumeHddBytes),
                UnknownBytes = PositiveKbToIntBytesOrDefault(
                    policy.CopyRoutingUnknownBufferKb,
                    CopyBufferRoutingSettings.DefaultUnknownBytes),
            },
        };

        if (!policy.Enabled)
        {
            return settings.Normalize();
        }

        return (settings with
        {
            TinyFileFastPathThresholdBytes = NonNegativeKbToLongBytesOrDefault(
                policy.TinyFileFastPathKb,
                OperationalSettings.DefaultEnabledTinyFileFastPathThresholdBytes),
            BatchBufferBytes = NonNegativeKbToIntBoundedBytesOrDefault(
                policy.BatchBufferKb,
                OperationalSettings.DefaultEnabledBatchBufferBytes),
            HddSourceBatchTraversalOrder = policy.HddSourceBatchTraversalOrder,
            OtherSourceBatchTraversalOrder = policy.OtherSourceBatchTraversalOrder,
            BatchFlushPolicy = policy.BatchFlushPolicy,
            DestinationRoutingEnabled = true,
        }).Normalize();
    }

    public CopyOptimisationPolicy GetCurrentCopyOptimisationPolicy() => GetCopyOptimisationPolicy(GetCurrentPlatform());

    internal CopyOptimisationPolicy GetCopyOptimisationPolicy(OSPlatform platform) =>
        CopyOptimisationPlatformPolicy?.For(platform) ?? CopyOptimisationPolicy.DisabledDefaults();

    private static OSPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return OSPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OSPlatform.OSX;
        }

        if (OperatingSystem.IsLinux())
        {
            return OSPlatform.Linux;
        }

        return OSPlatform.Create("UNKNOWN");
    }

    private static int PositiveKbToIntBytesOrDefault(int valueKb, int defaultBytes)
    {
        if (valueKb <= 0)
        {
            return defaultBytes;
        }

        var bytes = (long)valueKb * 1024;
        return bytes <= int.MaxValue ? (int)bytes : defaultBytes;
    }

    private static long NonNegativeKbToLongBytesOrDefault(int valueKb, long defaultBytes)
    {
        if (valueKb < 0)
        {
            return defaultBytes;
        }

        return (long)valueKb * 1024;
    }

    private static long NonNegativeKbToIntBoundedBytesOrDefault(int valueKb, long defaultBytes)
    {
        if (valueKb < 0)
        {
            return defaultBytes;
        }

        var bytes = (long)valueKb * 1024;
        return bytes <= int.MaxValue ? bytes : defaultBytes;
    }

    /// <summary>
    /// Copies all persisted properties from <paramref name="saved"/> into this instance,
    /// preserving <see cref="SettingsFilePath"/> which is not serialised to JSON.
    /// Use this after loading a file to update an existing shared instance (e.g. one already
    /// referenced by ViewModels) without replacing the object reference.
    /// </summary>
    public void MergeFrom(AppSettings saved)
    {
        foreach (var prop in typeof(AppSettings).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.IsDefined(typeof(JsonIgnoreAttribute), inherit: false)) continue;
            prop.SetValue(this, prop.GetValue(saved));
        }
    }
}
