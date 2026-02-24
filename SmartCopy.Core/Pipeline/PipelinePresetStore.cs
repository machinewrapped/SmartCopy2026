using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline;

public sealed class PipelinePresetStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public static Task<IReadOnlyList<PipelinePreset>> GetStandardPresetsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PipelinePreset>>(
        [
            BuildStandardPreset(
                "copy_only",
                "Copy only",
                [new TransformStepConfig(StepKind.Copy, new JsonObject { ["destinationPath"] = "/mem/Target" })]),
            BuildStandardPreset(
                "move_only",
                "Move only",
                [new TransformStepConfig(StepKind.Move, new JsonObject { ["destinationPath"] = "/mem/Target" })]),
            BuildStandardPreset(
                "delete_to_trash",
                "Delete to Trash",
                [new TransformStepConfig(StepKind.Delete, new JsonObject { ["deleteMode"] = DeleteMode.Trash.ToString() })]),
            BuildStandardPreset(
                "flatten_copy",
                "Flatten -> Copy",
                [
                    new TransformStepConfig(StepKind.Flatten, []),
                    new TransformStepConfig(StepKind.Copy, new JsonObject { ["destinationPath"] = "/mem/Target" }),
                ]),
        ]);
    }

    public async Task<IReadOnlyList<PipelinePreset>> GetUserPresetsAsync(
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var presets = new List<PipelinePreset>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.sc2pipe", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var config = JsonSerializer.Deserialize<PipelineConfig>(json, _jsonOptions);
                if (config is null)
                {
                    continue;
                }

                presets.Add(new PipelinePreset
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = config.Name,
                    IsBuiltIn = false,
                    Config = config,
                });
            }
            catch (JsonException ex)            { Debug.WriteLine($"[PipelinePresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (IOException ex)              { Debug.WriteLine($"[PipelinePresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Debug.WriteLine($"[PipelinePresetStore] Skipping preset '{file}': {ex.Message}"); }
        }

        return [.. presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<PipelinePreset>> GetAllPresetsAsync(
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        var standard = await GetStandardPresetsAsync(ct);
        var user = await GetUserPresetsAsync(explicitDirectory, ct);
        return [.. standard, .. user];
    }

    public async Task SaveUserPresetAsync(
        string name,
        PipelineConfig config,
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
        Directory.CreateDirectory(directory);
        var fileName = $"{ToSafeId(name)}.sc2pipe";
        var filePath = Path.Combine(directory, fileName);

        var updatedConfig = config with { Name = name };
        var json = JsonSerializer.Serialize(updatedConfig, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public async Task DeleteUserPresetAsync(
        string name,
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        var directPath = Path.Combine(directory, $"{ToSafeId(name)}.sc2pipe");
        if (File.Exists(directPath))
        {
            File.Delete(directPath);
            return;
        }

        var presets = await GetUserPresetsAsync(directory, ct);
        var matching = presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        if (matching is not null)
        {
            var path = Path.Combine(directory, $"{matching.Id}.sc2pipe");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static string GetDefaultPresetDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCopy2026",
                "pipelines");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "SmartCopy2026", "pipelines");
    }

    private static PipelinePreset BuildStandardPreset(
        string id,
        string name,
        IReadOnlyList<TransformStepConfig> steps)
    {
        return new PipelinePreset
        {
            Id = id,
            Name = name,
            IsBuiltIn = true,
            Config = new PipelineConfig(
                Name: name,
                Description: null,
                Steps: [.. steps],
                OverwriteMode: OverwriteMode.IfNewer.ToString(),
                DeleteMode: DeleteMode.Trash.ToString()),
        };
    }

    private static string ToSafeId(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var mapped = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray();

        return new string(mapped)
            .Replace(' ', '_')
            .Replace("->", "-")
            .Replace("--", "-")
            .Trim('_');
    }
}
