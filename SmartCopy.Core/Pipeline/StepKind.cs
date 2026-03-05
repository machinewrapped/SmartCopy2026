using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartCopy.Core.Pipeline;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepKind
{
    [Display(Name = "Flatten path")]
    Flatten,
    [Display(Name = "Rebase paths")]
    Rebase,
    [Display(Name = "Rename files")]
    Rename,
    [Display(Name = "Convert files")]
    Convert,
    [Display(Name = "Copy files")]
    Copy,
    [Display(Name = "Move files")]
    Move,
    [Display(Name = "Delete files")]
    Delete,
    [Display(Name = "Custom")]
    Custom,
    [Display(Name = "Select all")]
    SelectAll,
    [Display(Name = "Invert selection")]
    InvertSelection,
    [Display(Name = "Clear selection")]
    ClearSelection,
}
