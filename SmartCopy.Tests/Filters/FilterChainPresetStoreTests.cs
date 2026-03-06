using System;
using System.Linq;
using System.Text.Json.Nodes;
using SmartCopy.Core.Filters;
using Xunit;

namespace SmartCopy.Tests.Filters;

public sealed class FilterChainPresetStoreTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "SmartCopy2026.Tests.Change",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task SaveGetDeleteUserPreset_RoundTrips()
    {
        var dir = CreateTempDirectory();
        var store = new FilterChainPresetStore(dir);

        var config = new FilterChainConfig(
            Name: "Test Chain",
            Description: "A chain for testing",
            Filters:
            [
                new FilterConfig(
                    FilterType: "Extension",
                    IsEnabled: true,
                    Mode: "Include",
                    Parameters: new JsonObject { ["extensions"] = "mp4" })
            ]);

        // Save
        await store.SaveUserPresetAsync("Test Chain", config);

        // Get
        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("Test Chain", loaded[0].Name);
        Assert.Equal("Test Chain", loaded[0].Config.Name);
        Assert.Equal("A chain for testing", loaded[0].Config.Description);
        Assert.Single(loaded[0].Config.Filters);
        Assert.Equal("Extension", loaded[0].Config.Filters[0].FilterType);

        // Delete
        await store.DeleteUserPresetAsync("Test Chain");
        var afterDelete = await store.GetUserPresetsAsync();
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task SaveUserPreset_SameName_Overwrites()
    {
        var dir = CreateTempDirectory();
        var store = new FilterChainPresetStore(dir);

        var config1 = new FilterChainConfig(
            Name: "My Chain",
            Description: null,
            Filters:
            [
                new FilterConfig(
                    FilterType: "Extension",
                    IsEnabled: true,
                    Mode: "Include",
                    Parameters: new JsonObject { ["extensions"] = "txt" })
            ]);

        var config2 = config1 with
        {
            Filters =
            [
                new FilterConfig(
                    FilterType: "Attribute",
                    IsEnabled: false,
                    Mode: "Exclude",
                    Parameters: new JsonObject())
            ]
        };

        await store.SaveUserPresetAsync("My Chain", config1);
        await store.SaveUserPresetAsync("My Chain", config2);

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("My Chain", loaded[0].Name);
        Assert.Equal("Attribute", loaded[0].Config.Filters[0].FilterType);
        Assert.False(loaded[0].Config.Filters[0].IsEnabled);
    }

    [Fact]
    public async Task GetUserPresetsAsync_SkipsMalformedFiles()
    {
        var dir = CreateTempDirectory();
        var store = new FilterChainPresetStore(dir);

        // Write a valid preset
        var validConfig = new FilterChainConfig("Valid", null, []);
        await store.SaveUserPresetAsync("Valid", validConfig);

        // Write a malformed preset
        await File.WriteAllTextAsync(Path.Combine(dir, "malformed.sc2filterchain"), "{ invalid json ]");

        var loaded = await store.GetUserPresetsAsync();

        // Should only load the valid one
        Assert.Single(loaded);
        Assert.Equal("Valid", loaded[0].Name);
    }
}
