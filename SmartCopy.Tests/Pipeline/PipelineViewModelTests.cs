using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineViewModelTests
{
    [Fact]
    public void BuildLivePipeline_ReturnsStepSequence()
    {
        var vm = new PipelineViewModel();
        vm.AddStepFromResult(StepKind.Flatten, new FlattenStep());
        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/out"));

        var pipeline = vm.BuildLivePipeline();

        Assert.Equal(2, pipeline.Steps.Count);
        Assert.Equal("Flatten", pipeline.Steps[0].StepType);
        Assert.Equal("Copy", pipeline.Steps[1].StepType);
    }

    [Fact]
    public void AddRemoveStep_RaisesPipelineChanged()
    {
        var vm = new PipelineViewModel();
        var count = 0;
        vm.PipelineChanged += (_, _) => count++;

        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/out"));
        vm.RemoveStepCommand.Execute(vm.Steps[0]);

        Assert.True(count >= 2);
    }

    [Fact]
    public void DestinationPathInlineEdit_RaisesPipelineChanged()
    {
        var vm = new PipelineViewModel();
        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/out"));
        var count = 0;
        vm.PipelineChanged += (_, _) => count++;

        vm.Steps[0].DestinationPath = "/mem/new";

        Assert.True(count >= 1);
    }

    [Fact]
    public void ReplaceStep_UpdatesViewModelStep()
    {
        var vm = new PipelineViewModel();
        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/out"));
        var first = vm.Steps[0];

        vm.ReplaceStep(first, new MoveStep("/mem/archive"));

        Assert.Equal(StepKind.Move, first.Kind);
        Assert.Equal("/mem/archive", first.DestinationPath);
    }

    [Fact]
    public void FirstDestinationPath_TracksFirstCopyMove()
    {
        var vm = new PipelineViewModel();
        vm.AddStepFromResult(StepKind.Flatten, new FlattenStep());
        vm.AddStepFromResult(StepKind.Move, new MoveStep("/mem/archive"));
        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/backup"));

        Assert.Equal("/mem/archive", vm.FirstDestinationPath);
    }

    [Fact]
    public void InvalidStep_ShowsValidationMessageAndBlocksRun()
    {
        var vm = new PipelineViewModel();
        vm.AddStepFromResult(StepKind.Copy, new CopyStep(""));

        Assert.False(vm.CanRun);
        Assert.False(string.IsNullOrWhiteSpace(vm.BlockingValidationMessage));
        Assert.True(vm.Steps[0].HasValidationError);
        Assert.False(string.IsNullOrWhiteSpace(vm.Steps[0].ValidationMessage));
    }

    [Fact]
    public void AddStep_CustomName_OverridesAutoSummary()
    {
        var vm = new PipelineViewModel();

        vm.AddStepFromResult(StepKind.Copy, new CopyStep("/mem/out"), "Music Mirror");

        Assert.Equal("Music Mirror", vm.Steps[0].Summary);
        Assert.Equal("Music Mirror", vm.Steps[0].CustomName);
    }

    [Fact]
    public void LoadPreset_ReadsCustomNameMetadata()
    {
        var vm = new PipelineViewModel();
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
                        "Copy",
                        new JsonObject
                        {
                            ["destinationPath"] = "/mem/out",
                            ["customName"] = "Audio Backup",
                        }),
                ],
                OverwriteMode: OverwriteMode.IfNewer.ToString(),
                DeleteMode: DeleteMode.Trash.ToString()),
        };

        vm.LoadPreset(preset);

        Assert.Single(vm.Steps);
        Assert.Equal("Audio Backup", vm.Steps[0].Summary);
        Assert.Equal("Audio Backup", vm.Steps[0].CustomName);
    }
}
