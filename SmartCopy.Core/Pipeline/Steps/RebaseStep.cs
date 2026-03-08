using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RebaseStep : IPipelineStep
{
    private string _stripPrefix = string.Empty;
    private string _addPrefix = string.Empty;
    private string[] _stripSegments = [];
    private string[] _addSegments = [];

    public RebaseStep(string stripPrefix, string addPrefix)
    {
        StripPrefix = stripPrefix ?? string.Empty;
        AddPrefix = addPrefix ?? string.Empty;
    }

    public string StripPrefix
    {
        get => _stripPrefix;
        set
        {
            _stripPrefix = value ?? string.Empty;
            _stripSegments = SplitPrefix(_stripPrefix);
        }
    }

    public string AddPrefix
    {
        get => _addPrefix;
        set
        {
            _addPrefix = value ?? string.Empty;
            _addSegments = SplitPrefix(_addPrefix);
        }
    }

    public StepKind StepType => StepKind.Rebase;
    public bool IsExecutable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => $"Strip: '{StripPrefix}'  Add: '{AddPrefix}'";

    public TransformStepConfig Config => new(StepType,
        new JsonObject { ["stripPrefix"] = StripPrefix, ["addPrefix"] = AddPrefix, });

    public async Task Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Rebase");
        if (string.IsNullOrWhiteSpace(StripPrefix) && string.IsNullOrWhiteSpace(AddPrefix))
            context.AddBlockingIssue("Step.RebaseConfigRequired",
                "Rebase requires StripPrefix or AddPrefix.");
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
        var segments = context.PathSegments;

        if (_stripSegments.Length > 0
            && segments.Length >= _stripSegments.Length
            && segments.Take(_stripSegments.Length)
                       .SequenceEqual(_stripSegments, StringComparer.OrdinalIgnoreCase))
        {
            segments = segments[_stripSegments.Length..];
        }

        if (_addSegments.Length > 0)
            segments = [.. _addSegments, .. segments];

        context.PathSegments = segments;
    }

    private static string[] SplitPrefix(string prefix)
        => prefix.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
