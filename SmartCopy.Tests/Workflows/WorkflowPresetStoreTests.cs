using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Workflows;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Workflows;

public sealed class WorkflowPresetStoreTests
{
    private static WorkflowConfig MakeConfig(string name) => new(
        Name: name,
        Description: null,
        SourcePath: "/mem/Source",
        FilterChain: new FilterChainConfig(name, null, []),
        Pipeline: new PipelineConfig(
            Name: name,
            Description: null,
            Steps: [new TransformStepConfig(StepKind.Copy, new System.Text.Json.Nodes.JsonObject { ["destinationPath"] = "/mem/Target" })],
            OverwriteMode: OverwriteMode.IfNewer.ToString()));

    [Fact]
    public async Task GetUserPresets_EmptyForMissingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartCopy2026.Tests", Guid.NewGuid().ToString("N"), "missing");
        var store = new WorkflowPresetStore(dir);

        var presets = await store.GetUserPresetsAsync();

        Assert.Empty(presets);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("My Workflow", MakeConfig("My Workflow"));

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("My Workflow", loaded[0].Name);
        Assert.Equal("/mem/Source", loaded[0].Config.SourcePath);
    }

    [Fact]
    public async Task SaveAndGet_MultiplePresets_SortedByName()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("Zed", MakeConfig("Zed"));
        await store.SaveUserPresetAsync("Alpha", MakeConfig("Alpha"));
        await store.SaveUserPresetAsync("Mango", MakeConfig("Mango"));

        var loaded = await store.GetUserPresetsAsync();
        Assert.Equal(["Alpha", "Mango", "Zed"], loaded.Select(p => p.Name));
    }

    [Fact]
    public async Task Save_SameName_Overwrites()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("Workflow", MakeConfig("Workflow") with { SourcePath = "/mem/A" });
        await store.SaveUserPresetAsync("Workflow", MakeConfig("Workflow") with { SourcePath = "/mem/B" });

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("/mem/B", loaded[0].Config.SourcePath);
    }

    [Fact]
    public async Task DeleteUserPreset_RemovesFile()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("ToDelete", MakeConfig("ToDelete"));
        await store.DeleteUserPresetAsync("ToDelete");

        var loaded = await store.GetUserPresetsAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteUserPreset_NonExistent_DoesNotThrow()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.DeleteUserPresetAsync("NonExistent");
    }

    [Fact]
    public async Task RenameUserPreset_UpdatesNameAndContent()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("Old Name", MakeConfig("Old Name"));
        await store.RenameUserPresetAsync("Old Name", "New Name");

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("New Name", loaded[0].Name);
    }

    [Fact]
    public async Task GetUserPresets_SkipsCorruptFiles()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);

        await store.SaveUserPresetAsync("Good", MakeConfig("Good"));
        File.WriteAllText(Path.Combine(tmp.Path, "corrupt.sc2workflow"), "not valid json {{{{");

        var loaded = await store.GetUserPresetsAsync();
        Assert.Single(loaded);
        Assert.Equal("Good", loaded[0].Name);
    }
}
