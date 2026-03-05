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

    public string Title => Kind.GetDefaultTitle() + " Step";

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
            AutoUpdateStepName();

            if (e.PropertyName == nameof(StepEditorViewModelBase.IsValid))
            {
                OnPropertyChanged(nameof(IsValid));
                OkCommand.NotifyCanExecuteChanged();
            }
        };

        if (string.IsNullOrWhiteSpace(initialCustomName))
        {
            AutoUpdateStepName(force: true);
        }
        else
        {
            _userHasEditedStepName = true;
            StepName = initialCustomName.Trim();
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
        if (!_isAutoUpdatingStepName && !string.IsNullOrWhiteSpace(value))
        {
            _userHasEditedStepName = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Ok()
    {
        ResultStep = Editor.BuildStep();
        var generated = GenerateAutoName();
        var autoName = string.IsNullOrWhiteSpace(generated) ? string.Empty : generated.Trim();
        var finalName = string.IsNullOrWhiteSpace(StepName) ? string.Empty : StepName.Trim();
        ResultCustomName =
            string.IsNullOrWhiteSpace(finalName) ||
            string.Equals(finalName, autoName, StringComparison.OrdinalIgnoreCase)
                ? null
                : finalName;
        OkRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }

    private void AutoUpdateStepName(bool force = false)
    {
        if (!force && _userHasEditedStepName)
        {
            return;
        }

        _isAutoUpdatingStepName = true;
        try
        {
            StepName = GenerateAutoName();
        }
        finally
        {
            _isAutoUpdatingStepName = false;
        }
    }

    private string GenerateAutoName()
    {
        try
        {
            return Editor.BuildStep().Display.Summary;
        }
        catch
        {
            return Kind.GetDefaultTitle();
        }
    }
}
