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
    var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
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

    if (!File.Exists(fileResultsPath) && !File.Exists(resultsPath))
    {
        Report($"No benchmark results found: {resultsPath}");
        Report($"No file-level results found: {fileResultsPath}");
        Report("Run benchmark mode first to produce benchmark-results.ndjson and benchmark-file-results.ndjson.");
        await FlushReportAsync();
        return;
    }

    var allRuns = File.Exists(resultsPath)
        ? await ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct)
        : [];
    var allRecords = File.Exists(fileResultsPath)
        ? await ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileResultsPath, ct)
        : [];

    if (allRuns.Count == 0 && allRecords.Count == 0)
    {
        Report("No benchmark records available.");
        await FlushReportAsync();
        return;
    }

    var scenarioOrder = BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
    var scenariosToAnalyze = !string.IsNullOrWhiteSpace(selection.ScenarioName)
        ? [selection.ScenarioName.Trim()]
        : scenarioOrder;

    if (scenariosToAnalyze.Count == 0)
    {
        scenariosToAnalyze = allRuns
            .Select(r => r.ScenarioName)
            .Concat(allRecords.Select(r => r.ScenarioName))
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

    var filteredRuns = allRuns
        .Where(r => scenarioSet.Contains(r.ScenarioName))
        .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                    string.Equals(r.VariantName, selection.VariantName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (filteredRecords.Count == 0 && filteredRuns.Count == 0)
    {
        if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
        {
            Report($"No records found for scenario '{selection.ScenarioName.Trim()}'.");
        }
        else
        {
            Report("No records found for the selected scenarios.");
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
        .Concat(filteredRuns.Select(r => r.VariantName))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, new VariantNameComparer())
        .ToList();

    Report("## Analysis Summary");
    Report($"- **Mode:** `analysis`");
    Report($"- **Source:** `{Path.GetFullPath(config.SourcePath)}`");
    Report($"- **Scenario filter:** `{(string.IsNullOrWhiteSpace(selection.ScenarioName) ? "all (configured order)" : selection.ScenarioName.Trim())}`");
    Report($"- **Scenario count:** `{scenariosToAnalyze.Count}`");
    Report($"- **Run records:** `{filteredRuns.Count}`");
    Report($"- **File records:** `{filteredRecords.Count}`");
    Report($"- **Variants:** {string.Join(", ", allVariants.Select(v => $"`{v}`"))}");
    Report($"- **Run input:** `{resultsPath}`");
    Report($"- **File input:** `{fileResultsPath}`");
    Report("- **Verdicts:** `PASS` means the measured improvement exceeds both the gate and observed variance. `INCONCLUSIVE` means the delta is inside variance or a matched control is missing.");
    Report();

    var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                  ?? FileSizeBuckets.All.ToList();

    var matchedControlLookup = config.Variants
        .Where(v => !string.IsNullOrWhiteSpace(v.MatchedControl))
        .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);

    foreach (var scenarioName in scenariosToAnalyze)
    {
        var records = filteredRecords
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var runs = filteredRuns
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .Where(IsTerminalRun)
            .ToList();

        Report($"## Scenario: `{scenarioName}`");

        if (records.Count == 0 && runs.Count == 0)
        {
            Report("No records for this scenario.");
            Report();
            continue;
        }

        var variants = records
            .Select(r => r.VariantName)
            .Concat(runs.Select(r => r.VariantName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, new VariantNameComparer())
            .ToList();

        Report($"- **Run records:** `{runs.Count}`");
        Report($"- **File records:** `{records.Count}`");
        Report($"- **Variants:** {string.Join(", ", variants.Select(v => $"`{v}`"))}");
        Report();

        var warnings = BuildMissingControlWarnings(variants, matchedControlLookup);

        ReportRunEvidence(Report, runs, variants);
        ReportBucketRecommendations(Report, records, buckets, variants, warnings, matchedControlLookup);
        ReportBucketMetrics(Report, records, buckets, variants);
        ReportBatchingIsolationEvidence(Report, records, buckets, variants);

        if (warnings.Count > 0)
        {
            Report("### Missing Matched Controls");
            foreach (var warning in warnings)
            {
                Report($"- {warning}");
            }
            Report();
        }

        Report();

        var htmlPath = Path.ChangeExtension(analysisPath, ".html");
        if (scenariosToAnalyze.Count > 1)
        {
            htmlPath = Path.Combine(Path.GetDirectoryName(htmlPath) ?? "", $"{Path.GetFileNameWithoutExtension(htmlPath)}-{scenarioName}.html");
        }
        
        await BenchmarkHtmlReportGenerator.GenerateAsync(htmlPath, scenarioName, buckets, variants, records);
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

    if (historicalRuns.Count > 0)
    {
        var hasPending = false;
        foreach (var scenario in config.Scenarios.Where(s => s.Enabled))
        {
            foreach (var variant in config.Variants.Where(v => v.Enabled))
            {
                var successfulRuns = historicalRuns.Count(r => IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name));
                if (successfulRuns < variant.DesiredRunCount)
                {
                    hasPending = true;
                    break;
                }
            }
            if (hasPending) break;
        }

        if (!hasPending)
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("All previous benchmark runs in the active scenarios are completed.");
            Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
            Console.WriteLine("-------------------------------------------------------------------");

            await ArchiveResultsAsync(artifactDirectory, selection.ConfigPath, ct);

            // Reload historical runs (should be empty now)
            historicalRuns = await ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        }
    }

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

            var bufferBatchBytes = variant.BufferBatchBytes
                ?? scenario.BufferBatchBytes
                ?? 0L;

            var skipExistsCheckForOverwrite = variant.SkipExistsCheckForOverwrite
                ?? scenario.SkipExistsCheckForOverwrite
                ?? false;

            Console.WriteLine("Executing copy...");
            using (var executeProgress = new ThrottledConsoleProgress<OperationProgress>(p =>
                UpdateProgress($"Copying: {p.FilesCompleted}/{p.FilesTotal} files ({FormatSize(p.TotalBytesCompleted)}/{FormatSize(p.TotalBytes)}), ETR: {FormatDuration(p.EstimatedRemaining)}")))
            {
                state.ExecuteStopwatch.Start();
                if (directWriteThresholdBytes > 0 || bufferBatchBytes > 0)
                {
                    state.Results = await BenchmarkCopyRunner.RunAsync(new PipelineJob
                    {
                        RootNode = state.Root,
                        SourceProvider = sourceProvider,
                        ProviderRegistry = registry,
                        Progress = executeProgress,
                        CancellationToken = ct,
                    }, destinationPath, overwriteMode, directWriteThresholdBytes, bufferBatchBytes, skipExistsCheckForOverwrite, providerOptions.CopyBufferSizeBytes, ct);
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

        // Apply a 60-second cooldown if there are more benchmarks left in the queue
        var nextSelection = SelectBenchmarkSelection(config, historicalRuns, selection);
        if (nextSelection is not null && nextSelection.SuccessfulRunCount < nextSelection.Variant.DesiredRunCount)
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("Applying 60-second cooldown to let the drive cache settle and cool...");
            Console.WriteLine("-------------------------------------------------------------------");
            for (int i = 60; i > 0; i--)
            {
                if (ct.IsCancellationRequested) break;
                Console.Write($"\rResuming next variant in {i} seconds...   ");
                await Task.Delay(1000, ct);
            }
            Console.WriteLine("\rCooldown complete. Starting next variant.                   ");
            Console.WriteLine();
        }
    }

    Console.WriteLine();
    Console.WriteLine("-------------------------------------------------------------------");
    Console.WriteLine("Benchmark runs complete. Generating analysis report automatically...");
    Console.WriteLine("-------------------------------------------------------------------");
    await RunAnalysisModeAsync(workingDirectory, config, selection, ct);
}

static async Task ArchiveResultsAsync(string artifactDirectory, string configPath, CancellationToken ct)
{
    var fileNames = FileNamesResolver.GetFileNames(configPath);
    var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var configName = Path.GetFileNameWithoutExtension(configPath);
    var archiveDirName = $"{timestamp}_{configName}";
    var archiveDirectory = Path.Combine(artifactDirectory, "archive", archiveDirName);

    Directory.CreateDirectory(archiveDirectory);
    Console.WriteLine($"Created archive directory: {archiveDirectory}");

    var filesToMove = new[]
    {
        fileNames.Results,
        fileNames.FileResults,
        fileNames.Analysis,
        fileNames.TaskList,
        "benchmark-path-pool.json"
    };

    foreach (var fileName in filesToMove)
    {
        var src = Path.Combine(artifactDirectory, fileName);
        if (File.Exists(src))
        {
            var dest = Path.Combine(archiveDirectory, fileName);
            try
            {
                File.Move(src, dest, overwrite: true);
                Console.WriteLine($"Archived file: {fileName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error archiving {fileName}: {ex.Message}");
            }
        }
    }

    var journalSrc = Path.Combine(artifactDirectory, JournalDirectoryName);
    if (Directory.Exists(journalSrc))
    {
        var journalDest = Path.Combine(archiveDirectory, JournalDirectoryName);
        try
        {
            Directory.Move(journalSrc, journalDest);
            Console.WriteLine("Archived journals directory.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error archiving journals directory: {ex.Message}");
        }
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
        ["bufferBatchBytes"] = (variant.BufferBatchBytes ?? scenario.BufferBatchBytes ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["directWriteThresholdBytes"] = (variant.DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    var pendingCandidates = candidates
        .Where(c => c.SuccessfulRunCount < c.Variant.DesiredRunCount)
        .ToList();

    if (pendingCandidates.Count > 0)
    {
        var rand = new Random();
        return pendingCandidates[rand.Next(pendingCandidates.Count)];
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

static void ReportRunEvidence(
    Action<string?> report,
    IReadOnlyList<BenchmarkRunRecord> runs,
    IReadOnlyList<string> variants)
{
    report("### Run-Level Evidence");
    report(null);

    if (runs.Count == 0)
    {
        report("No run-level records available. Whole-policy wall-clock verdicts cannot be produced.");
        report(null);
        return;
    }

    var baselineVariant = FindBaselineVariant(variants);
    var baselineEvidence = baselineVariant is null
        ? null
        : BuildRunEvidence(runs, baselineVariant);

    report("| Variant | Valid Runs | Invalid Runs | Median Execute | Mean Execute | Min | Max | Spread | Delta vs Control | Noise Floor | Verdict |");
    report("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

    foreach (var variant in variants)
    {
        var evidence = BuildRunEvidence(runs, variant);
        if (evidence.TotalRuns == 0)
        {
            continue;
        }

        var control = baselineVariant is not null &&
                      !string.Equals(variant, baselineVariant, StringComparison.OrdinalIgnoreCase)
            ? baselineEvidence
            : null;
        var comparison = CompareRunEvidence(evidence, control);

        report(
            $"| {EscapeTable(variant)} | {evidence.ValidRuns} | {evidence.InvalidRuns} | " +
            $"{FormatDurationHuman(evidence.MedianSeconds)} | {FormatDurationHuman(evidence.MeanSeconds)} | " +
            $"{FormatDurationHuman(evidence.MinSeconds)} | {FormatDurationHuman(evidence.MaxSeconds)} | " +
            $"{FormatDurationHuman(evidence.SpreadSeconds)} | {comparison.DeltaText} | {comparison.NoiseText} | {comparison.Verdict} |");
    }

    if (baselineVariant is null)
    {
        report(null);
        report("Run-level verdicts are `INCONCLUSIVE`: no baseline/control variant was found.");
    }

    report(null);
}

static void ReportBucketRecommendations(
    Action<string?> report,
    IReadOnlyList<BenchmarkFileCopyRecord> records,
    IReadOnlyList<FileSizeBucket> buckets,
    IReadOnlyList<string> variants,
    List<string> warnings,
    IReadOnlyDictionary<string, string> matchedControls)
{
    report("### Bucket Strategy Evidence");
    report(null);

    if (records.Count == 0)
    {
        report("No file-level records available. Bucket strategy recommendations cannot be produced.");
        report(null);
        return;
    }

    report("| Bucket | Best Observed Variant | Matched Control | Median File Duration | Control Median | Delta | Noise Floor | Aggregate MiB/s | Verdict | Recommendation |");
    report("|---|---|---|---:|---:|---:|---:|---:|---|---|");

    foreach (var bucket in buckets)
    {
        var candidates = variants
            .Select(v => BuildBucketEvidence(records, bucket, v))
            .Where(e => e.RecordCount > 0)
            .OrderBy(e => e.MedianDurationMilliseconds)
            .ToList();

        if (candidates.Count == 0)
        {
            continue;
        }

        var best = candidates[0];
        var controlName = FindMatchedControlVariant(best.VariantName, variants, matchedControls);
        BucketVariantEvidence? control = null;
        if (controlName is not null)
        {
            control = BuildBucketEvidence(records, bucket, controlName);
            if (control.RecordCount == 0)
            {
                warnings.Add($"`{best.VariantName}` in `{bucket.Label}` has matched control `{controlName}`, but that control has no file-level records in the bucket.");
                control = null;
            }
        }

        var comparison = CompareBucketEvidence(best, control);
        var recommendation = comparison.Verdict == "PASS"
            ? "Candidate for policy"
            : IsBaselineVariant(best.VariantName)
                ? "Keep control"
                : "No supported change";

        report(
            $"| {bucket.Label} | {EscapeTable(best.VariantName)} | {EscapeTable(controlName ?? "-")} | " +
            $"{best.MedianDurationMilliseconds:0.###} ms | {(control is null ? "-" : $"{control.MedianDurationMilliseconds:0.###} ms")} | " +
            $"{comparison.DeltaText} | {comparison.NoiseText} | {best.AggregateThroughputMiBPerSecond:0.00} | " +
            $"{comparison.Verdict} | {recommendation} |");
    }

    report(null);
}

static void ReportBucketMetrics(
    Action<string?> report,
    IReadOnlyList<BenchmarkFileCopyRecord> records,
    IReadOnlyList<FileSizeBucket> buckets,
    IReadOnlyList<string> variants)
{
    report("### Bucket Metrics");
    report(null);

    if (records.Count == 0)
    {
        report("No file-level records available.");
        report(null);
        return;
    }

    report("| Bucket | Variant | Records | Bytes | Median Duration | P95 Duration | Aggregate MiB/s | Mean MiB/s | P50 MiB/s | P95 MiB/s | Run-Median Spread |");
    report("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var bucket in buckets)
    {
        foreach (var variant in variants)
        {
            var evidence = BuildBucketEvidence(records, bucket, variant);
            if (evidence.RecordCount == 0)
            {
                continue;
            }

            report(
                $"| {bucket.Label} | {EscapeTable(variant)} | {evidence.RecordCount} | {FormatBytesHuman(evidence.TotalBytes)} | " +
                $"{evidence.MedianDurationMilliseconds:0.###} ms | {evidence.P95DurationMilliseconds:0.###} ms | " +
                $"{evidence.AggregateThroughputMiBPerSecond:0.00} | {evidence.MeanThroughputMiBPerSecond:0.00} | " +
                $"{evidence.P50ThroughputMiBPerSecond:0.00} | {evidence.P95ThroughputMiBPerSecond:0.00} | " +
                $"{evidence.RunMedianSpreadMilliseconds:0.###} ms |");
        }
    }

    report(null);
}

static void ReportBatchingIsolationEvidence(
    Action<string?> report,
    IReadOnlyList<BenchmarkFileCopyRecord> records,
    IReadOnlyList<FileSizeBucket> buckets,
    IReadOnlyList<string> variants)
{
    var directBatchVariants = variants
        .Where(v => v.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase))
        .OrderBy(v => v, new VariantNameComparer())
        .ToList();

    var unbatchedControl = variants.FirstOrDefault(v =>
        v.Contains("UnbatchedDirectWrite", StringComparison.OrdinalIgnoreCase));

    if (directBatchVariants.Count == 0 || unbatchedControl is null)
    {
        return;
    }

    report("### Batching Isolation Evidence");
    report(null);
    report($"Compares each `DirectWriteBatch*` variant against `{unbatchedControl}` to isolate the contribution of batching beyond direct write alone (Section 7.2.2).");
    report(null);
    report("| Bucket | Variant | Unbatched Control | Median Duration | Control Median | Delta | Noise Floor | Verdict |");
    report("|---|---|---|---:|---:|---:|---:|---|");

    foreach (var bucket in buckets)
    {
        var control = BuildBucketEvidence(records, bucket, unbatchedControl);
        if (control.RecordCount == 0)
        {
            continue;
        }

        foreach (var variant in directBatchVariants)
        {
            var candidate = BuildBucketEvidence(records, bucket, variant);
            if (candidate.RecordCount == 0)
            {
                continue;
            }

            var comparison = CompareBucketEvidence(candidate, control);
            report(
                $"| {bucket.Label} | {EscapeTable(variant)} | {EscapeTable(unbatchedControl)} | " +
                $"{candidate.MedianDurationMilliseconds:0.###} ms | {control.MedianDurationMilliseconds:0.###} ms | " +
                $"{comparison.DeltaText} | {comparison.NoiseText} | {comparison.Verdict} |");
        }
    }

    report(null);
}

static List<string> BuildMissingControlWarnings(IReadOnlyList<string> variants, IReadOnlyDictionary<string, string> matchedControls)
{
    var warnings = new List<string>();
    foreach (var variant in variants)
    {
        if (IsBaselineVariant(variant))
        {
            continue;
        }

        var control = FindMatchedControlVariant(variant, variants, matchedControls);
        if (control is null)
        {
            warnings.Add($"`{variant}` has no matched control; causal effect cannot be isolated.");
        }

        if (variant.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase) &&
            !variants.Any(v => v.Contains("UnbatchedDirectWrite", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"`{variant}` has no `UnbatchedDirectWrite*` control; batching cannot be isolated from direct write alone.");
        }
    }

    return warnings
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static RunVariantEvidence BuildRunEvidence(IReadOnlyList<BenchmarkRunRecord> runs, string variant)
{
    var variantRuns = runs
        .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
        .ToList();
    var validDurations = variantRuns
        .Where(IsSuccessfulRun)
        .Select(r => r.ExecuteDuration.TotalSeconds)
        .OrderBy(v => v)
        .ToList();

    if (validDurations.Count == 0)
    {
        return new RunVariantEvidence(
            variant,
            variantRuns.Count,
            0,
            variantRuns.Count,
            0,
            0,
            0,
            0,
            0);
    }

    var min = validDurations[0];
    var max = validDurations[^1];
    return new RunVariantEvidence(
        variant,
        variantRuns.Count,
        validDurations.Count,
        variantRuns.Count - validDurations.Count,
        Percentile(validDurations, 0.50),
        validDurations.Average(),
        min,
        max,
        max - min);
}

static BucketVariantEvidence BuildBucketEvidence(
    IReadOnlyList<BenchmarkFileCopyRecord> records,
    FileSizeBucket bucket,
    string variant)
{
    var bucketRecords = records
        .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
        .Where(r => bucket.Contains(r.FileSizeBytes))
        .ToList();

    if (bucketRecords.Count == 0)
    {
        return BucketVariantEvidence.Empty(bucket.Label, variant);
    }

    var durations = bucketRecords
        .Select(r => r.CopyDurationMilliseconds)
        .Where(v => v > 0)
        .OrderBy(v => v)
        .ToList();
    var throughputs = bucketRecords
        .Select(r => r.ThroughputMiBPerSecond)
        .Where(v => v is not null)
        .Select(v => v!.Value)
        .OrderBy(v => v)
        .ToList();
    var runMedians = bucketRecords
        .GroupBy(r => (r.RunStartedUtc, r.RunIndex))
        .Select(g => g
            .Select(r => r.CopyDurationMilliseconds)
            .Where(v => v > 0)
            .OrderBy(v => v)
            .ToList())
        .Where(values => values.Count > 0)
        .Select(values => Percentile(values, 0.50))
        .OrderBy(v => v)
        .ToList();

    var totalBytes = bucketRecords.Sum(r => r.FileSizeBytes);
    var totalSeconds = bucketRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
    var aggregateThroughput = totalBytes > 0 && totalSeconds > 0
        ? (totalBytes / 1048576.0) / totalSeconds
        : 0.0;

    return new BucketVariantEvidence(
        bucket.Label,
        variant,
        bucketRecords.Count,
        totalBytes,
        durations.Count > 0 ? durations.Average() : 0.0,
        durations.Count > 0 ? Percentile(durations, 0.50) : 0.0,
        durations.Count > 0 ? Percentile(durations, 0.95) : 0.0,
        aggregateThroughput,
        throughputs.Count > 0 ? throughputs.Average() : 0.0,
        throughputs.Count > 0 ? Percentile(throughputs, 0.50) : 0.0,
        throughputs.Count > 0 ? Percentile(throughputs, 0.95) : 0.0,
        runMedians.Count >= 2 ? runMedians[^1] - runMedians[0] : 0.0);
}

static EvidenceComparison CompareRunEvidence(RunVariantEvidence candidate, RunVariantEvidence? control)
{
    if (candidate.ValidRuns == 0)
    {
        return new EvidenceComparison("INVALID", "-", "-");
    }

    if (control is null)
    {
        return IsBaselineVariant(candidate.VariantName)
            ? new EvidenceComparison("CONTROL", "-", "-")
            : new EvidenceComparison("INCONCLUSIVE", "-", "-");
    }

    if (control.ValidRuns == 0)
    {
        return new EvidenceComparison("INCONCLUSIVE", "-", "-");
    }

    var deltaSeconds = control.MedianSeconds - candidate.MedianSeconds;
    var deltaPercent = control.MedianSeconds > 0 ? deltaSeconds / control.MedianSeconds * 100.0 : 0.0;
    var noiseFloor = Math.Max(control.SpreadSeconds, candidate.SpreadSeconds);
    var verdict = GetDeltaVerdict(deltaSeconds, deltaPercent, noiseFloor, gatePercent: 10.0);
    return new EvidenceComparison(
        verdict,
        $"{FormatSignedPercent(deltaPercent)} ({FormatSignedDurationSeconds(deltaSeconds)})",
        FormatDurationHuman(noiseFloor));
}

static EvidenceComparison CompareBucketEvidence(BucketVariantEvidence candidate, BucketVariantEvidence? control)
{
    if (candidate.RecordCount == 0)
    {
        return new EvidenceComparison("INVALID", "-", "-");
    }

    if (control is null)
    {
        return IsBaselineVariant(candidate.VariantName)
            ? new EvidenceComparison("CONTROL", "-", "-")
            : new EvidenceComparison("INCONCLUSIVE", "-", "-");
    }

    if (control.RecordCount == 0)
    {
        return new EvidenceComparison("INCONCLUSIVE", "-", "-");
    }

    var deltaMilliseconds = control.MedianDurationMilliseconds - candidate.MedianDurationMilliseconds;
    var deltaPercent = control.MedianDurationMilliseconds > 0
        ? deltaMilliseconds / control.MedianDurationMilliseconds * 100.0
        : 0.0;
    var noiseFloor = Math.Max(control.RunMedianSpreadMilliseconds, candidate.RunMedianSpreadMilliseconds);
    var verdict = GetDeltaVerdict(deltaMilliseconds, deltaPercent, noiseFloor, gatePercent: 10.0);
    return new EvidenceComparison(
        verdict,
        $"{FormatSignedPercent(deltaPercent)} ({deltaMilliseconds:+0.###;-0.###;0} ms)",
        $"{noiseFloor:0.###} ms");
}

static string GetDeltaVerdict(double delta, double deltaPercent, double noiseFloor, double gatePercent)
{
    if (delta < -noiseFloor)
    {
        return "REGRESSION";
    }

    if (delta <= noiseFloor)
    {
        return "INCONCLUSIVE";
    }

    return deltaPercent >= gatePercent ? "PASS" : "FAIL";
}

static string? FindMatchedControlVariant(string variant, IReadOnlyList<string> variants, IReadOnlyDictionary<string, string> matchedControls)
{
    if (IsBaselineVariant(variant))
    {
        return null;
    }

    if (matchedControls.TryGetValue(variant, out var configuredControl) && !string.IsNullOrWhiteSpace(configuredControl))
    {
        return variants.FirstOrDefault(v => string.Equals(v, configuredControl, StringComparison.OrdinalIgnoreCase));
    }

    if (variant.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase))
    {
        var buffer = ExtractBatchBufferLabel(variant);
        if (buffer is not null)
        {
            var staged = variants.FirstOrDefault(v =>
                v.Contains("StagedWriteBatch", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ExtractBatchBufferLabel(v), buffer, StringComparison.OrdinalIgnoreCase));
            if (staged is not null)
            {
                return staged;
            }
        }

        return null;
    }

    return FindBaselineVariant(variants);
}

static string? ExtractBatchBufferLabel(string variant)
{
    var match = System.Text.RegularExpressions.Regex.Match(variant, @"Batch(?<size>\d+MiB)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return match.Success ? match.Groups["size"].Value : null;
}

static string? FindBaselineVariant(IReadOnlyList<string> variants)
{
    var preferred = new[] { "Control_BaselineAuto", "BaselineAuto", "ScenarioDefaults" };
    foreach (var name in preferred)
    {
        var match = variants.FirstOrDefault(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }
    }

    return variants.FirstOrDefault(IsBaselineVariant);
}

static bool IsBaselineVariant(string variant) =>
    variant.Contains("Baseline", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(variant, "ScenarioDefaults", StringComparison.OrdinalIgnoreCase);

static bool IsSuccessfulRun(BenchmarkRunRecord run) =>
    string.Equals(run.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
    run.FailedFiles == 0 &&
    run.ExceptionType is null;

static string FormatSignedPercent(double value) =>
    value switch
    {
        > 0 => $"+{value:0.0}%",
        < 0 => $"{value:0.0}%",
        _ => "0.0%",
    };

static string FormatSignedDurationSeconds(double seconds) =>
    seconds switch
    {
        > 0 => $"+{FormatDurationHuman(seconds)}",
        < 0 => $"-{FormatDurationHuman(Math.Abs(seconds))}",
        _ => "0s",
    };

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

internal sealed record RunVariantEvidence(
    string VariantName,
    int TotalRuns,
    int ValidRuns,
    int InvalidRuns,
    double MedianSeconds,
    double MeanSeconds,
    double MinSeconds,
    double MaxSeconds,
    double SpreadSeconds);

internal sealed record BucketVariantEvidence(
    string BucketLabel,
    string VariantName,
    int RecordCount,
    long TotalBytes,
    double MeanDurationMilliseconds,
    double MedianDurationMilliseconds,
    double P95DurationMilliseconds,
    double AggregateThroughputMiBPerSecond,
    double MeanThroughputMiBPerSecond,
    double P50ThroughputMiBPerSecond,
    double P95ThroughputMiBPerSecond,
    double RunMedianSpreadMilliseconds)
{
    public static BucketVariantEvidence Empty(string bucketLabel, string variantName) =>
        new(bucketLabel, variantName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed record EvidenceComparison(
    string Verdict,
    string DeltaText,
    string NoiseText);

internal static class FileSizeBuckets
{
    public static IReadOnlyList<FileSizeBucket> All { get; } =
    [
        new FileSizeBucket(0, 4 * 1024, "Sub4KiB"),
        new FileSizeBucket(4 * 1024 + 1, 16 * 1024, "Sub16KiB"),
        new FileSizeBucket(16 * 1024 + 1, 64 * 1024, "Sub64KiB"),
        new FileSizeBucket(64 * 1024 + 1, 256 * 1024, "Sub256KiB"),
        new FileSizeBucket(256 * 1024 + 1, 512 * 1024, "Sub512KiB"),
        new FileSizeBucket(512 * 1024 + 1, 1024 * 1024, "Sub1MiB"),
        new FileSizeBucket(1024 * 1024 + 1, 4L * 1024 * 1024, "Sub4MiB"),
        new FileSizeBucket(4L * 1024 * 1024 + 1, long.MaxValue, "Tail"),
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

internal sealed class VariantNameComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var matchX = System.Text.RegularExpressions.Regex.Match(x, @"\d+");
        var matchY = System.Text.RegularExpressions.Regex.Match(y, @"\d+");
        
        long numX = matchX.Success ? long.Parse(matchX.Value) : -1;
        long numY = matchY.Success ? long.Parse(matchY.Value) : -1;

        // If both have numbers and the numbers differ, sort by the number
        if (numX != -1 && numY != -1 && numX != numY)
        {
            return numX.CompareTo(numY);
        }

        // Fallback to string comparison
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
