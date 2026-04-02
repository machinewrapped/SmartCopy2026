using System.IO.Compression;
using System.Text.Json.Nodes;

namespace SmartCopy.Core.Settings;

/// <summary>
/// Exports and imports a portable .sc2backup archive containing the user's presets and
/// portable settings. Logs, crash reports, session state, and window geometry are excluded.
/// </summary>
public sealed class SettingsArchiveService
{
    /// <summary>File names (relative to base directory) excluded from the archive.</summary>
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.sc2session",
        "window.json",
    };

    /// <summary>Subdirectory names excluded from the archive.</summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Logs",
        "Crash Reports",
    };

    /// <summary>
    /// AppSettings JSON property names that are machine-specific or transient and must not
    /// be exported. On import these keys are preserved from the existing settings.json.
    /// </summary>
    private static readonly HashSet<string> ExcludedSettingsProperties = new(StringComparer.Ordinal)
    {
        "RecentSources",
        "RecentTargets",
        "RecentSelectionFiles",
        "RecentFilterChains",
        "RecentPipelines",
        "FilterTypeMruPresetIds",
        "StepTypeMruPresetIds",
        "EnableMemoryFileSystem",
        "AddArtificialDelay",
        "LimitMemoryFileSystemCapacity",
    };

    /// <summary>
    /// AppSettings list properties that should be merged additively on import (union)
    /// rather than replaced, keyed by property name.
    /// </summary>
    private static readonly HashSet<string> AdditiveMergeProperties = new(StringComparer.Ordinal)
    {
        "FavouritePaths",
        "FavouriteSelectionFiles",
    };

    /// <summary>
    /// Creates a .sc2backup ZIP archive at <paramref name="archivePath"/> containing all
    /// portable files from <paramref name="baseDirectory"/>.
    /// </summary>
    public async Task ExportAsync(string baseDirectory, string archivePath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        // Write to a temp file first so a failure doesn't corrupt an existing archive.
        // Use the system temp folder so the temp file is never inside baseDirectory.
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in EnumeratePortableFiles(baseDirectory))
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(baseDirectory, file);
                    var entryName = relativePath.Replace('\\', '/');

                    if (Path.GetFileName(file).Equals("settings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var filtered = await FilterSettingsForExportAsync(file, ct);
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(filtered, ct);
                    }
                    else
                    {
                        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }
                }
            }

            File.Move(tempPath, archivePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Inspects the archive without extracting, returning which entries are new vs conflicting
    /// with files already present in <paramref name="baseDirectory"/>.
    /// </summary>
    public Task<ImportManifest> AnalyzeArchiveAsync(string archivePath, string baseDirectory, CancellationToken ct = default)
    {
        var newFiles = new List<string>();
        var conflicting = new List<string>();

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            // Skip directory entries
            if (entry.FullName.EndsWith('/')) continue;

            var localPath = Path.Combine(baseDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
                conflicting.Add(entry.FullName);
            else
                newFiles.Add(entry.FullName);
        }

        return Task.FromResult(new ImportManifest
        {
            NewFiles = newFiles,
            ConflictingFiles = conflicting,
            TotalEntries = newFiles.Count + conflicting.Count,
        });
    }

    /// <summary>
    /// Extracts the archive into <paramref name="baseDirectory"/>, merging settings.json
    /// (preserving excluded/machine-specific properties) and respecting
    /// <paramref name="resolution"/> for preset file conflicts.
    /// </summary>
    public async Task ImportAsync(
        string archivePath,
        string baseDirectory,
        ConflictResolution resolution,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(baseDirectory);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;

            var localPath = Path.Combine(baseDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            var isSettings = entry.FullName.Equals("settings.json", StringComparison.OrdinalIgnoreCase);

            if (!isSettings && resolution == ConflictResolution.SkipExisting && File.Exists(localPath))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            if (isSettings)
            {
                await MergeSettingsAsync(entry, localPath, ct);
            }
            else
            {
                entry.ExtractToFile(localPath, overwrite: true);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumeratePortableFiles(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(baseDirectory))
        {
            var name = Path.GetFileName(file);
            if (!ExcludedFiles.Contains(name))
                yield return file;
        }

        foreach (var dir in Directory.EnumerateDirectories(baseDirectory))
        {
            var dirName = Path.GetFileName(dir);
            if (ExcludedDirectories.Contains(dirName)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                yield return file;
        }
    }

    private static async Task<byte[]> FilterSettingsForExportAsync(string settingsPath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(settingsPath, ct);
        if (JsonNode.Parse(json) is not JsonObject obj)
            return System.Text.Encoding.UTF8.GetBytes(json);

        foreach (var key in ExcludedSettingsProperties)
            obj.Remove(key);

        return System.Text.Encoding.UTF8.GetBytes(obj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task MergeSettingsAsync(ZipArchiveEntry archiveEntry, string localPath, CancellationToken ct)
    {
        // Read archive settings
        using var entryStream = archiveEntry.Open();
        using var reader = new StreamReader(entryStream);
        var archiveJson = await reader.ReadToEndAsync(ct);
        var archiveObj = JsonNode.Parse(archiveJson) as JsonObject ?? new JsonObject();

        // Read existing settings (if any) to preserve excluded properties
        JsonObject existingObj = new();
        if (File.Exists(localPath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(localPath, ct);
                existingObj = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
            }
            catch (Exception) { /* malformed existing file — start fresh */ }
        }

        // Build merged object: start with existing, apply archive portable values
        var merged = existingObj;

        foreach (var (key, archiveValue) in archiveObj)
        {
            if (ExcludedSettingsProperties.Contains(key))
                continue; // preserve existing value (don't touch)

            if (AdditiveMergeProperties.Contains(key))
            {
                // Union the two lists, deduplicating case-insensitively
                var existing = (existingObj[key] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>() ?? [];
                var incoming = (archiveValue as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>() ?? [];
                var union = existing
                    .Concat(incoming)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                merged[key] = new JsonArray(union.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>());
            }
            else
            {
                merged[key] = archiveValue?.DeepClone();
            }
        }

        var output = merged.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(localPath, output, ct);
    }
}
