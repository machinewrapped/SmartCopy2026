#if WINDOWS
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaDevices;

namespace SmartCopy.UI.ViewModels.Dialogs;

public partial class MtpDevicePickerViewModel : ObservableObject
{
    private readonly List<MediaDevice> _devices;

    public ObservableCollection<string> DeviceNames { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private int _selectedIndex = -1;

    public MediaDevice? SelectedDevice =>
        SelectedIndex >= 0 && SelectedIndex < _devices.Count ? _devices[SelectedIndex] : null;

    public event Action? OkRequested;
    public event Action? CancelRequested;

    public MtpDevicePickerViewModel()
    {
        _devices = [.. MediaDevice.GetDevices()];
        foreach (var device in _devices)
            DeviceNames.Add(device.FriendlyName);
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => OkRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    private bool CanConfirm() => SelectedDevice is not null;
}
#endif
