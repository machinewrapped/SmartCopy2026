namespace SmartCopy.Benchmarks;

/// <summary>
/// Entry point for <c>--mode validation</c>. Creates the <see cref="SessionPaths"/>, constructs
/// a <see cref="ValidationPass"/>, and runs it to completion. Mirrors <see cref="BenchmarkModeRunner"/>
/// but with the validation pass (depth-first, fail-fast) instead of the benchmark pass
/// (broad-first, all-scenarios-interleaved).
/// </summary>
internal static class ValidationModeRunner
{
    private const string JournalDirectoryName = "benchmark-journals";

    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var paths = new SessionPaths(
            ArtifactDirectory: artifactDirectory,
            ResultsPath: Path.Combine(artifactDirectory, fileNames.Results),
            FileResultsPath: Path.Combine(artifactDirectory, fileNames.FileResults),
            TaskListPath: Path.Combine(artifactDirectory, fileNames.TaskList),
            JournalDirectory: Path.Combine(artifactDirectory, JournalDirectoryName));

        Directory.CreateDirectory(artifactDirectory);

        var pass = new ValidationPass(workingDirectory, config, selection, paths, ct);
        await pass.ExecuteAsync();
    }
}
