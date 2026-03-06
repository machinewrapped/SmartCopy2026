using SmartCopy.Core.Settings;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Settings;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "settings.json");
        var store = new AppSettingsStore();

        var settings = new AppSettings
        {
            SettingsFilePath = filePath,
            LastSourcePath = "/music",
            IncludeHidden = true,
            CopyChunkSizeKb = 1024,
            RecentSources = ["/one", "/two"],
        };

        await store.SaveAsync(settings, CancellationToken.None);
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
        var filePath = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json");

        var store = new AppSettingsStore();
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SchemaVersion);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsNewOptions()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "settings.json");
        var store = new AppSettingsStore();

        var settings = new AppSettings
        {
            SettingsFilePath = filePath,
            RestoreLastWorkflow     = true,
            RestoreLastSourcePath   = false,
            DisableDestructivePreview = true,
            DeleteToRecycleBin      = false,
            DefaultOverwriteMode    = "Always",
            FullPreScan             = true,
            LazyExpandScan          = true,
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.True(loaded.RestoreLastWorkflow);
        Assert.False(loaded.RestoreLastSourcePath);
        Assert.True(loaded.DisableDestructivePreview);
        Assert.False(loaded.DeleteToRecycleBin);
        Assert.Equal("Always", loaded.DefaultOverwriteMode);
        Assert.True(loaded.FullPreScan);
        Assert.True(loaded.LazyExpandScan);
    }
}

