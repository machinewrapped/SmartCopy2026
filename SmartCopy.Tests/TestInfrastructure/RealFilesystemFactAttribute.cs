namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that exercise a real filesystem and therefore depend on
/// OS file-change notification timing. Such tests are too flaky to gate CI, so they are skipped
/// unless <see cref="EnvironmentVariable"/> is set:
/// <code>
/// $env:SMARTCOPY_REALFS_TESTS = "1"; dotnet test
/// </code>
/// </summary>
public sealed class RealFilesystemFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "SMARTCOPY_REALFS_TESTS";

    public RealFilesystemFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Real-filesystem test. Set {EnvironmentVariable}=1 to run.";
    }
}
