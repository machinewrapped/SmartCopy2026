using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class CopyStepEditorViewModel : StepEditorViewModelBase, IDestinationProvider
{
    public PathPickerViewModel DestinationPathPicker { get; }
    
    [ObservableProperty]
    private OverwriteMode _selectedOverwriteMode;

    public static OverwriteMode[] OverwriteModes => Enum.GetValues<OverwriteMode>();

    public string DestinationPath 
    {
        get => DestinationPathPicker.Path;
        set => DestinationPathPicker.Path = value;
    }

    public CopyStepEditorViewModel(AppSettings settings)
    {
        DestinationPathPicker = new PathPickerViewModel(settings, PathPickerMode.Target);
        DestinationPathPicker.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(DestinationPath));
                OnPropertyChanged(nameof(IsValid));
            }
        };

        SelectedOverwriteMode = settings.DefaultOverwriteMode;
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(DestinationPath);

    public override IPipelineStep BuildStep() => new CopyStep(DestinationPath.Trim(), SelectedOverwriteMode);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is CopyStep copyStep)
        {
            DestinationPath = copyStep.DestinationPath ?? string.Empty;
            SelectedOverwriteMode = copyStep.OverwriteMode;
        }
    }
}
