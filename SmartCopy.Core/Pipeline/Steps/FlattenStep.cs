using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class FlattenStep : IPipelineStep
{
    public FlattenStep(
        FlattenConflictStrategy conflictStrategy = FlattenConflictStrategy.AutoRenameCounter,
        FlattenTrimMode trimMode = FlattenTrimMode.KeepTrailing,
        int levels = 1)
    {
        ConflictStrategy = conflictStrategy;
        TrimMode = trimMode;
        Levels = levels;
    }

    public FlattenConflictStrategy ConflictStrategy { get; set; }
    public FlattenTrimMode TrimMode { get; set; }
    public int Levels { get; set; }

    internal static FlattenStep FromConfig(TransformStepConfig config) =>
        new(config.ParseEnum("conflictStrategy", FlattenConflictStrategy.AutoRenameCounter),
            config.ParseEnum("trimMode", FlattenTrimMode.KeepTrailing),
            config.GetOptionalInt("levels", 1));

    public StepKind StepType => StepKind.Flatten;
    public bool IsExecutable => false;

    public string AutoSummary => StepType.ForDisplay();

    public string Description => TrimMode == FlattenTrimMode.StripLeading
        ? $"Strip {Levels} from start; {ConflictStrategy}"
        : $"Keep {Levels} from end; {ConflictStrategy}";

    public TransformStepConfig Config => new(StepType, new JsonObject
    {
        ["conflictStrategy"] = ConflictStrategy.ToString(),
        ["trimMode"] = TrimMode.ToString(),
        ["levels"] = Levels.ToString(),
    });

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        context.ValidateSourceExists("Flatten");
        if (Levels < 1)
            context.AddBlockingIssue("Step.FlattenLevelsRequired",
                "Flatten requires Levels to be at least 1.");
        return Task.CompletedTask;
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
        if (segments.Length == 0) return;

        if (TrimMode == FlattenTrimMode.StripLeading)
        {
            // Never strip the filename (always preserve the last segment).
            var maxStrip = segments.Length - 1;
            var strip = Math.Min(Levels, maxStrip);
            segments = segments[strip..];
        }
        else
        {
            if (segments.Length > Levels)
                segments = segments[^Levels..];
        }

        context.PathSegments = segments;
    }
}
