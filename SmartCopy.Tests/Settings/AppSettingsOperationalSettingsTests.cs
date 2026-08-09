using System.Runtime.InteropServices;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Settings;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsOperationalSettingsTests
{
    public static TheoryData<OSPlatform, bool> PlatformDefaultCases => new()
    {
        { OSPlatform.Windows, true },
        { OSPlatform.OSX, true },
        { OSPlatform.Linux, true },
        { OSPlatform.Create("OTHER"), true },
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
        var linux = settings.CreateOperationalSettings(OSPlatform.Linux);
        var other = settings.CreateOperationalSettings(OSPlatform.Create("OTHER"));

        Assert.Equal(enabled, windows.DestinationRoutingEnabled);
        Assert.Equal(enabled, mac.DestinationRoutingEnabled);
        Assert.Equal(enabled, linux.DestinationRoutingEnabled);
        Assert.Equal(enabled, other.DestinationRoutingEnabled);
    }

    [Theory]
    [InlineData("Windows", true, null)]
    [InlineData("Windows", false, false)]
    [InlineData("OSX", true, null)]
    [InlineData("OSX", false, false)]
    [InlineData("Linux", true, null)]
    [InlineData("Linux", false, false)]
    [InlineData("Other", true, null)]
    [InlineData("Other", false, false)]
    public void SetOptimisedCopyEnabled_StoresOnlyNonDefaultChoices(
        string platformName,
        bool value,
        bool? expectedPersistedValue)
    {
        var platform = platformName switch
        {
            "Windows" => OSPlatform.Windows,
            "OSX" => OSPlatform.OSX,
            "Linux" => OSPlatform.Linux,
            _ => OSPlatform.Create("OTHER"),
        };
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
