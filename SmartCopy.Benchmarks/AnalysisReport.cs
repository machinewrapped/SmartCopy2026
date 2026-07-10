using System.Text;

namespace SmartCopy.Benchmarks;

internal sealed class AnalysisReport
{
    private readonly StringBuilder _content = new();

    public void Write(string? line = null)
    {
        var text = line ?? string.Empty;
        Console.WriteLine(text);
        _content.AppendLine(text);
    }

    public async Task FlushAsync(string artifactDirectory, string outputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(outputPath, _content.ToString(), ct);
    }
}
