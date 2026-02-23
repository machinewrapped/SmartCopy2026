using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class EditStepDialogViewModel : ObservableObject
{
    public StepEditorViewModelBase Editor { get; }

    public StepKind Kind { get; }

    public string Title => Kind switch
    {
        StepKind.Copy => "Copy Step",
        StepKind.Move => "Move Step",
        StepKind.Delete => "Delete Step",
        StepKind.Flatten => "Flatten Step",
        StepKind.Rename => "Rename Step",
        StepKind.Rebase => "Rebase Step",
        StepKind.Convert => "Convert Step",
        _ => "Step",
    };

    [ObservableProperty]
    private ITransformStep? _resultStep;

    public bool IsValid => Editor.IsValid;

    public event Action? OkRequested;
    public event Action? CancelRequested;

    private EditStepDialogViewModel(StepKind kind, StepEditorViewModelBase editor)
    {
        Kind = kind;
        Editor = editor;
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StepEditorViewModelBase.IsValid))
            {
                OnPropertyChanged(nameof(IsValid));
                OkCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public static EditStepDialogViewModel ForNew(StepKind kind)
    {
        return new EditStepDialogViewModel(kind, StepEditorViewModelFactory.Create(kind));
    }

    public static EditStepDialogViewModel ForEdit(PipelineStepViewModel existing)
    {
        var editor = StepEditorViewModelFactory.Create(existing.Kind);
        editor.LoadFrom(existing);
        return new EditStepDialogViewModel(existing.Kind, editor);
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Ok()
    {
        ResultStep = Editor.BuildStep();
        OkRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }
}
