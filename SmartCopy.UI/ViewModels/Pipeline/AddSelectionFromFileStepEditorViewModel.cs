using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class AddSelectionFromFileStepEditorViewModel : StepEditorViewModelBase
{
    public PathPickerViewModel FilePathPicker { get; }

    public string FilePath
    {
        get => FilePathPicker.Path;
        set => FilePathPicker.Path = value;
    }

    public AddSelectionFromFileStepEditorViewModel(AppSettings settings)
    {
        FilePathPicker = new PathPickerViewModel(settings, PathPickerMode.SelectionFile);
        FilePathPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(IsValid));
            }
        };
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(FilePath);

    public override IPipelineStep BuildStep() =>
        new AddSelectionFromFileStep(FilePath.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is AddSelectionFromFileStep step)
            FilePath = step.FilePath;
    }
}
