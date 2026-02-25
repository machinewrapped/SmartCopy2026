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
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString()));

    [Fact]
    public async Task GetUserPresets_EmptyForMissingDirectory()
    {
        var store = new WorkflowPresetStore();
        var dir = Path.Combine(Path.GetTempPath(), "SmartCopy2026.Tests", Guid.NewGuid().ToString("N"), "missing");

        var presets = await store.GetUserPresetsAsync(dir);

        Assert.Empty(presets);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("My Workflow", MakeConfig("My Workflow"), tmp.Path);

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Single(loaded);
        Assert.Equal("My Workflow", loaded[0].Name);
        Assert.Equal("/mem/Source", loaded[0].Config.SourcePath);
    }

    [Fact]
    public async Task SaveAndGet_MultiplePresets_SortedByName()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("Zed", MakeConfig("Zed"), tmp.Path);
        await store.SaveUserPresetAsync("Alpha", MakeConfig("Alpha"), tmp.Path);
        await store.SaveUserPresetAsync("Mango", MakeConfig("Mango"), tmp.Path);

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Equal(["Alpha", "Mango", "Zed"], loaded.Select(p => p.Name));
    }

    [Fact]
    public async Task Save_SameName_Overwrites()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("Workflow", MakeConfig("Workflow") with { SourcePath = "/mem/A" }, tmp.Path);
        await store.SaveUserPresetAsync("Workflow", MakeConfig("Workflow") with { SourcePath = "/mem/B" }, tmp.Path);

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Single(loaded);
        Assert.Equal("/mem/B", loaded[0].Config.SourcePath);
    }

    [Fact]
    public async Task DeleteUserPreset_RemovesFile()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("ToDelete", MakeConfig("ToDelete"), tmp.Path);
        await store.DeleteUserPresetAsync("ToDelete", tmp.Path);

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteUserPreset_NonExistent_DoesNotThrow()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.DeleteUserPresetAsync("NonExistent", tmp.Path);
    }

    [Fact]
    public async Task RenameUserPreset_UpdatesNameAndContent()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("Old Name", MakeConfig("Old Name"), tmp.Path);
        await store.RenameUserPresetAsync("Old Name", "New Name", tmp.Path);

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Single(loaded);
        Assert.Equal("New Name", loaded[0].Name);
    }

    [Fact]
    public async Task GetUserPresets_SkipsCorruptFiles()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore();

        await store.SaveUserPresetAsync("Good", MakeConfig("Good"), tmp.Path);
        File.WriteAllText(Path.Combine(tmp.Path, "corrupt.sc2workflow"), "not valid json {{{{");

        var loaded = await store.GetUserPresetsAsync(tmp.Path);
        Assert.Single(loaded);
        Assert.Equal("Good", loaded[0].Name);
    }

    [Fact]
    public void GetDefaultPresetDirectory_ReturnsNonEmpty()
    {
        var dir = WorkflowPresetStore.GetDefaultPresetDirectory();
        Assert.False(string.IsNullOrWhiteSpace(dir));
        Assert.Contains("SmartCopy2026", dir);
    }
}
