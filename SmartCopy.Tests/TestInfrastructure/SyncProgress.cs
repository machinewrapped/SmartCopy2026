namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// Synchronous IProgress&lt;T&gt; shim for tests. Unlike Progress&lt;T&gt;, the callback is invoked
/// inline on the reporting thread rather than being posted to the captured SynchronizationContext,
/// so assertions can check accumulated values immediately after the awaited operation completes.
/// </summary>
internal sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
