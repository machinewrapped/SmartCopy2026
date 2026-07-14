using System.Runtime.InteropServices;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Settings;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsOperationalSettingsTests
{
    public static TheoryData<OSPlatform, bool> CleanInstallPlatformCases => new()
    {
        { OSPlatform.Windows, true },
        { OSPlatform.OSX, false },
        { OSPlatform.Linux, false },
        { OSPlatform.Create("OTHER"), false },
    };

    public static TheoryData<OSPlatform, CopyOptimisationPlatformPolicy, bool> PlatformPolicyCases => new()
    {
        {
            OSPlatform.Windows,
            new CopyOptimisationPlatformPolicy
            {
                Windows = TestPolicy(enabled: false),
            },
            false
        },
        {
            OSPlatform.Windows,
            new CopyOptimisationPlatformPolicy
            {
                Windows = TestPolicy(enabled: true),
            },
            true
        },
        {
            OSPlatform.OSX,
            new CopyOptimisationPlatformPolicy
            {
                Windows = TestPolicy(enabled: true),
                MacOS = TestPolicy(enabled: false),
            },
            false
        },
        {
            OSPlatform.OSX,
            new CopyOptimisationPlatformPolicy
            {
                Windows = TestPolicy(enabled: true),
                MacOS = TestPolicy(enabled: true),
                Linux = TestPolicy(enabled: false),
            },
            true
        },
        {
            OSPlatform.Linux,
            new CopyOptimisationPlatformPolicy
            {
                Windows = TestPolicy(enabled: false),
                MacOS = TestPolicy(enabled: true),
                Linux = TestPolicy(enabled: false),
            },
            false
        },
    };

    [Theory]
    [MemberData(nameof(CleanInstallPlatformCases))]
    public void CreateOperationalSettings_CleanInstall_UsesWindowsPolicyOnly(
        OSPlatform platform,
        bool expectOptimised)
    {
        var settings = new AppSettings();

        var operational = settings.CreateOperationalSettings(platform);

        Assert.Equal(expectOptimised, operational.DestinationRoutingEnabled);
        Assert.Equal(expectOptimised ? 256 * 1024 : 0, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(expectOptimised ? 1024 * 1024 : 0, operational.BatchBufferBytes);
    }

    [Theory]
    [MemberData(nameof(PlatformPolicyCases))]
    public void CreateOperationalSettings_UsesSelectedPlatformPolicy(
        OSPlatform platform,
        CopyOptimisationPlatformPolicy platformPolicy,
        bool expectOptimised)
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = 384,
            CopyOptimisationPlatformPolicy = platformPolicy,
        };

        var operational = settings.CreateOperationalSettings(platform);

        Assert.Equal(384 * 1024, operational.CopyBufferSizeBytes);
        Assert.Equal(expectOptimised, operational.DestinationRoutingEnabled);
        Assert.Equal(expectOptimised ? 256 * 1024 : 0, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(expectOptimised ? 1024 * 1024 : 0, operational.BatchBufferBytes);
    }

    [Fact]
    public void CreateOperationalSettings_EnabledPolicy_UsesCanonicalValues()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = -1,
            CopyOptimisationPlatformPolicy = new CopyOptimisationPlatformPolicy
            {
                Windows = CopyOptimisationPolicy.EnabledDefaults(),
            },
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

    [Fact]
    public void GetCopyOptimisationPolicy_WhenPersistedPlatformPolicyIsNull_MaterializesFallback()
    {
        var settings = new AppSettings
        {
            CopyOptimisationPlatformPolicy = new CopyOptimisationPlatformPolicy
            {
                Windows = null!,
            },
        };

        var policy = settings.GetCopyOptimisationPolicy(OSPlatform.Windows);
        policy.Enabled = true;

        Assert.Same(policy, settings.CopyOptimisationPlatformPolicy.Windows);
        Assert.True(settings.CopyOptimisationPlatformPolicy.Windows.Enabled);
    }

    private static CopyOptimisationPolicy TestPolicy(bool enabled) => new() { Enabled = enabled };
}
