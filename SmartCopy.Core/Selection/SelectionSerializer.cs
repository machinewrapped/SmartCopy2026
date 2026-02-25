using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Selection;

public sealed class SelectionSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public Task SaveTxtAsync(string path, SelectionSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = snapshot.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        return File.WriteAllLinesAsync(path, lines, ct);
    }

    public async Task<SelectionSnapshot> LoadTxtAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = await File.ReadAllLinesAsync(path, ct);
        return new SelectionSnapshot(lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(NormalizePath));
    }

    public async Task SaveM3uAsync(string path, SelectionSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = new List<string> { "#EXTM3U" };
        lines.AddRange(snapshot.Paths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(NormalizePath));
        await File.WriteAllLinesAsync(path, lines, ct);
    }

    public async Task<SelectionSnapshot> LoadM3uAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = await File.ReadAllLinesAsync(path, ct);
        return new SelectionSnapshot(lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))
            .Select(NormalizePath));
    }

    public async Task SaveSc2SelAsync(string path, SelectionSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var payload = new SelectionPayload
        {
            SchemaVersion = 1,
            Paths = snapshot.Paths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(NormalizePath)
                .ToList(),
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<SelectionSnapshot> LoadSc2SelAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var json = await File.ReadAllTextAsync(path, ct);
        var payload = JsonSerializer.Deserialize<SelectionPayload>(json) ?? new SelectionPayload();
        return new SelectionSnapshot(payload.Paths.Select(NormalizePath));
    }

    public async Task SaveM3u8Async(string path, SelectionSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = new List<string> { "#EXTM3U" };
        lines.AddRange(snapshot.Paths
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(NormalizePath));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, ct);
    }

    public async Task<SelectionSnapshot> LoadM3u8Async(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, ct);
        return new SelectionSnapshot(lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))
            .Select(NormalizePath));
    }

    /// <summary>Unified saver: routes by extension (.m3u, .m3u8, .sc2sel, else .txt).</summary>
    public Task SaveAsync(string path, SelectionSnapshot snapshot, CancellationToken ct = default)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".m3u"    => SaveM3uAsync(path, snapshot, ct),
            ".m3u8"   => SaveM3u8Async(path, snapshot, ct),
            ".sc2sel" => SaveSc2SelAsync(path, snapshot, ct),
            _         => SaveTxtAsync(path, snapshot, ct),
        };
    }

    /// <summary>Unified loader: routes by extension (.m3u, .m3u8, .sc2sel, else .txt).</summary>
    public Task<SelectionSnapshot> LoadAsync(string path, CancellationToken ct = default)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".m3u"    => LoadM3uAsync(path, ct),
            ".m3u8"   => LoadM3u8Async(path, ct),
            ".sc2sel" => LoadSc2SelAsync(path, ct),
            _         => LoadTxtAsync(path, ct),
        };
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private sealed class SelectionPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> Paths { get; set; } = [];
    }
}

