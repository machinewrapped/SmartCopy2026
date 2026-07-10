using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

public sealed class OperationalSettingsTests
{
    [Fact]
    public void Normalize_WhenBatchBufferExceedsArrayMaxLength_Throws()
    {
        var settings = new OperationalSettings
        {
            BatchBufferBytes = (long)Array.MaxLength + 1,
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => settings.Normalize());

        Assert.Equal(nameof(OperationalSettings.BatchBufferBytes), ex.ParamName);
    }
}
