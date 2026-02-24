using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RebaseStep : ITransformStep
{
    public RebaseStep(string stripPrefix, string addPrefix)
    {
        StripPrefix = stripPrefix ?? string.Empty;
        AddPrefix = addPrefix ?? string.Empty;
    }

    public string StripPrefix { get; set; }
    public string AddPrefix { get; set; }

    public StepKind StepType => StepKind.Rebase;
    public bool IsPathStep => true;
    public bool IsContentStep => false;
    public bool IsExecutable => false;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["stripPrefix"] = StripPrefix,
            ["addPrefix"] = AddPrefix,
        });

    public TransformResult Preview(TransformContext context)
    {
        var rebased = BuildRebasedPath(context.CurrentPath);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: rebased,
            Message: "Path rebased");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.CurrentPath = BuildRebasedPath(context.CurrentPath);

        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.CurrentPath,
            Message: "Path rebased"));
    }

    private string BuildRebasedPath(string currentPath)
    {
        var path = NormalizePath(currentPath);
        var stripPrefix = NormalizePath(StripPrefix);
        var addPrefix = NormalizePath(AddPrefix);

        if (!string.IsNullOrWhiteSpace(stripPrefix))
        {
            if (path.Equals(stripPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = string.Empty;
            }
            else if (path.StartsWith(stripPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                path = path[(stripPrefix.Length + 1)..];
            }
        }

        if (!string.IsNullOrWhiteSpace(addPrefix))
        {
            path = string.IsNullOrWhiteSpace(path) ? addPrefix : $"{addPrefix}/{path}";
        }

        return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').Trim().Trim('/');
    }
}
