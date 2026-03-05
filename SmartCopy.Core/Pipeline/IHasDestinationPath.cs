namespace SmartCopy.Core.Pipeline;

public interface IHasDestinationPath
{
    string? DestinationPath { get; set; }

    bool HasDestinationPath { get; }
}
