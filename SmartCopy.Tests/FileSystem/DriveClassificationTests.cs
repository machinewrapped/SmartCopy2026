
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.Tests.FileSystem;

public class DriveClassificationTests
{
    [Fact]
    public async Task MemoryFileSystemProvider_ReturnsMemoryVirtualClassification()
    {
        // Arrange
        var provider = new MemoryFileSystemProvider();

        // Act
        var classification = await provider.GetClassificationAsync();

        // Assert
        Assert.Equal(DriveMediaType.Memory, classification.MediaType);
        Assert.Equal(DriveInterfaceType.Virtual, classification.InterfaceType);
    }



    [Fact]
    public async Task LocalFileSystemProvider_ReturnsClassificationFromRegistry()
    {
        // Arrange
        var root = Environment.CurrentDirectory;
        var provider = new LocalFileSystemProvider(root);

        // Act
        var classification = await provider.GetClassificationAsync();

        // Assert
        // We can't guarantee SSD or HDD, but it shouldn't be Memory or MTP on a local provider.
        Assert.NotEqual(DriveMediaType.Memory, classification.MediaType);
        Assert.NotEqual(DriveMediaType.MTP, classification.MediaType);
    }
    
    [Fact]
    public async Task DriveClassificationRegistry_CachesResultsByVolumeId()
    {
        // Arrange
        var root1 = Path.Combine(Environment.CurrentDirectory, "FolderA");
        var root2 = Path.Combine(Environment.CurrentDirectory, "FolderB");
        
        // Let's get a unique volume ID for testing
        var testVolumeId = "TEST_VOL_123";

        // Act
        var c1 = await DriveClassificationRegistry.GetOrClassifyAsync(root1, testVolumeId);
        var c2 = await DriveClassificationRegistry.GetOrClassifyAsync(root2, testVolumeId);

        // Assert
        Assert.Equal(c1, c2);
    }

    [Fact]
    public async Task DriveClassificationRegistry_DoesNotCacheTimedOutAttempt()
    {
        var attempts = 0;
        var volumeId = $"TRANSIENT_TIMEOUT_{Guid.NewGuid():N}";

        Task<DriveClassification> ClassifyAsync(string _, CancellationToken __)
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<DriveClassification>(new DriveClassificationTimeoutException())
                : Task.FromResult(new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.USB));
        }

        var first = await DriveClassificationRegistry.GetOrClassifyAsync(
            "/first", volumeId, ClassifyAsync);
        var second = await DriveClassificationRegistry.GetOrClassifyAsync(
            "/second", volumeId, ClassifyAsync);

        Assert.Equal(DriveClassification.Unknown, first);
        Assert.Equal(DriveMediaType.SSD, second.MediaType);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DriveClassificationRegistry_DoesNotCacheFailedAttempt()
    {
        var attempts = 0;
        var volumeId = $"TRANSIENT_FAULT_{Guid.NewGuid():N}";

        Task<DriveClassification> ClassifyAsync(string _, CancellationToken __)
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<DriveClassification>(new IOException("device not ready"))
                : Task.FromResult(new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.USB));
        }

        await Assert.ThrowsAsync<IOException>(async () =>
            await DriveClassificationRegistry.GetOrClassifyAsync("/first", volumeId, ClassifyAsync));

        var second = await DriveClassificationRegistry.GetOrClassifyAsync(
            "/second", volumeId, ClassifyAsync);

        Assert.Equal(DriveMediaType.HDD, second.MediaType);
        Assert.Equal(2, attempts);
    }

    /// <summary>A caller giving up does not invalidate a probe that is still running for others.</summary>
    [Fact]
    public async Task DriveClassificationRegistry_KeepsInFlightAttemptWhenOneCallerCancels()
    {
        var attempts = 0;
        var volumeId = $"INFLIGHT_{Guid.NewGuid():N}";
        var release = new TaskCompletionSource<DriveClassification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<DriveClassification> ClassifyAsync(string _, CancellationToken __)
        {
            attempts++;
            return release.Task;
        }

        using var cancellation = new CancellationTokenSource();
        var abandoned = DriveClassificationRegistry
            .GetOrClassifyAsync("/first", volumeId, ClassifyAsync, cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        release.SetResult(new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe));
        var second = await DriveClassificationRegistry.GetOrClassifyAsync(
            "/second", volumeId, ClassifyAsync);

        Assert.Equal(DriveMediaType.SSD, second.MediaType);
        Assert.Equal(1, attempts);
    }
}
