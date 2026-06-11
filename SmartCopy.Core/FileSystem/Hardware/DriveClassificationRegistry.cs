using System;
using System.Collections.Concurrent;

namespace SmartCopy.Core.FileSystem.Hardware;

public static class DriveClassificationRegistry
{
    private static readonly ConcurrentDictionary<string, DriveClassification> _cache 
        = new(StringComparer.OrdinalIgnoreCase);

    public static DriveClassification GetOrClassify(string rootPath, string? volumeId)
    {
        string key = string.IsNullOrWhiteSpace(volumeId) ? rootPath : volumeId;
        
        return _cache.GetOrAdd(key, _ => CrossPlatformDriveClassifier.Classify(rootPath));
    }
}
