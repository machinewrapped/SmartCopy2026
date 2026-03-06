using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.Core.Filters;

/// <summary>
/// Persists user-defined filter presets to disk and merges them with hardcoded built-in presets.
/// Mirrors the pattern from <see cref="SmartCopy.Core.Settings.AppSettingsStore"/>.
/// </summary>
public sealed class FilterPresetStore
{
    private readonly string _presetPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public FilterPresetStore(string presetPath)
    {
        _presetPath = presetPath;
    }

    /// <summary>
    /// Returns built-in presets (prepended, <see cref="FilterPreset.IsBuiltIn"/> = true)
    /// followed by user-saved presets for the given filter type.
    /// </summary>
    public async Task<IReadOnlyList<FilterPreset>> GetPresetsForTypeAsync(
        string filterType,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(ct);
        var builtIns = GetBuiltInPresets(filterType);

        if (!collection.UserPresets.TryGetValue(filterType, out var userList))
        {
            userList = [];
        }

        return [.. builtIns, .. userList];
    }

    /// <summary>
    /// Saves a user preset for <paramref name="filterType"/>.
    /// Overwrites an existing preset with the same <see cref="FilterPreset.Name"/>.
    /// </summary>
    public async Task SaveUserPresetAsync(
        string filterType,
        FilterPreset preset,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(ct);

        if (!collection.UserPresets.TryGetValue(filterType, out var list))
        {
            list = [];
            collection.UserPresets[filterType] = list;
        }

        var existing = list.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            list[existing] = preset;
        }
        else
        {
            list.Add(preset);
        }

        await SaveCollectionAsync(collection, ct);
    }

    /// <summary>
    /// Deletes a user preset by Id. No-op if not found.
    /// </summary>
    public async Task DeleteUserPresetAsync(
        string filterType,
        string presetId,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(ct);

        if (!collection.UserPresets.TryGetValue(filterType, out var list))
        {
            return;
        }

        list.RemoveAll(p => p.Id == presetId);
        await SaveCollectionAsync(collection, ct);
    }

    // -------------------------------------------------------------------------
    // Built-in presets
    // -------------------------------------------------------------------------

    private static IReadOnlyList<FilterPreset> GetBuiltInPresets(string filterType)
    {
        return filterType switch
        {
            "Extension" => BuiltInExtensionPresets,
            "Wildcard"  => BuiltInWildcardPresets,
            _ => []
        };
    }

    private static readonly IReadOnlyList<FilterPreset> BuiltInExtensionPresets =
    [
        MakeBuiltIn("Only Audio files",  "Extension", FilterMode.Only,
            new JsonObject { ["extensions"] = "mp3;flac;aac;ogg;wav;m4a" }),
        MakeBuiltIn("Only Images",       "Extension", FilterMode.Only,
            new JsonObject { ["extensions"] = "jpg;jpeg;png;gif;webp;bmp;tiff;svg" }),
        MakeBuiltIn("Only Documents",    "Extension", FilterMode.Only,
            new JsonObject { ["extensions"] = "pdf;docx;xlsx;pptx;txt;odt" }),
        MakeBuiltIn("Only Log files",    "Extension", FilterMode.Only,
            new JsonObject { ["extensions"] = "log;txt" }),
    ];

    private static readonly IReadOnlyList<FilterPreset> BuiltInWildcardPresets =
    [
        MakeBuiltIn("Exclude Temp files", "Wildcard", FilterMode.Exclude,
            new JsonObject { ["pattern"] = "*.tmp;*.bak;~*;Thumbs.db" }),
    ];

    private static FilterPreset MakeBuiltIn(string name, string filterType, FilterMode mode, JsonObject parameters)
    {
        return new FilterPreset
        {
            Id = $"builtin_{filterType}_{name.Replace(' ', '_').ToLowerInvariant()}",
            Name = name,
            IsBuiltIn = true,
            Config = new FilterConfig(filterType, true, mode.ToString(), parameters, name),
        };
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    private async Task<FilterPresetCollection> LoadCollectionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_presetPath))
        {
            return new FilterPresetCollection();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_presetPath, ct);
            var collection = JsonSerializer.Deserialize<FilterPresetCollection>(json, _jsonOptions);
            return collection ?? new FilterPresetCollection();
        }
        catch (JsonException)
        {
            return new FilterPresetCollection();
        }
        catch (IOException)
        {
            return new FilterPresetCollection();
        }
        catch (UnauthorizedAccessException)
        {
            return new FilterPresetCollection();
        }
    }

    private async Task SaveCollectionAsync(FilterPresetCollection collection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_presetPath)!);
        var json = JsonSerializer.Serialize(collection, _jsonOptions);
        await File.WriteAllTextAsync(_presetPath, json, ct);
    }
}
