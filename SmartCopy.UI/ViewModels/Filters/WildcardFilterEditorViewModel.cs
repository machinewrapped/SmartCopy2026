using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class WildcardFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _pattern = string.Empty;

    partial void OnPatternChanged(string value) => AutoUpdateName();

    public override bool IsValid => !string.IsNullOrWhiteSpace(Pattern);

    public override IFilter BuildFilter() => new WildcardFilter(Pattern, Mode, IsEnabled);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not WildcardFilter wf)
        {
            return;
        }

        Mode = wf.Mode;
        IsEnabled = wf.IsEnabled;
        Pattern = wf.Pattern;
        FilterName = wf.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        return string.IsNullOrWhiteSpace(Pattern) ? prefix : $"{prefix} {Pattern}";
    }
}
