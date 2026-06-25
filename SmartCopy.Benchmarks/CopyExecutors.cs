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
    private readonly bool _writeSequentialScan;

    public PrototypeCopyExecutor(
        string destinationPath,
        OverwriteMode overwriteMode,
        long directWriteThresholdBytes,
        long bufferBatchBytes,
        long batchEligibilityThresholdBytes,
        bool writeSequentialScan)
    {
        _destinationPath = destinationPath;
        _overwriteMode = overwriteMode;
        _directWriteThresholdBytes = directWriteThresholdBytes;
        _bufferBatchBytes = bufferBatchBytes;
        _batchEligibilityThresholdBytes = batchEligibilityThresholdBytes;
        _writeSequentialScan = writeSequentialScan;
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
                job.OperationalSettings.CopyBufferSizeBytes,
                _writeSequentialScan,
                ct);
        }

        var pipelineRunner = new PipelineRunner(new TransformPipeline(
        [
            new CopyStep(_destinationPath, _overwriteMode),
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
    private readonly OperationalSettings _settings;
    private readonly IFileSystemProvider _sourceProvider;
    private readonly IFileSystemProvider _destinationProvider;

    public ProductionCopyExecutor(
        string destinationPath,
        OverwriteMode overwriteMode,
        OperationalSettings settings,
        IFileSystemProvider sourceProvider,
        IFileSystemProvider destinationProvider)
    {
        _destinationPath = destinationPath;
        _overwriteMode = overwriteMode;
        _settings = settings;
        _sourceProvider = sourceProvider;
        _destinationProvider = destinationProvider;
    }

    public Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct)
    {
        var pipelineRunner = new PipelineRunner(new TransformPipeline(
        [
            new CopyStep(_destinationPath, _overwriteMode),
        ]));
        return pipelineRunner.ExecuteAsync(job, ct);
    }
}
