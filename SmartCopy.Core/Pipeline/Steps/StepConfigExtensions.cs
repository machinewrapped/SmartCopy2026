namespace SmartCopy.Core.Pipeline.Steps;

internal static class StepConfigExtensions
{
    internal static string GetRequired(this TransformStepConfig config, string key)
    {
        var value = config.GetOptional(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Step '{config.StepType}' requires non-empty parameter '{key}'.",
                nameof(config));
        }
        return value;
    }

    internal static string GetOptional(this TransformStepConfig config, string key) =>
        config.Parameters[key]?.GetValue<string>()?.Trim() ?? string.Empty;

    internal static T ParseEnum<T>(this TransformStepConfig config, string key, T defaultValue)
        where T : struct, Enum
    {
        var raw = config.GetOptional(key);
        return Enum.TryParse<T>(raw, ignoreCase: true, out var value) ? value : defaultValue;
    }

    internal static int GetOptionalInt(this TransformStepConfig config, string key, int defaultValue)
    {
        var raw = config.GetOptional(key);
        return int.TryParse(raw, out var v) ? v : defaultValue;
    }
}
