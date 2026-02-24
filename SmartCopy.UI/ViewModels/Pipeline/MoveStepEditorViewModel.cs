using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class MoveStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<OverwriteMode> OverwriteModes { get; } = Enum.GetValues<OverwriteMode>();

    public IReadOnlyList<string> DestinationBookmarks { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private OverwriteMode _overwriteMode = OverwriteMode.IfNewer;

    [ObservableProperty]
    private string? _selectedDestinationBookmark;

    public MoveStepEditorViewModel(AppSettings? settings = null)
    {
        DestinationBookmarks = settings is null
            ? []
            : settings.FavouritePaths.Concat(settings.RecentTargets).Distinct().ToList();
    }

    partial void OnSelectedDestinationBookmarkChanged(string? value)
    {
        if (value is not null) DestinationPath = value;
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(DestinationPath);

    public override ITransformStep BuildStep() => new MoveStep(DestinationPath.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is MoveStep moveStep)
        {
            DestinationPath = moveStep.DestinationPath;
        }
    }
}
