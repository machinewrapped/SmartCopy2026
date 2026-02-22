using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.UI.ViewModels.Filters;

namespace SmartCopy.UI.ViewModels;

/// <summary>
/// View model for the modal EditFilterDialog window.
/// Created via <see cref="ForNew"/> or <see cref="ForEdit"/> factory methods.
/// </summary>
public partial class EditFilterDialogViewModel : ObservableObject
{
    /// <summary>The type-specific editor hosted by the dialog.</summary>
    public FilterEditorViewModelBase Editor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ModeIsInclude))]
    [NotifyPropertyChangedFor(nameof(ModeIsExclude))]
    private FilterMode _mode = FilterMode.Include;

    public bool ModeIsInclude
    {
        get => Mode == FilterMode.Include;
        set { if (value) Mode = FilterMode.Include; }
    }

    public bool ModeIsExclude
    {
        get => Mode == FilterMode.Exclude;
        set { if (value) Mode = FilterMode.Exclude; }
    }

    [ObservableProperty]
    private string _filterName = string.Empty;

    [ObservableProperty]
    private bool _saveAsPreset;

    /// <summary>Set after a successful OK. Null if the user cancelled.</summary>
    public IFilter? ResultFilter { get; private set; }

    private EditFilterDialogViewModel(FilterEditorViewModelBase editor)
    {
        Editor = editor;

        // Keep mode and name in sync with the editor's observables
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilterEditorViewModelBase.FilterName))
                FilterName = editor.FilterName;
            if (e.PropertyName == nameof(FilterEditorViewModelBase.Mode))
                Mode = editor.Mode;
            if (e.PropertyName == nameof(FilterEditorViewModelBase.IsValid))
                OnPropertyChanged(nameof(IsValid));
        };

        // Push initial values
        Mode = editor.Mode;
        FilterName = editor.FilterName;
    }

    partial void OnModeChanged(FilterMode value)
    {
        Editor.Mode = value;
    }

    partial void OnFilterNameChanged(string value)
    {
        Editor.FilterName = value;
    }

    public bool IsValid => Editor.IsValid;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    /// <summary>Raised when the user clicks OK. Callers should close the dialog.</summary>
    public event System.Action? OkRequested;

    /// <summary>Raised when the user clicks Cancel. Callers should close the dialog.</summary>
    public event System.Action? CancelRequested;

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Ok()
    {
        ResultFilter = Editor.BuildFilter();
        ResultFilter.CustomName = string.IsNullOrWhiteSpace(FilterName) ? null : FilterName;
        OkRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>Creates an empty editor for a new filter of the given type.</summary>
    public static EditFilterDialogViewModel ForNew(
        string filterType,
        string pipelineDestinationPath = "")
    {
        var editor = FilterEditorViewModelFactory.Create(filterType);
        if (editor is MirrorFilterEditorViewModel mirrorEditor)
        {
            mirrorEditor.SetSuggestedPath(pipelineDestinationPath);
        }

        return new EditFilterDialogViewModel(editor);
    }

    /// <summary>Creates a pre-populated editor for editing an existing filter.</summary>
    public static EditFilterDialogViewModel ForEdit(
        IFilter existingFilter,
        string pipelineDestinationPath = "")
    {
        var editor = FilterEditorViewModelFactory.CreateFrom(existingFilter);
        if (editor is MirrorFilterEditorViewModel mirrorEditor)
        {
            mirrorEditor.SetSuggestedPath(pipelineDestinationPath);
        }

        return new EditFilterDialogViewModel(editor);
    }
}
