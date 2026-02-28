using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Core.Progress;

public sealed class OperationJournal
{
    public OperationJournal(string? explicitDirectory = null)
    {
        LogDirectory = explicitDirectory ?? GetDefaultLogDirectory();
    }

    public string LogDirectory { get; }

    public async Task RotateAsync(int retentionDays, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (retentionDays <= 0 || !Directory.Exists(LogDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(LogDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        await Task.CompletedTask;
    }

    public async Task<string> WriteAsync(
        IEnumerable<TransformResult> results,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(LogDirectory);

        var timestamp = DateTime.UtcNow;
        var path = Path.Combine(LogDirectory, $"operation-{timestamp:yyyyMMdd-HHmmss-fff}.log");

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);

        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();
            var action = MapAction(result);
            var source = result.SourcePath;
            var destination = result.DestinationPath ?? string.Empty;
            var status = result.IsSuccess ? "ok" : "failed";

            await writer.WriteLineAsync(
                $"{DateTime.UtcNow:O}\t{status}\t{action}\t{source}\t{destination}\t{result.OutputBytes}");
        }

        await writer.FlushAsync(ct);
        return path;
    }

    public static string GetDefaultLogDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCopy2026",
                "logs");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "SmartCopy2026", "logs");
    }

    private static string MapAction(TransformResult result) => result.SourcePathResult switch
    {
        SourcePathResult.Copied  => "copy",
        SourcePathResult.Moved   => "move",
        SourcePathResult.Trashed => "trash",
        SourcePathResult.Deleted => "delete",
        _                        => "skipped",
    };
}
