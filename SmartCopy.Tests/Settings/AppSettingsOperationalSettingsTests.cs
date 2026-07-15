using System.Runtime.InteropServices;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Settings;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsOperationalSettingsTests
{
    public static TheoryData<OSPlatform, bool> PlatformDefaultCases => new()
    {
        { OSPlatform.Windows, true },
        { OSPlatform.OSX, false },
        { OSPlatform.Linux, false },
        { OSPlatform.Create("OTHER"), false },
    };

    [Theory]
    [MemberData(nameof(PlatformDefaultCases))]
    public void CreateOperationalSettings_UsesPlatformDefault(
        OSPlatform platform,
        bool expectOptimised)
    {
        var operational = new AppSettings().CreateOperationalSettings(platform);

        Assert.Equal(expectOptimised, operational.DestinationRoutingEnabled);
        Assert.Equal(expectOptimised ? 256 * 1024 : 0, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(expectOptimised ? 1024 * 1024 : 0, operational.BatchBufferBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateOperationalSettings_UsesExplicitChoiceRegardlessOfPlatform(bool enabled)
    {
        var settings = new AppSettings { OptimisedCopyEnabled = enabled };

        var windows = settings.CreateOperationalSettings(OSPlatform.Windows);
        var mac = settings.CreateOperationalSettings(OSPlatform.OSX);

        Assert.Equal(enabled, windows.DestinationRoutingEnabled);
        Assert.Equal(enabled, mac.DestinationRoutingEnabled);
    }

    [Theory]
    [InlineData("Windows", true, null)]
    [InlineData("Windows", false, false)]
    [InlineData("OSX", false, null)]
    [InlineData("OSX", true, true)]
    public void SetOptimisedCopyEnabled_StoresOnlyNonDefaultChoices(
        string platformName,
        bool value,
        bool? expectedPersistedValue)
    {
        var platform = platformName == "Windows" ? OSPlatform.Windows : OSPlatform.OSX;
        var settings = new AppSettings();

        settings.SetOptimisedCopyEnabled(platform, value);

        Assert.Equal(expectedPersistedValue, settings.OptimisedCopyEnabled);
    }

    [Fact]
    public void CreateOperationalSettings_EnabledPolicy_UsesCanonicalValues()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = -1,
            OptimisedCopyEnabled = true,
        };

        var operational = settings.CreateOperationalSettings(OSPlatform.Windows);

        Assert.Equal(256 * 1024, operational.CopyBufferSizeBytes);
        Assert.True(operational.DestinationRoutingEnabled);
        Assert.Equal(256 * 1024, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(1024 * 1024, operational.BatchBufferBytes);
        Assert.Equal(1024 * 1024, operational.CopyBufferRouting.SsdBytes);
        Assert.Equal(1024 * 1024, operational.CopyBufferRouting.UsbBytes);
        Assert.Equal(512 * 1024, operational.CopyBufferRouting.HddBytes);
        Assert.Equal(256 * 1024, operational.CopyBufferRouting.SameVolumeHddBytes);
        Assert.Equal(512 * 1024, operational.CopyBufferRouting.UnknownBytes);
    }
}
