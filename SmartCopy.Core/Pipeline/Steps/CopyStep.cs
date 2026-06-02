using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : IPipelineStep, IHasDestinationPath, IHasFreeSpaceCheck
{
    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public CopyStep(string destinationPath, OverwriteMode overwriteMode = OverwriteMode.Skip)
    {
        DestinationPath = destinationPath;
        OverwriteMode = overwriteMode;
    }

    public OverwriteMode OverwriteMode { get; set; }
    public bool SkipExistsCheckForOverwrite { get; set; }

    internal static CopyStep FromConfig(TransformStepConfig config) =>
        new(config.GetRequired("destinationPath"),
            config.ParseEnum("overwriteMode", OverwriteMode.Skip));

    public TransformStepConfig Config => new(StepType, new JsonObject 
    { 
        ["destinationPath"] = DestinationPath,
        ["overwriteMode"] = OverwriteMode.ToString()
    });

    public string AutoSummary => HasDestinationPath ? $"Copy to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();
    public string Description => HasDestinationPath ? $"Copy to {DestinationPath} ({OverwriteMode})" : "Destination required";

    private string? _destinationPath;
    public string? DestinationPath 
    { 
        get => _destinationPath; 
        set => _destinationPath = value; 
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    public FreeSpaceValidationResult? ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        FreeSpaceCache freeSpaceCache)
    {
        if (bytesNeeded <= 0) return null;
        if (DestinationPath is null) return null;

        var target = registry.ResolveProvider(DestinationPath);
        if (target is null) return null;

        var cachedFreeSpace = freeSpaceCache.GetForProvider(target);
        if (cachedFreeSpace is null) return null;

        return new FreeSpaceValidationResult(bytesNeeded, cachedFreeSpace.Value, target.RootPath);
    }

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Copy");
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            context.AddBlockingIssue("Step.MissingDestination", "Copy requires a destination path.");
        }
        context.AddFreeSpaceWarning(this);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            yield break;
        }

        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node is DirectoryNode)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException($"No IFileSystemProvider for path {DestinationPath}");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);

            var destinationExists = await targetProvider.ExistsAsync(destination, ct);
            if (destinationExists && OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    DestinationPath: destination,
                    NumberOfFilesSkipped: 1,
                    InputBytes: node.Size);
                continue;
            }

            var destResult = destinationExists
                ? DestinationResult.Overwritten
                : DestinationResult.Created;

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Copied,
                DestinationPath: destination,
                DestinationResult: destResult,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            throw new InvalidOperationException("DestinationPath must be set for CopyStep.");
        }

        var targetProvider = context.GetNodeContext(context.RootNode).ResolveProvider(DestinationPath)
            ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        await using var _ = targetProvider.BeginBulkWriteAsync();

        var resolved = context.OperationalSettings
            .WithProviderConstraints(context.SourceProvider.Capabilities, targetProvider.Capabilities);
        var useBatch = resolved.BatchBufferBytes > 0;

        if (useBatch)
        {
            await foreach (var result in ApplyBatchedAsync(context, targetProvider, resolved, ct))
                yield return result;
        }
        else
        {
            await foreach (var result in ApplyUnbatchedAsync(context, targetProvider, resolved, ct))
                yield return result;
        }
    }

    private async IAsyncEnumerable<TransformResult> ApplyUnbatchedAsync(
        IStepContext context,
        IFileSystemProvider targetProvider,
        OperationalSettings resolved,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node.IsDirectory)
            {
                yield return new TransformResult(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var destination = targetProvider.JoinPath(DestinationPath!, nodeCtx.PathSegments);
            var destResult = await ResolveDestResultAsync(targetProvider, destination, ct);
            if (destResult is null)
            {
                yield return SkippedResult(node, destination);
                continue;
            }

            yield return await CopySingleFileAsync(context, node, destination, destResult.Value, targetProvider, resolved, ct);
        }
    }

    private async IAsyncEnumerable<TransformResult> ApplyBatchedAsync(
        IStepContext context,
        IFileSystemProvider targetProvider,
        OperationalSettings resolved,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var buffer = new BatchCopyBuffer(resolved.BatchBufferBytes);

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node.IsDirectory)
            {
                yield return new TransformResult(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var destination = targetProvider.JoinPath(DestinationPath!, nodeCtx.PathSegments);
            var destResult = await ResolveDestResultAsync(targetProvider, destination, ct);
            if (destResult is null)
            {
                yield return SkippedResult(node, destination);
                continue;
            }

            if (buffer.WouldFitEver(node.Size))
            {
                var fileSize = (int)node.Size;
                if (!buffer.HasSpaceFor(fileSize))
                {
                    await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, resolved, ct))
                        yield return r;
                }

                string? readError = null;
                try
                {
                    await using var src = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
                    await buffer.AccumulateAsync(src, fileSize, destination, destResult.Value, node, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    context.MarkFailed(node);
                    readError = ex.Message;
                }

                if (readError is not null)
                {
                    yield return new TransformResult(IsSuccess: false, SourceNode: node,
                        SourceNodeResult: SourceResult.Skipped, ErrorMessage: readError);
                }
            }
            else
            {
                // File too large for any batch — flush pending entries then copy normally.
                await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, resolved, ct))
                    yield return r;

                yield return await CopySingleFileAsync(context, node, destination, destResult.Value, targetProvider, resolved, ct);
            }
        }

        await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, resolved, ct))
            yield return r;
    }

    private static async IAsyncEnumerable<TransformResult> FlushBatchAsync(
        BatchCopyBuffer buffer,
        IFileSystemProvider targetProvider,
        IStepContext context,
        OperationalSettings resolved,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!buffer.HasEntries)
            yield break;

        foreach (var entry in buffer.Entries)
        {
            IProgress<long>? progress = null;
            if (context is IFileTransferProgressSink sink)
                progress = new DelegateProgress<long>(b => sink.ReportFileTransferBytes(entry.Node, b, entry.Length));

            string? error = null;
            try
            {
                using var ms = buffer.OpenSegmentStream(entry);
                await targetProvider.WriteAsync(entry.Destination, ms, progress, resolved, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.MarkFailed(entry.Node);
                error = ex.Message;
            }

            yield return error is null
                ? new TransformResult(IsSuccess: true, SourceNode: entry.Node,
                    SourceNodeResult: SourceResult.Copied, DestinationPath: entry.Destination,
                    DestinationResult: entry.DestResult, NumberOfFilesAffected: 1,
                    InputBytes: entry.Length, OutputBytes: entry.Length)
                : new TransformResult(IsSuccess: false, SourceNode: entry.Node,
                    SourceNodeResult: SourceResult.Skipped, ErrorMessage: error);
        }

        buffer.Reset();
    }

    private static async Task<TransformResult> CopySingleFileAsync(
        IStepContext context,
        DirectoryTreeNode node,
        string destination,
        DestinationResult destResult,
        IFileSystemProvider targetProvider,
        OperationalSettings resolved,
        CancellationToken ct)
    {
        string? copyError = null;
        try
        {
            IProgress<long>? writeProgress = null;
            if (context is IFileTransferProgressSink progressSink)
            {
                writeProgress = new DelegateProgress<long>(
                    bytes => progressSink.ReportFileTransferBytes(node, bytes, node.Size));
            }

            await using var sourceStream = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
            await targetProvider.WriteAsync(destination, sourceStream, writeProgress, resolved, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            copyError = ex.Message;
        }

        if (copyError is not null)
        {
            context.MarkFailed(node);
            return new TransformResult(IsSuccess: false, SourceNode: node,
                SourceNodeResult: SourceResult.Skipped, ErrorMessage: copyError);
        }

        return new TransformResult(IsSuccess: true, SourceNode: node,
            SourceNodeResult: SourceResult.Copied, DestinationPath: destination,
            DestinationResult: destResult, NumberOfFilesAffected: 1,
            InputBytes: node.Size, OutputBytes: node.Size);
    }

    /// <summary>
    /// Returns the <see cref="DestinationResult"/> for the file, or null if the file
    /// should be skipped (OverwriteMode.Skip and destination exists).
    /// </summary>
    private async Task<DestinationResult?> ResolveDestResultAsync(
        IFileSystemProvider targetProvider,
        string destination,
        CancellationToken ct)
    {
        if (SkipExistsCheckForOverwrite && OverwriteMode != OverwriteMode.Skip)
            return DestinationResult.Written;

        var exists = await targetProvider.ExistsAsync(destination, ct);
        if (exists && OverwriteMode == OverwriteMode.Skip)
            return null;

        return exists ? DestinationResult.Overwritten : DestinationResult.Created;
    }

    private static TransformResult SkippedResult(DirectoryTreeNode node, string destination) =>
        new(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.Skipped,
            DestinationPath: destination, NumberOfFilesSkipped: 1, InputBytes: node.Size);
}
