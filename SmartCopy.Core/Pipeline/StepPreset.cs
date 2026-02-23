using System;

namespace SmartCopy.Core.Pipeline;

public sealed class StepPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public TransformStepConfig Config { get; set; } = null!;
}
