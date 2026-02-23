using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RenameStep : ITransformStep
{
    public RenameStep(string pattern)
    {
        Pattern = pattern;
    }

    public string Pattern { get; set; }

    public string StepType => "Rename";
    public bool IsPathStep => true;
    public bool IsContentStep => false;
    public bool IsExecutable => false;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["pattern"] = Pattern,
        });

    public TransformResult Preview(TransformContext context)
    {
        var renamedPath = BuildRenamedPath(context.CurrentPath);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: renamedPath,
            Message: "Path renamed");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.CurrentPath = BuildRenamedPath(context.CurrentPath);

        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.CurrentPath,
            Message: "Path renamed"));
    }

    private string BuildRenamedPath(string currentPath)
    {
        var directory = Path.GetDirectoryName(currentPath) ?? string.Empty;
        var extension = Path.GetExtension(currentPath);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(currentPath);

        var renamedFileName = Pattern
            .Replace("{name}", nameWithoutExtension, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(renamedFileName))
            && !string.IsNullOrWhiteSpace(extension)
            && !Pattern.Contains("{ext}", StringComparison.OrdinalIgnoreCase))
        {
            renamedFileName += extension;
        }

        return string.IsNullOrWhiteSpace(directory)
            ? renamedFileName
            : Path.Combine(directory, renamedFileName);
    }
}
