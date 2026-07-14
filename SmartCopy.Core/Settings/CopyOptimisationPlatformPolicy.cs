using System.Runtime.InteropServices;
namespace SmartCopy.Core.Settings;

public sealed class CopyOptimisationPolicy
{
    public bool Enabled { get; set; }

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
            return Windows ??= CopyOptimisationPolicy.DisabledDefaults();
        }

        if (platform.Equals(OSPlatform.OSX))
        {
            return MacOS ??= CopyOptimisationPolicy.DisabledDefaults();
        }

        if (platform.Equals(OSPlatform.Linux))
        {
            return Linux ??= CopyOptimisationPolicy.DisabledDefaults();
        }

        return Other ??= CopyOptimisationPolicy.DisabledDefaults();
    }
}
