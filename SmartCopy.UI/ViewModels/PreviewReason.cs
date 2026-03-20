namespace SmartCopy.UI.ViewModels;

/// <summary>
/// Indicates why <c>PreviewPipelineAsync</c> was invoked, so the UI can
/// show an appropriate message and decide whether to auto-proceed.
/// </summary>
public enum PreviewReason
{
    /// <summary>The user explicitly clicked the Preview button.</summary>
    Manual,

    /// <summary>
    /// The pipeline contains a delete step that requires the user to confirm
    /// before execution can proceed.
    /// </summary>
    DeleteConfirm,

    /// <summary>
    /// The pipeline has a Copy/Move step with <c>OverwriteMode != Skip</c> and a
    /// destination that exists. The preview checks if any files would actually be
    /// overwritten; if none are, execution proceeds automatically without
    /// requiring the user to confirm.
    /// </summary>
    OverwriteCheck,
}
