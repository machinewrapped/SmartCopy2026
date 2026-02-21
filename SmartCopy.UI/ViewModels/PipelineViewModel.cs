using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    // True for steps that require a destination path (Copy, Move).
    public bool HasDestination => Kind is StepKind.Copy or StepKind.Move;

    partial void OnKindChanged(StepKind value) => OnPropertyChanged(nameof(HasDestination));
}

public partial class PipelineViewModel : ViewModelBase
{
    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();
    public ObservableCollection<string> SavedPipelineNames { get; } = new();

    // The destination path of the first Copy or Move step in the pipeline.
    // Empty if no such step exists. MainViewModel propagates this to FilterChainViewModel.
    public string FirstDestinationPath
    {
        get
        {
            var step = Steps.FirstOrDefault(s => s.Kind is StepKind.Copy or StepKind.Move);
            return step?.DestinationPath ?? string.Empty;
        }
    }

    public PipelineViewModel()
    {
        Steps.CollectionChanged += OnStepsCollectionChanged;

        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Flatten, Label = "Flatten", Icon = "⊞", Details = "Strip directory structure" });
        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Convert, Label = "Convert", Icon = "⚙", Details = "mp3 320k" });
        Steps.Add(new PipelineStepViewModel { Kind = StepKind.Copy,    Label = "Copy To", Icon = "→", DestinationPath = "/mnt/phone/Music" });
    }

    private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (PipelineStepViewModel step in e.NewItems)
                step.PropertyChanged += OnStepPropertyChanged;

        if (e.OldItems != null)
            foreach (PipelineStepViewModel step in e.OldItems)
                step.PropertyChanged -= OnStepPropertyChanged;

        OnPropertyChanged(nameof(FirstDestinationPath));
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PipelineStepViewModel.DestinationPath)
                           or nameof(PipelineStepViewModel.Kind))
            OnPropertyChanged(nameof(FirstDestinationPath));
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
            StepKind.Copy    => new PipelineStepViewModel { Kind = kind, Label = "Copy To", Icon = "→" },
            StepKind.Move    => new PipelineStepViewModel { Kind = kind, Label = "Move To", Icon = "⇒" },
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
        new() { Kind = StepKind.Copy, Label = "Copy To", Icon = "→" },
    ];

    private static List<PipelineStepViewModel> BuildMovePreset() =>
    [
        new() { Kind = StepKind.Move, Label = "Move To", Icon = "⇒" },
    ];

    private static List<PipelineStepViewModel> BuildDeletePreset() =>
    [
        new() { Kind = StepKind.Delete, Label = "Delete", Icon = "🗑", Details = "Send to trash" },
    ];
}
