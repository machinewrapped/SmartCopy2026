using System.ComponentModel.DataAnnotations;

namespace SmartCopy.Core.Pipeline.Steps;

public enum FlattenConflictStrategy
{
    [Display(Name = "Auto-rename (counter)")]
    AutoRenameCounter,

    [Display(Name = "Auto-rename (source path)")]
    AutoRenameSourcePath,

    Skip,
    Overwrite,
}
