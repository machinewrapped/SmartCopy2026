using System;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public enum DateField
{
    Created,
    Modified,
}

public sealed class DateRangeFilter : FilterBase
{
    public DateRangeFilter(
        DateField field,
        DateTime? min,
        DateTime? max,
        FilterMode mode,
        bool isEnabled = true)
        : base("DateRange", mode, isEnabled)
    {
        Field = field;
        Min = min;
        Max = max;
    }

    public DateField Field { get; }
    public DateTime? Min { get; }
    public DateTime? Max { get; }

    public override string TypeDisplayName => "Date Range";
    public override string Summary => $"{Field} between {Min:yyyy-MM-dd} and {Max:yyyy-MM-dd}";
    public override string Description => $"DateRange: {Field}";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IPathResolver context,
        CancellationToken ct = default)
    {
        var value = Field == DateField.Created ? node.CreatedAt : node.ModifiedAt;
        if (Min.HasValue && value < Min.Value)
        {
            return ValueTask.FromResult(false);
        }

        if (Max.HasValue && value > Max.Value)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    protected override JsonObject BuildParameters()
    {
        var obj = new JsonObject
        {
            ["field"] = Field.ToString(),
        };

        if (Min.HasValue)
        {
            obj["min"] = Min.Value;
        }

        if (Max.HasValue)
        {
            obj["max"] = Max.Value;
        }

        return obj;
    }
}
