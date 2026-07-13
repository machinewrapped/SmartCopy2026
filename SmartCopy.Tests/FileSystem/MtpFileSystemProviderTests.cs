using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class MtpFileSystemProviderTests
{
    [Fact]
    public async Task DeleteAsync_EmptyDirectory_UsesNonRecursiveCleanupAndVerifiesFirstSuccess()
    {
        var device = new FakeMtpDeleteDevice();
        device.AddDirectory("/Music");
        var provider = MtpFileSystemProvider.CreateForDeleteTesting(device);
        provider.BeginDeleteOperation();

        await provider.DeleteAsync("mtp://test/Music", CancellationToken.None);

        Assert.Equal([("/Music", false)], device.DirectoryDeletes);
        Assert.Equal(
            ["DirectoryExists:/Music", "DeleteDirectory:/Music:False", "FileExists:/Music", "DirectoryExists:/Music"],
            device.Calls);
    }

    [Fact]
    public async Task DeleteAsync_FirstSuccessfulDeleteThatRemainsOnDevice_Throws()
    {
        var device = new FakeMtpDeleteDevice { IgnoreDeletes = true };
        device.AddDirectory("/Music");
        var provider = MtpFileSystemProvider.CreateForDeleteTesting(device);
        provider.BeginDeleteOperation();

        var exception = await Assert.ThrowsAsync<IOException>(
            () => provider.DeleteAsync("mtp://test/Music", CancellationToken.None));

        Assert.Contains("did not remove", exception.Message);
    }

    [Fact]
    public async Task DeleteAsync_VerifiesOnlyTheFirstSuccessfulDeleteInOperation()
    {
        var device = new FakeMtpDeleteDevice();
        device.AddFile("/first.mp3");
        device.AddFile("/second.mp3");
        var provider = MtpFileSystemProvider.CreateForDeleteTesting(device);
        provider.BeginDeleteOperation();

        await provider.DeleteAsync("mtp://test/first.mp3", CancellationToken.None);
        await provider.DeleteAsync("mtp://test/second.mp3", CancellationToken.None);

        Assert.Equal(
            [
                "DirectoryExists:/first.mp3", "DeleteFile:/first.mp3",
                "FileExists:/first.mp3", "DirectoryExists:/first.mp3",
                "DirectoryExists:/second.mp3", "DeleteFile:/second.mp3",
            ],
            device.Calls);
    }

    [Fact]
    public void SortChildren_PutsDirectoriesFirstThenUsesWindowsStyleNameOrder()
    {
        var sorted = MtpFileSystemProvider.SortChildren(
        [
            new FileSystemNode { Name = "zebra.mp3", IsDirectory = false },
            new FileSystemNode { Name = "World & New Age", IsDirectory = true },
            new FileSystemNode { Name = "__Just Ripped", IsDirectory = true },
            new FileSystemNode { Name = "Alternative", IsDirectory = true },
            new FileSystemNode { Name = "alpha.mp3", IsDirectory = false },
        ]);

        Assert.Equal(
            ["__Just Ripped", "Alternative", "World & New Age", "alpha.mp3", "zebra.mp3"],
            sorted.Select(node => node.Name));
    }

    [Fact]
    public void GetDeviceName_PrefersConnectedMetadataAndCreatesDistinctFallbacks()
    {
        Assert.Equal(
            "motorola edge 60 fusion",
            MtpFileSystemProvider.GetDeviceName("motorola edge 60 fusion", "Motorola", "device-a"));
        Assert.Equal(
            "Motorola",
            MtpFileSystemProvider.GetDeviceName("", "Motorola", "device-a"));

        var firstFallback = MtpFileSystemProvider.GetDeviceName(null, null, "device-a");
        var secondFallback = MtpFileSystemProvider.GetDeviceName(null, null, "device-b");

        Assert.StartsWith("Connected portable device ", firstFallback);
        Assert.NotEqual(firstFallback, secondFallback);
    }

    private sealed class FakeMtpDeleteDevice : IMtpDeleteDevice
    {
        private readonly HashSet<string> _directories = [];
        private readonly HashSet<string> _files = [];

        public bool IgnoreDeletes { get; init; }
        public List<(string Path, bool Recursive)> DirectoryDeletes { get; } = [];
        public List<string> Calls { get; } = [];

        public void AddDirectory(string path) => _directories.Add(path);
        public void AddFile(string path) => _files.Add(path);

        public bool DirectoryExists(string path)
        {
            Calls.Add($"DirectoryExists:{path}");
            return _directories.Contains(path);
        }

        public bool FileExists(string path)
        {
            Calls.Add($"FileExists:{path}");
            return _files.Contains(path);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            Calls.Add($"DeleteDirectory:{path}:{recursive}");
            DirectoryDeletes.Add((path, recursive));
            if (!IgnoreDeletes) _directories.Remove(path);
        }

        public void DeleteFile(string path)
        {
            Calls.Add($"DeleteFile:{path}");
            if (!IgnoreDeletes) _files.Remove(path);
        }
    }
}
