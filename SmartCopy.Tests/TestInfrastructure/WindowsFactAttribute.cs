namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that depend on Windows filesystem behaviour, such as
/// case-insensitive paths. Deterministic where they run, so they are not opt-in.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows filesystem behaviour.";
    }
}
