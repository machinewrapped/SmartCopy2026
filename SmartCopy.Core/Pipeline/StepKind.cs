using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartCopy.Core.Pipeline;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepKind
{
    [Display(Name = "Flatten path")]
    [StepIcon("⊞")]
    Flatten,

[Display(Name = "Rename files")]
    [StepIcon("✏")]
    Rename,

    [Display(Name = "Convert files")]
    [StepIcon("⚙")]
    Convert,

    [Display(Name = "Copy files")]
    [StepIcon("→")]
    Copy,

    [Display(Name = "Move files")]
    [StepIcon("⇒")]
    Move,

    [Display(Name = "Delete files")]
    [StepIcon("🗑")]
    Delete,

    [Display(Name = "Custom")]
    [StepIcon("★")]
    Custom,

    [Display(Name = "Select all")]
    [StepIcon("☑")]
    SelectAll,

    [Display(Name = "Invert selection")]
    [StepIcon("⇄")]
    InvertSelection,

    [Display(Name = "Clear selection")]
    [StepIcon("✖")]
    ClearSelection,

    [Display(Name = "Save selection to file")]
    [StepIcon("💾")]
    SaveSelectionToFile,

    [Display(Name = "Add selection from file")]
    [StepIcon("☑")]
    AddSelectionFromFile,

    [Display(Name = "Remove selection from file")]
    [StepIcon("✖")]
    RemoveSelectionFromFile,
}
