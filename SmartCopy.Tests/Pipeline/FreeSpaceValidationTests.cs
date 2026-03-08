using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests for real-time free-space warnings emitted by PipelineValidator.Validate()
/// using the pre-cached free-space map path (distinct from the async PipelineRunner path).
/// </summary>
public sealed class FreeSpaceValidationTests
{
    private static (MemoryFileSystemProvider source, FileSystemProviderRegistry registry) MakeSource(string volumeId = "SRC")
    {
        var source = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", new byte[500]),
            volumeId: volumeId);
        return (source, source.CreateRegistry());
    }

    private static MemoryFileSystemProvider MakeTarget(long? capacity, string rootPath, string volumeId = "DST")
    {
        var target = MemoryFileSystemFixtures.Create(b => b
            .WithDirectory("/dst"),
            customRootPath: rootPath,
            volumeId: volumeId);
        target.SimulatedCapacity = capacity;
        return target;
    }

    private static PipelineValidationContext MakeContext(
        IFileSystemProvider source,
        IPathResolver registry,
        long selectedBytes,
        IReadOnlyDictionary<string, long?> cache)
        => new(
            HasSelectedIncludedInputs: true,
            SelectedBytes: selectedBytes,
            SourceProvider: source,
            ProviderRegistry: registry,
            CachedFreeSpace: cache);

    [Fact]
    public void CopyStep_InsufficientSpace_EmitsStepScopedWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 100, rootPath: "/target");
        registry.Register(target);

        var cache = new Dictionary<string, long?> { [target.RootPath] = 100L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new CopyStep("/target/dst")], context);

        // Warning — not a blocking issue, so CanRun stays true
        Assert.True(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.InsufficientSpace");
        Assert.Equal(0, issue.StepIndex);
        Assert.Equal(PipelineValidationSeverity.Warning, issue.Severity);
        Assert.Contains("Not enough space", issue.Message);
    }

    [Fact]
    public void CopyStep_SufficientSpace_NoWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1_000_000, rootPath: "/target");
        registry.Register(target);

        var cache = new Dictionary<string, long?> { [target.RootPath] = 1_000_000L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new CopyStep("/target/dst")], context);

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public void CopyStep_NullCacheEntry_NoWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: null, rootPath: "/target");
        registry.Register(target);

        // Cache has null entry for target — means unknown/unsupported
        var cache = new Dictionary<string, long?> { [target.RootPath] = null };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new CopyStep("/target/dst")], context);

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public void CopyStep_EmptyCache_NoWarning()
    {
        var (source, registry) = MakeSource();
        var context = MakeContext(source, registry, selectedBytes: 500, cache: new Dictionary<string, long?>());

        var result = PipelineValidator.Validate([new CopyStep("/target/dst")], context);

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public void MoveStep_SameVolume_NoWarning()
    {
        // Same VolumeId → ResolveFreeSpaceTarget returns null → no warning
        var source = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", new byte[500])
            .WithDirectory("/dst"),
            volumeId: "VOL");
        var registry = source.CreateRegistry();

        // Cache has very little space, but same-volume should short-circuit
        var cache = new Dictionary<string, long?> { [source.RootPath] = 1L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new MoveStep("/mem/dst")], context);

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public void MoveStep_CrossVolume_InsufficientSpace_EmitsWarning()
    {
        var (source, registry) = MakeSource(volumeId: "SRC");
        var target = MakeTarget(capacity: 10, rootPath: "/target", volumeId: "DST");
        registry.Register(target);

        var cache = new Dictionary<string, long?> { [target.RootPath] = 10L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new MoveStep("/target/dst")], context);

        Assert.True(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.InsufficientSpace");
        Assert.Equal(0, issue.StepIndex);
        Assert.Contains("Not enough space", issue.Message);
    }

    [Fact]
    public void InvertSelection_ResetsBytes_DownstreamCopySkipsCheck()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1, rootPath: "/target"); // very low
        registry.Register(target);

        var cache = new Dictionary<string, long?> { [target.RootPath] = 1L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        // InvertSelectionStep resets SelectedBytes → CopyStep should not warn
        var result = PipelineValidator.Validate(
            [new InvertSelectionStep(), new CopyStep("/target/dst")],
            context);

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public void BlockingIssueFirst_NoSpaceWarning()
    {
        // CopyStep with no destination → blocking issue only; space warning is a no-op
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1, rootPath: "/target");
        registry.Register(target);

        var cache = new Dictionary<string, long?> { [target.RootPath] = 1L };
        var context = MakeContext(source, registry, selectedBytes: 500, cache);

        var result = PipelineValidator.Validate([new CopyStep("")], context);

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, i => i.Code == "Step.MissingDestination");
        // Space warning is still emitted (AddFreeSpaceWarning is not guarded by HasBlockingIssue)
        // But destination is null so ResolveFreeSpaceTarget returns null → no warning
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }
}
