using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SmartCopy.Benchmarks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Scanning;
using SmartCopy.Core.Selection;

const string JournalDirectoryName = "benchmark-journals";

var ct = CancellationToken.None;
var workingDirectory = Directory.GetCurrentDirectory();
var selection = BenchmarkCliOptions.Parse(args);
var configPath = Path.IsPathFullyQualified(selection.ConfigPath)
    ? selection.ConfigPath
    : Path.Combine(workingDirectory, selection.ConfigPath);

if (!File.Exists(configPath))
{
    var template = BenchmarkConfig.CreateTemplate();
    await BenchmarkJson.WriteAsync(configPath, template, ct);
    Console.WriteLine($"Created {Path.GetFileName(configPath)} in {Path.GetDirectoryName(configPath) ?? workingDirectory}.");
    Console.WriteLine("Edit the scenario file if needed, then run the benchmark app again.");
    return 0;
}

var config = await BenchmarkJson.ReadAsync<BenchmarkConfig>(configPath, ct)
    ?? throw new InvalidOperationException($"Could not read {configPath}.");
config.Normalize();

if (selection.Mode == BenchmarkRunMode.DatasetPreparation)
{
    await RunDatasetPreparationModeAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.Analysis)
{
    await RunAnalysisModeAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.SizeScaling)
{
    await RunSizeScalingModeAsync(workingDirectory, config, selection, ct);
    return 0;
}

await RunBenchmarkModeAsync(workingDirectory, config, selection, ct);

return 0;

static async Task RunDatasetPreparationModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var preparation = config.DatasetPreparation
        ?? throw new InvalidOperationException("benchmark-scenarios.json does not define datasetPreparation.");
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, preparation.SourcePath, config.ArtifactPath);

    Console.WriteLine("Mode:     dataset-prep");
    Console.WriteLine($"Source:   {preparation.SourcePath}");
    Console.WriteLine($"Dataset:  {preparation.DestinationPath}");
    Console.WriteLine($"Artifacts:{artifactDirectory}");

    var service = new DatasetPreparationService();
    DatasetPreparationRunSummary summary;

    using (var progress = new ThrottledConsoleProgress<DatasetPreparationProgress>(p =>
        UpdateProgress($"Prep: Scanned={p.TotalFilesScanned}, Imported={p.TotalFilesImported} ({FormatSize(p.TotalBytesImported)}), File={p.CurrentFile}")))
    {
        summary = await service.RunAsync(preparation, artifactDirectory, config.IncludeHidden, selection.Notes, progress, ct);
    }
    Console.WriteLine(); // Finalize progress line

    Console.WriteLine($"Imported files: {summary.ImportedFileCount}, bytes: {summary.ImportedTotalBytes}.");
    Console.WriteLine($"Skipped duplicates: {summary.DuplicateSourceSkips}, skipped conflicts: {summary.ExistingDestinationSkips}.");
    Console.WriteLine($"Summary:  {summary.SummaryPath}");

    foreach (var bucket in summary.Buckets)
    {
        var status = bucket.IsFull ? "full" : "underfilled";
        Console.WriteLine(
            $"Bucket {bucket.BucketName}: +{bucket.AddedFileCount} files / +{bucket.AddedTotalBytes} bytes, " +
            $"now {bucket.AfterFileCount} files / {bucket.AfterTotalBytes} bytes ({status}).");
    }

    int totalRunsNeeded = config.Scenarios.Count(s => s.Enabled && s.UsePathPool)
        * config.Variants.Where(v => v.Enabled).Sum(v => v.DesiredRunCount);
    if (totalRunsNeeded > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Duplicating dataset {totalRunsNeeded} times for caching resistance...");
        var baseDest = preparation.DestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 1; i <= totalRunsNeeded; i++)
        {
            var targetPath = $"{baseDest}_{i:D2}";
            Console.WriteLine($"[{i:D2}/{totalRunsNeeded:D2}] Copying base dataset to {targetPath}...");
            DuplicateDirectory(baseDest, targetPath);
        }
        Console.WriteLine("Dataset duplication complete.");
    }
}

