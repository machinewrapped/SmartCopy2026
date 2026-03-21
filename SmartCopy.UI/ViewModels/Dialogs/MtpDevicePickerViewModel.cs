using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaDevices;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels.Dialogs;

[SupportedOSPlatform("windows")]
public sealed class MtpPickerResult
{
    public MediaDevice Device { get; init; } = null!;
    public string Path { get; init; } = "";
}

[SupportedOSPlatform("windows")]
public sealed class MtpPickerItem
{
    public enum ItemKind { Back, Device, Folder }

    public ItemKind Kind { get; init; }
    public string DisplayName { get; init; } = "";
    public MediaDevice? Device { get; init; }
    public string? FolderPath { get; init; }
}

[SupportedOSPlatform("windows")]
public partial class MtpDevicePickerViewModel : ObservableObject
{
    private readonly List<MediaDevice> _allDevices;
    private MtpFileSystemProvider? _browsingProvider;
    private MediaDevice? _currentDevice;
    private string _currentPath = "";
    private readonly Stack<string> _pathStack = new();

    public ObservableCollection<MtpPickerItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _locationHeader = "Connected Devices";

    public MtpPickerResult? Result { get; private set; }

    public event Action? OkRequested;
    public event Action? CancelRequested;

    private static bool IsMtpDevice(MediaDevice d)
    {
        try { return d.Protocol?.StartsWith("MTP:", StringComparison.OrdinalIgnoreCase) == true; }
        catch { return false; }
    }

    public MtpDevicePickerViewModel()
    {
        _allDevices = [.. MediaDevice.GetDevices().Where(IsMtpDevice)];
        ShowDeviceList();
    }

    private void ShowDeviceList()
    {
        Items.Clear();
        foreach (var dev in _allDevices)
            Items.Add(new MtpPickerItem { Kind = MtpPickerItem.ItemKind.Device, DisplayName = dev.FriendlyName ?? dev.Model ?? "Unknown Device", Device = dev });
        LocationHeader = "Connected Devices";
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    public async Task NavigateIntoAsync(MtpPickerItem item)
    {
        switch (item.Kind)
        {
            case MtpPickerItem.ItemKind.Back when _pathStack.Count == 0:
                _browsingProvider?.Dispose();
                _browsingProvider = null;
                _currentDevice = null;
                _pathStack.Clear();
                ShowDeviceList();
                break;

            case MtpPickerItem.ItemKind.Back:
                _currentPath = _pathStack.Pop();
                await LoadFolderAsync();
                break;

            case MtpPickerItem.ItemKind.Device:
                _currentDevice = item.Device!;
                var name = string.IsNullOrEmpty(item.Device!.FriendlyName) ? item.Device.Model : item.Device.FriendlyName;
                _browsingProvider = new MtpFileSystemProvider(item.Device!, $"mtp://{name}/");
                _currentPath = _browsingProvider.RootPath;
                await LoadFolderAsync();
                break;

            case MtpPickerItem.ItemKind.Folder:
                _pathStack.Push(_currentPath);
                _currentPath = item.FolderPath!;
                await LoadFolderAsync();
                break;
        }
    }

    private async Task LoadFolderAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        Items.Clear();

        var backLabel = _pathStack.Count == 0 ? "← Back to devices" : "← ..";

        try
        {
            var children = await _browsingProvider!.GetChildrenAsync(_currentPath, CancellationToken.None);

            Items.Add(new MtpPickerItem { Kind = MtpPickerItem.ItemKind.Back, DisplayName = backLabel });

            foreach (var node in children.Where(n => n.IsDirectory).OrderBy(n => n.Name))
                Items.Add(new MtpPickerItem { Kind = MtpPickerItem.ItemKind.Folder, DisplayName = node.Name, FolderPath = node.FullPath });

            LocationHeader = _browsingProvider.SplitPath(_currentPath).LastOrDefault() ?? "Device";
        }
        catch (Exception ex)
        {
            Items.Add(new MtpPickerItem { Kind = MtpPickerItem.ItemKind.Back, DisplayName = backLabel });
            ErrorMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (_currentDevice is null) return;
        _browsingProvider?.Dispose();
        _browsingProvider = null;
        Result = new MtpPickerResult { Device = _currentDevice, Path = _currentPath };
        OkRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        _browsingProvider?.Dispose();
        _browsingProvider = null;
        CancelRequested?.Invoke();
    }

    private bool CanConfirm() => _currentDevice is not null && !IsLoading;
}
