using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var lines = snapshot.RelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
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
        lines.AddRange(snapshot.RelativePaths
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
            RelativePaths = snapshot.RelativePaths
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
        return new SelectionSnapshot(payload.RelativePaths.Select(NormalizePath));
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private sealed class SelectionPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> RelativePaths { get; set; } = [];
    }
}

