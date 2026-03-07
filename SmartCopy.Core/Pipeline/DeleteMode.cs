using System.ComponentModel.DataAnnotations;

namespace SmartCopy.Core.Pipeline;

public enum DeleteMode
{
    [Display(Name = "Trash (Recycle Bin)")]
    Trash,
    [Display(Name = "Permanent delete")]
    Permanent,
}

