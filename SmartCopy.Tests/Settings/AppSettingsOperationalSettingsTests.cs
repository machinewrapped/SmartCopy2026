using SmartCopy.Core.Settings;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsOperationalSettingsTests
{
    [Fact]
    public void CreateOperationalSettings_WhenOptimisationsDisabled_MapsBaseBufferOnly()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = 384,
            AllowCopyOptimisations = false,
            TinyFileFastPathKb = 64,
            BatchBufferKb = 2048,
        };

        var operational = settings.CreateOperationalSettings();

        Assert.Equal(384 * 1024, operational.CopyBufferSizeBytes);
        Assert.False(operational.DestinationRoutingEnabled);
        Assert.Equal(0, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(0, operational.BatchBufferBytes);
    }

    [Fact]
    public void CreateOperationalSettings_WhenOptimisationsEnabled_MapsRoutingBundle()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = 384,
            AllowCopyOptimisations = true,
            TinyFileFastPathKb = 64,
            BatchBufferKb = 2048,
            CopyRoutingSsdBufferKb = 1536,
            CopyRoutingUsbBufferKb = 1792,
            CopyRoutingHddBufferKb = 640,
            CopyRoutingSameVolumeHddBufferKb = 256,
            CopyRoutingUnknownBufferKb = 320,
        };

        var operational = settings.CreateOperationalSettings();

        Assert.Equal(384 * 1024, operational.CopyBufferSizeBytes);
        Assert.True(operational.DestinationRoutingEnabled);
        Assert.Equal(64 * 1024, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(2048 * 1024, operational.BatchBufferBytes);
        Assert.Equal(1536 * 1024, operational.RoutedSsdCopyBufferSizeBytes);
        Assert.Equal(1792 * 1024, operational.RoutedUsbCopyBufferSizeBytes);
        Assert.Equal(640 * 1024, operational.RoutedHddCopyBufferSizeBytes);
        Assert.Equal(256 * 1024, operational.RoutedSameVolumeHddCopyBufferSizeBytes);
        Assert.Equal(320 * 1024, operational.RoutedUnknownCopyBufferSizeBytes);
    }

    [Fact]
    public void CreateOperationalSettings_WithInvalidPersistedValues_FallsBackToCleanInstallDefaults()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = -1,
            AllowCopyOptimisations = true,
            TinyFileFastPathKb = -1,
            BatchBufferKb = -1,
            CopyRoutingSsdBufferKb = -1,
            CopyRoutingUsbBufferKb = -1,
            CopyRoutingHddBufferKb = -1,
            CopyRoutingSameVolumeHddBufferKb = -1,
            CopyRoutingUnknownBufferKb = -1,
        };

        var operational = settings.CreateOperationalSettings();

        Assert.Equal(256 * 1024, operational.CopyBufferSizeBytes);
        Assert.True(operational.DestinationRoutingEnabled);
        Assert.Equal(256 * 1024, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(1024 * 1024, operational.BatchBufferBytes);
        Assert.Equal(1024 * 1024, operational.RoutedSsdCopyBufferSizeBytes);
        Assert.Equal(1024 * 1024, operational.RoutedUsbCopyBufferSizeBytes);
        Assert.Equal(512 * 1024, operational.RoutedHddCopyBufferSizeBytes);
        Assert.Equal(256 * 1024, operational.RoutedSameVolumeHddCopyBufferSizeBytes);
        Assert.Equal(512 * 1024, operational.RoutedUnknownCopyBufferSizeBytes);
    }
}
