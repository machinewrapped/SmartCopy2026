namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidator
{
    public static async Task<PipelineValidationResult> ValidateAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineValidationContext context,
        CancellationToken ct = default)
    {
        var freeSpaceCache = new Dictionary<string,long?>();

        if (context.ProviderRegistry is not null)
        {
            // Cache free space on devices used as destination
            foreach (var step in steps)
            {
                if (step is IHasDestinationPath destination)
                {
                    PipelineHelper.CacheFreeSpaceForDestination(freeSpaceCache, 
                        destination, 
                        context.ProviderRegistry, 
                        ct );
                }            
            }            
        }

        var validationContext = new StepValidationContext(
            context.HasSelectedIncludedInputs,
            selectedBytes:    context.SelectedBytes,
            sourceProvider:   context.SourceProvider,
            providerRegistry: context.ProviderRegistry,
            cachedFreeSpace:  freeSpaceCache);

        if (steps.Count == 0)
        {
            validationContext.AddPipelineIssue("Pipeline.Empty", "Pipeline is empty.", PipelineValidationSeverity.Blocking);
            return new PipelineValidationResult(validationContext.Issues);
        }

        if (steps.Any(step => step.IsExecutable) == false)
        {
            validationContext.AddPipelineIssue("Pipeline.NoExecutableStep", "Pipeline has no executable steps.", PipelineValidationSeverity.Blocking);
            return new PipelineValidationResult(validationContext.Issues);
        }

        for (var i = 0; i < steps.Count; i++)
        {
            validationContext.StepIndex = i;

            await steps[i].Validate(validationContext);

            if (validationContext.HasBlockingIssue)
            {
                break;
            }
        }

        return new PipelineValidationResult(validationContext.Issues);
    }
}
