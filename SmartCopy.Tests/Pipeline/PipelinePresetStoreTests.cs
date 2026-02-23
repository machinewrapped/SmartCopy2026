using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelinePresetStoreTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "SmartCopy2026.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task GetStandardPresets_AlwaysReturnsBaselineSet()
    {
        var store = new PipelinePresetStore();

        var presets = await store.GetStandardPresetsAsync();

        Assert.Contains(presets, preset => preset.Name == "Copy only");
        Assert.Contains(presets, preset => preset.Name == "Move only");
        Assert.Contains(presets, preset => preset.Name == "Delete to Trash");
        Assert.Contains(presets, preset => preset.Name == "Flatten -> Copy");
        Assert.All(presets, preset => Assert.True(preset.IsBuiltIn));
    }

    [Fact]
    public async Task SaveGetDeleteUserPreset_RoundTrips()
    {
        var store = new PipelinePresetStore();
        var dir = CreateTempDirectory();
        var config = new PipelineConfig(
            Name: "Music Copy",
            Description: "copy music",
            Steps:
            [
                new TransformStepConfig("Copy", new JsonObject { ["destinationPath"] = "/mem/Mirror" }),
            ],
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString());

        await store.SaveUserPresetAsync("Music Copy", config, dir);
        var loaded = await store.GetUserPresetsAsync(dir);
        Assert.Single(loaded);
        Assert.Equal("Music Copy", loaded[0].Name);
        Assert.False(loaded[0].IsBuiltIn);

        await store.DeleteUserPresetAsync("Music Copy", dir);
        var afterDelete = await store.GetUserPresetsAsync(dir);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task SaveUserPreset_SameName_Overwrites()
    {
        var store = new PipelinePresetStore();
        var dir = CreateTempDirectory();

        var copyConfig = new PipelineConfig(
            Name: "My Pipeline",
            Description: null,
            Steps:
            [
                new TransformStepConfig("Copy", new JsonObject { ["destinationPath"] = "/a" }),
            ],
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString());

        var moveConfig = copyConfig with
        {
            Steps =
            [
                new TransformStepConfig("Move", new JsonObject { ["destinationPath"] = "/b" }),
            ],
        };

        await store.SaveUserPresetAsync("My Pipeline", copyConfig, dir);
        await store.SaveUserPresetAsync("My Pipeline", moveConfig, dir);

        var loaded = await store.GetUserPresetsAsync(dir);
        Assert.Single(loaded);
        Assert.Equal("Move", loaded[0].Config.Steps[0].StepType);
    }

    [Fact]
    public async Task GetAllPresets_StandardPrecedesUser()
    {
        var store = new PipelinePresetStore();
        var dir = CreateTempDirectory();
        var config = new PipelineConfig(
            Name: "Z Last",
            Description: null,
            Steps:
            [
                new TransformStepConfig("Copy", new JsonObject { ["destinationPath"] = "/mem/out" }),
            ],
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString());

        await store.SaveUserPresetAsync("Z Last", config, dir);

        var all = await store.GetAllPresetsAsync(dir);

        Assert.True(all.Count >= 5);
        Assert.True(all[0].IsBuiltIn);
        Assert.True(all[1].IsBuiltIn);
        Assert.True(all[2].IsBuiltIn);
        Assert.True(all[3].IsBuiltIn);
        Assert.False(all[^1].IsBuiltIn);
        Assert.Equal("Z Last", all[^1].Name);
    }

    [Fact]
    public void PipelineStepFactory_RoundTripsImplementedStepTypes()
    {
        var input = new ITransformStep[]
        {
            new CopyStep("/mem/copy"),
            new MoveStep("/mem/move"),
            new DeleteStep(DeleteMode.Permanent),
            new FlattenStep(FlattenConflictStrategy.Skip),
            new RenameStep("{name}_new"),
            new RebaseStep("source", "target"),
            new ConvertStep("mp3"),
        };

        foreach (var step in input)
        {
            var rebuilt = PipelineStepFactory.FromConfig(step.Config);
            Assert.Equal(step.StepType, rebuilt.StepType);
        }
    }

    [Fact]
    public void PipelineStepFactory_UnknownStepType_ThrowsUnknownStepTypeException()
    {
        var config = new TransformStepConfig("Nope", new JsonObject());

        Assert.Throws<UnknownStepTypeException>(() => PipelineStepFactory.FromConfig(config));
    }
}
