using System.ComponentModel.DataAnnotations;

namespace SmartCopy.Core.Pipeline;

public enum OverwriteMode
{
    Skip,
    Always,
    [Display(Name = "If newer")]
    IfNewer,
}

