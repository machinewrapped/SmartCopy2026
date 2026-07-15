using SmartCopy.Core.Pipeline;
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
            ShowHiddenFiles = true,
            CopyChunkSizeKb = 1024,
            OptimisedCopyEnabled = true,
            RecentSources = ["/one", "/two"],
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.Equal("/music", loaded.LastSourcePath);
        Assert.True(loaded.ShowHiddenFiles);
        Assert.Equal(1024, loaded.CopyChunkSizeKb);
        Assert.True(loaded.OptimisedCopyEnabled);
        Assert.Equal(2, loaded.RecentSources.Count);
    }

    [Fact]
    public async Task LoadInto_RoundTripsConditionallyIgnoredOptimisedCopySetting()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "settings.json");
        var store = new AppSettingsStore();
        await store.SaveAsync(new AppSettings
        {
            SettingsFilePath = filePath,
            OptimisedCopyEnabled = true,
        }, CancellationToken.None);

        var settings = new AppSettings { SettingsFilePath = filePath };
        await store.LoadIntoAsync(settings, CancellationToken.None);

        Assert.True(settings.OptimisedCopyEnabled);
    }

    [Fact]
    public async Task LoadAndSave_LegacyCopyTuning_IgnoresAndRemovesNumericFields()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(filePath, """
            {
              "CopyOptimisationPlatformPolicy": {
                "Windows": {
                  "Enabled": false,
                  "TinyFileFastPathKb": 64,
                  "BatchBufferKb": 2048,
                  "CopyRoutingSsdBufferKb": 1536,
                  "CopyRoutingUsbBufferKb": 1792,
                  "CopyRoutingHddBufferKb": 640,
                  "CopyRoutingSameVolumeHddBufferKb": 128,
                  "CopyRoutingUnknownBufferKb": 320
                }
              }
            }
            """);
        var store = new AppSettingsStore();

        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.Null(loaded.OptimisedCopyEnabled);
        loaded.OptimisedCopyEnabled = true;
        var operational = loaded.CreateOperationalSettings(System.Runtime.InteropServices.OSPlatform.Windows);
        Assert.Equal(256 * 1024, operational.TinyFileFastPathThresholdBytes);
        Assert.Equal(1024 * 1024, operational.BatchBufferBytes);

        await store.SaveAsync(loaded, CancellationToken.None);
        var savedJson = await File.ReadAllTextAsync(filePath);

        Assert.Contains("\"OptimisedCopyEnabled\": true", savedJson);
        Assert.DoesNotContain("CopyOptimisationPlatformPolicy", savedJson);
        Assert.DoesNotContain("TinyFileFastPathKb", savedJson);
        Assert.DoesNotContain("BatchBufferKb", savedJson);
        Assert.DoesNotContain("CopyRoutingSsdBufferKb", savedJson);
        Assert.DoesNotContain("CopyRoutingUsbBufferKb", savedJson);
        Assert.DoesNotContain("CopyRoutingHddBufferKb", savedJson);
        Assert.DoesNotContain("CopyRoutingSameVolumeHddBufferKb", savedJson);
        Assert.DoesNotContain("CopyRoutingUnknownBufferKb", savedJson);
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
            AllowDeleteWithoutPreview = true,
            AllowOverwriteWithoutPreview = true,
            DefaultOverwriteMode    = OverwriteMode.Always,
            FullPreScan             = true,
            LazyExpandScan          = true,
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(filePath, CancellationToken.None);

        Assert.True(loaded.RestoreLastWorkflow);
        Assert.False(loaded.RestoreLastSourcePath);
        Assert.True(loaded.AllowDeleteWithoutPreview);
        Assert.True(loaded.AllowOverwriteWithoutPreview);
        Assert.Equal(OverwriteMode.Always, loaded.DefaultOverwriteMode);
        Assert.True(loaded.FullPreScan);
        Assert.True(loaded.LazyExpandScan);
    }
}

