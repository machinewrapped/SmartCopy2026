using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class EditStepDialogViewModel : ObservableObject
{
    private bool _userHasEditedStepName;
    private bool _isAutoUpdatingStepName;

    public StepEditorViewModelBase Editor { get; }

    public StepKind Kind { get; }

    public string Title => Kind.ForDisplay() + " step";

    [ObservableProperty]
    private IPipelineStep? _resultStep;

    [ObservableProperty]
    private string? _resultCustomName;

    [ObservableProperty]
    private string _stepName = string.Empty;

    [ObservableProperty]
    private bool _saveAsPreset;

    public bool IsValid => Editor.IsValid;

    public event Action? OkRequested;
    public event Action? CancelRequested;

    private EditStepDialogViewModel(
        StepKind kind,
        StepEditorViewModelBase editor,
        string? initialCustomName = null)
    {
        Kind = kind;
        Editor = editor;
        Editor.PropertyChanged += (_, e) =>
        {
            if (!_userHasEditedStepName)
            {
                AutoUpdateStepName();                
            }

            if (e.PropertyName == nameof(StepEditorViewModelBase.IsValid))
            {
                OnPropertyChanged(nameof(IsValid));
                OkCommand.NotifyCanExecuteChanged();
            }
        };

        if (!string.IsNullOrWhiteSpace(initialCustomName))
        {
            StepName = initialCustomName.Trim();
            _userHasEditedStepName = true;
        }
        else
        {
            AutoUpdateStepName();
        }

        OnPropertyChanged(nameof(IsValid));
        OkCommand.NotifyCanExecuteChanged();
    }

    public static EditStepDialogViewModel ForNew(StepKind kind, AppSettings? settings = null)
    {
        return new EditStepDialogViewModel(kind, StepEditorViewModelFactory.Create(kind, settings));
    }

    public static EditStepDialogViewModel ForEdit(PipelineStepViewModel existing, AppSettings? settings = null)
    {
        var editor = StepEditorViewModelFactory.Create(existing.Kind, settings);
        editor.LoadFrom(existing);
        return new EditStepDialogViewModel(existing.Kind, editor, existing.CustomName);
    }

    partial void OnStepNameChanged(string value)
    {
        // Disable auto-naming if user edits the name, re-neable it if they delete it
        _userHasEditedStepName = !_isAutoUpdatingStepName && !string.IsNullOrWhiteSpace(value);
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Ok()
    {
        ResultStep = Editor.BuildStep();

        var stepName = StepName?.Trim();
        var autoName = ResultStep.AutoSummary;

        ResultCustomName = string.IsNullOrEmpty(stepName) || string.Equals(stepName, autoName, StringComparison.OrdinalIgnoreCase)
            ? null
            : stepName;

        OkRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }

    private void AutoUpdateStepName()
    {
        _isAutoUpdatingStepName = true;
        try
        {
            StepName = Editor.BuildStep().AutoSummary;
        }
        finally
        {
            _isAutoUpdatingStepName = false;
        }
    }
}
