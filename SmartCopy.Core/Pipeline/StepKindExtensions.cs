using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace SmartCopy.Core.Pipeline;

public static class StepKindExtensions
{
    public static string ForDisplay(this StepKind kind) => kind.GetDisplayName();

    public static string GetIcon(this StepKind kind)
    {
        var field = kind.GetType().GetField(kind.ToString());
        var attribute = field?.GetCustomAttribute<StepIconAttribute>();
        return attribute?.Icon ?? "?";
    }
}
