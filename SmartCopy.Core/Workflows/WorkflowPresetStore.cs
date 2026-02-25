using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Workflows;

public sealed class WorkflowPresetStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<IReadOnlyList<WorkflowPreset>> GetUserPresetsAsync(
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var presets = new List<WorkflowPreset>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.sc2workflow", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var config = JsonSerializer.Deserialize<WorkflowConfig>(json, _jsonOptions);
                if (config is null)
                {
                    continue;
                }

                presets.Add(new WorkflowPreset
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = config.Name,
                    Config = config,
                });
            }
            catch (JsonException ex)                 { Debug.WriteLine($"[WorkflowPresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (IOException ex)                   { Debug.WriteLine($"[WorkflowPresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (UnauthorizedAccessException ex)   { Debug.WriteLine($"[WorkflowPresetStore] Skipping preset '{file}': {ex.Message}"); }
        }

        return [.. presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task SaveUserPresetAsync(
        string name,
        WorkflowConfig config,
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
        var fileName = $"{ToSafeId(name)}.sc2workflow";
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

        var directPath = Path.Combine(directory, $"{ToSafeId(name)}.sc2workflow");
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
            var path = Path.Combine(directory, $"{matching.Id}.sc2workflow");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public async Task RenameUserPresetAsync(
        string oldName,
        string newName,
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(oldName))
        {
            throw new ArgumentException("Old name must not be empty.", nameof(oldName));
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New name must not be empty.", nameof(newName));
        }

        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
        var presets = await GetUserPresetsAsync(directory, ct);
        var existing = presets.FirstOrDefault(p =>
            string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        await DeleteUserPresetAsync(oldName, directory, ct);
        await SaveUserPresetAsync(newName, existing.Config, directory, ct);
    }

    public static string GetDefaultPresetDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCopy2026",
                "workflows");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "SmartCopy2026", "workflows");
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
