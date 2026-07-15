using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public abstract partial class CopyMoveStepEditorViewModelBase : StepEditorViewModelBase, IDestinationProvider
{
    public PathPickerViewModel DestinationPathPicker { get; }

    [ObservableProperty]
    private OverwriteMode _selectedOverwriteMode;

    public OverwriteMode[] OverwriteModes => Enum.GetValues<OverwriteMode>();

    public string DestinationPath
    {
        get => DestinationPathPicker.Path;
        set => DestinationPathPicker.Path = value;
    }

    protected CopyMoveStepEditorViewModelBase(IAppContext ctx)
    {
        DestinationPathPicker = new PathPickerViewModel(ctx.Settings, PathPickerMode.Target);
        DestinationPathPicker.RegisterProvider = ctx.Register;
        DestinationPathPicker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(DestinationPath));
                OnPropertyChanged(nameof(IsValid));
            }
        };

        SelectedOverwriteMode = ctx.Settings.DefaultOverwriteMode;
    }

    public override bool IsValid => !string.IsNullOrWhiteSpace(DestinationPath);
}
