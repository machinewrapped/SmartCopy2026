using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Filters;

public sealed class FilterChainPresetStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<IReadOnlyList<FilterChainPreset>> GetUserPresetsAsync(
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
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
        var fileName = $"{ToSafeId(name)}.sc2filterchain";
        var filePath = Path.Combine(directory, fileName);

        var updatedConfig = config with { Name = name };
        var json = JsonSerializer.Serialize(updatedConfig, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task DeleteUserPresetAsync(
        string name,
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        var directory = explicitDirectory ?? GetDefaultPresetDirectory();
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

    public static string GetDefaultPresetDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCopy2026",
                "filterchains");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "SmartCopy2026", "filterchains");
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
