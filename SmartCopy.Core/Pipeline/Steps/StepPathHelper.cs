using System.IO;

namespace SmartCopy.Core.Pipeline.Steps;

internal static class StepPathHelper
{
    public static string BuildDestinationPath(string destinationRoot, string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return destinationRoot;
        }

        var normalizedCurrentPath = currentPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(destinationRoot, normalizedCurrentPath);
    }
}

