using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineViewModelTests
{
    [Fact]
    public async Task BuildLivePipeline_ReturnsStepSequence()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new FlattenStep());
        await vm.AddStepFromResult(new CopyStep("mem://out"));

        var pipeline = vm.BuildLivePipeline();

        Assert.Equal(2, pipeline.Steps.Count);
        Assert.Equal(StepKind.Flatten, pipeline.Steps[0].StepType);
        Assert.Equal(StepKind.Copy, pipeline.Steps[1].StepType);
    }

    [Fact]
    public async Task AddRemoveStep_RaisesPipelineChanged()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        var count = 0;
        vm.PipelineChanged += (_, _) => count++;

        await vm.AddStepFromResult(new CopyStep("mem://out"));
        vm.RemoveStepCommand.Execute(vm.Steps[0]);

        Assert.True(count >= 2);
    }

    [Fact]
    public async Task ReplaceStep_UpdatesViewModelStep()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("mem://out"));
        var first = vm.Steps[0];

        await vm.ReplaceStep(first, new MoveStep("mem://archive"));

        Assert.Equal(StepKind.Move, first.Kind);
        Assert.Equal("mem://archive", first.DestinationPath);
    }

    [Fact]
    public async Task FirstDestinationPath_TracksFirstCopyMove()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new FlattenStep());
        await vm.AddStepFromResult(new MoveStep("mem://archive"));
        await vm.AddStepFromResult(new CopyStep("mem://backup"));

        Assert.Equal("mem://archive", vm.FirstDestinationPath);
    }

    [Fact]
    public async Task InvalidStep_ShowsValidationMessageAndBlocksRun()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep(""));

        Assert.False(vm.CanRun);
        Assert.False(string.IsNullOrWhiteSpace(vm.BlockingValidationMessage));
        Assert.True(vm.Steps[0].HasValidationError);
        Assert.False(string.IsNullOrWhiteSpace(vm.Steps[0].ValidationMessage));
    }

    [Fact]
    public async Task ExecutablePipeline_RequiresSelectedIncludedFiles()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("mem://out"));

        Assert.False(vm.CanRun);
        Assert.Equal("At least one file must be selected.", vm.BlockingValidationMessage);

        vm.SetSelectedIncludedFileCount(1);

        Assert.True(vm.CanRun);
        Assert.Null(vm.BlockingValidationMessage);
    }

    [Fact]
    public async Task AddStep_CustomName_OverridesAutoSummary()
    {
        var vm = new PipelineViewModel(new TestAppContext());

        await vm.AddStepFromResult(new CopyStep("mem://out"), "Music Mirror");

        Assert.Equal("Music Mirror", vm.Steps[0].Label);
        Assert.Equal("Music Mirror", vm.Steps[0].CustomName);
    }

    [Fact]
    public async Task LoadPreset_ReadsCustomNameMetadata()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        var preset = new PipelinePreset
        {
            Name = "Named step",
            IsBuiltIn = false,
            Config = new PipelineConfig(
                Name: "Named step",
                Description: null,
                Steps:
                [
                    new TransformStepConfig(
                        StepKind.Copy,
                        new JsonObject
                        {
                            ["destinationPath"] = "mem://out",
                            ["customName"] = "Audio Backup",
                        }),
                ]),
        };

        await vm.LoadPreset(preset);

        Assert.Single(vm.Steps);
        Assert.Equal("Audio Backup", vm.Steps[0].Label);
        Assert.Equal("Audio Backup", vm.Steps[0].CustomName);
    }

    // ── Locking ───────────────────────────────────────────────────────────────

    private static async Task<PipelineViewModel> MakeRunnableVm()
    {
        TestAppContext appContext = TestAppContext.FromProvider(new MemoryFileSystemProvider());
        var vm = new PipelineViewModel(appContext);
        await vm.AddStepFromResult(new CopyStep("mem://out"));
        vm.SetSelectedIncludedFileCount(1);
        return vm;
    }

    [Fact]
    public async Task IsScanning_BlocksCanRun()
    {
        var vm = await MakeRunnableVm();
        Assert.True(vm.CanRun);

        vm.IsScanning = true;

        Assert.False(vm.CanRun);
    }

    [Fact]
    public async Task IsRunning_BlocksRemoveStepCommand()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("mem://out"));

        vm.IsRunning = true;

        Assert.False(vm.RemoveStepCommand.CanExecute(vm.Steps[0]));
    }

    [Fact]
    public async Task IsRunning_BlocksRequestEditStepCommand()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("mem://out"));

        vm.IsRunning = true;

        Assert.False(vm.RequestEditStepCommand.CanExecute(vm.Steps[0]));
    }

    [Fact]
    public async Task IsNotRunning_UnblocksStepCommands()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("mem://out"));
        vm.IsRunning = true;

        vm.IsRunning = false;

        Assert.True(vm.RemoveStepCommand.CanExecute(vm.Steps[0]));
        Assert.True(vm.RequestEditStepCommand.CanExecute(vm.Steps[0]));
    }
}
