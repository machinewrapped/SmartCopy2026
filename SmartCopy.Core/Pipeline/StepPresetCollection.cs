namespace SmartCopy.Core.Pipeline;

public sealed class StepPresetCollection
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Key = TransformStepConfig.StepType ("Delete", "Flatten", etc.).
    /// Only user-saved presets are stored; built-ins are merged at read time.
    /// </summary>
    public Dictionary<string, List<StepPreset>> UserPresets { get; set; } = [];
}
