using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Strategy;

namespace SmartCopy.Benchmarks;

/// <summary>
/// Copy executor seam: <see cref="BenchmarkTask.RunCopyAsync"/> delegates the actual copy to an
/// <see cref="ICopyExecutor"/> selected from the run mode. <see cref="PrototypeCopyExecutor"/>
/// preserves the legacy <c>--mode benchmark</c> branch verbatim (routing batch/direct-write
/// variants through <see cref="BenchmarkCopyRunner"/>, everything else through
/// <see cref="PipelineRunner"/>). <see cref="ProductionCopyExecutor"/> always drives the
/// production runner (<see cref="PipelineRunner.ExecuteAsync"/>) so the batched/routed
/// <see cref="OperationalSettings"/> flow through <see cref="DefaultCopyStrategyPolicy"/> —
/// this is what <c>--mode validation</c> measures.
/// </summary>
internal interface ICopyExecutor
{
    Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct);
}

/// <summary>
/// The <c>--mode benchmark</c> path: when a variant carries batch/direct-write settings it is
/// diverted to the <see cref="BenchmarkCopyRunner"/> prototype (which consumes those settings as
/// separate arguments); otherwise it runs the production streaming path. This is the verbatim
/// equivalent of the historical <c>RunCopyAsync</c> branch.
/// </summary>
internal sealed class PrototypeCopyExecutor : ICopyExecutor
{
    private readonly string _destinationPath;
    private readonly OverwriteMode _overwriteMode;
    private readonly long _directWriteThresholdBytes;
    private readonly long _bufferBatchBytes;
    private readonly long _batchEligibilityThresholdBytes;
    private readonly bool _skipExistsCheckForOverwrite;

    public PrototypeCopyExecutor(
        string destinationPath,
        OverwriteMode overwriteMode,
        long directWriteThresholdBytes,
        long bufferBatchBytes,
        long batchEligibilityThresholdBytes,
        bool skipExistsCheckForOverwrite)
    {
        _destinationPath = destinationPath;
        _overwriteMode = overwriteMode;
        _directWriteThresholdBytes = directWriteThresholdBytes;
        _bufferBatchBytes = bufferBatchBytes;
        _batchEligibilityThresholdBytes = batchEligibilityThresholdBytes;
        _skipExistsCheckForOverwrite = skipExistsCheckForOverwrite;
    }

    public Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct)
    {
        if (_directWriteThresholdBytes > 0 || _bufferBatchBytes > 0)
        {
            return BenchmarkCopyRunner.RunAsync(
                job,
                _destinationPath,
                _overwriteMode,
                _directWriteThresholdBytes,
                _bufferBatchBytes,
                _batchEligibilityThresholdBytes,
                _skipExistsCheckForOverwrite,
                job.OperationalSettings.CopyBufferSizeBytes,
                ct);
        }

        var pipelineRunner = new PipelineRunner(new TransformPipeline(
        [
            new CopyStep(_destinationPath, _overwriteMode)
            {
                SkipExistsCheckForOverwrite = _skipExistsCheckForOverwrite,
            },
        ]));
        return pipelineRunner.ExecuteAsync(job, ct);
    }
}

/// <summary>
/// The <c>--mode validation</c> path: always drives the production
/// <see cref="PipelineRunner.ExecuteAsync"/> regardless of variant settings, so batched,
/// direct-write, and destination-routed configurations flow through the real
/// <see cref="DefaultCopyStrategyPolicy"/> and <see cref="BatchedCopyStrategy"/> /
/// <see cref="StreamingCopyStrategy"/> — the path users actually run. Logs the resolved
/// <see cref="OperationalSettings"/> and the policy's resolved buffer at startup as the
/// run-1 sanity check.
/// </summary>
internal sealed class ProductionCopyExecutor : ICopyExecutor
{
    private readonly string _destinationPath;
    private readonly OverwriteMode _overwriteMode;
    private readonly bool _skipExistsCheckForOverwrite;
    private readonly OperationalSettings _settings;
    private readonly IFileSystemProvider _sourceProvider;
    private readonly IFileSystemProvider _destinationProvider;

    public ProductionCopyExecutor(
        string destinationPath,
        OverwriteMode overwriteMode,
        bool skipExistsCheckForOverwrite,
        OperationalSettings settings,
        IFileSystemProvider sourceProvider,
        IFileSystemProvider destinationProvider)
    {
        _destinationPath = destinationPath;
        _overwriteMode = overwriteMode;
        _skipExistsCheckForOverwrite = skipExistsCheckForOverwrite;
        _settings = settings;
        _sourceProvider = sourceProvider;
        _destinationProvider = destinationProvider;
    }

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct)
    {
        await LogResolvedSettingsAsync(ct);

        var pipelineRunner = new PipelineRunner(new TransformPipeline(
        [
            new CopyStep(_destinationPath, _overwriteMode)
            {
                SkipExistsCheckForOverwrite = _skipExistsCheckForOverwrite,
            },
        ]));
        return await pipelineRunner.ExecuteAsync(job, ct);
    }

    /// <summary>
    /// Run-1 sanity check: resolve the strategy the production path will actually use and log its
    /// buffer/batch/durability decisions. The policy's resolved buffer can differ from
    /// <see cref="OperationalSettings.CopyBufferSizeBytes"/> when destination routing is enabled.
    /// </summary>
    private async Task LogResolvedSettingsAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine(
                $"Production OperationalSettings: buffer={_settings.CopyBufferSizeBytes} bytes, " +
                $"batchBuffer={_settings.BatchBufferBytes} bytes, " +
                $"batchEligibilityCeiling={_settings.BatchEligibilityCeilingBytes} bytes, " +
                $"tinyFileFastPathThreshold={_settings.TinyFileFastPathThresholdBytes} bytes, " +
                $"destinationRouting={_settings.DestinationRoutingEnabled}, " +
                $"writeMode={_settings.WriteMode}, " +
                $"arrayPool={_settings.UseArrayPoolForManualLoop}, " +
                $"preallocate={_settings.PreallocateDestinationFile}");

            var source = await _sourceProvider.GetClassificationAsync(ct);
            var target = await _destinationProvider.GetClassificationAsync(ct);
            var sameVolume = _sourceProvider.VolumeId is { } vid && _destinationProvider.VolumeId == vid;
            var strategy = DefaultCopyStrategyPolicy.Instance.Resolve(new CopyStrategyInputs(
                _settings, source, target, _sourceProvider.Capabilities, _destinationProvider.Capabilities, sameVolume));

            if (strategy is CopyStrategyBase baseStrategy)
            {
                var resolved = baseStrategy.Settings;
                Console.WriteLine(
                    $"Resolved strategy: {strategy.GetType().Name}, " +
                    $"buffer={resolved.CopyBufferSizeBytes} bytes, " +
                    $"batchBuffer={resolved.BatchBufferBytes} bytes, " +
                    $"preallocate={resolved.PreallocateDestinationFile} " +
                    $"(source={source.MediaType}/{source.InterfaceType}, " +
                    $"target={target.MediaType}/{target.InterfaceType}, sameVolume={sameVolume})");
            }
        }
        catch (Exception ex)
        {
            // Sanity log must never break the run — the engine will re-resolve and surface real errors.
            Console.Error.WriteLine($"Warning: could not log resolved strategy: {ex.Message}");
        }
    }
}
