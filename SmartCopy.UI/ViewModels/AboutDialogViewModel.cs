using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels;

public partial class AboutDialogViewModel : ViewModelBase
{
    public string AppVersion { get; } =
        typeof(AboutDialogViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
