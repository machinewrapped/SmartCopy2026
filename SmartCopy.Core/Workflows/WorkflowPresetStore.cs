using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace SmartCopy.Core.Workflows;

public sealed class WorkflowPresetStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public WorkflowPresetStore(string directory)
    {
        _directory = directory;
    }

    public async Task<IReadOnlyList<WorkflowPreset>> GetUserPresetsAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = _directory;
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
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        var directory = _directory;
        Directory.CreateDirectory(directory);
        var fileName = $"{ToSafeId(name)}.sc2workflow";
        var filePath = Path.Combine(directory, fileName);

        var updatedConfig = config with { Name = name };
        var json = JsonSerializer.Serialize(updatedConfig, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public async Task DeleteUserPresetAsync(
        string name,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var directory = _directory;
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

        var presets = await GetUserPresetsAsync(ct);
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

        var directory = _directory;
        var presets = await GetUserPresetsAsync(ct);
        var existing = presets.FirstOrDefault(p =>
            string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        // Save the new configuration. This will either create a new file or overwrite an existing one.
        await SaveUserPresetAsync(newName, existing.Config, ct);

        // If the rename resulted in a new file path, delete the old one.
        var oldPath = Path.Combine(directory, $"{existing.Id}.sc2workflow");
        var newPath = Path.Combine(directory, $"{ToSafeId(newName)}.sc2workflow");
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }
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
