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
    public static bool IsPreviewSelected(this IStepContext ctx, DirectoryTreeNode node)
        => ctx.GetNodeContext(node).VirtualCheckState == CheckState.Checked
           && node.FilterResult == FilterResult.Included;

    /// <summary>
    /// Enumerates all nodes that are virtually selected in the current pipeline run,
    /// mirroring <see cref="DirectoryTreeNode.GetSelectedDescendants"/> but reading
    /// virtual state so that selection-step <c>PreviewAsync</c> mutations are visible.
    /// </summary>
    public static IEnumerable<DirectoryTreeNode> GetVirtuallySelectedDescendants(
        this IStepContext ctx)
        => GetDescendantsFrom(ctx, ctx.RootNode);

    private static IEnumerable<DirectoryTreeNode> GetDescendantsFrom(
        IStepContext ctx, DirectoryTreeNode node)
    {
        foreach (var file in node.Files)
            if (ctx.IsPreviewSelected(file))
                yield return file;

        foreach (var child in node.Children)
        {
            if (!child.IsFilterIncluded) continue;

            if (ctx.IsPreviewSelected(child))
                yield return child;

            foreach (var desc in GetDescendantsFrom(ctx, child))
                yield return desc;
        }
    }
}
