using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RebaseStep : ITransformStep
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

    public void Validate(StepValidationContext context)
    {
        if (!context.SourceExists)
            context.AddBlockingIssue("Step.SourceMissing",
                "Rebase cannot run because the source no longer exists after earlier steps.");
        if (string.IsNullOrWhiteSpace(StripPrefix) && string.IsNullOrWhiteSpace(AddPrefix))
            context.AddBlockingIssue("Step.RebaseConfigRequired",
                "Rebase requires StripPrefix or AddPrefix.");
        // Post-condition: source is unchanged.
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["stripPrefix"] = StripPrefix,
            ["addPrefix"] = AddPrefix,
        });

    public TransformResult Preview(TransformContext context)
    {
        Apply(context);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path rebased");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path rebased"));
    }

    private void Apply(TransformContext context)
    {
        var segments = context.PathSegments;

        // Strip leading segments that match the strip prefix (case-insensitive)
        if (_stripSegments.Length > 0
            && segments.Length >= _stripSegments.Length
            && segments.Take(_stripSegments.Length)
                       .SequenceEqual(_stripSegments, StringComparer.OrdinalIgnoreCase))
        {
            segments = segments[_stripSegments.Length..];
        }

        // Prepend add-prefix segments
        if (_addSegments.Length > 0)
        {
            segments = [.. _addSegments, .. segments];
        }

        context.PathSegments = segments;
    }

    private static string[] SplitPrefix(string prefix)
        => prefix.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
