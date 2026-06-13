namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Resolves the copy parameters and selects the concrete <see cref="ICopyStrategy"/> for a
/// source→destination pair. This is the single decision point for destination/source/same-drive
/// sensitive routing, and the seam where per-device learned profiles (and future parallel
/// strategies) plug in.
/// </summary>
public interface ICopyStrategyPolicy
{
    /// <summary>Resolves settings and returns the strategy that will perform the transfer.</summary>
    ICopyStrategy Resolve(CopyStrategyInputs inputs);
}
