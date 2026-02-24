using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Persists user-defined step presets to disk and merges them with hardcoded built-in presets.
/// Mirrors the pattern from <see cref="SmartCopy.Core.Filters.FilterPresetStore"/>.
/// </summary>
public sealed class StepPresetStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Returns built-in presets (prepended, <see cref="StepPreset.IsBuiltIn"/> = true)
    /// followed by user-saved presets for the given step type.
    /// </summary>
    public async Task<IReadOnlyList<StepPreset>> GetPresetsForTypeAsync(
        string stepType,
        string? explicitPath = null,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(explicitPath, ct);
        var builtIns = GetBuiltInPresets(stepType);

        if (!collection.UserPresets.TryGetValue(stepType, out var userList))
        {
            userList = [];
        }

        return [.. builtIns, .. userList];
    }

    /// <summary>
    /// Saves a user preset for <paramref name="stepType"/>.
    /// Overwrites an existing preset with the same <see cref="StepPreset.Name"/>.
    /// </summary>
    public async Task SaveUserPresetAsync(
        string stepType,
        StepPreset preset,
        string? explicitPath = null,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(explicitPath, ct);

        if (!collection.UserPresets.TryGetValue(stepType, out var list))
        {
            list = [];
            collection.UserPresets[stepType] = list;
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

        await SaveCollectionAsync(collection, explicitPath, ct);
    }

    /// <summary>
    /// Deletes a user preset by Id. No-op if not found.
    /// </summary>
    public async Task DeleteUserPresetAsync(
        string stepType,
        string presetId,
        string? explicitPath = null,
        CancellationToken ct = default)
    {
        var collection = await LoadCollectionAsync(explicitPath, ct);

        if (!collection.UserPresets.TryGetValue(stepType, out var list))
        {
            return;
        }

        list.RemoveAll(p => p.Id == presetId);
        await SaveCollectionAsync(collection, explicitPath, ct);
    }

    public static string GetDefaultPresetPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCopy2026",
                "step-presets.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "SmartCopy2026", "step-presets.json");
    }

    // -------------------------------------------------------------------------
    // Built-in presets
    // -------------------------------------------------------------------------

    private static IReadOnlyList<StepPreset> GetBuiltInPresets(string stepType)
    {
        return stepType switch
        {
            "Delete"  => BuiltInDeletePresets,
            "Flatten" => BuiltInFlattenPresets,
            _ => []
        };
    }

    private static readonly IReadOnlyList<StepPreset> BuiltInDeletePresets =
    [
        MakeBuiltIn("Delete to Trash", StepKind.Delete,
            new JsonObject { ["deleteMode"] = "Trash" }),
        MakeBuiltIn("Delete permanently", StepKind.Delete,
            new JsonObject { ["deleteMode"] = "Permanent" }),
    ];

    private static readonly IReadOnlyList<StepPreset> BuiltInFlattenPresets =
    [
        MakeBuiltIn("Flatten (auto-rename)", StepKind.Flatten,
            new JsonObject { ["conflictStrategy"] = "AutoRenameCounter" }),
    ];

    private static StepPreset MakeBuiltIn(string name, StepKind stepType, JsonObject parameters)
    {
        return new StepPreset
        {
            Id = $"builtin_{stepType.ToString().ToLowerInvariant()}_{name.Replace(' ', '_').ToLowerInvariant()}",
            Name = name,
            IsBuiltIn = true,
            Config = new TransformStepConfig(stepType, parameters),
        };
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    private async Task<StepPresetCollection> LoadCollectionAsync(string? explicitPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = explicitPath ?? GetDefaultPresetPath();
        if (!File.Exists(path))
        {
            return new StepPresetCollection();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var collection = JsonSerializer.Deserialize<StepPresetCollection>(json, _jsonOptions);
            return collection ?? new StepPresetCollection();
        }
        catch (JsonException)
        {
            return new StepPresetCollection();
        }
        catch (IOException)
        {
            return new StepPresetCollection();
        }
        catch (UnauthorizedAccessException)
        {
            return new StepPresetCollection();
        }
    }

    private async Task SaveCollectionAsync(StepPresetCollection collection, string? explicitPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = explicitPath ?? GetDefaultPresetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(collection, _jsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
