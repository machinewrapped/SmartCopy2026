using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SmartCopy.UI.Views.Pipeline.StepEditors;

public partial class SelectionFromFileStepEditor : UserControl
{
    public SelectionFromFileStepEditor()
    {
        InitializeComponent();

        FilePathPicker.FileTypeChoices =
        [
            new FilePickerFileType("Selection Files") { Patterns = ["*.sc2sel", "*.txt", "*.m3u", "*.m3u8"] },
            new FilePickerFileType("All Files")       { Patterns = ["*.*"] },
        ];
    }
}
