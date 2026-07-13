namespace SmartCopy.Core.FileSystem;

/// <summary>Allows a provider to initialise state that is scoped to one pipeline execution.</summary>
internal interface IDeleteOperationProvider
{
    void BeginDeleteOperation();
}
