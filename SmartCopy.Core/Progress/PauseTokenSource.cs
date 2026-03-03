namespace SmartCopy.Core.Progress;

/// <summary>
/// A cooperative pause gate for async pipelines.
/// Starts in the running state (not paused).
/// </summary>
public sealed class PauseTokenSource : IDisposable
{
    // A completed TCS means "running"; an incomplete TCS means "paused".
    // Replaced on each Pause(); completed on Resume() or Dispose().
    private TaskCompletionSource _resumeTcs;
    private readonly Lock _lock = new();
    private volatile bool _paused; // volatile: enables lock-free fast-path read in WaitIfPausedAsync
    private bool _disposed;

    public PauseTokenSource()
    {
        _resumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _resumeTcs.SetResult(); // starts running
    }

    public bool IsPaused => _paused; // volatile read — no lock needed

    /// <summary>Pauses the gate. No-op if already paused.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused) return;
            _paused = true;
            _resumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Resumes the gate. No-op if not paused.</summary>
    public void Resume()
    {
        TaskCompletionSource? tcs;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_paused) return;
            _paused = false;
            tcs = _resumeTcs;
        }
        tcs.TrySetResult(); // complete outside lock — avoids invoking continuations while holding lock
    }

    /// <summary>
    /// Returns immediately when not paused; blocks asynchronously while paused.
    /// Propagates <paramref name="ct"/> cancellation while blocked.
    /// </summary>
    public async ValueTask WaitIfPausedAsync(CancellationToken ct)
    {
        // First check: lock-free volatile read. Covers the common (running) case with no contention.
        if (!_paused) return;

        // Second check: under lock, to safely capture the TCS before Resume() can replace it.
        Task task;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_paused) return;
            task = _resumeTcs.Task;
        }
        // Awaits the resume signal; throws OperationCanceledException if ct fires first.
        await task.WaitAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        TaskCompletionSource? tcs;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _paused = false;
            tcs = _resumeTcs;
        }
        tcs.TrySetResult(); // unblock any in-flight WaitIfPausedAsync calls
    }
}
