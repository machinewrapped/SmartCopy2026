namespace SmartCopy.Core.Pipeline;

[AttributeUsage(AttributeTargets.Field)]
public sealed class StepIconAttribute : Attribute
{
    public string Icon { get; }

    public StepIconAttribute(string icon)
    {
        Icon = icon;
    }
}
