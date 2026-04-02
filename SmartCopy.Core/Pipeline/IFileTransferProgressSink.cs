using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Optional step-context hook for reporting in-flight file-transfer bytes
/// while a copy/write operation is still running.
/// </summary>
public interface IFileTransferProgressSink
{
    /// <summary>
    /// Reports bytes transferred for <paramref name="node"/> as a delta.
    /// </summary>
    void ReportFileTransferBytes(DirectoryTreeNode node, long bytesDelta, long fileTotalBytes);
}
