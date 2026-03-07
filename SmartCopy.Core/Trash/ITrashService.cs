namespace SmartCopy.Core.Trash;

public interface ITrashService
{
    bool IsAvailable { get; }
    Task TrashAsync(string fullPath, CancellationToken ct);
}
