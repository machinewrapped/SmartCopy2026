using System;
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

    public TransformStepConfig Config => new(StepType, new JsonObject { ["pattern"] = Pattern });

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Rename");
        if (string.IsNullOrWhiteSpace(Pattern))
            context.AddBlockingIssue("Step.RenamePatternRequired", "Rename requires a non-empty pattern.");
        // Post-condition: source is unchanged.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        Apply(context);
        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None);
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None));
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
