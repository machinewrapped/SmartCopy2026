using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class AttributeFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    private bool _hidden;

    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private bool _system;

    partial void OnHiddenChanged(bool value) { OnPropertyChanged(nameof(IsValid)); AutoUpdateName(); }
    partial void OnReadOnlyChanged(bool value) { OnPropertyChanged(nameof(IsValid)); AutoUpdateName(); }
    partial void OnSystemChanged(bool value) { OnPropertyChanged(nameof(IsValid)); AutoUpdateName(); }

    public override bool IsValid => Hidden || ReadOnly || System;

    private FileAttributes BuildAttributes()
    {
        var attrs = FileAttributes.Normal;
        if (Hidden) attrs |= FileAttributes.Hidden;
        if (ReadOnly) attrs |= FileAttributes.ReadOnly;
        if (System) attrs |= FileAttributes.System;
        return attrs;
    }

    public override IFilter BuildFilter() => new AttributeFilter(BuildAttributes(), Mode);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not AttributeFilter af)
        {
            return;
        }

        Mode = af.Mode;
        Hidden = (af.RequiredAttributes & FileAttributes.Hidden) != 0;
        ReadOnly = (af.RequiredAttributes & FileAttributes.ReadOnly) != 0;
        System = (af.RequiredAttributes & FileAttributes.System) != 0;
        FilterName = af.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        var attrs = BuildAttributes() & ~FileAttributes.Normal;
        return attrs == 0 ? prefix : $"{prefix} {attrs}";
    }
}
