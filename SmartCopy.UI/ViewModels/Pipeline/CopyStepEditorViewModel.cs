using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class CopyStepEditorViewModel : StepEditorViewModelBase, IDestinationProvider
{
    public PathPickerViewModel DestinationPathPicker { get; }

    public string DestinationPath 
    {
        get => DestinationPathPicker.Path;
        set => DestinationPathPicker.Path = value;
    }

    public CopyStepEditorViewModel(AppSettings settings)
    {
        DestinationPathPicker = new PathPickerViewModel(settings, new AppSettingsStore(), PathPickerMode.Target);
        DestinationPathPicker.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(DestinationPath));
                OnPropertyChanged(nameof(IsValid));
            }
        };
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(DestinationPath);

    public override IPipelineStep BuildStep() => new CopyStep(DestinationPath.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is CopyStep copyStep)
        {
            DestinationPath = copyStep.DestinationPath ?? string.Empty;
        }
    }
}
