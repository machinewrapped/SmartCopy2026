using System.Text;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Abstract contract suite that every <see cref="IFileSystemProvider"/> implementation must satisfy.
/// Concrete subclasses supply a pre-seeded provider via <see cref="CreateProvider"/>.
/// xUnit discovers [Fact] methods on the concrete classes.
///
/// Baseline structure seeded by each implementation:
///   data/
///     file.txt   ("hello" — 5 bytes)
///     subdir/
///       nested.txt  ("nested" — 6 bytes)
/// </summary>
public abstract class ProviderContractTests : IDisposable
{
    private readonly IDisposable? _cleanup;

    protected readonly IFileSystemProvider Provider;
    protected readonly string DataDir;
    protected readonly string DataFile;
    protected readonly string SubDir;
    protected readonly string NestedFile;

    protected ProviderContractTests()
    {
        Provider = CreateProvider(out _cleanup);
        DataDir    = Provider.JoinPath(Provider.RootPath, ["data"]);
        DataFile   = Provider.JoinPath(Provider.RootPath, ["data", "file.txt"]);
        SubDir     = Provider.JoinPath(Provider.RootPath, ["data", "subdir"]);
        NestedFile = Provider.JoinPath(Provider.RootPath, ["data", "subdir", "nested.txt"]);
    }

    /// <summary>
    /// Creates and seeds the provider under test.
    /// <paramref name="cleanup"/> is disposed after the test (may be null).
    /// </summary>
    protected abstract IFileSystemProvider CreateProvider(out IDisposable? cleanup);

    public void Dispose() => _cleanup?.Dispose();

    // -------------------------------------------------------------------------
    // Contract tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetChildrenAsync_ReturnsSeededChildren()
    {
        var children = await Provider.GetChildrenAsync(DataDir, CancellationToken.None);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "file.txt" && !c.IsDirectory);
        Assert.Contains(children, c => c.Name == "subdir" && c.IsDirectory);
    }

    [Fact]
    public async Task GetNodeAsync_ReturnsCorrectMetadata()
    {
        var node = await Provider.GetNodeAsync(DataFile, CancellationToken.None);

        Assert.Equal("file.txt", node.Name);
        Assert.False(node.IsDirectory);
        Assert.Equal(5, node.Size);
    }

    [Fact]
    public async Task WriteAsync_ThenOpenReadAsync_RoundTrips()
    {
        var dest = Provider.JoinPath(Provider.RootPath, ["roundtrip.txt"]);
        var payload = Encoding.UTF8.GetBytes("round-trip");
        await using var writeStream = new MemoryStream(payload);
        await Provider.WriteAsync(dest, writeStream, progress: null, CancellationToken.None);

        await using var readStream = await Provider.OpenReadAsync(dest, CancellationToken.None);
        using var reader = new StreamReader(readStream);
        Assert.Equal("round-trip", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ExistsAsync_TrueForFile_FalseForMissing()
    {
        Assert.True(await Provider.ExistsAsync(DataFile, CancellationToken.None));
        var missing = Provider.JoinPath(Provider.RootPath, ["does-not-exist.txt"]);
        Assert.False(await Provider.ExistsAsync(missing, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDirectoryAsync_ThenGetChildren_Empty()
    {
        var newDir = Provider.JoinPath(Provider.RootPath, ["newdir"]);
        await Provider.CreateDirectoryAsync(newDir, CancellationToken.None);

        Assert.True(await Provider.ExistsAsync(newDir, CancellationToken.None));
        var children = await Provider.GetChildrenAsync(newDir, CancellationToken.None);
        Assert.Empty(children);
    }

    [Fact]
    public async Task MoveAsync_File_SourceGoneDestExists()
    {
        var dest = Provider.JoinPath(Provider.RootPath, ["moved.txt"]);
        await Provider.MoveAsync(DataFile, dest, CancellationToken.None);

        Assert.False(await Provider.ExistsAsync(DataFile, CancellationToken.None));
        Assert.True(await Provider.ExistsAsync(dest, CancellationToken.None));
    }

    [Fact]
    public async Task MoveAsync_Directory_SubtreeRelocated()
    {
        var dest = Provider.JoinPath(Provider.RootPath, ["moved-data"]);
        await Provider.MoveAsync(DataDir, dest, CancellationToken.None);

        Assert.False(await Provider.ExistsAsync(DataDir, CancellationToken.None));
        Assert.True(await Provider.ExistsAsync(dest, CancellationToken.None));

        var movedFile = Provider.JoinPath(Provider.RootPath, ["moved-data", "file.txt"]);
        Assert.True(await Provider.ExistsAsync(movedFile, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_File_Removed()
    {
        await Provider.DeleteAsync(DataFile, CancellationToken.None);
        Assert.False(await Provider.ExistsAsync(DataFile, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_Directory_SubtreeRemoved()
    {
        await Provider.DeleteAsync(DataDir, CancellationToken.None);

        Assert.False(await Provider.ExistsAsync(DataDir, CancellationToken.None));
        Assert.False(await Provider.ExistsAsync(DataFile, CancellationToken.None));
        Assert.False(await Provider.ExistsAsync(NestedFile, CancellationToken.None));
    }

    [Fact]
    public void Capabilities_AreWellFormed()
    {
        var caps = Provider.Capabilities;
        Assert.True(caps.CanSeek);
        Assert.True(caps.CanAtomicMove);
        Assert.True(caps.MaxPathLength > 0);
    }

    [Fact]
    public void GetRelativePath_IsConsistentWithSplitPath()
    {
        var relative = Provider.GetRelativePath(DataDir, DataFile);
        var segments = Provider.SplitPath(relative);

        Assert.Single(segments);
        Assert.Equal("file.txt", segments[0]);
    }
}

// -------------------------------------------------------------------------
// Concrete instantiations — xUnit discovers their inherited [Fact] methods
// -------------------------------------------------------------------------

public sealed class MemoryProviderContractTests : ProviderContractTests
{
    protected override IFileSystemProvider CreateProvider(out IDisposable? cleanup)
    {
        cleanup = null;
        return MemoryFileSystemFixtures.Create(f => f
            .WithFile("/data/file.txt", "hello"u8)
            .WithTextFile("/data/subdir/nested.txt", "nested"));
    }
}

public sealed class LocalProviderContractTests : ProviderContractTests
{
    protected override IFileSystemProvider CreateProvider(out IDisposable? cleanup)
    {
        var temp = new TempDirectory();
        cleanup = temp;

        Directory.CreateDirectory(Path.Combine(temp.Path, "data", "subdir"));
        File.WriteAllBytes(Path.Combine(temp.Path, "data", "file.txt"), "hello"u8.ToArray());
        File.WriteAllText(Path.Combine(temp.Path, "data", "subdir", "nested.txt"), "nested");

        return new LocalFileSystemProvider(temp.Path);
    }
}
