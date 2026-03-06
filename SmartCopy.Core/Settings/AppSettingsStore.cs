using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Settings;

public sealed class AppSettingsStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<AppSettings> LoadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return new AppSettings { SettingsFilePath = path };
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            settings.SettingsFilePath = path;
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings { SettingsFilePath = path };
        }
        catch (IOException)
        {
            return new AppSettings { SettingsFilePath = path };
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings { SettingsFilePath = path };
        }
    }

    /// <summary>
    /// Loads persisted settings from <see cref="AppSettings.SettingsFilePath"/> and merges them
    /// into <paramref name="settings"/> in-place via <see cref="AppSettings.MergeFrom"/>.
    /// Use this when other objects already hold a reference to the same <paramref name="settings"/>
    /// instance and replacing it is not practical.
    /// </summary>
    public async Task LoadIntoAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(settings.SettingsFilePath))
            return;

        var saved = await LoadAsync(settings.SettingsFilePath, ct);
        settings.MergeFrom(saved);
    }

    /// <remarks>
    /// If <see cref="AppSettings.SettingsFilePath"/> is <see langword="null"/> or empty the call
    /// is a deliberate no-op — this is the expected behaviour in test contexts where no path is
    /// configured and silently writing to disk would be harmful.
    /// </remarks>
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(settings.SettingsFilePath))
            return;

        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(settings.SettingsFilePath)!);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(settings.SettingsFilePath, json, ct);
    }


}
