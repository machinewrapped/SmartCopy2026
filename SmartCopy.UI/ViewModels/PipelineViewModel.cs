using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels;

public enum StepKind { Flatten, Rebase, Rename, Convert, Copy, Move, Delete, Custom }

public partial class PipelineStepViewModel : ViewModelBase
{
    [ObservableProperty]
    private StepKind _kind = StepKind.Custom;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;
}

public partial class PipelineViewModel : ViewModelBase
{
    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();
    public ObservableCollection<string> SavedPipelineNames { get; } = new();

    public PipelineViewModel()
    {
        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Flatten, Label = "Flatten", Icon = "⊞", Details = "Strip directory structure" });
        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Convert, Label = "Convert", Icon = "⚙", Details = "mp3 320k" });
        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Copy,    Label = "Copy",    Icon = "→", Details = "Copy to target" });
    }

    [RelayCommand]
    private void AddStep(StepKind kind)
    {
        var step = kind switch
        {
            StepKind.Flatten => new PipelineStepViewModel { Kind = kind, Label = "Flatten", Icon = "⊞", Details = "Strip directory structure" },
            StepKind.Rebase  => new PipelineStepViewModel { Kind = kind, Label = "Rebase",  Icon = "⤢", Details = "Change root path" },
            StepKind.Rename  => new PipelineStepViewModel { Kind = kind, Label = "Rename",  Icon = "✏", Details = "Rename pattern" },
            StepKind.Convert => new PipelineStepViewModel { Kind = kind, Label = "Convert", Icon = "⚙", Details = "Configure format" },
            StepKind.Copy    => new PipelineStepViewModel { Kind = kind, Label = "Copy",    Icon = "→", Details = "Copy to target" },
            StepKind.Move    => new PipelineStepViewModel { Kind = kind, Label = "Move",    Icon = "⇒", Details = "Move to target" },
            StepKind.Delete  => new PipelineStepViewModel { Kind = kind, Label = "Delete",  Icon = "🗑", Details = "Send to trash" },
            _                => new PipelineStepViewModel { Kind = kind, Label = "Custom",  Icon = "?", Details = "Configure step" },
        };
        Steps.Add(step);
    }

    [RelayCommand]
    private void RemoveStep(PipelineStepViewModel step)
    {
        Steps.Remove(step);
    }

    [RelayCommand]
    private void LoadPreset(string name)
    {
        var steps = name switch
        {
            "Copy"   => BuildCopyPreset(),
            "Move"   => BuildMovePreset(),
            "Delete" => BuildDeletePreset(),
            _        => null,
        };
        if (steps is null) return;
        Steps.Clear();
        foreach (var s in steps)
            Steps.Add(s);
    }

    [RelayCommand]
    private void SavePipeline()
    {
        SavedPipelineNames.Add($"My Pipeline {SavedPipelineNames.Count + 1}");
    }

    [RelayCommand]
    private void RunPipeline() { }

    [RelayCommand]
    private void PreviewPipeline() { }

    private static List<PipelineStepViewModel> BuildCopyPreset() =>
    [
        new() { Kind = StepKind.Copy,   Label = "Copy",   Icon = "→", Details = "Copy to target" },
    ];

    private static List<PipelineStepViewModel> BuildMovePreset() =>
    [
        new() { Kind = StepKind.Move,   Label = "Move",   Icon = "⇒", Details = "Move to target" },
    ];

    private static List<PipelineStepViewModel> BuildDeletePreset() =>
    [
        new() { Kind = StepKind.Delete, Label = "Delete", Icon = "🗑", Details = "Send to trash" },
    ];
}
