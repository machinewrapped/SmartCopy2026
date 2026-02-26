using System.Collections.Generic;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Steps;

internal static class StepPathHelper
{
    /// <summary>
    /// Builds the destination path using the target provider's conventions (use during Apply).
    /// </summary>
    public static string BuildDestinationPath(
        IFileSystemProvider target, string root, IReadOnlyList<string> segments)
        => target.JoinPath(root, segments);

    /// <summary>
    /// Builds a canonical forward-slash display path (use during Preview, where no provider is available).
    /// </summary>
    public static string BuildDestinationPath(string root, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
            return root;
        return root.TrimEnd('/') + "/" + string.Join("/", segments);
    }
}
