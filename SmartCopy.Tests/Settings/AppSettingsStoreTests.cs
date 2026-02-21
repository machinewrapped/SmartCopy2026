using SmartCopy.Core.Settings;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        using var temp = new TempDirectory();
        var filePath = System.IO.Path.Combine(temp.Path, "settings.json");
        var store = new AppSettingsStore();

        var settings = new AppSettings
        {
            LastSourcePath = "/music",
            IncludeHidden = true,
            CopyChunkSizeKb = 1024,
            RecentSources = ["/one", "/two"],
        };

        await store.SaveAsync(settings, filePath, CancellationToken.None);
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.Equal("/music", loaded.LastSourcePath);
        Assert.True(loaded.IncludeHidden);
        Assert.Equal(1024, loaded.CopyChunkSizeKb);
        Assert.Equal(2, loaded.RecentSources.Count);
    }

    [Fact]
    public async Task Load_WithCorruptJson_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var filePath = System.IO.Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json");

        var store = new AppSettingsStore();
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SchemaVersion);
    }
}

