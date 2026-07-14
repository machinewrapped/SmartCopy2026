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
        Assert.Equal(expectOptimised ? 64 * 1024 : 0, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(expectOptimised ? 2048 * 1024 : 0, operational.BatchBufferBytes);
    }

    [Fact]
    public void CreateOperationalSettings_WithInvalidPersistedValues_FallsBackToCleanInstallDefaults()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = -1,
            CopyOptimisationPlatformPolicy = new CopyOptimisationPlatformPolicy
            {
                Windows = new CopyOptimisationPolicy
                {
                    Enabled = true,
                    TinyFileFastPathKb = -1,
                    BatchBufferKb = -1,
                    CopyRoutingSsdBufferKb = -1,
                    CopyRoutingUsbBufferKb = -1,
                    CopyRoutingHddBufferKb = -1,
                    CopyRoutingSameVolumeHddBufferKb = -1,
                    CopyRoutingUnknownBufferKb = -1,
                },
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
    public void CreateOperationalSettings_WhenPolicyOverridesRouting_MapsRoutingProfile()
    {
        var settings = new AppSettings
        {
            CopyChunkSizeKb = 384,
            CopyOptimisationPlatformPolicy = new CopyOptimisationPlatformPolicy
            {
                Windows = new CopyOptimisationPolicy
                {
                    Enabled = true,
                    TinyFileFastPathKb = 64,
                    BatchBufferKb = 2048,
                    CopyRoutingSsdBufferKb = 1536,
                    CopyRoutingUsbBufferKb = 1792,
                    CopyRoutingHddBufferKb = 640,
                    CopyRoutingSameVolumeHddBufferKb = 256,
                    CopyRoutingUnknownBufferKb = 320,
                },
            },
        };

        var operational = settings.CreateOperationalSettings(OSPlatform.Windows);

        Assert.Equal(384 * 1024, operational.CopyBufferSizeBytes);
        Assert.True(operational.DestinationRoutingEnabled);
        Assert.Equal(64 * 1024, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(2048 * 1024, operational.BatchBufferBytes);
        Assert.Equal(1536 * 1024, operational.CopyBufferRouting.SsdBytes);
        Assert.Equal(1792 * 1024, operational.CopyBufferRouting.UsbBytes);
        Assert.Equal(640 * 1024, operational.CopyBufferRouting.HddBytes);
        Assert.Equal(256 * 1024, operational.CopyBufferRouting.SameVolumeHddBytes);
        Assert.Equal(320 * 1024, operational.CopyBufferRouting.UnknownBytes);
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

    private static CopyOptimisationPolicy TestPolicy(bool enabled) => new()
    {
        Enabled = enabled,
        TinyFileFastPathKb = 64,
        BatchBufferKb = 2048,
    };
}
