using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels;

public enum LogLevel { Info, Warning, Error }

public record LogEntry(DateTime Timestamp, string Message, LogLevel Level = LogLevel.Info)
{
    public string ForegroundColor => Level switch
    {
        LogLevel.Warning => "#ffa500",
        LogLevel.Error => "#fa8072",
        _ => "#e0e0e0",
    };
}

public partial class LogPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded = false;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public int EntryCount => Entries.Count;

    public void AddEntry(string message, LogLevel level = LogLevel.Info)
    {
        Entries.Add(new LogEntry(DateTime.Now, message, level));
        OnPropertyChanged(nameof(EntryCount));
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        OnPropertyChanged(nameof(EntryCount));
    }
}
