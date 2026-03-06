using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace SmartCopy.Core.Filters;

public sealed class FilterChainPresetStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public FilterChainPresetStore(string directory)
    {
        _directory = directory;
    }

    public async Task<IReadOnlyList<FilterChainPreset>> GetUserPresetsAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = _directory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var presets = new List<FilterChainPreset>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.sc2filterchain", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var config = JsonSerializer.Deserialize<FilterChainConfig>(json, _jsonOptions);
                if (config is null)
                {
                    continue;
                }

                presets.Add(new FilterChainPreset
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = config.Name,
                    IsBuiltIn = false,
                    Config = config,
                });
            }
            catch (JsonException ex)            { Debug.WriteLine($"[FilterChainPresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (IOException ex)              { Debug.WriteLine($"[FilterChainPresetStore] Skipping preset '{file}': {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Debug.WriteLine($"[FilterChainPresetStore] Skipping preset '{file}': {ex.Message}"); }
        }

        return [.. presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task SaveUserPresetAsync(
        string name,
        FilterChainConfig config,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        var directory = _directory;
        Directory.CreateDirectory(directory);
        var fileName = $"{ToSafeId(name)}.sc2filterchain";
        var filePath = Path.Combine(directory, fileName);

        var updatedConfig = config with { Name = name };
        var json = JsonSerializer.Serialize(updatedConfig, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task DeleteUserPresetAsync(
        string name,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        var directory = _directory;
        if (!Directory.Exists(directory))
        {
            Debug.WriteLine($"[FilterChainPresetStore] Cannot delete preset '{name}': directory not found at '{directory}'");
            return Task.CompletedTask;
        }

        var directPath = Path.Combine(directory, $"{ToSafeId(name)}.sc2filterchain");
        if (File.Exists(directPath))
        {
            File.Delete(directPath);
        }
        else
        {
            Debug.WriteLine($"[FilterChainPresetStore] Cannot delete preset '{name}': file not found at '{directPath}'");
        }

        return Task.CompletedTask;
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
