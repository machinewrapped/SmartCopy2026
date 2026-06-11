
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.Tests.FileSystem;

public class DriveClassificationTests
{
    [Fact]
    public void MemoryFileSystemProvider_ReturnsMemoryVirtualClassification()
    {
        // Arrange
        var provider = new MemoryFileSystemProvider();

        // Act
        var classification = provider.Classification;

        // Assert
        Assert.Equal(DriveMediaType.Memory, classification.MediaType);
        Assert.Equal(DriveInterfaceType.Virtual, classification.InterfaceType);
    }



    [Fact]
    public void LocalFileSystemProvider_ReturnsClassificationFromRegistry()
    {
        // Arrange
        var root = Environment.CurrentDirectory;
        var provider = new LocalFileSystemProvider(root);

        // Act
        var classification = provider.Classification;

        // Assert
        // We can't guarantee SSD or HDD, but it shouldn't be Memory or MTP on a local provider.
        Assert.NotEqual(DriveMediaType.Memory, classification.MediaType);
        Assert.NotEqual(DriveMediaType.MTP, classification.MediaType);
    }
    
    [Fact]
    public void DriveClassificationRegistry_CachesResultsByVolumeId()
    {
        // Arrange
        var root1 = Environment.CurrentDirectory;
        var root2 = Environment.CurrentDirectory;
        
        // Let's get a unique volume ID for testing
        var testVolumeId = "TEST_VOL_123";

        // Act
        var c1 = DriveClassificationRegistry.GetOrClassify(root1, testVolumeId);
        var c2 = DriveClassificationRegistry.GetOrClassify(root2, testVolumeId);

        // Assert
        Assert.Equal(c1, c2);
    }
}
