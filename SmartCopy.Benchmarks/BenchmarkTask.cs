using System.Text;
using System.Text.Json;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Scanning;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Progress;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Strategy;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkTask
{
    private readonly BenchmarkScenario _scenario;
    private readonly BenchmarkVariant _variant;
    private readonly int _nextRunIndex;
    private readonly BenchmarkConfig _config;
    private readonly BenchmarkCliOptions _cliOptions;
    private readonly SessionPaths _paths;
    private readonly List<BenchmarkRunRecord> _historicalRuns;
    private readonly Func<Task> _onTaskListUpdate;
    private readonly CancellationToken _ct;

    private readonly BenchmarkState _state = new();
    private string _sourcePath = "";
    private string _destinationPath = "";
    private int? _pathIndex;
    private LocalFileSystemProvider? _sourceProvider;
    private FileSystemProviderRegistry? _registry;
    private OperationalSettings? _executionSettings;
    private OperationalSettings? _recordedSettings;

    internal BenchmarkTask(
        BenchmarkSelection selection,
        BenchmarkConfig config,
        BenchmarkCliOptions cliOptions,
        SessionPaths paths,
        List<BenchmarkRunRecord> historicalRuns,
        Func<Task> onTaskListUpdate,
        CancellationToken ct)
    {
        _scenario = selection.Scenario;
        _variant = selection.Variant;
        _nextRunIndex = selection.NextRunIndex;
        _config = config;
        _cliOptions = cliOptions;
        _paths = paths;
        _historicalRuns = historicalRuns;
        _onTaskListUpdate = onTaskListUpdate;
        _ct = ct;
    }

    internal async Task ExecuteAsync(BenchmarkScenario? lastScenario)
    {
        CheckColdCacheBoundary(lastScenario);
        await ResolvePathsAsync();
        BenchmarkHelpers.ValidatePaths(_sourcePath, _destinationPath);
        var runStartedUtc = DateTime.UtcNow;
        PrintHeader();
        await WriteInProgressRecordAsync(runStartedUtc);

        try
        {
            if (_config.ClearDestinationBeforeRun)
                ClearDestination("Clearing destination contents before benchmark...");

            SetupProviders();
            await ResolveAndValidateRunSettingsAsync();
            await ScanAsync();
            await RunCopyAsync();
            if (ShouldWriteJournal())
                await WriteJournalAsync(runStartedUtc);
            await WriteSuccessRecordAsync(runStartedUtc);

            Console.WriteLine();

            if (_config.ClearDestinationAfterRun)
                ClearDestination("Clearing destination contents after benchmark (post-clear)...");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.Error.WriteLine($"Error during benchmark run: {ex.Message}");
            await WriteFailureRecordAsync(runStartedUtc, ex);

            if (_config.ClearDestinationAfterRun)
                TryClearDestination();

            throw;
        }
    }

    private void CheckColdCacheBoundary(BenchmarkScenario? lastScenario)
    {
        if (lastScenario is null || lastScenario.UsePathPool == _scenario.UsePathPool)
            return;

        Console.WriteLine();
        Console.WriteLine($"--- Cold cache boundary before {_scenario.Name} ---");
        Console.WriteLine(_scenario.UsePathPool
            ? "Returning to path-pool runs. Reboot to clear the OS file cache, then run again..."
            : "Switching to a non-pool run. Reboot to clear the OS file cache, then run again...");
        if (!Console.IsInputRedirected)
        {
            Console.ReadKey(intercept: true);
        }
        Console.WriteLine();
    }

    private async Task ResolvePathsAsync()
    {
        var baseSourcePath = Path.GetFullPath(
            !string.IsNullOrWhiteSpace(_scenario.SourcePath) ? _scenario.SourcePath : _config.SourcePath);
        _sourcePath = baseSourcePath;
        _destinationPath = Path.GetFullPath(_scenario.DestinationPath);
        _pathIndex = null;

        if (!_scenario.UsePathPool)
            return;

        var normalizedBase = baseSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!_config.SourcePools.TryGetValue(normalizedBase, out var pool) || pool.Count == 0)
            pool = await BenchmarkHelpers.DiscoverAndOrderPoolAsync(_config, baseSourcePath, _cliOptions.ConfigPath, _ct);

        var usedCount = _historicalRuns.Count(r =>
            BenchmarkHelpers.IsTerminalRun(r) &&
            pool.Contains(r.SourcePath, StringComparer.OrdinalIgnoreCase));
        _sourcePath = pool[usedCount % pool.Count];

        var folderName = Path.GetFileName(_sourcePath);
        var underscorePos = folderName.LastIndexOf('_');
        if (underscorePos >= 0 && int.TryParse(folderName.AsSpan(underscorePos + 1), out var parsedIndex))
        {
            _pathIndex = parsedIndex;
            _destinationPath = _destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + $"_{parsedIndex:D2}";
        }
        else
        {
            throw new InvalidOperationException($"Could not extract numeric index from path pool folder name: {folderName}");
        }
    }

    private void PrintHeader()
    {
        var useProductionExecutor = UseProductionExecutor();
        var providerOptions = useProductionExecutor
            ? _variant.CreateProductionOperationalSettings(_scenario)
            : _variant.CreateOperationalSettings(_scenario);
        var modeLabel = _cliOptions.Mode.ToString().ToLowerInvariant();
        var executorLabel = useProductionExecutor ? "production" : "prototype";
        Console.WriteLine();
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine($"Scenario: {_scenario.Name}");
        Console.WriteLine($"Variant:  {_variant.Name} (run {_nextRunIndex}) [{modeLabel}/{executorLabel}]");
        Console.WriteLine("-------------------------------------------------------------------");

        var sourceDisplay = _pathIndex is null
            ? _sourcePath
            : $"{_sourcePath} [Folder Index: {_pathIndex:D2}]";

        Console.WriteLine($"Source:   {sourceDisplay}");
        Console.WriteLine($"Target:   {_destinationPath}");
        Console.WriteLine();
        Console.WriteLine("Provider Settings:");
        Console.WriteLine($"  Buffer:                  {BenchmarkHelpers.FormatSize(providerOptions.CopyBufferSizeBytes)}");
        Console.WriteLine($"  Small file threshold:    {BenchmarkHelpers.FormatSize(providerOptions.SmallFileProgressThresholdBytes)}");
        Console.WriteLine($"  Batch traversal order:  {providerOptions.BatchTraversalOrder}");
        Console.WriteLine($"  Batch flush policy:     {providerOptions.BatchFlushPolicy}");
    }

    private async Task WriteInProgressRecordAsync(DateTime runStartedUtc)
    {
        var record = BenchmarkRunRecord.CreateInProgress(
            _scenario, _variant, _sourcePath, _destinationPath,
            _paths.ArtifactDirectory, runStartedUtc, _cliOptions.Notes, _nextRunIndex,
            GetRecordedSettings());

        await AppendJsonLineAsync(_paths.ResultsPath, record, _ct);
        _historicalRuns.Add(record);
        await _onTaskListUpdate();
    }

    private void SetupProviders()
    {
        _sourceProvider = new LocalFileSystemProvider(_sourcePath);
        var destinationProvider = new LocalFileSystemProvider(_destinationPath);
        _registry = new FileSystemProviderRegistry();
        _registry.Register(_sourceProvider);
        _registry.Register(destinationProvider);
    }

    private async Task ResolveAndValidateRunSettingsAsync()
    {
        var useProductionExecutor = UseProductionExecutor();

        _executionSettings = useProductionExecutor
            ? _variant.CreateProductionOperationalSettings(_scenario)
            : _variant.CreateOperationalSettings(_scenario);
        _recordedSettings = _executionSettings;

        if (!useProductionExecutor)
        {
            return;
        }

        var destinationProvider = _registry!.ResolveProvider(_destinationPath)
            ?? throw new InvalidOperationException($"No destination provider for {_destinationPath}.");
        var source = await _sourceProvider!.GetClassificationAsync(_ct);
        var target = await destinationProvider.GetClassificationAsync(_ct);
        var sourceVolumeId = _sourceProvider.VolumeId;
        var targetVolumeId = destinationProvider.VolumeId;
        var sameVolume = sourceVolumeId is { } vid && targetVolumeId == vid;
        var strategy = DefaultCopyStrategyPolicy.Instance.Resolve(new CopyStrategyInputs(
            _executionSettings,
            source,
            target,
            _sourceProvider.Capabilities,
            destinationProvider.Capabilities,
            sameVolume,
            sourceVolumeId,
            targetVolumeId));

        _recordedSettings = strategy is CopyStrategyBase baseStrategy
            ? baseStrategy.Settings
            : _executionSettings;

        Console.WriteLine();
        Console.WriteLine($"Resolved Production Settings ({strategy.GetType().Name}):");

        void PrintSetting(string label, string resolvedValue, string requestedValue)
        {
            if (resolvedValue == requestedValue)
                Console.WriteLine($"  {label,-24} {resolvedValue}");
            else
                Console.WriteLine($"  {label,-24} {resolvedValue} (requested: {requestedValue})");
        }

        PrintSetting("Buffer:", BenchmarkHelpers.FormatSize(_recordedSettings.CopyBufferSizeBytes), BenchmarkHelpers.FormatSize(_executionSettings.CopyBufferSizeBytes));
        PrintSetting("Batch buffer:", BenchmarkHelpers.FormatSize(_recordedSettings.BatchBufferBytes), BenchmarkHelpers.FormatSize(_executionSettings.BatchBufferBytes));
        PrintSetting("Batch elig. ceiling:", BenchmarkHelpers.FormatSize(_recordedSettings.BatchEligibilityCeilingBytes), BenchmarkHelpers.FormatSize(_executionSettings.BatchEligibilityCeilingBytes));
        PrintSetting("Batch traversal order:", _recordedSettings.BatchTraversalOrder.ToString(), _executionSettings.BatchTraversalOrder.ToString());
        PrintSetting("Batch flush policy:", _recordedSettings.BatchFlushPolicy.ToString(), _executionSettings.BatchFlushPolicy.ToString());
        PrintSetting("Tiny file threshold:", BenchmarkHelpers.FormatSize(_recordedSettings.TinyFileFastPathThresholdBytes), BenchmarkHelpers.FormatSize(_executionSettings.TinyFileFastPathThresholdBytes));
        PrintSetting("Destination routing:", _recordedSettings.DestinationRoutingEnabled.ToString(), _executionSettings.DestinationRoutingEnabled.ToString());

        Console.WriteLine($"  {"Source:",-24} {source}");
        Console.WriteLine($"  {"Target:",-24} {target}");
        Console.WriteLine($"  {"Same volume:",-24} {sameVolume}");

        ValidateExpectedEffectiveSettings(_recordedSettings);
    }

    private OperationalSettings GetExecutionSettings()
    {
        if (_executionSettings is not null)
        {
            return _executionSettings;
        }

        return UseProductionExecutor()
            ? _variant.CreateProductionOperationalSettings(_scenario)
            : _variant.CreateOperationalSettings(_scenario);
    }

    private bool UseProductionExecutor() =>
        _variant.UsePrototypeExecutor switch
        {
            true => false,
            false => true,
            null => _cliOptions.Mode == BenchmarkRunMode.Validation,
        };

    private OperationalSettings GetRecordedSettings() => _recordedSettings ?? GetExecutionSettings();

    private void ValidateExpectedEffectiveSettings(OperationalSettings effectiveSettings)
    {
        var failures = new List<string>();
        CheckExpected(_variant.ExpectedEffectiveCopyBufferSizeBytes, effectiveSettings.CopyBufferSizeBytes, "copyBufferSizeBytes");
        CheckExpected(_variant.ExpectedEffectiveBatchBufferBytes, effectiveSettings.BatchBufferBytes, "batchBufferBytes");
        CheckExpected(_variant.ExpectedEffectiveBatchEligibilityCeilingBytes, effectiveSettings.BatchEligibilityCeilingBytes, "batchEligibilityCeilingBytes");
        CheckExpected(_variant.ExpectedEffectiveBatchTraversalOrder, effectiveSettings.BatchTraversalOrder, "batchTraversalOrder");
        CheckExpected(_variant.ExpectedEffectiveBatchFlushPolicy, effectiveSettings.BatchFlushPolicy, "batchFlushPolicy");
        CheckExpected(_variant.ExpectedEffectiveTinyFileFastPathThresholdBytes, effectiveSettings.TinyFileFastPathThresholdBytes, "tinyFileFastPathThresholdBytes");
        CheckExpected(_variant.ExpectedEffectiveDestinationRoutingEnabled, effectiveSettings.DestinationRoutingEnabled, "destinationRoutingEnabled");

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Variant '{_variant.Name}' resolved unexpected production settings: {string.Join("; ", failures)}.");
        }

        void CheckExpected<T>(T? expected, T actual, string name)
            where T : struct
        {
            if (expected is { } value && !EqualityComparer<T>.Default.Equals(value, actual))
            {
                failures.Add($"{name} expected {value} but resolved {actual}");
            }
        }
    }

    private async Task ScanAsync()
    {
        var scanOptions = new ScanOptions
        {
            IncludeHidden = _config.IncludeHidden,
            FullPreScan = true,
            LazyExpand = false,
            FollowSymlinks = false,
        };

        _state.ScanStopwatch.Start();
        BenchmarkHelpers.UpdateProgress("Scanning source...");
        using (var scanProgress = new ThrottledConsoleProgress<ScanProgress>(p => BenchmarkHelpers.UpdateProgress($"Scanning source: {p.NodesDiscovered} nodes, {p.DirectoriesScanned} dirs")))
        {
            _state.Root = await ScanTreeAsync(_sourceProvider!, _sourcePath, scanOptions, scanProgress, _ct);
        }
        BenchmarkHelpers.UpdateProgress("");
        _state.ScanStopwatch.Stop();

        new SelectionManager().SelectAll(_state.Root);
        _state.Root.BuildStats();
    }

    private async Task RunCopyAsync()
    {
        var resolvedDestinationProvider = _registry!.ResolveProvider(_destinationPath)
            ?? throw new InvalidOperationException($"No destination provider for {_destinationPath}.");

        _state.FreeSpaceBefore = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(_ct);

        var providerOptions = GetExecutionSettings();
        var useProductionExecutor = UseProductionExecutor();
        var overwriteMode = _variant.OverwriteMode ?? _scenario.OverwriteMode;

        var directWriteThresholdBytes = _variant.DirectWriteThresholdBytes ?? _scenario.DirectWriteThresholdBytes ?? 0L;
        var bufferBatchBytes = _variant.BufferBatchBytes ?? _scenario.BufferBatchBytes ?? 0L;
        var batchEligibilityThresholdBytes = _variant.BatchEligibilityThresholdBytes ?? _scenario.BatchEligibilityThresholdBytes ?? 0L;
        var batchOrderByFileSize = providerOptions.BatchTraversalOrder == BatchTraversalOrder.AscendingFileSize;
        var writeSequentialScan = _variant.ProviderWriteSequentialScan ?? _scenario.ProviderWriteSequentialScan ?? false;

        ICopyExecutor executor = useProductionExecutor
            ? new ProductionCopyExecutor(_destinationPath, overwriteMode)
            : new PrototypeCopyExecutor(
                _destinationPath, overwriteMode,
                directWriteThresholdBytes, bufferBatchBytes, batchEligibilityThresholdBytes,
                batchOrderByFileSize, writeSequentialScan);

        BenchmarkHelpers.UpdateProgress("Preparing copy...");
        using (var executeProgress = new ThrottledConsoleProgress<OperationProgress>(p =>
            BenchmarkHelpers.UpdateProgress($"Copying: {p.FilesCompleted}/{p.FilesTotal} files ({BenchmarkHelpers.FormatSize(p.TotalBytesCompleted)}/{BenchmarkHelpers.FormatSize(p.TotalBytes)}), ETR: {BenchmarkHelpers.FormatDuration(p.EstimatedRemaining)}")))
        {
            var gcBefore = BenchmarkGcSnapshot.Capture();
            _state.ExecuteStopwatch.Start();
            try
            {
                _state.Results = await executor.ExecuteAsync(new PipelineJob
                {
                    RootNode = _state.Root!,
                    SourceProvider = _sourceProvider!,
                    ProviderRegistry = _registry!,
                    Progress = executeProgress,
                    CancellationToken = _ct,
                    OperationalSettings = providerOptions,
                }, _ct);
            }
            finally
            {
                _state.ExecuteStopwatch.Stop();
                _state.ExecuteGcStats = BenchmarkGcStats.Between(gcBefore, BenchmarkGcSnapshot.Capture());
            }
        }
        BenchmarkHelpers.UpdateProgress("");

        _state.FreeSpaceAfter = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(_ct);
    }

    private async Task WriteJournalAsync(DateTime runStartedUtc)
    {
        Directory.CreateDirectory(_paths.JournalDirectory);
        var journal = new OperationJournal(_paths.JournalDirectory);
        _state.JournalPath = await journal.WriteAsync(
            _state.Results.Where(r => r.SourceNodeResult != SourceResult.None),
            BuildBenchmarkJournalMetadata(runStartedUtc),
            _ct);
    }

    private bool ShouldWriteJournal() => _variant.WriteJournal;

    private async Task WriteSuccessRecordAsync(DateTime runStartedUtc)
    {
        var record = BenchmarkRunRecord.CreateSuccess(
            _scenario, _variant, _sourcePath, _destinationPath,
            _paths.ArtifactDirectory, runStartedUtc, _state, _cliOptions.Notes, _nextRunIndex,
            GetRecordedSettings());

        await AppendJsonLineAsync(_paths.ResultsPath, record, _ct);
        await AppendFileCopyRecordsAsync(_paths.FileResultsPath, record, _state.Results, _ct);
        _historicalRuns.Add(record);
        await _onTaskListUpdate();

        var totalBytes = _state.Results.Sum(r => r.OutputBytes);
        Console.WriteLine($"Copied {record.CopiedFiles} files ({BenchmarkHelpers.FormatSize(totalBytes)}) in {BenchmarkHelpers.FormatDurationHuman(record.ExecuteDuration.TotalSeconds)}.");
        if (_state.ExecuteGcStats is { } gc)
        {
            Console.WriteLine(
                $"Execute GC: allocated {BenchmarkHelpers.FormatSize(gc.AllocatedBytes)}, " +
                $"collections Gen0/1/2 = {gc.Gen0Collections}/{gc.Gen1Collections}/{gc.Gen2Collections}, " +
                $"heap delta {FormatSignedSize(gc.HeapSizeDeltaBytes)}, fragmentation delta {FormatSignedSize(gc.FragmentedDeltaBytes)}.");
        }
        if (record.FailedFiles > 0 || record.SkippedFiles > 0)
        {
            Console.WriteLine($"Failed: {record.FailedFiles}, Skipped: {record.SkippedFiles}");
        }

        Console.WriteLine($"Results: {_paths.ResultsPath}");
        Console.WriteLine($"File results: {_paths.FileResultsPath}");
        Console.WriteLine(ShouldWriteJournal()
            ? $"Journal: {_state.JournalPath}"
            : "Journal: disabled");

        var convergenceStatus = BenchmarkConvergence.Check(_historicalRuns, _scenario.Name, _variant, _config);
        var spread = BenchmarkConvergence.GetCurrentSpreadPercent(_historicalRuns, _scenario.Name, _variant, _config);

        if (convergenceStatus == BenchmarkConvergence.Status.Converged)
        {
            Console.WriteLine();
            Console.WriteLine($"**** Scenario {_scenario.Name} Variant {_variant.Name} has converged with spread {spread:F2}% ****");
        }
        else if (!double.IsNaN(spread))
        {
            Console.WriteLine($"Current spread: {spread:F2}%");
        }

        static string FormatSignedSize(long bytes)
        {
            if (bytes > 0)
                return $"+{BenchmarkHelpers.FormatSize(bytes)}";
            if (bytes < 0)
                return $"-{BenchmarkHelpers.FormatSize(Math.Abs(bytes))}";
            return "0 B";
        }
    }

    private async Task WriteFailureRecordAsync(DateTime runStartedUtc, Exception ex)
    {
        var record = BenchmarkRunRecord.CreateFailure(
            _scenario, _variant, _sourcePath, _destinationPath,
            _paths.ArtifactDirectory, runStartedUtc, _state, _cliOptions.Notes, _nextRunIndex, ex,
            GetRecordedSettings());

        await AppendJsonLineAsync(_paths.ResultsPath, record, _ct);
        await AppendFileCopyRecordsAsync(_paths.FileResultsPath, record, _state.Results, _ct);
        _historicalRuns.Add(record);
        await _onTaskListUpdate();
    }

    private void ClearDestination(string message)
    {
        BenchmarkHelpers.UpdateProgress(message);
        using (var clearProgress = new ThrottledConsoleProgress<string>(s => BenchmarkHelpers.UpdateProgress($"Clearing: {s}")))
        {
            BenchmarkHelpers.ClearDirectoryContents(_destinationPath, clearProgress);
        }
        BenchmarkHelpers.UpdateProgress("");
    }

    private void TryClearDestination()
    {
        try
        {
            ClearDestination("Clearing destination contents after failed benchmark (post-clear)...");
        }
        catch (Exception clearEx)
        {
            Console.Error.WriteLine($"Warning: Failed to clear destination after benchmark failure: {clearEx.Message}");
        }
    }

    private Dictionary<string, string?> BuildBenchmarkJournalMetadata(DateTime runStartedUtc)
    {
        var useProductionExecutor = UseProductionExecutor();
        var usePrototypeExecutor = !useProductionExecutor;
        var providerOptions = GetRecordedSettings();
        return new Dictionary<string, string?>
        {
            ["recordType"] = "benchmarkRun",
            ["runStatus"] = BenchmarkRunStatus.Completed,
            ["runMode"] = _cliOptions.Mode.ToString(),
            ["usePrototypeExecutor"] = usePrototypeExecutor.ToString(),
            ["useProductionExecutor"] = useProductionExecutor.ToString(),
            ["scenarioName"] = _scenario.Name,
            ["variantName"] = _variant.Name,
            ["sourcePath"] = _sourcePath,
            ["destinationPath"] = _destinationPath,
            ["artifactPath"] = _paths.ArtifactDirectory,
            ["runStartedUtc"] = runStartedUtc.ToString("O"),
            ["hostName"] = Environment.MachineName,
            ["osDescription"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["notes"] = _cliOptions.Notes,
            ["runIndex"] = _nextRunIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerCopyBufferSizeBytes"] = providerOptions.CopyBufferSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerSmallFileProgressThresholdBytes"] = providerOptions.SmallFileProgressThresholdBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerWriteSequentialScan"] = (_variant.ProviderWriteSequentialScan ?? _scenario.ProviderWriteSequentialScan ?? false).ToString(),
            ["bufferBatchBytes"] = (_variant.BufferBatchBytes ?? _scenario.BufferBatchBytes ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["batchEligibilityThresholdBytes"] = (_variant.BatchEligibilityThresholdBytes ?? _scenario.BatchEligibilityThresholdBytes ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["batchTraversalOrder"] = providerOptions.BatchTraversalOrder.ToString(),
            ["batchFlushPolicy"] = providerOptions.BatchFlushPolicy.ToString(),
            ["directWriteThresholdBytes"] = (_variant.DirectWriteThresholdBytes ?? _scenario.DirectWriteThresholdBytes ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["destinationRoutingEnabled"] = (_variant.DestinationRoutingEnabled ?? false).ToString(),
            ["productionBatchBufferBytes"] = providerOptions.BatchBufferBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["productionBatchEligibilityCeilingBytes"] = providerOptions.BatchEligibilityCeilingBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["productionTinyFileFastPathThresholdBytes"] = providerOptions.TinyFileFastPathThresholdBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["journalEnabled"] = ShouldWriteJournal().ToString(),
            ["scanDuration"] = _state.ScanStopwatch.Elapsed.ToString("c"),
            ["executeDuration"] = _state.ExecuteStopwatch.Elapsed.ToString("c"),
            ["copiedFiles"] = _state.Results.Sum(r => r.NumberOfFilesAffected).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["skippedFiles"] = _state.Results.Sum(r => r.NumberOfFilesSkipped).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failedFiles"] = _state.Results.Count(r => !r.IsSuccess).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["outputBytes"] = _state.Results.Sum(r => r.OutputBytes).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["destinationFreeSpaceBeforeBytes"] = _state.FreeSpaceBefore?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["destinationFreeSpaceAfterBytes"] = _state.FreeSpaceAfter?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
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
}
