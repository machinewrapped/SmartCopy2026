using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels;

public enum LogLevel { Info, Warning, Error }

public record LogEntry(DateTime Timestamp, string Message, LogLevel Level = LogLevel.Info);

public partial class LogPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private LogLevel _minimumLevel = LogLevel.Info;

    public Array LogLevels { get; } = Enum.GetValues<LogLevel>();

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public int EntryCount => Entries.Count;

    private int _warningCount;
    public int WarningCount
    {
        get => _warningCount;
        private set { if (SetProperty(ref _warningCount, value)) OnPropertyChanged(nameof(IsWarningBadgeVisible)); }
    }

    private int _errorCount;
    public int ErrorCount
    {
        get => _errorCount;
        private set { if (SetProperty(ref _errorCount, value)) OnPropertyChanged(nameof(IsErrorBadgeVisible)); }
    }

    public bool IsWarningBadgeVisible => WarningCount > 0;
    public bool IsErrorBadgeVisible => ErrorCount > 0;

    public void AddEntry(string message, LogLevel level = LogLevel.Info)
    {
        Entries.Add(new LogEntry(DateTime.Now, message, level));
        OnPropertyChanged(nameof(EntryCount));
        if (level == LogLevel.Warning)
            WarningCount++;
        else if (level == LogLevel.Error)
            ErrorCount++;
        if (level >= LogLevel.Warning)
            IsExpanded = true;
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        WarningCount = 0;
        ErrorCount = 0;
        OnPropertyChanged(nameof(EntryCount));
    }
}
