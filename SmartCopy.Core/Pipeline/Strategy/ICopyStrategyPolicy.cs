namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Resolves the copy parameters and selects the concrete <see cref="ICopyStrategy"/> for a
/// source→destination pair. This is the single decision point for destination/source/same-drive
/// sensitive routing. The default implementation uses a settings-backed baseline table; learned
/// policies can replace or wrap it using the same classifications, volume IDs, capabilities, and
/// base settings.
/// </summary>
public interface ICopyStrategyPolicy
{
    /// <summary>Resolves settings and returns the strategy that will perform the transfer.</summary>
    ICopyStrategy Resolve(CopyStrategyInputs inputs);
}
