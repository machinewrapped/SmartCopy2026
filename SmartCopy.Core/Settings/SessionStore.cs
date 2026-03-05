using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Workflows;

namespace SmartCopy.Core.Settings;

/// <summary>
/// Persists a session snapshot (source path + filter chain + pipeline) so the app can
/// restore the user's last state on startup when <c>RestoreLastWorkflow</c> is enabled.
/// The snapshot lives alongside <c>settings.json</c> in the app settings directory —
/// it is app state, not a user-managed preset.
/// </summary>
public sealed class SessionStore
{
    private const string SessionFileName = "session.sc2session";

    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Writes the current session state to <c>session.sc2session</c>.
    /// Creates the settings directory if it doesn't exist yet.
    /// </summary>
    public async Task SaveAsync(
        WorkflowConfig config,
        string path,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Loads the session snapshot, or returns <c>null</c> if none exists or it is unreadable.
    /// </summary>
    public async Task<WorkflowConfig?> LoadAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<WorkflowConfig>(json, _jsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[SessionStore] Failed to load session: {ex.Message}");
            return null;
        }
    }
}
