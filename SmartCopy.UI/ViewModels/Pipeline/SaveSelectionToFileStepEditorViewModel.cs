using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class SaveSelectionToFileStepEditorViewModel : StepEditorViewModelBase
{
    public PathPickerViewModel FilePathPicker { get; }

    [ObservableProperty]
    private bool _useAbsolutePaths;

    public string FilePath
    {
        get => FilePathPicker.Path;
        set => FilePathPicker.Path = value;
    }

    public SaveSelectionToFileStepEditorViewModel(AppSettings settings)
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
        new SaveSelectionToFileStep(FilePath.Trim(), UseAbsolutePaths);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is SaveSelectionToFileStep step)
        {
            FilePath = step.FilePath;
            UseAbsolutePaths = step.UseAbsolutePaths;
        }
    }
}
