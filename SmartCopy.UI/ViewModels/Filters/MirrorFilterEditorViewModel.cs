using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class MirrorFilterEditorViewModel : FilterEditorViewModelBase
{
    public bool UnmirroredFolder { get; }
    public bool MirrorFolder { get; }
    public bool ShowCompareMode => !UnmirroredFolder && !MirrorFolder;
    public PathPickerViewModel ComparisonPathPicker { get; }

    public string ComparisonPath
    {
        get => ComparisonPathPicker.Path;
        set => ComparisonPathPicker.Path = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(CompareModeIsNameOnly))]
    [NotifyPropertyChangedFor(nameof(CompareModeIsNameAndSize))]
    private MirrorCompareMode _compareMode = MirrorCompareMode.NameAndSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(IsManualPath))]
    private bool _useAutomaticPath;

    public bool IsManualPath => !UseAutomaticPath;

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

    public MirrorFilterEditorViewModel(AppSettings settings, bool unmirroredFolder = false, bool mirrorFolder = false)
    {
        UnmirroredFolder = unmirroredFolder;
        MirrorFolder = mirrorFolder;
        ComparisonPathPicker = new PathPickerViewModel(settings, PathPickerMode.Target);
        ComparisonPathPicker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(ComparisonPath));
                OnPropertyChanged(nameof(IsValid));
                AutoUpdateName();
            }
        };
    }

    partial void OnCompareModeChanged(MirrorCompareMode value) => AutoUpdateName();

    partial void OnUseAutomaticPathChanged(bool value) => AutoUpdateName();

    public override bool IsValid => UseAutomaticPath || !string.IsNullOrWhiteSpace(ComparisonPath);

    /// <summary>
    /// Populates ComparisonPath from the pipeline destination, but only if the user
    /// hasn't already typed a path and automatic mode is not active.
    /// </summary>
    public void SetSuggestedPath(string path)
    {
        if (!UseAutomaticPath && string.IsNullOrEmpty(ComparisonPath))
        {
            ComparisonPath = path;
        }
    }

    public override IFilter BuildFilter()
        => new MirrorFilter(ComparisonPath, CompareMode, Mode, IsEnabled, UseAutomaticPath, UnmirroredFolder, MirrorFolder);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not MirrorFilter mf)
        {
            return;
        }

        Mode = mf.Mode;
        IsEnabled = mf.IsEnabled;
        ComparisonPath = mf.ComparisonPath;
        CompareMode = mf.CompareMode;
        UseAutomaticPath = mf.UseAutomaticPath;
        FilterName = mf.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        if (UnmirroredFolder || MirrorFolder)
            return $"{prefix} {(UnmirroredFolder ? "unmirrored" : "mirrored")} folders";
        if (UseAutomaticPath)
            return $"{prefix} mirrored (auto)";
        return string.IsNullOrEmpty(ComparisonPath)
            ? $"{prefix} mirrored files"
            : $"{prefix} already in {ComparisonPath}";
    }
}
