using System.Text;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Services;

internal static class MockMemoryFileSystemFactory
{
    public const string RootPath = "/mem";
    public const string DefaultFileListPath = "/mem/Rock/Classic Rock/Beatles/Abbey Road";

    public static MemoryFileSystemProvider CreateSeeded()
    {
        var provider = new MemoryFileSystemProvider();

        provider.SeedDirectory(RootPath);
        provider.SeedDirectory("/mem/Rock");
        provider.SeedDirectory("/mem/Rock/Classic Rock");
        provider.SeedDirectory("/mem/Rock/Classic Rock/Beatles");
        provider.SeedDirectory("/mem/Rock/Classic Rock/Beatles/Abbey Road");
        provider.SeedDirectory("/mem/Rock/Classic Rock/Rolling Stones");
        provider.SeedDirectory("/mem/Rock/Metal");
        provider.SeedDirectory("/mem/Jazz");
        provider.SeedDirectory("/mem/Classical");

        provider.SeedFile("/mem/Rock/Classic Rock/Beatles/Abbey Road/Come Together.flac", new byte[4_800_000]);
        provider.SeedFile("/mem/Rock/Classic Rock/Beatles/Abbey Road/Something.flac", new byte[3_200_000]);
        provider.SeedFile("/mem/Rock/Classic Rock/Beatles/Abbey Road/cover.jpg", new byte[420_000]);
        provider.SeedFile("/mem/Rock/Classic Rock/Beatles/Abbey Road/desktop.ini", Encoding.UTF8.GetBytes("[.ShellClassInfo]"));

        provider.SeedFile("/mem/Jazz/Miles Davis - So What.flac", new byte[2_400_000]);
        provider.SeedFile("/mem/Classical/Mozart - Symphony 40.flac", new byte[2_100_000]);

        return provider;
    }
}

