using System.Runtime.InteropServices;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Settings;

public sealed class CopyOptimisationPolicy
{
    public bool Enabled { get; set; }
    public int TinyFileFastPathKb { get; set; } =
        (int)(OperationalSettings.DefaultEnabledTinyFileFastPathThresholdBytes / 1024);
    public int BatchBufferKb { get; set; } =
        (int)(OperationalSettings.DefaultEnabledBatchBufferBytes / 1024);
    public int CopyRoutingSsdBufferKb { get; set; } = CopyBufferRoutingSettings.DefaultSsdBytes / 1024;
    public int CopyRoutingUsbBufferKb { get; set; } = CopyBufferRoutingSettings.DefaultUsbBytes / 1024;
    public int CopyRoutingHddBufferKb { get; set; } = CopyBufferRoutingSettings.DefaultHddBytes / 1024;
    public int CopyRoutingSameVolumeHddBufferKb { get; set; } = CopyBufferRoutingSettings.DefaultSameVolumeHddBytes / 1024;
    public int CopyRoutingUnknownBufferKb { get; set; } = CopyBufferRoutingSettings.DefaultUnknownBytes / 1024;

    public static CopyOptimisationPolicy EnabledDefaults() => new() { Enabled = true };

    public static CopyOptimisationPolicy DisabledDefaults() => new() { Enabled = false };
}

public sealed class CopyOptimisationPlatformPolicy
{
    public CopyOptimisationPolicy Windows { get; set; } = CopyOptimisationPolicy.EnabledDefaults();
    public CopyOptimisationPolicy MacOS { get; set; } = CopyOptimisationPolicy.DisabledDefaults();
    public CopyOptimisationPolicy Linux { get; set; } = CopyOptimisationPolicy.DisabledDefaults();
    public CopyOptimisationPolicy Other { get; set; } = CopyOptimisationPolicy.DisabledDefaults();

    public CopyOptimisationPolicy For(OSPlatform platform)
    {
        if (platform.Equals(OSPlatform.Windows))
        {
            return Windows ?? CopyOptimisationPolicy.DisabledDefaults();
        }

        if (platform.Equals(OSPlatform.OSX))
        {
            return MacOS ?? CopyOptimisationPolicy.DisabledDefaults();
        }

        if (platform.Equals(OSPlatform.Linux))
        {
            return Linux ?? CopyOptimisationPolicy.DisabledDefaults();
        }

        return Other ?? CopyOptimisationPolicy.DisabledDefaults();
    }
}
