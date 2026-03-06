using System;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Core.Progress;

public sealed class OperationJournal
{
    public OperationJournal(string logDirectory)
    {
        LogDirectory = logDirectory;
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
            var source = SanitizeField(result.SourceNode.CanonicalRelativePath ?? string.Empty);
            var destination = SanitizeField(result.DestinationPath ?? string.Empty);
            var status = result.IsSuccess ? "ok" : "failed";
            var outputSize = FileSizeFormatter.FormatBytes(result.OutputBytes);

            await writer.WriteLineAsync(
                $"{DateTime.UtcNow:O}\t{status}\t{action}\t{source}\t{destination}\t{outputSize}");
        }

        await writer.FlushAsync(ct);
        return path;
    }

    private static string SanitizeField(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string MapAction(TransformResult result) => result.SourceNodeResult switch
    {
        SourceResult.Copied  => "copy",
        SourceResult.Moved   => "move",
        SourceResult.Trashed => "trash",
        SourceResult.Deleted => "delete",
        _                    => "skipped",
    };
}
