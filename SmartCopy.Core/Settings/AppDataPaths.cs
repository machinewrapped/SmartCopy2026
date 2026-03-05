namespace SmartCopy.Core.Settings;

/// <summary>
/// Single authority for all app-data file and directory paths.
/// Construct via <see cref="ForCurrentUser"/> at the composition root;
/// inject the instance wherever paths are needed.
/// Tests construct <c>new AppDataPaths(tempDir)</c> — no static oracle exists.
/// </summary>
public sealed class AppDataPaths
{
    public AppDataPaths(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        Settings      = Path.Combine(baseDirectory, "settings.json");
        Session       = Path.Combine(baseDirectory, "session.sc2session");
        FilterPresets = Path.Combine(baseDirectory, "filter-presets.json");
        StepPresets   = Path.Combine(baseDirectory, "step-presets.json");
        FilterChains  = Path.Combine(baseDirectory, "filterchains");
        Pipelines     = Path.Combine(baseDirectory, "pipelines");
        Workflows     = Path.Combine(baseDirectory, "workflows");
        Logs          = Path.Combine(baseDirectory, "logs");
    }

    public string BaseDirectory { get; }
    public string Settings      { get; }
    public string Session       { get; }
    public string FilterPresets { get; }
    public string StepPresets   { get; }
    public string FilterChains  { get; }
    public string Pipelines     { get; }
    public string Workflows     { get; }
    public string Logs          { get; }

    /// <summary>
    /// Deliberate factory — only call this at the composition root (<c>MainViewModel</c>).
    /// Tests should use <c>new AppDataPaths(tempDir)</c> instead.
    /// </summary>
    public static AppDataPaths ForCurrentUser()
    {
        var baseDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartCopy2026")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "SmartCopy2026");

        return new AppDataPaths(baseDir);
    }
}
