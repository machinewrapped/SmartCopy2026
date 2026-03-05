using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace SmartCopy.Core.Pipeline;

public static class StepKindExtensions
{
    public static string ForDisplay(this StepKind kind)
    {
        var member = typeof(StepKind).GetMember(kind.ToString());
        if (member.Length > 0)
        {
            var attribute = member[0].GetCustomAttribute<DisplayAttribute>();
            if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Name))
            {
                return attribute.Name;
            }
        }
        return kind.ToString();
    }
}
