using System.Collections.Generic;

namespace SmartCopy.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? LastSourcePath { get; set; }
    public bool IncludeHidden { get; set; }
    public bool ShowFilteredFiles { get; set; }
    public bool ShowFilteredNodesInTree { get; set; } = true;
    public bool AutoSelectOnSelectionRestore { get; set; } = true;
    public bool AllowOverwrite { get; set; }
    public bool AllowDeleteReadOnly { get; set; }
    public bool LazyExpandScan { get; set; }
    public bool FullPreScan { get; set; }
    public bool EnableFilesystemWatcher { get; set; } = true;
    public int CopyChunkSizeKb { get; set; } = 256;
    public string DefaultOverwriteMode { get; set; } = "Skip";
    public string DefaultDeleteMode { get; set; } = "Trash";

    /// <summary>Reload the last saved workflow on startup.</summary>
    public bool RestoreLastWorkflow { get; set; } = false;
    /// <summary>Restore the last source path on startup (suppressed when <see cref="RestoreLastWorkflow"/> is true).</summary>
    public bool RestoreLastSourcePath { get; set; } = true;
    /// <summary>Skip the mandatory preview confirmation for delete/destructive pipelines.</summary>
    public bool DisableDestructivePreview { get; set; } = false;
    /// <summary>Send deleted files to the recycle bin when the platform supports it.</summary>
    public bool DeleteToRecycleBin { get; set; } = true;
    public int LogRetentionDays { get; set; } = 30;
    public List<string> RecentSources { get; set; } = [];
    public List<string> RecentTargets { get; set; } = [];
    public List<string> FavouritePaths { get; set; } = [];
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
}

