using System.Diagnostics;
using System.Text;
using Avalonia.Threading;

namespace SmartCopy.UI.ViewModels;

/// <summary>
/// Routes Debug.WriteLine / Trace output to the log panel so that diagnostic
/// messages written anywhere in the app appear alongside explicit log entries.
/// </summary>
public sealed class LogPanelTraceListener : TraceListener
{
    private readonly LogPanelViewModel _vm;
    private readonly StringBuilder _buffer = new();

    public LogPanelTraceListener(LogPanelViewModel vm) => _vm = vm;

    public override void Write(string? message)
    {
        if (message is null) return;
        if (!ShouldAccept(message)) return;
        lock (_buffer)
            _buffer.Append(message);
    }

    public override void WriteLine(string? message)
    {
        if (!ShouldAccept(message))
        {
            lock (_buffer) _buffer.Clear(); // discard any buffered partial write
            return;
        }

        string line;
        lock (_buffer)
        {
            _buffer.Append(message);
            line = _buffer.ToString();
            _buffer.Clear();
        }

        var level = DetectLevel(line);
        Dispatcher.UIThread.Post(() => _vm.AddEntry(line, level), DispatcherPriority.Background);
    }

    // Honour the base-class Filter property so callers can configure filtering.
    // We use Verbose as the event type since Debug.WriteLine doesn't carry one.
    private bool ShouldAccept(string? message) =>
        Filter is null || Filter.ShouldTrace(null, "", TraceEventType.Verbose, 0, message, null, null, null);

    private static LogLevel DetectLevel(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("[ERROR", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;
        if (trimmed.StartsWith("[WARN", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Warning;
        return LogLevel.Info;
    }
}
