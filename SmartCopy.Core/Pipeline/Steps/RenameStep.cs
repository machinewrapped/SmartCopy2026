using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RenameStep : ITransformStep
{
    public RenameStep(string pattern)
    {
        Pattern = pattern;
    }

    public string Pattern { get; set; }

    public StepKind StepType => StepKind.Rename;
    public bool IsExecutable => false;
    public bool RequiresSourceExists => true;
    public bool RequiresSelectedIncludedInputs => false;
    public bool? SetsSourceExists => null;

    public IEnumerable<PipelineValidationIssue> Validate(int stepIndex)
    {
        if (string.IsNullOrWhiteSpace(Pattern))
            yield return new PipelineValidationIssue(
                StepIndex: stepIndex,
                Code: "Step.RenamePatternRequired",
                Message: "Rename requires a non-empty pattern.",
                Severity: PipelineValidationSeverity.Blocking);
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["pattern"] = Pattern,
        });

    public TransformResult Preview(TransformContext context)
    {
        Apply(context);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path renamed");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path renamed"));
    }

    private void Apply(TransformContext context)
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
