using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;
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
    [NotifyPropertyChangedFor(nameof(ModeIsOnly))]
    [NotifyPropertyChangedFor(nameof(ModeIsAdd))]
    [NotifyPropertyChangedFor(nameof(ModeIsExclude))]
    private FilterMode _mode = FilterMode.Only;

    public bool ModeIsOnly
    {
        get => Mode == FilterMode.Only;
        set { if (value) Mode = FilterMode.Only; }
    }

    public bool ModeIsAdd
    {
        get => Mode == FilterMode.Add;
        set { if (value) Mode = FilterMode.Add; }
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

        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilterEditorViewModelBase.FilterName))
                FilterName = editor.FilterName;
            if (e.PropertyName == nameof(FilterEditorViewModelBase.Mode))
                Mode = editor.Mode;
            if (e.PropertyName == nameof(FilterEditorViewModelBase.IsValid))
            {
                OnPropertyChanged(nameof(IsValid));
                OkCommand.NotifyCanExecuteChanged();
            }
        };

        // Push initial values
        FilterName = editor.FilterName;
        Mode = editor.Mode;

        // Ensure the prefix is correctly applied on initialization
        UpdateFilterNameForMode(Mode);

        // Ensure OK button evaluates its initial state
        OnPropertyChanged(nameof(IsValid));
        OkCommand.NotifyCanExecuteChanged();
    }

    partial void OnModeChanged(FilterMode value)
    {
        Editor.Mode = value;
        UpdateFilterNameForMode(value);
    }

    private void UpdateFilterNameForMode(FilterMode newMode)
    {
        if (string.IsNullOrWhiteSpace(FilterName)) return;

        var prefixes = new[] { "Only ", "Add ", "Exclude " };
        string baseName = FilterName;

        foreach (var prefix in prefixes)
        {
            if (baseName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[prefix.Length..].TrimStart();
                break;
            }
        }

        FilterName = $"{newMode} {baseName}";
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
        AppSettings settings,
        string pipelineDestinationPath = "")
    {
        var editor = FilterEditorViewModelFactory.Create(filterType, settings);
        return CreateFromEditor(editor, pipelineDestinationPath);
    }

    /// <summary>Creates a pre-populated editor for editing an existing filter.</summary>
    public static EditFilterDialogViewModel ForEdit(
        IFilter existingFilter,
        AppSettings settings,
        string pipelineDestinationPath = "")
    {
        var editor = FilterEditorViewModelFactory.CreateFrom(existingFilter, settings);
        return CreateFromEditor(editor, pipelineDestinationPath);
    }

    private static EditFilterDialogViewModel CreateFromEditor(
        FilterEditorViewModelBase editor,
        string pipelineDestinationPath)
    {
        if (editor is MirrorFilterEditorViewModel mirrorEditor)
            mirrorEditor.SetSuggestedPath(pipelineDestinationPath);

        return new EditFilterDialogViewModel(editor);
    }
}
