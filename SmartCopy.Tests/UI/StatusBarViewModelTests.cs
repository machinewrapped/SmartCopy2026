using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

public sealed class StatusBarViewModelTests
{
    [Fact]
    public void CancelScanCommand_RaisesCancelScanRequestedEvent()
    {
        var vm = new StatusBarViewModel();
        var raised = false;
        vm.CancelScanRequested += (_, _) => raised = true;

        vm.CancelScanCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void IsIdle_IsTrueByDefault()
    {
        var vm = new StatusBarViewModel();
        Assert.True(vm.IsIdle);
    }

    [Fact]
    public void IsIdle_IsFalseWhenScanning()
    {
        var vm = new StatusBarViewModel();
        vm.IsScanning = true;
        Assert.False(vm.IsIdle);
    }

    [Fact]
    public void IsIdle_IsFalseWhenProgressIsActive()
    {
        var vm = new StatusBarViewModel();
        vm.Progress.IsActive = true;
        Assert.False(vm.IsIdle);
    }

    [Fact]
    public void IsScanning_SetToTrue_FiresIsIdlePropertyChanged()
    {
        var vm = new StatusBarViewModel();
        var fired = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsIdle)) fired = true; };

        vm.IsScanning = true;

        Assert.True(fired);
    }

    [Fact]
    public void ProgressIsActive_SetToTrue_FiresIsIdlePropertyChanged()
    {
        var vm = new StatusBarViewModel();
        var fired = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsIdle)) fired = true; };

        vm.Progress.IsActive = true;

        Assert.True(fired);
    }
}
