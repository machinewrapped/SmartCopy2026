using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class RemoveSelectionFromFileStepEditorViewModel : StepEditorViewModelBase
{
    public PathPickerViewModel FilePathPicker { get; }

    public string FilePath
    {
        get => FilePathPicker.Path;
        set => FilePathPicker.Path = value;
    }

    public RemoveSelectionFromFileStepEditorViewModel(IAppContext ctx)
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
        new RemoveSelectionFromFileStep(FilePath.Trim());

    public override string GetAutoName(IPathResolver resolver) =>
        string.IsNullOrWhiteSpace(FilePath)
            ? BuildStep().AutoSummary
            : $"Remove from {resolver.GetFileName(FilePath)}";

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is RemoveSelectionFromFileStep step)
            FilePath = step.FilePath;
    }
}
