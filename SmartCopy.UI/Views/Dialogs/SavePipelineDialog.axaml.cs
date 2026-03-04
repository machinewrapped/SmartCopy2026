using Avalonia.Controls;

namespace SmartCopy.UI.Views.Dialogs;

public partial class SavePipelineDialog : Window
{
    public SavePipelineDialog()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is SmartCopy.UI.ViewModels.Dialogs.SavePipelineDialogViewModel vm)
            {
                vm.OkRequested += () => Close(true);
                vm.CancelRequested += () => Close(false);
            }
        };
    }
}
