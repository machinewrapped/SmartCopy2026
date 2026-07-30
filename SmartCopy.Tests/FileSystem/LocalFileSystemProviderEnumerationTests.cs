using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Pins the observable behaviour of <see cref="LocalFileSystemProvider.GetChildrenAsync"/>, which
/// enumerates a directory in a single <c>FileSystemEnumerable</c> pass and reads every field off the
/// <c>FileSystemEntry</c> the OS already returned.
///
/// Two behaviours are easy to regress and are therefore asserted explicitly:
///   1. Hidden and system entries are returned. The enumeration options must mirror
///      <c>EnumerationOptions.Compatible</c>; the <c>EnumerationOptions</c> default constructor
///      instead sets <c>AttributesToSkip = Hidden | System</c> and would silently drop them.
///   2. Directories are returned before files, matching MemoryFileSystemProvider so that behaviour
///      does not diverge between tests and production.
/// </summary>
public sealed class LocalFileSystemProviderEnumerationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LocalFileSystemProvider _provider;

    public LocalFileSystemProviderEnumerationTests()
    {
        _provider = new LocalFileSystemProvider(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task GetChildrenAsync_IncludesHiddenEntries()
    {
        var hiddenFile = CreateHiddenFile("hidden.txt");
        var hiddenDir = CreateHiddenDirectory("hiddendir");
        File.WriteAllText(Path.Combine(_temp.Path, "visible.txt"), "visible");

        // Guard against a vacuous pass: if the platform did not actually mark these hidden,
        // the assertions below would not be testing anything.
        Assert.True((File.GetAttributes(hiddenFile) & FileAttributes.Hidden) != 0);
        Assert.True((File.GetAttributes(hiddenDir) & FileAttributes.Hidden) != 0);

        var children = await _provider.GetChildrenAsync(_temp.Path, CancellationToken.None);

        Assert.Equal(3, children.Count);
        Assert.Contains(children, c => c.Name == Path.GetFileName(hiddenFile) && !c.IsDirectory);
        Assert.Contains(children, c => c.Name == Path.GetFileName(hiddenDir) && c.IsDirectory);
        Assert.Contains(children, c => c.Name == "visible.txt");
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsDirectoriesBeforeFiles()
    {
        // Interleaved names, so a single unpartitioned pass would not produce this order by luck.
        Directory.CreateDirectory(Path.Combine(_temp.Path, "b-dir"));
        Directory.CreateDirectory(Path.Combine(_temp.Path, "d-dir"));
        File.WriteAllText(Path.Combine(_temp.Path, "a-file.txt"), "a");
        File.WriteAllText(Path.Combine(_temp.Path, "c-file.txt"), "c");

        var children = await _provider.GetChildrenAsync(_temp.Path, CancellationToken.None);

        Assert.Equal(4, children.Count);
        var firstFileIndex = children.Select((c, i) => (c, i)).First(x => !x.c.IsDirectory).i;
        Assert.DoesNotContain(children.Skip(firstFileIndex), c => c.IsDirectory);
    }

    [Fact]
    public async Task GetChildrenAsync_MetadataMatchesFileInfo()
    {
        var filePath = Path.Combine(_temp.Path, "data.bin");
        File.WriteAllBytes(filePath, new byte[42]);
        var dirPath = Path.Combine(_temp.Path, "folder");
        Directory.CreateDirectory(dirPath);

        var children = await _provider.GetChildrenAsync(_temp.Path, CancellationToken.None);

        var fileNode = Assert.Single(children, c => !c.IsDirectory);
        var fileInfo = new FileInfo(filePath);
        Assert.Equal(fileInfo.Name, fileNode.Name);
        Assert.Equal(fileInfo.FullName, fileNode.FullPath);
        Assert.Equal(fileInfo.Length, fileNode.Size);
        Assert.Equal(fileInfo.CreationTimeUtc, fileNode.CreatedAt);
        Assert.Equal(fileInfo.LastWriteTimeUtc, fileNode.ModifiedAt);
        Assert.Equal(fileInfo.Attributes, fileNode.Attributes);

        var dirNode = Assert.Single(children, c => c.IsDirectory);
        var dirInfo = new DirectoryInfo(dirPath);
        Assert.Equal(dirInfo.Name, dirNode.Name);
        Assert.Equal(dirInfo.FullName, dirNode.FullPath);
        Assert.Equal(0, dirNode.Size);
        Assert.Equal(dirInfo.CreationTimeUtc, dirNode.CreatedAt);
        Assert.Equal(dirInfo.LastWriteTimeUtc, dirNode.ModifiedAt);
        Assert.Equal(dirInfo.Attributes, dirNode.Attributes);
    }

    [Fact]
    public async Task GetChildrenAsync_MatchesGetNodeAsync()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "same.txt"), "same");

        var child = Assert.Single(await _provider.GetChildrenAsync(_temp.Path, CancellationToken.None));
        var node = await _provider.GetNodeAsync(child.FullPath, CancellationToken.None);

        Assert.Equal(node.Name, child.Name);
        Assert.Equal(node.FullPath, child.FullPath);
        Assert.Equal(node.IsDirectory, child.IsDirectory);
        Assert.Equal(node.Size, child.Size);
        Assert.Equal(node.CreatedAt, child.CreatedAt);
        Assert.Equal(node.ModifiedAt, child.ModifiedAt);
        Assert.Equal(node.Attributes, child.Attributes);
    }

    /// <summary>
    /// Unix marks dot-prefixed entries hidden; Windows needs the attribute set explicitly.
    /// The dot prefix is used on both so the name is identical across platforms.
    /// </summary>
    private string CreateHiddenFile(string name)
    {
        var path = Path.Combine(_temp.Path, "." + name);
        File.WriteAllText(path, "hidden");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        return path;
    }

    private string CreateHiddenDirectory(string name)
    {
        var path = Path.Combine(_temp.Path, "." + name);
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        return path;
    }
}