static async Task RunAnalysisModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
    var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
    var analysisPath = Path.Combine(artifactDirectory, fileNames.Analysis);
    var reportBuilder = new StringBuilder();

    void Report(string? line = null)
    {
        var text = line ?? string.Empty;
        Console.WriteLine(text);
        reportBuilder.AppendLine(text);
    }

    async Task FlushReportAsync()
    {
        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(analysisPath, reportBuilder.ToString(), ct);
    }

    if (!File.Exists(fileResultsPath))
    {
        Report($"No file-level results found: {fileResultsPath}");
        Report("Run benchmark mode first to produce benchmark-file-results.ndjson.");
        await FlushReportAsync();
        return;
    }

    var allRecords = await ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileResultsPath, ct);
    if (allRecords.Count == 0)
    {
        Report($"No records available in {fileResultsPath}.");
        await FlushReportAsync();
        return;
    }

    var scenarioOrder = BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
    var scenariosToAnalyze = !string.IsNullOrWhiteSpace(selection.ScenarioName)
        ? [selection.ScenarioName.Trim()]
        : scenarioOrder;

    if (scenariosToAnalyze.Count == 0)
    {
        scenariosToAnalyze = allRecords
            .Select(r => r.ScenarioName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    var scenarioSet = new HashSet<string>(scenariosToAnalyze, StringComparer.OrdinalIgnoreCase);
    var filteredRecords = allRecords
        .Where(r => scenarioSet.Contains(r.ScenarioName))
        .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                    string.Equals(r.VariantName, selection.VariantName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (filteredRecords.Count == 0)
    {
        if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
        {
            Report($"No file-level records found for scenario '{selection.ScenarioName.Trim()}'.");
        }
        else
        {
            Report("No file-level records found for the selected scenarios.");
        }

        if (!string.IsNullOrWhiteSpace(selection.VariantName))
        {
            Report($"Variant filter: '{selection.VariantName}'.");
        }

        await FlushReportAsync();
        return;
    }

    var allVariants = filteredRecords
        .Select(r => r.VariantName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Report("## Analysis Summary");
    Report($"- **Mode:** `analysis`");
    Report($"- **Source:** `{Path.GetFullPath(config.SourcePath)}`");
    Report($"- **Scenario filter:** `{(string.IsNullOrWhiteSpace(selection.ScenarioName) ? "all (configured order)" : selection.ScenarioName.Trim())}`");
    Report($"- **Scenario count:** `{scenariosToAnalyze.Count}`");
    Report($"- **Records:** `{filteredRecords.Count}`");
    Report($"- **Variants:** {string.Join(", ", allVariants.Select(v => $"`{v}`"))}");
    Report($"- **Input:** `{fileResultsPath}`");
    Report();

    foreach (var scenarioName in scenariosToAnalyze)
    {
        var records = filteredRecords
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Report($"## Scenario: `{scenarioName}`");

        if (records.Count == 0)
        {
            Report("No records for this scenario.");
            Report();
            continue;
        }

        var variants = records
            .Select(r => r.VariantName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Report($"- **Records:** `{records.Count}`");
        Report($"- **Variants:** {string.Join(", ", variants.Select(v => $"`{v}`"))}");
        Report();

        Report("### Overall by variant");
        Report("| Variant | Files | Avg MiB/s | P50 MiB/s | P95 MiB/s |");
        Report("|---|---:|---:|---:|---:|");

        foreach (var variant in variants)
        {
            var speeds = records
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ThroughputMiBPerSecond)
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();

            if (speeds.Count == 0)
            {
                continue;
            }

            Report(
                $"| {EscapeTable(variant)} | {speeds.Count} | {speeds.Average():0.00} | {Percentile(speeds, 0.50):0.00} | {Percentile(speeds, 0.95):0.00} |");
        }

        Report();
        Report("### Size buckets (Avg MiB/s by variant)");
        Report("| Size Bucket | Variant | Files | Avg MiB/s | P50 MiB/s | P95 MiB/s |");
        Report("|---|---|---:|---:|---:|---:|");

        var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                      ?? FileSizeBuckets.All.ToList();

        foreach (var bucket in buckets)
        {
            var bucketRecords = records.Where(r => bucket.Contains(r.FileSizeBytes)).ToList();
            if (bucketRecords.Count == 0)
            {
                continue;
            }

            foreach (var variant in variants)
            {
                var speeds = bucketRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.ThroughputMiBPerSecond)
                    .Where(v => v is not null)
                    .Select(v => v!.Value)
                    .OrderBy(v => v)
                    .ToList();

                if (speeds.Count == 0)
                {
                    continue;
                }

                Report(
                    $"| {bucket.Label} | {EscapeTable(variant)} | {speeds.Count} | {speeds.Average():0.00} | {Percentile(speeds, 0.50):0.00} | {Percentile(speeds, 0.95):0.00} |");
            }
        }

        Report();
        Report("### Size Bucket Breakdown");
        Report("| Size Bucket | Files | Total Bytes | % Bytes | Avg MiB/s | Est. Wall Time | % Wall Time |");
        Report("|---|---:|---:|---:|---:|---:|---:|");

        var uniqueFiles = records
            .GroupBy(r => r.SourceRelativePath)
            .Select(g => (g.Key, g.First().FileSizeBytes))
            .ToList();

        var totalBytesAll = (double)uniqueFiles.Sum(f => f.FileSizeBytes);

        var bucketBreakdowns = new List<(FileSizeBucket Bucket, int FileCount, long Bytes, double AvgThroughput)>();

        foreach (var bucket in buckets)
        {
            var bucketUniqueFiles = uniqueFiles
                .Where(f => bucket.Contains(f.FileSizeBytes))
                .ToList();

            if (bucketUniqueFiles.Count == 0)
            {
                continue;
            }

            var bytesInBucket = bucketUniqueFiles.Sum(f => f.FileSizeBytes);

            var bucketThroughputs = records
                .Where(r => bucket.Contains(r.FileSizeBytes))
                .Select(r => r.ThroughputMiBPerSecond)
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .ToList();

            var avgThroughput = bucketThroughputs.Count > 0 ? bucketThroughputs.Average() : 0.0;

            bucketBreakdowns.Add((bucket, bucketUniqueFiles.Count, bytesInBucket, avgThroughput));
        }

        var totalEstimatedWallSeconds = bucketBreakdowns
            .Where(b => b.AvgThroughput > 0.0)
            .Sum(b => b.Bytes / (b.AvgThroughput * 1024.0 * 1024.0));

        foreach (var (bucket, fileCount, bytesInBucket, avgThroughput) in bucketBreakdowns)
        {
            var pctBytes = totalBytesAll > 0 ? bytesInBucket / totalBytesAll * 100.0 : 0.0;
            var bytesStr = FormatBytesHuman(bytesInBucket);
            var estWallSeconds = avgThroughput > 0.0
                ? bytesInBucket / (avgThroughput * 1024.0 * 1024.0)
                : double.NaN;
            var pctWall = totalEstimatedWallSeconds > 0.0
                ? estWallSeconds / totalEstimatedWallSeconds * 100.0
                : 0.0;
            var wallStr = double.IsNaN(estWallSeconds) || double.IsInfinity(estWallSeconds)
                ? "—"
                : FormatDurationHuman(estWallSeconds);

            Report(
                $"| {bucket.Label} | {fileCount} | {bytesStr} | {pctBytes:0.0}% | {avgThroughput:0.00} | {wallStr} | {(double.IsNaN(estWallSeconds) ? "—" : $"{pctWall:0.0}%")} |");
        }

        Report();
    }

    await FlushReportAsync();
    Console.WriteLine($"Analysis: {analysisPath}");
}

static async Task RunSizeScalingModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
    var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
    var analysisPath = Path.Combine(artifactDirectory, fileNames.SizeScaling);

    if (!File.Exists(fileResultsPath))
    {
        Console.WriteLine($"No file-level results found: {fileResultsPath}");
        Console.WriteLine("Run benchmark mode first to produce benchmark-file-results.ndjson.");
        return;
    }

    var allRecords = await ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileResultsPath, ct);
    var filteredRecords = allRecords
        .Where(r => string.IsNullOrWhiteSpace(selection.ScenarioName) ||
                    string.Equals(r.ScenarioName, selection.ScenarioName.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                    string.Equals(r.VariantName, selection.VariantName.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(r => new SizeScalingInputRecord(
            r.ScenarioName,
            r.VariantName,
            r.SourceRelativePath,
            r.FileSizeBytes,
            r.CopyDurationMilliseconds,
            r.ThroughputMiBPerSecond))
        .ToList();

    if (filteredRecords.Count == 0)
    {
        Console.WriteLine("No file-level records found for the selected size-scaling filters.");
        return;
    }

    var report = BenchmarkSizeScalingAnalysis.Analyze(filteredRecords);
    var markdown = BenchmarkSizeScalingAnalysis.ToMarkdown(report, fileResultsPath);

    Directory.CreateDirectory(artifactDirectory);
    await File.WriteAllTextAsync(analysisPath, markdown, ct);

    Console.WriteLine(markdown);
    Console.WriteLine($"Size scaling analysis: {analysisPath}");
}

static async Task RunBenchmarkModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
    var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
    var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
    var taskListPath = Path.Combine(artifactDirectory, fileNames.TaskList);
    var journalDirectory = Path.Combine(artifactDirectory, JournalDirectoryName);
    var poolPath = Path.Combine(artifactDirectory, "benchmark-path-pool.json");

    Directory.CreateDirectory(artifactDirectory);

    var historicalRuns = await ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
    await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

    int totalPoolRunsNeeded = config.Scenarios.Count(s => s.Enabled && s.UsePathPool)
        * config.Variants.Where(v => v.Enabled).Sum(v => v.DesiredRunCount);

    BenchmarkScenario? lastScenario = null;
    while (true)
    {
        var benchmarkSelection = SelectBenchmarkSelection(config, historicalRuns, selection);
        if (benchmarkSelection is null || benchmarkSelection.SuccessfulRunCount >= benchmarkSelection.Variant.DesiredRunCount)
        {
            Console.WriteLine("All eligible benchmark scenarios and variants have completed their desired runs.");
            break;
        }

        var scenario = benchmarkSelection.Scenario;
        var variant = benchmarkSelection.Variant;
        var nextRunIndex = benchmarkSelection.NextRunIndex;

        if (lastScenario is not null && lastScenario.UsePathPool != scenario.UsePathPool)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Cold cache boundary before {scenario.Name} ---");
            Console.WriteLine(scenario.UsePathPool
                ? "Returning to path-pool runs. Reboot to clear the OS file cache, then press any key..."
                : "Switching to a non-pool run. Reboot to clear the OS file cache, then press any key...");
            Console.ReadKey(intercept: true);
            Console.WriteLine();
        }

        var runStartedUtc = DateTime.UtcNow;
        var sourcePath = Path.GetFullPath(!string.IsNullOrWhiteSpace(scenario.SourcePath) ? scenario.SourcePath : config.SourcePath);
        var destinationPath = Path.GetFullPath(scenario.DestinationPath);

        int? pathIndex = null;
        if (scenario.UsePathPool)
        {
            pathIndex = await PopPathIndexFromPoolAsync(poolPath, totalPoolRunsNeeded, ct);
            var suffix = $"_{pathIndex:D2}";
            sourcePath = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + suffix;
            destinationPath = destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + suffix;
        }

        ValidatePaths(sourcePath, destinationPath);

        var providerOptions = variant.CreateProviderOptions(scenario);
        var overwriteMode = variant.OverwriteMode ?? scenario.OverwriteMode;
        var state = new BenchmarkState();

        var inProgressRecord = BenchmarkRunRecord.CreateInProgress(
            scenario,
            variant,
            sourcePath,
            destinationPath,
            artifactDirectory,
            runStartedUtc,
            selection.Notes,
            nextRunIndex);

        await AppendJsonLineAsync(resultsPath, inProgressRecord, ct);
        historicalRuns.Add(inProgressRecord);
        await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

        Console.WriteLine();
        Console.WriteLine("Mode:     benchmark");
        Console.WriteLine($"Scenario: {scenario.Name}");
        Console.WriteLine(pathIndex is null
            ? $"Variant:  {variant.Name} (run {nextRunIndex}/{variant.DesiredRunCount})"
            : $"Variant:  {variant.Name} (run {nextRunIndex}/{variant.DesiredRunCount}) [Folder Index: {pathIndex:D2}]");
        Console.WriteLine($"Source:   {sourcePath}");
        Console.WriteLine($"Target:   {destinationPath}");
        Console.WriteLine($"Artifacts:{artifactDirectory}");
        Console.WriteLine(
            $"Provider: buffer={providerOptions.CopyBufferSizeBytes} bytes, " +
            $"small-file-threshold={providerOptions.SmallFileProgressThresholdBytes} bytes, " +
            $"write-mode={providerOptions.WriteMode}, " +
            $"array-pool={providerOptions.UseArrayPoolForManualLoop}, " +
            $"preallocate={providerOptions.PreallocateDestinationFile}");

        try
        {
            if (scenario.ClearDestinationBeforeRun)
            {
                Console.WriteLine("Clearing destination contents before benchmark...");
                using var clearProgress = new ThrottledConsoleProgress<string>(s => UpdateProgress($"Clearing: {s}"));
                ClearDirectoryContents(destinationPath, clearProgress);
                Console.WriteLine(); // Finalize progress line
            }

            Directory.CreateDirectory(journalDirectory);

            var sourceProvider = new LocalFileSystemProvider(sourcePath, options: providerOptions);
            var destinationProvider = new LocalFileSystemProvider(destinationPath, options: providerOptions);
            var registry = new FileSystemProviderRegistry();
            registry.Register(sourceProvider);
            registry.Register(destinationProvider);

            var scanOptions = new ScanOptions
            {
                IncludeHidden = config.IncludeHidden,
                FullPreScan = true,
                LazyExpand = false,
                FollowSymlinks = false,
            };

            state.ScanStopwatch.Start();
            Console.WriteLine("Scanning source...");
            using (var scanProgress = new ThrottledConsoleProgress<ScanProgress>(p => UpdateProgress($"Scanned: {p.NodesDiscovered} nodes, {p.DirectoriesScanned} dirs")))
            {
                state.Root = await ScanTreeAsync(sourceProvider, sourcePath, scanOptions, scanProgress, ct);
            }
            Console.WriteLine(); // Finalize progress line
            state.ScanStopwatch.Stop();

            new SelectionManager().SelectAll(state.Root);
            state.Root.BuildStats();

            var resolvedDestinationProvider = registry.ResolveProvider(destinationPath)
                ?? throw new InvalidOperationException($"No destination provider for {destinationPath}.");

            state.FreeSpaceBefore = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(ct);
            
            var copyStep = new CopyStep(destinationPath, overwriteMode)
            {
                SkipExistsCheckForOverwrite = variant.SkipExistsCheckForOverwrite
                    ?? scenario.SkipExistsCheckForOverwrite
                    ?? false
            };
            var runner = new PipelineRunner(new TransformPipeline([copyStep]));

            var directWriteThresholdBytes = variant.DirectWriteThresholdBytes
                ?? scenario.DirectWriteThresholdBytes
                ?? 0L;

            var skipExistsCheckForOverwrite = variant.SkipExistsCheckForOverwrite
                ?? scenario.SkipExistsCheckForOverwrite
                ?? false;

            Console.WriteLine("Executing copy...");
            using (var executeProgress = new ThrottledConsoleProgress<OperationProgress>(p =>
                UpdateProgress($"Copying: {p.FilesCompleted}/{p.FilesTotal} files ({FormatSize(p.TotalBytesCompleted)}/{FormatSize(p.TotalBytes)}), ETR: {FormatDuration(p.EstimatedRemaining)}")))
            {
                state.ExecuteStopwatch.Start();
                if (directWriteThresholdBytes > 0)
                {
                    state.Results = await BenchmarkCopyRunner.RunAsync(new PipelineJob
                    {
                        RootNode = state.Root,
                        SourceProvider = sourceProvider,
                        ProviderRegistry = registry,
                        Progress = executeProgress,
                        CancellationToken = ct,
                    }, destinationPath, overwriteMode, directWriteThresholdBytes, skipExistsCheckForOverwrite, providerOptions.CopyBufferSizeBytes, ct);
                }
                else
                {
                    state.Results = await runner.ExecuteAsync(new PipelineJob
                    {
                        RootNode = state.Root,
                        SourceProvider = sourceProvider,
                        ProviderRegistry = registry,
                        Progress = executeProgress,
                        CancellationToken = ct,
                    }, ct);
                }
                state.ExecuteStopwatch.Stop();
            }
            Console.WriteLine(); // Finalize progress line

            state.FreeSpaceAfter = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(ct);

            var journal = new OperationJournal(journalDirectory);
            state.JournalPath = await journal.WriteAsync(
                state.Results.Where(r => r.SourceNodeResult != SourceResult.None),
                BuildBenchmarkJournalMetadata(scenario, variant, sourcePath, destinationPath, artifactDirectory, runStartedUtc, state, selection.Notes, nextRunIndex),
                ct);

            var record = BenchmarkRunRecord.CreateSuccess(
                scenario,
                variant,
                sourcePath,
                destinationPath,
                artifactDirectory,
                runStartedUtc,
                state,
                selection.Notes,
                nextRunIndex);

            await AppendJsonLineAsync(resultsPath, record, ct);
            await AppendFileCopyRecordsAsync(fileResultsPath, record, state.Results, ct);

            historicalRuns.Add(record);
            await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

            Console.WriteLine($"Completed in {record.ExecuteDuration}.");
            Console.WriteLine($"Copied files: {record.CopiedFiles}, failed: {record.FailedFiles}, skipped: {record.SkippedFiles}.");
            Console.WriteLine($"Results: {resultsPath}");
            Console.WriteLine($"File results: {fileResultsPath}");
            Console.WriteLine($"Journal: {state.JournalPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(); // Finalize progress line
            Console.Error.WriteLine($"Error during benchmark run: {ex.Message}");

            var record = BenchmarkRunRecord.CreateFailure(
                scenario,
                variant,
                sourcePath,
                destinationPath,
                artifactDirectory,
                runStartedUtc,
                state,
                selection.Notes,
                nextRunIndex,
                ex);

            await AppendJsonLineAsync(resultsPath, record, ct);
            await AppendFileCopyRecordsAsync(fileResultsPath, record, state.Results, ct);

            historicalRuns.Add(record);
            await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);
            throw;
        }

        lastScenario = scenario;
    }
}

static async Task<DirectoryNode> ScanTreeAsync(
    IFileSystemProvider sourceProvider,
    string sourcePath,
    ScanOptions scanOptions,
    IProgress<ScanProgress>? progress,
    CancellationToken ct)
{
    var scanner = new DirectoryScanner(sourceProvider);
    DirectoryNode? root = null;

    await foreach (var node in scanner.ScanAsync(sourcePath, scanOptions, progress, ct))
    {
        root ??= node as DirectoryNode;
    }

    return root ?? throw new InvalidOperationException($"Scan did not produce a directory root for {sourcePath}.");
}

static Dictionary<string, string?> BuildBenchmarkJournalMetadata(
    BenchmarkScenario scenario,
    BenchmarkVariant variant,
    string sourcePath,
    string destinationPath,
    string artifactPath,
    DateTime runStartedUtc,
    BenchmarkState state,
    string? notes,
    int runIndex)
{
    var providerOptions = variant.CreateProviderOptions(scenario);
    return new Dictionary<string, string?>
    {
        ["recordType"] = "benchmarkRun",
        ["runStatus"] = BenchmarkRunStatus.Completed,
        ["scenarioName"] = scenario.Name,
        ["variantName"] = variant.Name,
        ["sourcePath"] = sourcePath,
        ["destinationPath"] = destinationPath,
        ["artifactPath"] = artifactPath,
        ["runStartedUtc"] = runStartedUtc.ToString("O"),
        ["hostName"] = Environment.MachineName,
        ["osDescription"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["notes"] = notes,
        ["runIndex"] = runIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["providerCopyBufferSizeBytes"] = providerOptions.CopyBufferSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["providerSmallFileProgressThresholdBytes"] = providerOptions.SmallFileProgressThresholdBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["providerWriteMode"] = providerOptions.WriteMode.ToString(),
        ["providerUseArrayPoolForManualLoop"] = providerOptions.UseArrayPoolForManualLoop.ToString(),
        ["providerPreallocateDestinationFile"] = providerOptions.PreallocateDestinationFile.ToString(),
        ["scanDuration"] = state.ScanStopwatch.Elapsed.ToString("c"),
        ["executeDuration"] = state.ExecuteStopwatch.Elapsed.ToString("c"),
        ["copiedFiles"] = state.Results.Sum(r => r.NumberOfFilesAffected).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["skippedFiles"] = state.Results.Sum(r => r.NumberOfFilesSkipped).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["failedFiles"] = state.Results.Count(r => !r.IsSuccess).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["outputBytes"] = state.Results.Sum(r => r.OutputBytes).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["destinationFreeSpaceBeforeBytes"] = state.FreeSpaceBefore?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["destinationFreeSpaceAfterBytes"] = state.FreeSpaceAfter?.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

static BenchmarkSelection? SelectBenchmarkSelection(
    BenchmarkConfig config,
    IReadOnlyList<BenchmarkRunRecord> historicalRuns,
    BenchmarkCliOptions selection)
{
    var candidates = (
        from scenario in config.Scenarios
        where scenario.Enabled
        from variant in config.Variants
        where variant.Enabled
        let successfulRuns = historicalRuns.Count(r => IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name))
        let totalRuns = historicalRuns.Count(r => IsTerminalRunForScenarioVariant(r, scenario.Name, variant.Name))
        let lastRunUtc = historicalRuns
            .Where(r => IsRunForScenarioVariant(r, scenario.Name, variant.Name))
            .Select(r => r.RunStartedUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max()
        let nextRunIndex = totalRuns + 1
        select new BenchmarkSelection(scenario, variant, successfulRuns, totalRuns, lastRunUtc, nextRunIndex))
        .ToList();

    if (!string.IsNullOrWhiteSpace(selection.ScenarioName) || !string.IsNullOrWhiteSpace(selection.VariantName))
    {
        candidates = candidates
            .Where(c =>
                (string.IsNullOrWhiteSpace(selection.ScenarioName) ||
                 string.Equals(c.Scenario.Name, selection.ScenarioName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(selection.VariantName) ||
                 string.Equals(c.Variant.Name, selection.VariantName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    if (string.IsNullOrWhiteSpace(selection.ScenarioName))
    {
        var scenarioOrder = BuildScenarioOrder(config, candidates.Select(c => c.Scenario.Name));
        foreach (var scenarioName in scenarioOrder)
        {
            var stageCandidates = candidates
                .Where(c => string.Equals(c.Scenario.Name, scenarioName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (stageCandidates.Count == 0)
            {
                continue;
            }

            var hasPendingRuns = stageCandidates.Any(c => c.SuccessfulRunCount < c.Variant.DesiredRunCount);
            if (hasPendingRuns)
            {
                candidates = stageCandidates;
                break;
            }
        }
    }

    var scenarioPriority = BuildScenarioPriority(config, candidates.Select(c => c.Scenario.Name));

    return candidates
        .OrderBy(c => c.SuccessfulRunCount >= c.Variant.DesiredRunCount ? 1 : 0)
        .ThenBy(c => scenarioPriority[c.Scenario.Name])
        .ThenBy(c => c.SuccessfulRunCount)
        .ThenBy(c => c.LastRunUtc)
        .FirstOrDefault();
}

static Dictionary<string, int> BuildScenarioPriority(BenchmarkConfig config, IEnumerable<string> scenarioNames)
{
    var ordered = BuildScenarioOrder(config, scenarioNames);
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < ordered.Count; i++)
    {
        map[ordered[i]] = i;
    }

    return map;
}

static List<string> BuildScenarioOrder(BenchmarkConfig config, IEnumerable<string> scenarioNames)
{
    var names = scenarioNames
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var configuredOrder = config.ScenarioExecutionOrder.Count > 0
        ? config.ScenarioExecutionOrder
        : ["SSDtoSSD", "SameDriveTest", "SSDtoHDD", "SSDtoUSBFlash"];

    var ordered = new List<string>();
    foreach (var configured in configuredOrder)
    {
        var match = names.FirstOrDefault(n => string.Equals(n, configured, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            ordered.Add(match);
        }
    }

    ordered.AddRange(names.Where(n => !ordered.Contains(n, StringComparer.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    return ordered;
}

static bool IsRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
    string.Equals(run.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(run.VariantName, variantName, StringComparison.OrdinalIgnoreCase);

static bool IsTerminalRun(BenchmarkRunRecord run) =>
    !string.Equals(run.RunStatus, BenchmarkRunStatus.InProgress, StringComparison.OrdinalIgnoreCase);

static bool IsTerminalRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
    IsRunForScenarioVariant(run, scenarioName, variantName) &&
    IsTerminalRun(run);

static bool IsSuccessfulRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
    IsRunForScenarioVariant(run, scenarioName, variantName) &&
    string.Equals(run.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
    run.FailedFiles == 0 &&
    run.ExceptionType is null;

static async Task<List<T>> ReadExistingRunsAsync<T>(string resultsPath, CancellationToken ct)
{
    var runs = new List<T>();
    if (!File.Exists(resultsPath))
    {
        return runs;
    }

    await foreach (var line in File.ReadLinesAsync(resultsPath, ct))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        try
        {
            var run = JsonSerializer.Deserialize<T>(line, JsonOptions.Default);
            if (run is not null)
            {
                runs.Add(run);
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Warning: Skipping malformed line in results file: {ex.Message}");
        }
    }

    return runs;
}

static double Percentile(IReadOnlyList<double> sortedValuesAscending, double percentile)
{
    if (sortedValuesAscending.Count == 0)
    {
        return 0;
    }

    if (sortedValuesAscending.Count == 1)
    {
        return sortedValuesAscending[0];
    }

    var clamped = Math.Clamp(percentile, 0d, 1d);
    var index = (sortedValuesAscending.Count - 1) * clamped;
    var lower = (int)Math.Floor(index);
    var upper = (int)Math.Ceiling(index);
    if (lower == upper)
    {
        return sortedValuesAscending[lower];
    }

    var weight = index - lower;
    return sortedValuesAscending[lower] + ((sortedValuesAscending[upper] - sortedValuesAscending[lower]) * weight);
}

static void ValidatePaths(string sourcePath, string destinationPath)
{
    if (!Directory.Exists(sourcePath))
    {
        throw new DirectoryNotFoundException(sourcePath);
    }

    if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Source and destination paths must differ.");
    }

    if (IsSameOrNestedPath(sourcePath, destinationPath) || IsSameOrNestedPath(destinationPath, sourcePath))
    {
        throw new InvalidOperationException("Source and destination paths must not be nested inside each other.");
    }
}

static bool IsSameOrNestedPath(string parentPath, string childPath)
{
    var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
    var normalizedChild = EnsureTrailingSeparator(Path.GetFullPath(childPath));
    return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
}

static string EnsureTrailingSeparator(string path)
{
    if (path.Length > 0 &&
        (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar))
    {
        return path;
    }

    return path + Path.DirectorySeparatorChar;
}

static string ResolveArtifactDirectory(string workingDirectory, string sourcePath, string? configuredArtifactPath)
{
    if (!string.IsNullOrWhiteSpace(configuredArtifactPath))
    {
        return Path.GetFullPath(configuredArtifactPath, workingDirectory);
    }

    var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
    var fullSourcePath = Path.GetFullPath(sourcePath);
    if (IsSameOrNestedPath(fullSourcePath, fullWorkingDirectory) || IsSameOrNestedPath(fullWorkingDirectory, fullSourcePath))
    {
        var sourceParent = Path.GetDirectoryName(fullSourcePath) ?? fullWorkingDirectory;
        var sourceLeaf = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(sourceParent, $"{sourceLeaf}-benchmark-artifacts");
    }

    return Path.Combine(fullWorkingDirectory, ".benchmarks");
}

static void ClearDirectoryContents(string destinationPath, IProgress<string>? progress = null)
{
    var fullDestinationPath = Path.GetFullPath(destinationPath);
    if (Path.GetPathRoot(fullDestinationPath)?.Equals(fullDestinationPath, StringComparison.OrdinalIgnoreCase) == true)
    {
        throw new InvalidOperationException($"Refusing to clear drive root: {fullDestinationPath}");
    }

    if (!Directory.Exists(fullDestinationPath))
    {
        Directory.CreateDirectory(fullDestinationPath);
        return;
    }

    foreach (var file in Directory.EnumerateFiles(fullDestinationPath))
    {
        progress?.Report(Path.GetFileName(file));
        File.Delete(file);
    }

    foreach (var directory in Directory.EnumerateDirectories(fullDestinationPath))
    {
        progress?.Report(Path.GetFileName(directory));
        Directory.Delete(directory, recursive: true);
    }
}

static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken ct)
{
    var line = JsonSerializer.Serialize(value, JsonOptions.Default);
    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, ct);
}

static async Task AppendFileCopyRecordsAsync(
    string path,
    BenchmarkRunRecord run,
    IReadOnlyList<TransformResult> results,
    CancellationToken ct)
{
    var copiedResults = results.Where(r =>
        r.IsSuccess &&
        r.SourceNodeResult == SourceResult.Copied &&
        r.NumberOfFilesAffected > 0 &&
        r.ExecutionDuration is TimeSpan duration &&
        duration > TimeSpan.Zero);

    await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    await using var writer = new StreamWriter(stream, Encoding.UTF8);

    foreach (var result in copiedResults)
    {
        ct.ThrowIfCancellationRequested();

        var duration = result.ExecutionDuration!.Value;
        var sizeBytes = result.InputBytes;
        double? throughputMiBPerSecond = sizeBytes > 0 && duration.TotalSeconds > 0
            ? (sizeBytes / 1048576d) / duration.TotalSeconds
            : null;

        var fileRecord = new BenchmarkFileCopyRecord
        {
            RunStartedUtc = run.RunStartedUtc,
            RunIndex = run.RunIndex,
            ScenarioName = run.ScenarioName,
            VariantName = run.VariantName,
            SourceRelativePath = result.SourceNode.CanonicalRelativePath ?? string.Empty,
            DestinationPath = result.DestinationPath ?? string.Empty,
            FileSizeBytes = sizeBytes,
            CopyDurationMilliseconds = duration.TotalMilliseconds,
            ThroughputMiBPerSecond = throughputMiBPerSecond,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(fileRecord, JsonOptions.Default));
    }

    await writer.FlushAsync(ct);
}

static async Task UpdateTaskListAsync(
    string taskListPath,
    BenchmarkConfig config,
    IReadOnlyList<BenchmarkRunRecord> runs,
    CancellationToken ct)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Benchmark Task List");
    builder.AppendLine();
    builder.AppendLine($"Source: `{Path.GetFullPath(config.SourcePath)}`");

    var scenarioOrder = BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
    if (scenarioOrder.Count > 0)
    {
        builder.AppendLine();
        builder.AppendLine($"Scenario execution order: `{string.Join(" -> ", scenarioOrder)}`");
    }

    builder.AppendLine();
    builder.AppendLine("| Scenario | Variant | Destination | Target runs | Successful runs | Last run (UTC) | Last status |");
    builder.AppendLine("|---|---|---|---:|---:|---|---|");

    foreach (var variant in config.Variants.Where(v => v.Enabled))
    {
        foreach (var scenario in config.Scenarios.Where(s => s.Enabled))
        {
            var scenarioRuns = runs
                .Where(r => IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                .OrderByDescending(r => r.RunStartedUtc)
                .ThenByDescending(IsTerminalRun)
                .ToList();
            var lastRun = scenarioRuns.FirstOrDefault();
            var successfulRuns = scenarioRuns.Count(r =>
                string.Equals(r.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
                r.FailedFiles == 0 &&
                r.ExceptionType is null);
            var lastStatus = lastRun is null
                ? "pending"
                : string.Equals(lastRun.RunStatus, BenchmarkRunStatus.InProgress, StringComparison.OrdinalIgnoreCase)
                    ? "in progress"
                    : lastRun.ExceptionType is not null
                    ? $"error ({lastRun.ExceptionType})"
                    : lastRun.FailedFiles > 0
                        ? $"failed ({lastRun.FailedFiles})"
                        : "ok";

            builder.Append("| ")
                .Append(EscapeTable(scenario.Name)).Append(" | ")
                .Append(EscapeTable(variant.Name)).Append(" | ")
                .Append(EscapeTable(Path.GetFullPath(scenario.DestinationPath))).Append(" | ")
                .Append(variant.DesiredRunCount).Append(" | ")
                .Append(successfulRuns).Append(" | ")
                .Append(lastRun?.RunStartedUtc.ToString("O") ?? "-").Append(" | ")
                .Append(EscapeTable(lastStatus)).AppendLine(" |");
        }
    }

    await File.WriteAllTextAsync(taskListPath, builder.ToString(), ct);
}

static string EscapeTable(string value) => value.Replace("|", "\\|");

static void UpdateProgress(string message)
{
    // Use carriage return to overwrite the current line
    // ANSI escape code \x1b[K clears from cursor to end of line
    Console.Write($"\r{message}\x1b[K");
}

static string FormatSize(long bytes)
{
    string[] units = { "B", "KB", "MB", "GB", "TB" };
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < units.Length - 1)
    {
        order++;
        len /= 1024;
    }
    return $"{len:0.##} {units[order]}";
}

static string FormatDuration(TimeSpan duration)
{
    if (duration == TimeSpan.Zero) return "0s";
    if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
    if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
    return $"{(int)duration.TotalSeconds}s";
}

static string FormatBytesHuman(long bytes) => FormatSize(bytes);

static string FormatDurationHuman(double totalSeconds)
{
    if (totalSeconds < 1.0) return $"{totalSeconds * 1000.0:0} ms";
    if (totalSeconds < 60.0) return $"{totalSeconds:0.0} sec";
    if (totalSeconds < 3600.0) return $"{(int)(totalSeconds / 60.0)}m {(int)(totalSeconds % 60.0)}s";
    return $"{(int)(totalSeconds / 3600.0)}h {((int)(totalSeconds % 3600.0) / 60)}m";
}

static void DuplicateDirectory(string sourceDir, string destDir)
{
    var source = new DirectoryInfo(sourceDir);
    if (!source.Exists)
    {
        throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
    }

    if (Directory.Exists(destDir))
    {
        var fullDest = Path.GetFullPath(destDir);
        if (Path.GetPathRoot(fullDest)?.Equals(fullDest, StringComparison.OrdinalIgnoreCase) != true)
        {
            Directory.Delete(fullDest, recursive: true);
        }
    }

    Directory.CreateDirectory(destDir);

    foreach (var dir in source.GetDirectories("*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDir, dir.FullName);
        Directory.CreateDirectory(Path.Combine(destDir, relativePath));
    }

    foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDir, file.FullName);
        var destFile = Path.Combine(destDir, relativePath);
        file.CopyTo(destFile, overwrite: true);
    }
}

static async Task<int> PopPathIndexFromPoolAsync(string poolPath, int totalRunsNeeded, CancellationToken ct)
{
    List<int> pool;
    if (File.Exists(poolPath))
    {
        var content = await File.ReadAllTextAsync(poolPath, ct);
        try
        {
            pool = JsonSerializer.Deserialize<List<int>>(content, JsonOptions.Default) ?? [];
        }
        catch
        {
            pool = [];
        }
    }
    else
    {
        pool = [];
    }

    if (pool.Count == 0)
    {
        if (totalRunsNeeded <= 0)
            throw new InvalidOperationException("Cannot initialize path pool: totalRunsNeeded must be positive.");
        pool = Enumerable.Range(1, totalRunsNeeded).ToList();
        var rand = new Random();
        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = rand.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
    }

    var selectedIndex = pool[0];
    pool.RemoveAt(0);

    // Save the remaining pool
    var directory = Path.GetDirectoryName(poolPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
    await File.WriteAllTextAsync(poolPath, JsonSerializer.Serialize(pool, JsonOptions.Default), ct);

    return selectedIndex;
}

internal sealed class ThrottledConsoleProgress<T> : IProgress<T>, IDisposable
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _throttle;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private T? _lastValue;
    private bool _hasValue;

    public ThrottledConsoleProgress(Action<T> handler, TimeSpan? throttle = null)
    {
        _handler = handler;
        _throttle = throttle ?? TimeSpan.FromMilliseconds(100);
    }

    public void Report(T value)
    {
        _lastValue = value;
        _hasValue = true;

        if (_stopwatch.Elapsed >= _throttle)
        {
            _handler(value);
            _stopwatch.Restart();
            _hasValue = false;
        }
    }

    public void Dispose()
    {
        if (_hasValue && _lastValue != null)
        {
            _handler(_lastValue);
        }
    }
}

internal sealed record BenchmarkSelection(
    BenchmarkScenario Scenario,
    BenchmarkVariant Variant,
    int SuccessfulRunCount,
    int TotalRunCount,
    DateTime LastRunUtc,
    int NextRunIndex);

internal sealed record FileSizeBucket(long MinBytesInclusive, long MaxBytesInclusive, string Label)
{
    public bool Contains(long value) => value >= MinBytesInclusive && value <= MaxBytesInclusive;
}

internal static class FileSizeBuckets
{
    public static IReadOnlyList<FileSizeBucket> All { get; } =
    [
        new FileSizeBucket(0, 64 * 1024, "0-64 KiB"),
        new FileSizeBucket(64 * 1024 + 1, 512 * 1024, "64-512 KiB"),
        new FileSizeBucket(512 * 1024 + 1, 4 * 1024 * 1024, "512 KiB-4 MiB"),
        new FileSizeBucket(4 * 1024 * 1024 + 1, 32 * 1024 * 1024, "4-32 MiB"),
        new FileSizeBucket(32 * 1024 * 1024 + 1, 256 * 1024 * 1024, "32-256 MiB"),
        new FileSizeBucket(256 * 1024 * 1024 + 1, 2L * 1024 * 1024 * 1024, "256 MiB-2 GiB"),
        new FileSizeBucket(2L * 1024 * 1024 * 1024 + 1, long.MaxValue, ">2 GiB"),
    ];
}

internal static class FileNamesResolver
{
    public const string DefaultResults = "benchmark-results.ndjson";
    public const string DefaultFileResults = "benchmark-file-results.ndjson";
    public const string DefaultAnalysis = "benchmark-analysis.md";
    public const string DefaultSizeScaling = "benchmark-size-scaling.md";
    public const string DefaultTaskList = "benchmark-tasklist.md";

    public static (string Results, string FileResults, string Analysis, string SizeScaling, string TaskList) GetFileNames(string configPath)
    {
        var configFileName = Path.GetFileName(configPath);
        var prefix = configFileName.EndsWith(".json") ? configFileName[..^5] : configFileName;

        var results = prefix.Replace("scenarios", "results") + ".ndjson";
        if (results == prefix + ".ndjson") results = DefaultResults;

        var fileResults = prefix.Replace("scenarios", "file-results") + ".ndjson";
        if (fileResults == prefix + ".ndjson") fileResults = DefaultFileResults;

        var analysis = prefix.Replace("scenarios", "analysis") + ".md";
        if (analysis == prefix + ".md") analysis = DefaultAnalysis;

        var sizeScaling = prefix.Replace("scenarios", "size-scaling") + ".md";
        if (sizeScaling == prefix + ".md") sizeScaling = DefaultSizeScaling;

        var taskList = prefix.Replace("scenarios", "tasklist") + ".md";
        if (taskList == prefix + ".md") taskList = DefaultTaskList;

        return (results, fileResults, analysis, sizeScaling, taskList);
    }
}
