using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineViewModelActiveStepTests
{
    private static async Task<PipelineViewModel> MakeVm(int stepCount = 3)
    {
        var vm = new PipelineViewModel(new TestAppContext());
        for (int i = 0; i < stepCount; i++)
        {
            await vm.AddStepFromResult(new FlattenStep());
        }

        return vm;
    }

    [Fact]
    public async Task IsActiveStep_IsFalseByDefault()
    {
        var vm = await MakeVm(2);

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }

    [Fact]
    public async Task SetActiveStep_HighlightsOnlyTargetStep()
    {
        var vm = await MakeVm(3);

        vm.SetActiveStep(1);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.True(vm.Steps[1].IsActiveStep);
        Assert.False(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public async Task SetActiveStep_ClearsPreviouslyActiveStep()
    {
        var vm = await MakeVm(3);

        vm.SetActiveStep(0);
        vm.SetActiveStep(2);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
        Assert.True(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public async Task ClearActiveStep_ResetsAllSteps()
    {
        var vm = await MakeVm(3);
        vm.SetActiveStep(1);

        vm.ClearActiveStep();

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }

    [Fact]
    public async Task SetActiveStep_FirstIndex_Works()
    {
        var vm = await MakeVm(2);

        vm.SetActiveStep(0);

        Assert.True(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
    }

    [Fact]
    public async Task SetActiveStep_LastIndex_Works()
    {
        var vm = await MakeVm(3);

        vm.SetActiveStep(2);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
        Assert.True(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public async Task ClearActiveStep_WhenNoActiveStep_DoesNotThrow()
    {
        var vm = await MakeVm(2);

        // Should not throw even with no step previously highlighted.
        vm.ClearActiveStep();

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }
}
