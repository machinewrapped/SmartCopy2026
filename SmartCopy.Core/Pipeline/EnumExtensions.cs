using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace SmartCopy.Core.Pipeline;

public static class EnumExtensions
{
    public static string GetDisplayName(this Enum value)
    {
        var type = value.GetType();
        var name = value.ToString();
        var field = type.GetField(name);
        
        if (field != null)
        {
            var attribute = field.GetCustomAttribute<DisplayAttribute>();
            var displayName = attribute?.GetName();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }
        }
        
        return name;
    }
}
