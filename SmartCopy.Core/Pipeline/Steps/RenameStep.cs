using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RenameStep : IPipelineStep
{
    public RenameStep(string pattern)
    {
        Pattern = pattern;
    }

    public string Pattern { get; set; }

    public StepKind StepType => StepKind.Rename;
    public bool IsExecutable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => $"Pattern: {Pattern}";

    public TransformStepConfig Config => new(StepType, new JsonObject { ["pattern"] = Pattern });

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Rename");
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            context.AddBlockingIssue("Step.RenamePatternRequired", "Rename requires a non-empty pattern.");
        }
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            ApplyToContext(context.GetNodeContext(node));
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            ApplyToContext(context.GetNodeContext(node));
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }

    private void ApplyToContext(PipelineContext context)
    {
        if (context.PathSegments.Length == 0) return;

        var filename = context.PathSegments[^1];
        var renamed = RenameFilename(filename);
        context.PathSegments = [.. context.PathSegments[..^1], renamed];
    }

    private string RenameFilename(string filename)
    {
        var extension = Path.GetExtension(filename);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);

        var renamedFileName = Pattern
            .Replace("{name}", nameWithoutExtension, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(renamedFileName))
            && !string.IsNullOrWhiteSpace(extension)
            && !Pattern.Contains("{ext}", StringComparison.OrdinalIgnoreCase))
        {
            renamedFileName += extension;
        }

        return renamedFileName;
    }
}
