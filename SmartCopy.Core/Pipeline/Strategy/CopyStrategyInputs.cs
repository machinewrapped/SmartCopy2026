using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Describes a source→destination copy pair so an <see cref="ICopyStrategyPolicy"/> can
/// resolve the effective <see cref="OperationalSettings"/> and select a concrete
/// <see cref="ICopyStrategy"/>. All inputs are already known before the first byte is copied,
/// so resolution performs no I/O. The default policy consumes the classifications and
/// same-volume flag; learned policies can additionally key profiles by the optional volume IDs.
/// </summary>
public readonly record struct CopyStrategyInputs(
    OperationalSettings Base,
    DriveClassification Source,
    DriveClassification Target,
    ProviderCapabilities SourceCaps,
    ProviderCapabilities TargetCaps,
    bool SameVolume,
    string? SourceVolumeId = null,
    string? TargetVolumeId = null);
