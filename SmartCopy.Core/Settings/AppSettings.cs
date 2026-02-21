using System.Collections.Generic;

namespace SmartCopy.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? LastSourcePath { get; set; }
    public bool IncludeHidden { get; set; }
    public bool ShowFilteredFiles { get; set; }
    public bool AutoSelectOnSelectionRestore { get; set; } = true;
    public bool AllowOverwrite { get; set; }
    public bool AllowDeleteReadOnly { get; set; }
    public bool LazyExpandScan { get; set; }
    public bool FullPreScan { get; set; }
    public bool EnableFilesystemWatcher { get; set; } = true;
    public int CopyChunkSizeKb { get; set; } = 256;
    public string DefaultOverwriteMode { get; set; } = "IfNewer";
    public string DefaultDeleteMode { get; set; } = "Trash";
    public int LogRetentionDays { get; set; } = 30;
    public List<string> RecentSources { get; set; } = [];
    public List<string> RecentTargets { get; set; } = [];
    public List<string> FavouritePaths { get; set; } = [];
    public List<string> RecentFilterChains { get; set; } = [];
    public List<string> RecentPipelines { get; set; } = [];
}

