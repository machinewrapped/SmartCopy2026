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
    public async Task SaveGetDeleteUserPreset_RoundTrips()
    {
        var dir = CreateTempDirectory();
        var store = new PipelinePresetStore(dir);
        var config = new PipelineConfig(
            Name: "Music Copy",
            Description: "copy music",
            Steps:
            [
                new TransformStepConfig(StepKind.Copy, new JsonObject { ["destinationPath"] = "/mem/Mirror" }),
            ]);

        await store.SaveUserPresetAsync("Music Copy", config);
        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("Music Copy", loaded[0].Name);
        Assert.False(loaded[0].IsBuiltIn);

        await store.DeleteUserPresetAsync("Music Copy");
        var afterDelete = await store.GetUserPresetsAsync();
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task SaveUserPreset_SameName_Overwrites()
    {
        var dir = CreateTempDirectory();
        var store = new PipelinePresetStore(dir);

        var copyConfig = new PipelineConfig(
            Name: "My Pipeline",
            Description: null,
            Steps:
            [
                new TransformStepConfig(StepKind.Copy, new JsonObject { ["destinationPath"] = "/a" }),
            ]);

        var moveConfig = copyConfig with
        {
            Steps =
            [
                new TransformStepConfig(StepKind.Move, new JsonObject { ["destinationPath"] = "/b" }),
            ],
        };

        await store.SaveUserPresetAsync("My Pipeline", copyConfig);
        await store.SaveUserPresetAsync("My Pipeline", moveConfig);

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal(StepKind.Move, loaded[0].Config.Steps[0].StepType);
    }

    [Fact]
    public void PipelineStepFactory_RoundTripsImplementedStepTypes()
    {
        var input = new IPipelineStep[]
        {
            new CopyStep("/mem/copy"),
            new MoveStep("/mem/move"),
            new DeleteStep(),
            new FlattenStep(FlattenConflictStrategy.Skip),
            new RenameStep("{name}_new"),
            new RebaseStep("source", "target"),
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
        var config = new TransformStepConfig((StepKind)999, []);

        Assert.Throws<UnknownStepTypeException>(() => PipelineStepFactory.FromConfig(config));
    }
}
