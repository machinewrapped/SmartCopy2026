using System.Text.Json.Serialization;

namespace SmartCopy.Core.Pipeline;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepKind
{
    Flatten,
    Rebase,
    Rename,
    Convert,
    Copy,
    Move,
    Delete,
    Custom,
    SelectAll,
    InvertSelection,
    ClearSelection,
}
