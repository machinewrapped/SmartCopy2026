using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Extension methods for <see cref="IStepContext"/> that provide virtual-selection
/// helpers used by executable steps' PreviewAsync implementations.
/// </summary>
public static class StepContextExtensions
{
    /// <summary>
    /// Returns true when the node is considered selected in the current pipeline run,
    /// using the virtual check state from <see cref="PipelineContext.VirtualCheckState"/>
    /// rather than the real <see cref="DirectoryTreeNode.CheckState"/>.
    /// </summary>
    public static bool IsPreviewSelected(this IStepContext context, DirectoryTreeNode node)
        => context.GetNodeContext(node).VirtualCheckState == CheckState.Checked
           && node.FilterResult == FilterResult.Included;

    /// <summary>
    /// Enumerates all nodes that are virtually selected in the current pipeline run,
    /// mirroring <see cref="DirectoryTreeNode.GetSelectedDescendants"/> but reading
    /// virtual state so that selection-step <c>PreviewAsync</c> mutations are visible.
    /// </summary>
    public static IEnumerable<DirectoryTreeNode> GetPreviewSelectedDescendants(
        this IStepContext context)
        => GetPreviewSelectedDescendantsFrom(context, context.RootNode);

    private static IEnumerable<DirectoryTreeNode> GetPreviewSelectedDescendantsFrom(
        IStepContext context, DirectoryTreeNode node)
    {
        foreach (var file in node.Files)
            if (context.IsPreviewSelected(file))
                yield return file;

        foreach (var child in node.Children)
        {
            if (!child.IsFilterIncluded) continue;

            if (context.IsPreviewSelected(child))
                yield return child;

            foreach (var desc in GetPreviewSelectedDescendantsFrom(context, child))
                yield return desc;
        }
    }
}
