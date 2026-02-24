using System.IO;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class StepPresetStoreTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static StepPreset MakeUserPreset(string name, StepKind stepType = StepKind.Copy) =>
        new()
        {
            Name = name,
            IsBuiltIn = false,
            Config = new TransformStepConfig(
                StepType: stepType,
                Parameters: new JsonObject { ["destinationPath"] = "/mem/out" }),
        };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPresetsForType_WhenNoUserFile_ReturnsBuiltIns_ForDelete()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var presets = await store.GetPresetsForTypeAsync(StepKind.Delete.ToString(), presetPath);

        Assert.Equal(2, presets.Count);
        Assert.All(presets, p => Assert.True(p.IsBuiltIn));
    }

    [Fact]
    public async Task GetPresetsForType_WhenNoUserFile_ReturnsBuiltIns_ForFlatten()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var presets = await store.GetPresetsForTypeAsync(StepKind.Flatten.ToString(), presetPath);

        Assert.Single(presets);
        Assert.True(presets[0].IsBuiltIn);
    }

    [Fact]
    public async Task GetPresetsForType_WhenNoUserFile_ReturnsEmpty_ForCopy()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var presets = await store.GetPresetsForTypeAsync(StepKind.Copy.ToString(), presetPath);

        Assert.Empty(presets);
    }

    [Fact]
    public async Task SaveUserPreset_ThenGet_ReturnsUserPreset()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var userPreset = MakeUserPreset("Copy to Backup");
        await store.SaveUserPresetAsync(StepKind.Copy.ToString(), userPreset, presetPath);

        var presets = await store.GetPresetsForTypeAsync(StepKind.Copy.ToString(), presetPath);

        Assert.Single(presets);
        Assert.Equal("Copy to Backup", presets[0].Name);
        Assert.False(presets[0].IsBuiltIn);
    }

    [Fact]
    public async Task SaveUserPreset_SameName_Overwrites()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        await store.SaveUserPresetAsync(StepKind.Copy.ToString(), MakeUserPreset("Duplicate"), presetPath);
        await store.SaveUserPresetAsync(StepKind.Copy.ToString(), MakeUserPreset("Duplicate"), presetPath);

        var presets = await store.GetPresetsForTypeAsync(StepKind.Copy.ToString(), presetPath);
        Assert.Single(presets);
    }

    [Fact]
    public async Task DeleteUserPreset_RemovesIt()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var preset = MakeUserPreset("To Be Deleted");
        await store.SaveUserPresetAsync(StepKind.Copy.ToString(), preset, presetPath);

        var before = await store.GetPresetsForTypeAsync(StepKind.Copy.ToString(), presetPath);
        var savedPreset = before.First(p => p.Name == "To Be Deleted");

        await store.DeleteUserPresetAsync(StepKind.Copy.ToString(), savedPreset.Id, presetPath);

        var after = await store.GetPresetsForTypeAsync(StepKind.Copy.ToString(), presetPath);
        Assert.DoesNotContain(after, p => p.Name == "To Be Deleted");
    }

    [Fact]
    public async Task BuiltIns_AlwaysPrecedeUserPresets()
    {
        using var dir = new TempDirectory();
        var store = new StepPresetStore();
        var presetPath = Path.Combine(dir.Path, "presets.json");

        var userPreset = MakeUserPreset("User Delete", StepKind.Delete);
        await store.SaveUserPresetAsync(StepKind.Delete.ToString(), userPreset, presetPath);

        var presets = await store.GetPresetsForTypeAsync(StepKind.Delete.ToString(), presetPath);

        // 2 built-ins + 1 user
        Assert.Equal(3, presets.Count);

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
