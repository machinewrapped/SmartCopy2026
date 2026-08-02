namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that run a real Unix system tool. Deterministic where they
/// run, so unlike <see cref="RealFilesystemFactAttribute"/> they are not opt-in.
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires a Unix system tool.";
    }
}
