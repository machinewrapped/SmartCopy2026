namespace SmartCopy.Core.Progress;

internal sealed class DelegateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
