using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests for real-time free-space warnings emitted by PipelineValidator.ValidateAsync()
/// using the lazy-populated free-space cache path (distinct from the async PipelineRunner path).
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

    private static async Task<PipelineValidationContext> MakeContext(
        IFileSystemProvider source,
        IPathResolver registry,
        IReadOnlyList<IPipelineStep> steps,
        long selectedBytes)
        => new(
            SourceProvider: source,
            ProviderRegistry: registry,
            CachedFreeSpace: await PipelineHelper.BuildFreeSpaceCacheForPipeline(steps, registry, CancellationToken.None),
            HasSelectedIncludedInputs: true,
            SelectedBytes: selectedBytes
            );

    [Fact]
    public async Task CopyStep_InsufficientSpace_EmitsStepScopedWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 100, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        // Warning — not a blocking issue, so CanRun stays true
        Assert.True(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.InsufficientSpace");
        Assert.Equal(0, issue.StepIndex);
        Assert.Equal(PipelineValidationSeverity.Warning, issue.Severity);
        Assert.Contains("Not enough space", issue.Message);
    }

    [Fact]
    public async Task CopyStep_SufficientSpace_NoWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1_000_000, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task CopyStep_NoCapability_NoWarning()
    {
        var (source, registry) = MakeSource();
        // No SimulatedCapacity → CanQueryFreeSpace = false → check skipped
        var target = MakeTarget(capacity: null, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task CopyStep_NoRegisteredProvider_NoWarning()
    {
        var (source, registry) = MakeSource();
        // No target registered at all

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task MoveStep_SameVolume_NoWarning()
    {
        // Same VolumeId → same-volume check short-circuits → no warning
        var source = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", new byte[500])
            .WithDirectory("/dst"),
            volumeId: "VOL");
        source.SimulatedCapacity = 1; // only 1 byte free, but same-volume should not trigger
        var registry = source.CreateRegistry();

        IReadOnlyList<IPipelineStep> steps = [new MoveStep("/mem/dst")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task MoveStep_CrossVolume_InsufficientSpace_EmitsWarning()
    {
        var (source, registry) = MakeSource(volumeId: "SRC");
        var target = MakeTarget(capacity: 10, rootPath: "/target", volumeId: "DST");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new MoveStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.InsufficientSpace");
        Assert.Equal(0, issue.StepIndex);
        Assert.Contains("Not enough space", issue.Message);
    }

    [Fact]
    public async Task InvertSelection_ResetsBytes_DownstreamCopySkipsCheck()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1, rootPath: "/target");
        registry.Register(target);

        // InvertSelectionStep resets SelectedBytes → CopyStep should not warn
        IReadOnlyList<IPipelineStep> steps = [new InvertSelectionStep(), new CopyStep("/target/dst")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task TwoCopySteps_SameVolume_CumulativeSpaceChecked()
    {
        // 600 bytes free, two Copy steps each needing 400 bytes → second step should warn
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 600, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst1"), new CopyStep("/target/dst2")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 400));

        Assert.True(result.CanRun);
        // First step: 400 <= 600 free → no warning; cache updated to 200
        // Second step: 400 > 200 → warning
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.InsufficientSpace");
        Assert.Equal(1, issue.StepIndex);
    }

    [Fact]
    public async Task TwoCopySteps_SameVolume_BothFit_NoWarning()
    {
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1_000_000, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("/target/dst1"), new CopyStep("/target/dst2")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 400));

        Assert.True(result.CanRun);
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }

    [Fact]
    public async Task BlockingIssueFirst_NoSpaceWarning()
    {
        // CopyStep with no destination → blocking issue; destination is null so no space check
        var (source, registry) = MakeSource();
        var target = MakeTarget(capacity: 1, rootPath: "/target");
        registry.Register(target);

        IReadOnlyList<IPipelineStep> steps = [new CopyStep("")];
        var result = await PipelineValidator.ValidateAsync(
            steps,
            await MakeContext(source, registry, steps, selectedBytes: 500));

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, i => i.Code == "Step.MissingDestination");
        Assert.DoesNotContain(result.Issues, i => i.Code == "Step.InsufficientSpace");
    }
}
