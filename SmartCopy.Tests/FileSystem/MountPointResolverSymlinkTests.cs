using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Symlink resolution needs real links on a real filesystem, so these are Unix-only; the resolver's
/// string matching stays hermetic in <see cref="MountPointResolverTests"/>.
/// </summary>
public sealed class MountPointResolverSymlinkTests : IDisposable
{
    private readonly string _root = CreateRoot();

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smartcopy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        // The temp directory is itself reached through a symlink on macOS (/var -> /private/var), so
        // anchor to its real path and let the tests below vary only the link they create.
        return MountPointResolver.ResolveSymlinks(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private (string Target, string Link) CreateLinkedDirectory()
    {
        var target = Path.Combine(_root, "target");
        var link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        return (target, link);
    }

    [UnixFact]
    public void ResolveSymlinks_LeavesAPathWithNoLinksAlone()
    {
        Assert.Equal(_root, MountPointResolver.ResolveSymlinks(_root));
    }

    [UnixFact]
    public void ResolveSymlinks_FollowsALinkToItsTarget()
    {
        var (target, link) = CreateLinkedDirectory();

        Assert.Equal(target, MountPointResolver.ResolveSymlinks(link));
    }

    /// <summary>The case <c>ResolveLinkTarget</c> cannot cover: the link is not the final component.</summary>
    [UnixFact]
    public void ResolveSymlinks_FollowsALinkInAnIntermediateComponent()
    {
        var (target, link) = CreateLinkedDirectory();
        Directory.CreateDirectory(Path.Combine(target, "nested"));

        Assert.Equal(
            Path.Combine(target, "nested"),
            MountPointResolver.ResolveSymlinks(Path.Combine(link, "nested")));
    }

    [UnixFact]
    public void ResolveSymlinks_ReturnsPathUnchangedWhenItDoesNotExist()
    {
        var missing = Path.Combine(_root, "no-such-directory");

        Assert.Equal(missing, MountPointResolver.ResolveSymlinks(missing));
    }

    /// <summary>
    /// The reported bug: a root reached through a symlink was attributed to the volume holding the
    /// link. "target" stands in for the separate mount a test cannot create.
    /// </summary>
    [UnixFact]
    public void Resolve_PathReachedThroughASymlink_UsesTheTargetsMount()
    {
        var (target, link) = CreateLinkedDirectory();

        Assert.Equal(target, MountPointResolver.Resolve(link, ["/", target]));
    }
}
