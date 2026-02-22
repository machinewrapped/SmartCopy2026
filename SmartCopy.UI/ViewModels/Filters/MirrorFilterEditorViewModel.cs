using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class MirrorFilterEditorViewModel : FilterEditorViewModelBase
{
    [ObservableProperty]
    private string _comparisonPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompareModeIsNameOnly))]
    [NotifyPropertyChangedFor(nameof(CompareModeIsNameAndSize))]
    private MirrorCompareMode _compareMode = MirrorCompareMode.NameAndSize;

    public bool CompareModeIsNameOnly
    {
        get => CompareMode == MirrorCompareMode.NameOnly;
        set { if (value) CompareMode = MirrorCompareMode.NameOnly; }
    }

    public bool CompareModeIsNameAndSize
    {
        get => CompareMode == MirrorCompareMode.NameAndSize;
        set { if (value) CompareMode = MirrorCompareMode.NameAndSize; }
    }

    partial void OnComparisonPathChanged(string value) => AutoUpdateName();
    partial void OnCompareModeChanged(MirrorCompareMode value) => AutoUpdateName();

    public override bool IsValid => !string.IsNullOrWhiteSpace(ComparisonPath);

    /// <summary>
    /// Populates ComparisonPath from the pipeline destination, but only if the user
    /// hasn't already typed a path.
    /// </summary>
    public void SetSuggestedPath(string path)
    {
        if (string.IsNullOrEmpty(ComparisonPath))
        {
            ComparisonPath = path;
        }
    }

    public override IFilter BuildFilter()
        => new MirrorFilter(ComparisonPath, CompareMode, Mode);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not MirrorFilter mf)
        {
            return;
        }

        Mode = mf.Mode;
        ComparisonPath = mf.ComparisonPath;
        CompareMode = mf.CompareMode;
        FilterName = mf.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        return string.IsNullOrEmpty(ComparisonPath)
            ? $"{prefix} mirrored files"
            : $"{prefix} already in {ComparisonPath}";
    }
}
