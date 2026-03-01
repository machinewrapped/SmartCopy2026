using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public sealed class SizeRangeFilter : FilterBase
{
    public SizeRangeFilter(long? minBytes, long? maxBytes, FilterMode mode, bool isEnabled = true)
        : base("SizeRange", mode, isEnabled)
    {
        MinBytes = minBytes;
        MaxBytes = maxBytes;
    }

    public long? MinBytes { get; }
    public long? MaxBytes { get; }

    public override string TypeDisplayName => "Size Range";
    public override string Summary => $"Size range: {MinBytes ?? 0} - {MaxBytes ?? long.MaxValue}";
    public override string Description => "SizeRange filter";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default)
    {
        if (node.IsDirectory)
        {
            return ValueTask.FromResult(false);
        }

        if (MinBytes.HasValue && node.Size < MinBytes.Value)
        {
            return ValueTask.FromResult(false);
        }

        if (MaxBytes.HasValue && node.Size > MaxBytes.Value)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    protected override JsonObject BuildParameters()
    {
        var obj = new JsonObject();
        if (MinBytes.HasValue)
        {
            obj["minBytes"] = MinBytes.Value;
        }

        if (MaxBytes.HasValue)
        {
            obj["maxBytes"] = MaxBytes.Value;
        }

        return obj;
    }
}
