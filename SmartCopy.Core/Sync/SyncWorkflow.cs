using System;
using System.Collections.Generic;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Core.Sync;

public static class SyncWorkflow
{
    public static TransformPipeline BuildUpdatePipeline(string destinationPath)
    {
        return new TransformPipeline(
        [
            new CopyStep(destinationPath),
        ]);
    }

    public static (TransformPipeline CopyPass, TransformPipeline DeletePass) BuildMirrorPipelines(string destinationPath)
    {
        var copyPass = BuildUpdatePipeline(destinationPath);
        var deletePass = new TransformPipeline(
        [
            new DeleteStep(),
        ]);
        return (copyPass, deletePass);
    }

    public static IReadOnlyList<string> FindOrphans(
        IEnumerable<string> sourceRelativePaths,
        IEnumerable<string> targetRelativePaths)
    {
        var source = new HashSet<string>(sourceRelativePaths, StringComparer.OrdinalIgnoreCase);
        var orphans = new List<string>();
        foreach (var targetPath in targetRelativePaths)
        {
            if (!source.Contains(targetPath))
            {
                orphans.Add(targetPath);
            }
        }

        return orphans;
    }
}
