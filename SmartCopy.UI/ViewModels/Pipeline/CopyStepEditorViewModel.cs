using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class CopyStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<string> DestinationBookmarks { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string? _selectedDestinationBookmark;

    public CopyStepEditorViewModel(AppSettings? settings = null)
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

    public override IPipelineStep BuildStep() => new CopyStep(DestinationPath.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is CopyStep copyStep)
        {
            DestinationPath = copyStep.DestinationPath;
        }
    }
}
