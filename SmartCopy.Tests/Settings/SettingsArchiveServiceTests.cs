using System.IO.Compression;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Settings;

public sealed class SettingsArchiveServiceTests
{
    private readonly SettingsArchiveService _service = new();

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_CreatesValidZipWithExpectedEntries()
    {
        using var temp = new TempDirectory();
        var baseDir = temp.Path;
        CreateMinimalAppData(baseDir);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(baseDir, archivePath);

        Assert.True(File.Exists(archivePath));
        using var zip = ZipFile.OpenRead(archivePath);
        var entries = zip.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("settings.json", entries);
        Assert.Contains("filter-presets.json", entries);
        Assert.Contains("Pipelines/my_pipeline.sc2pipe", entries);
    }

    [Fact]
    public async Task Export_StripsExcludedSettingsProperties()
    {
        using var temp = new TempDirectory();
        var baseDir = temp.Path;
        var settingsJson = """
            {
              "ShowHiddenFiles": true,
              "RecentSources": ["/one", "/two"],
              "FavouritePaths": ["/fav"],
              "EnableMemoryFileSystem": true
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(baseDir, "settings.json"), settingsJson);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(baseDir, archivePath);

        using var zip = ZipFile.OpenRead(archivePath);
        var entry = zip.GetEntry("settings.json")!;
        using var reader = new StreamReader(entry.Open());
        var json = await reader.ReadToEndAsync();
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.True(obj.ContainsKey("ShowHiddenFiles"));
        Assert.True(obj.ContainsKey("FavouritePaths"));
        Assert.False(obj.ContainsKey("RecentSources"));
        Assert.False(obj.ContainsKey("EnableMemoryFileSystem"));
    }

    [Fact]
    public async Task Export_ExcludesSessionWindowLogsAndCrashes()
    {
        using var temp = new TempDirectory();
        var baseDir = temp.Path;
        CreateMinimalAppData(baseDir);

        // Add excluded items
        await File.WriteAllTextAsync(Path.Combine(baseDir, "session.sc2session"), "{}");
        await File.WriteAllTextAsync(Path.Combine(baseDir, "window.json"), "{}");
        Directory.CreateDirectory(Path.Combine(baseDir, "Logs"));
        await File.WriteAllTextAsync(Path.Combine(baseDir, "Logs", "op.log"), "log");
        Directory.CreateDirectory(Path.Combine(baseDir, "Crash Reports"));
        await File.WriteAllTextAsync(Path.Combine(baseDir, "Crash Reports", "crash.txt"), "crash");

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(baseDir, archivePath);

        using var zip = ZipFile.OpenRead(archivePath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.DoesNotContain("session.sc2session", entries);
        Assert.DoesNotContain("window.json", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.StartsWith("Crash Reports/", StringComparison.OrdinalIgnoreCase));
    }

    // ── AnalyzeArchive ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeArchive_DetectsConflictsCorrectly()
    {
        using var temp = new TempDirectory();
        var baseDir = Path.Combine(temp.Path, "appdata");
        CreateMinimalAppData(baseDir);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(baseDir, archivePath);

        // Add an extra file to base that is NOT in the archive (won't appear anywhere)
        var extraTarget = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(extraTarget);
        // Copy the existing archive into a fresh target dir that already has settings.json
        await File.WriteAllTextAsync(Path.Combine(extraTarget, "settings.json"), "{}");

        var manifest = await _service.AnalyzeArchiveAsync(archivePath, extraTarget);

        Assert.Contains("settings.json", manifest.ConflictingFiles);
        Assert.Contains("filter-presets.json", manifest.NewFiles);
        Assert.Equal(manifest.TotalEntries, manifest.NewFiles.Count + manifest.ConflictingFiles.Count);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_OverwriteAll_ReplacesExistingFiles()
    {
        using var temp = new TempDirectory();
        var sourceDir = Path.Combine(temp.Path, "source");
        CreateMinimalAppData(sourceDir);
        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(sourceDir, archivePath);

        var targetDir = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(targetDir);
        // Pre-populate target with a different filter-presets.json
        await File.WriteAllTextAsync(Path.Combine(targetDir, "filter-presets.json"), """{"old": true}""");

        await _service.ImportAsync(archivePath, targetDir, ConflictResolution.OverwriteAll);

        var content = await File.ReadAllTextAsync(Path.Combine(targetDir, "filter-presets.json"));
        Assert.DoesNotContain("\"old\"", content);
        Assert.True(File.Exists(Path.Combine(targetDir, "Pipelines", "my_pipeline.sc2pipe")));
    }

    [Fact]
    public async Task Import_SkipExisting_PreservesExistingAddsMissing()
    {
        using var temp = new TempDirectory();
        var sourceDir = Path.Combine(temp.Path, "source");
        CreateMinimalAppData(sourceDir);
        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(sourceDir, archivePath);

        var targetDir = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(targetDir);
        const string originalContent = """{"original": true}""";
        await File.WriteAllTextAsync(Path.Combine(targetDir, "filter-presets.json"), originalContent);

        await _service.ImportAsync(archivePath, targetDir, ConflictResolution.SkipExisting);

        // Existing file should be untouched
        var content = await File.ReadAllTextAsync(Path.Combine(targetDir, "filter-presets.json"));
        Assert.Equal(originalContent, content);
        // New files should be added
        Assert.True(File.Exists(Path.Combine(targetDir, "Pipelines", "my_pipeline.sc2pipe")));
    }

    [Fact]
    public async Task Import_MergesSettingsJson_PreservesExcludedAndUpdatesPortable()
    {
        using var temp = new TempDirectory();

        // Archive has portable settings
        var sourceDir = Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "settings.json"), """
            {
              "ShowHiddenFiles": true,
              "FavouritePaths": ["/archive/path"]
            }
            """);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(sourceDir, archivePath);

        // Target has existing settings with Recent data and a different ShowHiddenFiles
        var targetDir = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "settings.json"), """
            {
              "ShowHiddenFiles": false,
              "RecentSources": ["/local/recent"],
              "FavouritePaths": ["/local/fav"]
            }
            """);

        await _service.ImportAsync(archivePath, targetDir, ConflictResolution.OverwriteAll);

        var merged = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(targetDir, "settings.json")))!.AsObject();

        // Portable setting updated from archive
        Assert.True(merged["ShowHiddenFiles"]!.GetValue<bool>());
        // Excluded property preserved from existing
        var recent = merged["RecentSources"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(["/local/recent"], recent);
    }

    [Fact]
    public async Task Import_MergesFavouritePathsAdditively()
    {
        using var temp = new TempDirectory();

        var sourceDir = Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "settings.json"), """
            {"FavouritePaths": ["/archive/a", "/shared/b"]}
            """);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(sourceDir, archivePath);

        var targetDir = Path.Combine(temp.Path, "target");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "settings.json"), """
            {"FavouritePaths": ["/local/c", "/shared/b"]}
            """);

        await _service.ImportAsync(archivePath, targetDir, ConflictResolution.OverwriteAll);

        var merged = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(targetDir, "settings.json")))!.AsObject();
        var favs = merged["FavouritePaths"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All three unique paths present; /shared/b deduplicated
        Assert.Contains("/local/c", favs);
        Assert.Contains("/archive/a", favs);
        Assert.Contains("/shared/b", favs);
        Assert.Equal(3, favs.Count);
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_RestoresAllPresets()
    {
        using var temp = new TempDirectory();
        var sourceDir = Path.Combine(temp.Path, "source");
        CreateMinimalAppData(sourceDir);

        var archivePath = Path.Combine(temp.Path, "export.sc2backup");
        await _service.ExportAsync(sourceDir, archivePath);

        var targetDir = Path.Combine(temp.Path, "target");
        await _service.ImportAsync(archivePath, targetDir, ConflictResolution.OverwriteAll);

        Assert.True(File.Exists(Path.Combine(targetDir, "filter-presets.json")));
        Assert.True(File.Exists(Path.Combine(targetDir, "step-presets.json")));
        Assert.True(File.Exists(Path.Combine(targetDir, "Pipelines", "my_pipeline.sc2pipe")));
        Assert.True(File.Exists(Path.Combine(targetDir, "FilterChains", "my_chain.sc2filterchain")));
        Assert.True(File.Exists(Path.Combine(targetDir, "Workflows", "my_workflow.sc2workflow")));
    }

    // ── MergeFrom reflection ──────────────────────────────────────────────────

    [Fact]
    public void MergeFrom_CopiesAllSerializableProperties()
    {
        var source = new AppSettings
        {
            ShowHiddenFiles = true,
            CopyChunkSizeKb = 512,
            RecentSources = ["/a"],
            FavouritePaths = ["/fav"],
            DefaultOverwriteMode = OverwriteMode.Always,
        };

        var target = new AppSettings { SettingsFilePath = "/target/path" };
        target.MergeFrom(source);

        Assert.True(target.ShowHiddenFiles);
        Assert.Equal(512, target.CopyChunkSizeKb);
        Assert.Equal(["/a"], target.RecentSources);
        Assert.Equal(["/fav"], target.FavouritePaths);
        Assert.Equal(OverwriteMode.Always, target.DefaultOverwriteMode);
    }

    [Fact]
    public void MergeFrom_PreservesSettingsFilePath()
    {
        var source = new AppSettings { ShowHiddenFiles = true };
        // SettingsFilePath has [JsonIgnore] so MergeFrom must skip it
        source.SettingsFilePath = "/source/path";

        var target = new AppSettings { SettingsFilePath = "/target/path" };
        target.MergeFrom(source);

        Assert.Equal("/target/path", target.SettingsFilePath);
        Assert.True(target.ShowHiddenFiles);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void CreateMinimalAppData(string baseDir)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "settings.json"), """{"ShowHiddenFiles": false}""");
        File.WriteAllText(Path.Combine(baseDir, "filter-presets.json"), """{"SchemaVersion": 1, "UserPresets": {}}""");
        File.WriteAllText(Path.Combine(baseDir, "step-presets.json"), """{"SchemaVersion": 1, "UserPresets": {}}""");

        Directory.CreateDirectory(Path.Combine(baseDir, "Pipelines"));
        File.WriteAllText(Path.Combine(baseDir, "Pipelines", "my_pipeline.sc2pipe"), """{"Name": "My Pipeline"}""");

        Directory.CreateDirectory(Path.Combine(baseDir, "FilterChains"));
        File.WriteAllText(Path.Combine(baseDir, "FilterChains", "my_chain.sc2filterchain"), """{"Name": "My Chain"}""");

        Directory.CreateDirectory(Path.Combine(baseDir, "Workflows"));
        File.WriteAllText(Path.Combine(baseDir, "Workflows", "my_workflow.sc2workflow"), """{"Name": "My Workflow"}""");
    }
}
