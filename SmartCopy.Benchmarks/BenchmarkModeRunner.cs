using System.Text;
using System.Text.Json;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Scanning;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Progress;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkModeRunner
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
        var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
        var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
        var taskListPath = Path.Combine(artifactDirectory, fileNames.TaskList);
        var journalDirectory = Path.Combine(artifactDirectory, JournalDirectoryName);

        Directory.CreateDirectory(artifactDirectory);

        var historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);

        if (selection.FreshStart)
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("Fresh start requested via --fresh flag.");
            Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
            Console.WriteLine("-------------------------------------------------------------------");

            await ArchiveResultsAsync(artifactDirectory, selection.ConfigPath, ct);

            // Reload historical runs (should be empty now)
            historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        }
        else if (historicalRuns.Count > 0)
        {
            var hasPending = false;
            foreach (var scenario in config.Scenarios.Where(s => s.Enabled))
            {
                foreach (var variant in config.Variants.Where(v => v.Enabled))
                {
                    var status = CheckConvergence(historicalRuns, scenario.Name, variant, config);
                    if (status == ConvergenceStatus.NotConverged)
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
                Console.WriteLine("All previous benchmark runs in the active scenarios are completed/converged.");
                Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
                Console.WriteLine("-------------------------------------------------------------------");

                await ArchiveResultsAsync(artifactDirectory, selection.ConfigPath, ct);

                // Reload historical runs (should be empty now)
                historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
            }
        }

        await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

        // Print Benchmark Queue & Convergence Status Report
        var candidates = (
            from scenario in config.Scenarios
            where scenario.Enabled
            from variant in config.Variants
            where variant.Enabled
            let successfulRuns = historicalRuns.Count(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name))
            let totalRuns = historicalRuns.Count(r => BenchmarkHelpers.IsTerminalRunForScenarioVariant(r, scenario.Name, variant.Name))
            let lastRunUtc = historicalRuns
                .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                .Select(r => r.RunStartedUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max()
            let nextRunIndex = successfulRuns + 1
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

        Console.WriteLine();
        Console.WriteLine("Benchmark Queue:");
        var eligibleCount = 0;
        foreach (var candidate in candidates)
        {
            var status = CheckConvergence(historicalRuns, candidate.Scenario.Name, candidate.Variant, config);
            if (status == ConvergenceStatus.NotConverged)
            {
                var spread = GetCurrentSpreadPercent(historicalRuns, candidate.Scenario.Name, candidate.Variant, config);
                var spreadText = double.IsNaN(spread) ? "N/A" : $"{spread:F2}%";
                Console.WriteLine($"  • {candidate.Variant.Name} (Run {candidate.NextRunIndex}/{candidate.Variant.DesiredRunCount}, spread: {spreadText})");
                eligibleCount++;
            }
        }
        if (eligibleCount == 0)
        {
            Console.WriteLine("  No variants are eligible to run.");
        }
        Console.WriteLine();

        BenchmarkScenario? lastScenario = null;
        while (true)
        {
            var benchmarkSelection = SelectBenchmarkSelection(config, historicalRuns, selection);
            if (benchmarkSelection is null)
            {
                Console.WriteLine("All eligible benchmark scenarios and variants have completed/converged.");
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
                if (scenario.PathPool == null || scenario.PathPool.Count == 0)
                {
                    await BenchmarkHelpers.DiscoverAndShufflePoolAsync(config, scenario, selection.ConfigPath, ct);
                }

                if (scenario.PathPool is not null && scenario.PathPool.Count > 0)
                {
                    var scenarioTerminalRunCount = historicalRuns.Count(r =>
                        string.Equals(r.ScenarioName, scenario.Name, StringComparison.OrdinalIgnoreCase) &&
                        BenchmarkHelpers.IsTerminalRun(r));
                    
                    var poolPathIndex = scenarioTerminalRunCount % scenario.PathPool.Count;
                    sourcePath = scenario.PathPool[poolPathIndex];

                    var folderSuffix = Path.GetFileName(sourcePath);
                    var indexPart = folderSuffix.Split('_').LastOrDefault();
                    if (int.TryParse(indexPart, out var parsedIndex))
                    {
                        pathIndex = parsedIndex;
                        var suffix = $"_{parsedIndex:D2}";
                        destinationPath = destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + suffix;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Could not extract index from path pool folder name: {folderSuffix}");
                    }
                }
                else
                {
                    throw new InvalidOperationException("UsePathPool is enabled but PathPool is empty.");
                }
            }

            BenchmarkHelpers.ValidatePaths(sourcePath, destinationPath);

            var providerOptions = variant.CreateOperationalSettings(scenario);
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
                ? $"Variant:  {variant.Name} (run {nextRunIndex})"
                : $"Variant:  {variant.Name} (run {nextRunIndex}) [Folder Index: {pathIndex:D2}]");
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
                    using var clearProgress = new ThrottledConsoleProgress<string>(s => BenchmarkHelpers.UpdateProgress($"Clearing: {s}"));
                    BenchmarkHelpers.ClearDirectoryContents(destinationPath, clearProgress);
                    Console.WriteLine(); // Finalize progress line
                }

                Directory.CreateDirectory(journalDirectory);

                var sourceProvider = new LocalFileSystemProvider(sourcePath);
                var destinationProvider = new LocalFileSystemProvider(destinationPath);
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
                using (var scanProgress = new ThrottledConsoleProgress<ScanProgress>(p => BenchmarkHelpers.UpdateProgress($"Scanned: {p.NodesDiscovered} nodes, {p.DirectoriesScanned} dirs")))
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
                    BenchmarkHelpers.UpdateProgress($"Copying: {p.FilesCompleted}/{p.FilesTotal} files ({BenchmarkHelpers.FormatSize(p.TotalBytesCompleted)}/{BenchmarkHelpers.FormatSize(p.TotalBytes)}), ETR: {BenchmarkHelpers.FormatDuration(p.EstimatedRemaining)}")))
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
                            OperationalSettings = providerOptions,
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
                            OperationalSettings = providerOptions,
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

                if (scenario.ClearDestinationAfterRun)
                {
                    Console.WriteLine("Clearing destination contents after benchmark (post-clear)...");
                    using var clearProgress = new ThrottledConsoleProgress<string>(s => BenchmarkHelpers.UpdateProgress($"Clearing: {s}"));
                    BenchmarkHelpers.ClearDirectoryContents(destinationPath, clearProgress);
                    Console.WriteLine(); // Finalize progress line
                }
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

                if (scenario.ClearDestinationAfterRun)
                {
                    try
                    {
                        Console.WriteLine("Clearing destination contents after failed benchmark (post-clear)...");
                        using var clearProgress = new ThrottledConsoleProgress<string>(s => BenchmarkHelpers.UpdateProgress($"Clearing: {s}"));
                        BenchmarkHelpers.ClearDirectoryContents(destinationPath, clearProgress);
                        Console.WriteLine(); // Finalize progress line
                    }
                    catch (Exception clearEx)
                    {
                        Console.Error.WriteLine($"Warning: Failed to clear destination after benchmark failure: {clearEx.Message}");
                    }
                }

                throw;
            }

            lastScenario = scenario;

            // Apply a cooldown if there are more benchmarks left in the queue
            var nextSelection = SelectBenchmarkSelection(config, historicalRuns, selection);
            if (config.CooldownSeconds > 0 && nextSelection is not null)
            {
                Console.WriteLine();
                Console.WriteLine("-------------------------------------------------------------------");
                Console.WriteLine($"Applying {config.CooldownSeconds}-second cooldown to let the drive cache settle and cool...");
                Console.WriteLine("-------------------------------------------------------------------");
                for (int i = config.CooldownSeconds; i > 0; i--)
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
        await AnalysisRunner.RunAsync(workingDirectory, config, selection, ct);
    }

    internal static async Task ArchiveResultsAsync(string artifactDirectory, string configPath, CancellationToken ct)
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
            fileNames.TaskList
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

    private static async Task<DirectoryNode> ScanTreeAsync(
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

    private static Dictionary<string, string?> BuildBenchmarkJournalMetadata(
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
        var providerOptions = variant.CreateOperationalSettings(scenario);
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

    internal static BenchmarkSelection? SelectBenchmarkSelection(
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> historicalRuns,
        BenchmarkCliOptions selection)
    {
        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));

        if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
        {
            scenarioOrder = scenarioOrder
                .Where(name => string.Equals(name, selection.ScenarioName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var scenarioName in scenarioOrder)
        {
            var scenario = config.Scenarios.First(s => string.Equals(s.Name, scenarioName, StringComparison.OrdinalIgnoreCase));

            var candidates = config.Variants
                .Where(v => v.Enabled && 
                            (string.IsNullOrWhiteSpace(selection.VariantName) || 
                             string.Equals(v.Name, selection.VariantName, StringComparison.OrdinalIgnoreCase)))
                .Select(variant =>
                {
                    var successfulRuns = historicalRuns.Count(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name));
                    var totalRuns = historicalRuns.Count(r => BenchmarkHelpers.IsTerminalRunForScenarioVariant(r, scenario.Name, variant.Name));
                    var lastRunUtc = historicalRuns
                        .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                        .Select(r => r.RunStartedUtc)
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Max();
                    var nextRunIndex = successfulRuns + 1;
                    return new BenchmarkSelection(scenario, variant, successfulRuns, totalRuns, lastRunUtc, nextRunIndex);
                })
                .ToList();

            var pending = candidates
                .Where(c => CheckConvergence(historicalRuns, c.Scenario.Name, c.Variant, config) == ConvergenceStatus.NotConverged)
                .ToList();

            if (pending.Count > 0)
            {
                var minRunIndex = pending.Min(c => c.NextRunIndex);
                var roundCandidates = pending.Where(c => c.NextRunIndex == minRunIndex).ToArray();

                Random.Shared.Shuffle(roundCandidates);
                return roundCandidates[0];
            }
        }

        return null;
    }

    internal enum ConvergenceStatus
    {
        NotConverged,
        Converged,
        GaveUp,
    }

    internal static ConvergenceStatus CheckConvergence(
        IReadOnlyList<BenchmarkRunRecord> runs,
        string scenarioName,
        BenchmarkVariant variant,
        BenchmarkConfig config)
    {
        var successfulRuns = runs
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variant.Name))
            .ToList();

        if (successfulRuns.Count < variant.DesiredRunCount)
        {
            return successfulRuns.Count >= variant.DesiredRunCount + config.MaxConvergenceRuns
                ? ConvergenceStatus.GaveUp
                : ConvergenceStatus.NotConverged;
        }

        if (!config.Converge)
        {
            return ConvergenceStatus.Converged;
        }

        // Sort durations ascending
        var durations = successfulRuns
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(d => d)
            .ToList();

        // Slide window of size variant.DesiredRunCount
        var W = variant.DesiredRunCount;
        for (var i = 0; i <= durations.Count - W; i++)
        {
            var window = durations.Skip(i).Take(W).ToList();
            var windowMin = window[0];
            var windowMax = window[W - 1];
            
            // Calculate window median
            double windowMedian;
            if (W % 2 == 1)
            {
                windowMedian = window[W / 2];
            }
            else
            {
                windowMedian = (window[W / 2 - 1] + window[W / 2]) / 2.0;
            }

            var spreadPercent = windowMedian > 0 
                ? ((windowMax - windowMin) / windowMedian) * 100.0 
                : 0.0;

            if (spreadPercent <= config.ConvergenceSpreadPercent)
            {
                return ConvergenceStatus.Converged;
            }
        }

        return successfulRuns.Count >= variant.DesiredRunCount + config.MaxConvergenceRuns
            ? ConvergenceStatus.GaveUp
            : ConvergenceStatus.NotConverged;
    }

    internal static double GetCurrentSpreadPercent(
        IReadOnlyList<BenchmarkRunRecord> runs,
        string scenarioName,
        BenchmarkVariant variant,
        BenchmarkConfig config)
    {
        var successfulRuns = runs
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variant.Name))
            .ToList();

        if (successfulRuns.Count < 2)
        {
            return double.NaN;
        }

        var durations = successfulRuns
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(d => d)
            .ToList();

        var W = Math.Min(variant.DesiredRunCount, durations.Count);
        if (W < 2)
        {
            return double.NaN;
        }

        var minSpreadPercent = double.MaxValue;
        for (var i = 0; i <= durations.Count - W; i++)
        {
            var window = durations.Skip(i).Take(W).ToList();
            var windowMin = window[0];
            var windowMax = window[W - 1];

            double windowMedian;
            if (W % 2 == 1)
            {
                windowMedian = window[W / 2];
            }
            else
            {
                windowMedian = (window[W / 2 - 1] + window[W / 2]) / 2.0;
            }

            var spreadPercent = windowMedian > 0
                ? ((windowMax - windowMin) / windowMedian) * 100.0
                : 0.0;

            if (spreadPercent < minSpreadPercent)
            {
                minSpreadPercent = spreadPercent;
            }
        }

        return minSpreadPercent;
    }

    private static Dictionary<string, int> BuildScenarioPriority(BenchmarkConfig config, IEnumerable<string> scenarioNames)
    {
        var ordered = BenchmarkHelpers.BuildScenarioOrder(config, scenarioNames);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            map[ordered[i]] = i;
        }

        return map;
    }

    private static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(value, JsonOptions.Default);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, ct);
    }

    private static async Task AppendFileCopyRecordsAsync(
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

    private static async Task UpdateTaskListAsync(
        string taskListPath,
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> runs,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Benchmark Task List");
        builder.AppendLine();
        builder.AppendLine($"Source: `{Path.GetFullPath(config.SourcePath)}`");

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
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
                    .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                    .OrderByDescending(r => r.RunStartedUtc)
                    .ThenByDescending(BenchmarkHelpers.IsTerminalRun)
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
                    .Append(BenchmarkHelpers.EscapeTable(scenario.Name)).Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(variant.Name)).Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(Path.GetFullPath(scenario.DestinationPath))).Append(" | ")
                    .Append(variant.DesiredRunCount).Append(" | ")
                    .Append(successfulRuns).Append(" | ")
                    .Append(lastRun?.RunStartedUtc.ToString("O") ?? "-").Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(lastStatus)).AppendLine(" |");
            }
        }

        await File.WriteAllTextAsync(taskListPath, builder.ToString(), ct);
    }
}
