using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public enum SizeUnit { Bytes, KB, MB, GB, TB }

public partial class SizeRangeFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    private double? _minValue;

    [ObservableProperty]
    private double? _maxValue;

    [ObservableProperty]
    private SizeUnit _unit = SizeUnit.MB;

    partial void OnMinValueChanged(double? value) => AutoUpdateName();
    partial void OnMaxValueChanged(double? value) => AutoUpdateName();
    partial void OnUnitChanged(SizeUnit value) => AutoUpdateName();

    public override bool IsValid => MinValue.HasValue || MaxValue.HasValue;

    private long? ToBytes(double? v) =>
        v.HasValue ? (long)(v.Value * UnitMultiplier(Unit)) : null;

    private static long UnitMultiplier(SizeUnit u) => u switch
    {
        SizeUnit.KB => 1024L,
        SizeUnit.MB => 1024L * 1024,
        SizeUnit.GB => 1024L * 1024 * 1024,
        SizeUnit.TB => 1024L * 1024 * 1024 * 1024,
        _ => 1L
    };

    public override IFilter BuildFilter()
        => new SizeRangeFilter(ToBytes(MinValue), ToBytes(MaxValue), Mode);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not SizeRangeFilter sr)
        {
            return;
        }

        Mode = sr.Mode;

        // Back-calculate the best display unit from stored bytes
        (MinValue, MaxValue, Unit) = BackCalculate(sr.MinBytes, sr.MaxBytes);
        FilterName = sr.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        var from = MinValue.HasValue ? $"{MinValue} {Unit}" : "any";
        var to = MaxValue.HasValue ? $"{MaxValue} {Unit}" : "any";
        return $"{prefix} size {from} – {to}";
    }

    private static (double? min, double? max, SizeUnit unit) BackCalculate(long? minBytes, long? maxBytes)
    {
        // Use the largest unit that produces a whole number >= 1 for at least one bound
        var referenceBytes = maxBytes ?? minBytes ?? 0L;

        var unit = referenceBytes >= 1024L * 1024 * 1024 * 1024 ? SizeUnit.TB
            : referenceBytes >= 1024L * 1024 * 1024 ? SizeUnit.GB
            : referenceBytes >= 1024L * 1024 ? SizeUnit.MB
            : referenceBytes >= 1024L ? SizeUnit.KB
            : SizeUnit.Bytes;

        var multiplier = (double)UnitMultiplier(unit);
        double? min = minBytes.HasValue ? Math.Round(minBytes.Value / multiplier, 2) : null;
        double? max = maxBytes.HasValue ? Math.Round(maxBytes.Value / multiplier, 2) : null;
        return (min, max, unit);
    }
}
