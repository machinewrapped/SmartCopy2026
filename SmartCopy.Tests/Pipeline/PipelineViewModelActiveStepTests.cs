using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineViewModelActiveStepTests
{
    private static PipelineViewModel MakeVm(int stepCount = 3)
    {
        var vm = new PipelineViewModel(new TestAppContext());
        for (int i = 0; i < stepCount; i++)
            vm.AddStepFromResult(new FlattenStep());
        return vm;
    }

    [Fact]
    public void IsActiveStep_IsFalseByDefault()
    {
        var vm = MakeVm(2);

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }

    [Fact]
    public void SetActiveStep_HighlightsOnlyTargetStep()
    {
        var vm = MakeVm(3);

        vm.SetActiveStep(1);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.True(vm.Steps[1].IsActiveStep);
        Assert.False(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public void SetActiveStep_ClearsPreviouslyActiveStep()
    {
        var vm = MakeVm(3);

        vm.SetActiveStep(0);
        vm.SetActiveStep(2);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
        Assert.True(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public void ClearActiveStep_ResetsAllSteps()
    {
        var vm = MakeVm(3);
        vm.SetActiveStep(1);

        vm.ClearActiveStep();

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }

    [Fact]
    public void SetActiveStep_FirstIndex_Works()
    {
        var vm = MakeVm(2);

        vm.SetActiveStep(0);

        Assert.True(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
    }

    [Fact]
    public void SetActiveStep_LastIndex_Works()
    {
        var vm = MakeVm(3);

        vm.SetActiveStep(2);

        Assert.False(vm.Steps[0].IsActiveStep);
        Assert.False(vm.Steps[1].IsActiveStep);
        Assert.True(vm.Steps[2].IsActiveStep);
    }

    [Fact]
    public void ClearActiveStep_WhenNoActiveStep_DoesNotThrow()
    {
        var vm = MakeVm(2);

        // Should not throw even with no step previously highlighted.
        vm.ClearActiveStep();

        Assert.All(vm.Steps, s => Assert.False(s.IsActiveStep));
    }
}
