using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SmartCopy.UI.ViewModels;

public record LogEntry(DateTime Timestamp, string Message, LogLevel Level = LogLevel.Information)
{
    public string FormattedTimestamp => $"{Timestamp:HH:mm:ss}  ";
    public string DisplayText        => LevelPrefix() + Message;
    public bool   IsWarning          => Level == LogLevel.Warning;
    public bool   IsError            => Level >= LogLevel.Error;

    private string LevelPrefix() => Level switch
    {
        LogLevel.Warning  => "[WARN]  ",
        >= LogLevel.Error => "[ERR]   ",
        _                 => ""
    };
}

public partial class LogPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private bool _verboseLogging;

    private LogLevel? _filterLevel;
    public LogLevel? FilterLevel
    {
        get => _filterLevel;
        private set
        {
            if (SetProperty(ref _filterLevel, value))
            {
                OnPropertyChanged(nameof(IsWarningFilterActive));
                OnPropertyChanged(nameof(IsErrorFilterActive));
                RebuildDisplayedEntries();
            }
        }
    }

    public bool IsWarningFilterActive => FilterLevel == LogLevel.Warning;
    public bool IsErrorFilterActive   => FilterLevel == LogLevel.Error;

    public ObservableCollection<LogEntry> Entries          { get; } = [];
    public ObservableCollection<LogEntry> DisplayedEntries { get; } = [];

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
    public bool IsErrorBadgeVisible   => ErrorCount > 0;

    public void AddEntry(string message, LogLevel level = LogLevel.Information)
    {
        var entry = new LogEntry(DateTime.Now, message, level);
        Entries.Add(entry);
        OnPropertyChanged(nameof(EntryCount));
        if (PassesFilter(entry)) DisplayedEntries.Add(entry);
        if (level == LogLevel.Warning)
            WarningCount++;
        else if (level >= LogLevel.Error)
            ErrorCount++;
        if (level >= LogLevel.Warning)
            IsExpanded = true;
    }

    [RelayCommand]
    private void ToggleWarningFilter() =>
        FilterLevel = IsWarningFilterActive ? null : LogLevel.Warning;

    [RelayCommand]
    private void ToggleErrorFilter() =>
        FilterLevel = IsErrorFilterActive ? null : LogLevel.Error;

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        DisplayedEntries.Clear();
        WarningCount = 0;
        ErrorCount = 0;
        FilterLevel = null;
        OnPropertyChanged(nameof(EntryCount));
    }

    private bool PassesFilter(LogEntry e) => _filterLevel == null || e.Level == _filterLevel;

    private void RebuildDisplayedEntries()
    {
        DisplayedEntries.Clear();
        foreach (var e in Entries)
            if (PassesFilter(e)) DisplayedEntries.Add(e);
    }
}
