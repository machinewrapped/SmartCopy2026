using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public enum SizeUnit { Bytes, KB, MB, GB, TB }

public partial class SizeRangeFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private double? _minValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private double? _maxValue;

    [ObservableProperty]
    private SizeUnit _minUnit = SizeUnit.MB;

    [ObservableProperty]
    private SizeUnit _maxUnit = SizeUnit.MB;

    partial void OnMinValueChanged(double? value) => AutoUpdateName();
    partial void OnMaxValueChanged(double? value) => AutoUpdateName();
    partial void OnMinUnitChanged(SizeUnit value) => AutoUpdateName();
    partial void OnMaxUnitChanged(SizeUnit value) => AutoUpdateName();

    public override bool IsValid => MinValue.HasValue || MaxValue.HasValue;

    private long? ToBytes(double? v, SizeUnit unit) =>
        v.HasValue ? (long)(v.Value * UnitMultiplier(unit)) : null;

    private static long UnitMultiplier(SizeUnit u) => u switch
    {
        SizeUnit.KB => 1024L,
        SizeUnit.MB => 1024L * 1024,
        SizeUnit.GB => 1024L * 1024 * 1024,
        SizeUnit.TB => 1024L * 1024 * 1024 * 1024,
        _ => 1L
    };

    public override IFilter BuildFilter()
        => new SizeRangeFilter(ToBytes(MinValue, MinUnit), ToBytes(MaxValue, MaxUnit), Mode, IsEnabled);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not SizeRangeFilter sr)
        {
            return;
        }

        Mode = sr.Mode;
        IsEnabled = sr.IsEnabled;
        (MinValue, MinUnit) = BackCalculate(sr.MinBytes);
        (MaxValue, MaxUnit) = BackCalculate(sr.MaxBytes);
        FilterName = sr.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        var from = MinValue.HasValue ? $"{MinValue} {MinUnit}" : "any";
        var to = MaxValue.HasValue ? $"{MaxValue} {MaxUnit}" : "any";
        return $"{prefix} size {from} – {to}";
    }

    private static (double? value, SizeUnit unit) BackCalculate(long? bytes)
    {
        if (!bytes.HasValue)
            return (null, SizeUnit.MB);
        var unit = BestUnitForBytes(bytes.Value);
        var multiplier = (double)UnitMultiplier(unit);
        return (Math.Round(bytes.Value / multiplier, 2), unit);
    }

    /// <summary>
    /// Returns the largest unit that keeps the value at or above 1.
    /// </summary>
    private static SizeUnit BestUnitForBytes(long bytes)
    {
        if (bytes >= UnitMultiplier(SizeUnit.TB)) return SizeUnit.TB;
        if (bytes >= UnitMultiplier(SizeUnit.GB)) return SizeUnit.GB;
        if (bytes >= UnitMultiplier(SizeUnit.MB)) return SizeUnit.MB;
        if (bytes >= UnitMultiplier(SizeUnit.KB)) return SizeUnit.KB;
        return SizeUnit.Bytes;
    }
}
