using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineViewModelSwapTests
{
    [Fact]
    public async Task RequestSwapWithSource_RaisesEvent_ForCopyStep()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("/mem/dest"));
        var step = vm.Steps[0];

        PipelineStepViewModel? raised = null;
        vm.SwapSourceRequested += (_, s) => raised = s;

        vm.RequestSwapWithSource(step);

        Assert.Same(step, raised);
    }

    [Fact]
    public async Task RequestSwapWithSource_RaisesEvent_ForMoveStep()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new MoveStep("/mem/dest"));
        var step = vm.Steps[0];

        PipelineStepViewModel? raised = null;
        vm.SwapSourceRequested += (_, s) => raised = s;

        vm.RequestSwapWithSource(step);

        Assert.Same(step, raised);
    }

    [Fact]
    public async Task RequestSwapWithSource_DoesNotRaise_WhenPipelineIsRunning()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep("/mem/dest"));
        var step = vm.Steps[0];

        vm.IsRunning = true;

        var raised = false;
        vm.SwapSourceRequested += (_, _) => raised = true;

        vm.RequestSwapWithSource(step);

        Assert.False(raised);
    }

    [Fact]
    public async Task RequestSwapWithSource_DoesNotRaise_WhenStepHasNoDestination()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new CopyStep(""));
        var step = vm.Steps[0];

        var raised = false;
        vm.SwapSourceRequested += (_, _) => raised = true;

        vm.RequestSwapWithSource(step);

        Assert.False(raised);
    }

    [Fact]
    public async Task RequestSwapWithSource_DoesNotRaise_ForNonDestinationStep()
    {
        var vm = new PipelineViewModel(new TestAppContext());
        await vm.AddStepFromResult(new FlattenStep());
        var step = vm.Steps[0];

        var raised = false;
        vm.SwapSourceRequested += (_, _) => raised = true;

        vm.RequestSwapWithSource(step);

        Assert.False(raised);
    }
}
