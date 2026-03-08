namespace SmartCopy.Core.Trash;

public sealed class NullTrashService : ITrashService
{
    public bool IsAvailable => false;

    public Task TrashAsync(string fullPath, CancellationToken ct)
        => throw new NotSupportedException("Trash is not supported on this platform.");
}
