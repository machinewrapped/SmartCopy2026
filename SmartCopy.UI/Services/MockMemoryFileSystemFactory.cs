using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Services;

internal static class MockMemoryFileSystemFactory
{
    public const string RootPath = "/mem";
    public const string SourcePath = "/mem/Music";
    public const string TargetPath = "/mem/Mirror";
    public const string DefaultFileListPath = "/mem/Music/Alternative/Pixies/1988 Surfer Rosa [SACD Remaster]";

    public static MemoryFileSystemProvider CreateSeeded()
    {
        var provider = new MemoryFileSystemProvider();

        provider.SeedDirectory(RootPath);

        using var stream = typeof(MockMemoryFileSystemFactory).Assembly.GetManifestResourceStream("sample_directory_structure.txt");
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            string? line;
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var path = $"{RootPath}/{line.Replace('\\', '/')}";
                var lastSlash = path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    var dir = path.Substring(0, lastSlash);
                    var parts = dir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var currentDir = "";
                    foreach (var part in parts)
                    {
                        currentDir += "/" + part;
                        if (directories.Add(currentDir))
                        {
                            provider.SeedDirectory(currentDir);
                        }
                    }
                }

                int dotIndex = line.LastIndexOf('.');
                var ext = dotIndex >= 0 ? line.Substring(dotIndex + 1).ToLowerInvariant() : "";
                
                long fakeSize = ext switch
                {
                    "flac" => 25_000_000,
                    "jpg" => 500_000,
                    "txt" => 10_000,
                    "ini" => 1_000,
                    _ => 1_000_000
                };
                
                provider.SeedSimulatedFile(path, fakeSize);
            }
        }

        return provider;
    }
}

