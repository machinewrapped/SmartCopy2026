using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace SmartCopy.UI.ViewModels;

public sealed class LogPanelLoggerProvider(LogPanelViewModel vm) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogPanelLogger(categoryName, vm);
    public void Dispose() { }
}

internal sealed class LogPanelLogger(string category, LogPanelViewModel vm) : ILogger
{
    // Shorten "SmartCopy.Core.Workflows.WorkflowPresetStore" → "WorkflowPresetStore"
    private readonly string _shortCategory = category.Contains('.')
        ? category[(category.LastIndexOf('.') + 1)..] : category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = $"[{_shortCategory}] {formatter(state, exception)}";

        if (exception != null)
        {
            message += ": " + exception.Message;
            if (vm.VerboseLogging)
                message += Environment.NewLine + exception;
        }

        var panelLevel = logLevel >= LogLevel.Error   ? LogLevel.Error
                       : logLevel >= LogLevel.Warning ? LogLevel.Warning
                       : LogLevel.Information;

        Dispatcher.UIThread.Post(() => vm.AddEntry(message, panelLevel), DispatcherPriority.Background);
    }
}
