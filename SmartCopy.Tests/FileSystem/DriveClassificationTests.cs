
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
}
