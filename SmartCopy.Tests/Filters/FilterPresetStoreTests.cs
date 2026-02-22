using System.IO;
using System.Text.Json.Nodes;
using SmartCopy.Core.Filters;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Filters;

public sealed class FilterPresetStoreTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static FilterPreset MakeUserPreset(string name, string filterType = "Extension") =>
        new FilterPreset
        {
            Name = name,
            IsBuiltIn = false,
            Config = new FilterConfig(
                FilterType: filterType,
                IsEnabled: true,
                Mode: "Include",
                Parameters: new JsonObject { ["extensions"] = "xyz" }),
        };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPresetsForType_WhenNoUserFile_ReturnsBuiltIns()
    {
        using var dir = new TempDirectory();
        var store = new FilterPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var presets = await store.GetPresetsForTypeAsync("Extension", presetPath);

        Assert.Equal(4, presets.Count);
        Assert.All(presets, p => Assert.True(p.IsBuiltIn));
    }

    [Fact]
    public async Task SaveUserPreset_ThenGet_ReturnsUserPreset()
    {
        using var dir = new TempDirectory();
        var store = new FilterPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var userPreset = MakeUserPreset("My Extensions");
        await store.SaveUserPresetAsync("Extension", userPreset, presetPath);

        var presets = await store.GetPresetsForTypeAsync("Extension", presetPath);

        // Built-ins (4) + 1 user preset
        Assert.Equal(5, presets.Count);

        var saved = presets.FirstOrDefault(p => p.Name == "My Extensions");
        Assert.NotNull(saved);
        Assert.False(saved.IsBuiltIn);
    }

    [Fact]
    public async Task SaveUserPreset_SameName_Overwrites()
    {
        using var dir = new TempDirectory();
        var store = new FilterPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var first = MakeUserPreset("Duplicate Name");
        await store.SaveUserPresetAsync("Extension", first, presetPath);

        var second = MakeUserPreset("Duplicate Name");
        await store.SaveUserPresetAsync("Extension", second, presetPath);

        var presets = await store.GetPresetsForTypeAsync("Extension", presetPath);

        // Should still be 4 built-ins + exactly 1 user preset
        var userPresets = presets.Where(p => !p.IsBuiltIn).ToList();
        Assert.Single(userPresets);
        Assert.Equal("Duplicate Name", userPresets[0].Name);
    }

    [Fact]
    public async Task DeleteUserPreset_RemovesIt()
    {
        using var dir = new TempDirectory();
        var store = new FilterPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var preset = MakeUserPreset("To Be Deleted");
        await store.SaveUserPresetAsync("Extension", preset, presetPath);

        // Confirm it was saved
        var before = await store.GetPresetsForTypeAsync("Extension", presetPath);
        var savedPreset = before.First(p => p.Name == "To Be Deleted");

        await store.DeleteUserPresetAsync("Extension", savedPreset.Id, presetPath);

        var after = await store.GetPresetsForTypeAsync("Extension", presetPath);
        Assert.DoesNotContain(after, p => p.Name == "To Be Deleted");
    }

    [Fact]
    public async Task BuiltIns_AlwaysPrecedeUserPresets()
    {
        using var dir = new TempDirectory();
        var store = new FilterPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var userPreset = MakeUserPreset("User First");
        await store.SaveUserPresetAsync("Extension", userPreset, presetPath);

        var presets = await store.GetPresetsForTypeAsync("Extension", presetPath);

        // All built-ins come before any user preset
        var firstUserIndex = presets
            .Select((p, i) => (p, i))
            .First(pair => !pair.p.IsBuiltIn)
            .i;

        var lastBuiltInIndex = presets
            .Select((p, i) => (p, i))
            .Last(pair => pair.p.IsBuiltIn)
            .i;

        Assert.True(lastBuiltInIndex < firstUserIndex,
            "Expected all built-in presets to appear before user presets.");
    }
}
