using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Workflows;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.Tests.Workflows;

public sealed class WorkflowMenuViewModelTests
{
    private static WorkflowConfig MakeConfig(string name) => new(
        Name: name,
        Description: null,
        SourcePath: "mem://Source",
        FilterChain: new FilterChainConfig(name, null, []),
        Pipeline: new PipelineConfig(
            Name: name,
            Description: null,
            Steps: [new TransformStepConfig(StepKind.Copy, 
                new System.Text.Json.Nodes.JsonObject { ["destinationPath"] = "mem://Target" })]));

    [Fact]
    public async Task RefreshAsync_PopulatesSavedWorkflows()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);
        await store.SaveUserPresetAsync("Workflow A", MakeConfig("Workflow A"));
        await store.SaveUserPresetAsync("Workflow B", MakeConfig("Workflow B"));

        var vm = new WorkflowMenuViewModel(store);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.SavedWorkflows.Count);
        Assert.Contains(vm.SavedWorkflows, p => p.Name == "Workflow A");
        Assert.Contains(vm.SavedWorkflows, p => p.Name == "Workflow B");
    }

    [Fact]
    public async Task RefreshAsync_ClearsPreviousItems()
    {
        using var tmp = new TempDirectory();
        var store = new WorkflowPresetStore(tmp.Path);
        await store.SaveUserPresetAsync("First", MakeConfig("First"));

        var vm = new WorkflowMenuViewModel(store);
        await vm.RefreshAsync();
        Assert.Single(vm.SavedWorkflows);

        await store.DeleteUserPresetAsync("First");
        await vm.RefreshAsync();
        Assert.Empty(vm.SavedWorkflows);
    }

    [Fact]
    public void SaveWorkflow_RaisesSaveRequested()
    {
        var vm = new WorkflowMenuViewModel(new WorkflowPresetStore(Path.GetTempPath()));
        vm.CanSave = true;

        var raised = false;
        vm.SaveRequested += (_, _) => raised = true;

        vm.SaveWorkflowCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void LoadWorkflow_RaisesLoadRequestedWithName()
    {
        var vm = new WorkflowMenuViewModel(new WorkflowPresetStore(Path.GetTempPath()));

        string? received = null;
        vm.LoadRequested += (_, name) => received = name;

        vm.LoadWorkflowCommand.Execute("My Workflow");

        Assert.Equal("My Workflow", received);
    }

    [Fact]
    public void ManageWorkflows_RaisesManageRequested()
    {
        var vm = new WorkflowMenuViewModel(new WorkflowPresetStore(Path.GetTempPath()));

        var raised = false;
        vm.ManageRequested += (_, _) => raised = true;

        vm.ManageWorkflowsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void CanSave_ControlsSaveCommandCanExecute()
    {
        var vm = new WorkflowMenuViewModel(new WorkflowPresetStore(Path.GetTempPath()));

        vm.CanSave = false;
        Assert.False(vm.SaveWorkflowCommand.CanExecute(null));

        vm.CanSave = true;
        Assert.True(vm.SaveWorkflowCommand.CanExecute(null));
    }
}
