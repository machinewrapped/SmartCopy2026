using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class DateRangeFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldIsCreated))]
    [NotifyPropertyChangedFor(nameof(FieldIsModified))]
    private DateField _field = DateField.Modified;

    public bool FieldIsCreated
    {
        get => Field == DateField.Created;
        set { if (value) Field = DateField.Created; }
    }

    public bool FieldIsModified
    {
        get => Field == DateField.Modified;
        set { if (value) Field = DateField.Modified; }
    }

    [ObservableProperty]
    private DateTimeOffset? _minDate;

    [ObservableProperty]
    private DateTimeOffset? _maxDate;

    partial void OnFieldChanged(DateField value) => AutoUpdateName();
    partial void OnMinDateChanged(DateTimeOffset? value) => AutoUpdateName();
    partial void OnMaxDateChanged(DateTimeOffset? value) => AutoUpdateName();

    public override bool IsValid => MinDate.HasValue || MaxDate.HasValue;

    public override IFilter BuildFilter()
        => new DateRangeFilter(Field, MinDate?.DateTime, MaxDate?.DateTime, Mode);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not DateRangeFilter dr)
        {
            return;
        }

        Mode = dr.Mode;
        Field = dr.Field;
        MinDate = dr.Min.HasValue ? new DateTimeOffset(dr.Min.Value) : null;
        MaxDate = dr.Max.HasValue ? new DateTimeOffset(dr.Max.Value) : null;
        FilterName = dr.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        var from = MinDate.HasValue ? MinDate.Value.ToString("yyyy-MM-dd") : "any";
        var to = MaxDate.HasValue ? MaxDate.Value.ToString("yyyy-MM-dd") : "any";
        return $"{prefix} {Field} {from} – {to}";
    }
}
