using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
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

    public SaveSelectionToFileStepEditorViewModel(IAppContext ctx)
    {
        FilePathPicker = new PathPickerViewModel(ctx.Settings, PathPickerMode.SelectionFile);
        FilePathPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(IsValid));
            }
        };

        var lastSourcePath = ctx.Settings.LastSourcePath;
        if (!string.IsNullOrWhiteSpace(lastSourcePath))
        {
            var provider = ctx.ResolveProvider(lastSourcePath);
            if (provider is not null)
                FilePath = provider.JoinPath(lastSourcePath, ["selection.sc2sel"]);
        }
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(FilePath);

    public override IPipelineStep BuildStep() =>
        new SaveSelectionToFileStep(FilePath.Trim(), UseAbsolutePaths);

    public override string GetAutoName(IPathResolver resolver) =>
        string.IsNullOrWhiteSpace(FilePath)
            ? BuildStep().AutoSummary
            : $"Save to {resolver.GetFileName(FilePath)}";

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is SaveSelectionToFileStep step)
        {
            FilePath = step.FilePath;
            UseAbsolutePaths = step.UseAbsolutePaths;
        }
    }
}
