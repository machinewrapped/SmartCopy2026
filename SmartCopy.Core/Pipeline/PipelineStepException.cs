namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Thrown by a pipeline step when it encounters a recoverable failure that should be
/// reported to the user with a clear description of which step failed and why.
/// </summary>
public sealed class PipelineStepException : Exception
{
    public string StepName { get; }
    public string UserMessage { get; }

    public PipelineStepException(string stepName, string userMessage, Exception innerException)
        : base($"{stepName}: {userMessage}", innerException)
    {
        StepName = stepName;
        UserMessage = userMessage;
    }
}
