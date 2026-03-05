namespace SmartCopy.Core.Pipeline;

public interface IHasDestinationPath
{
    string? DestinationPath { get; }

    bool HasDestinationPath { get; }

    void ChangeDestinationPath(string destinationPath);
}
