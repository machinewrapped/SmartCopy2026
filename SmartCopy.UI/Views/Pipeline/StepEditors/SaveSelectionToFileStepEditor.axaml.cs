using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SmartCopy.UI.Views.Pipeline.StepEditors;

public partial class SaveSelectionToFileStepEditor : UserControl
{
    public SaveSelectionToFileStepEditor()
    {
        InitializeComponent();

        FilePathPicker.SuggestedFileName = "selection";
        FilePathPicker.DefaultExtension = ".sc2sel";
        FilePathPicker.FileTypeChoices =
        [
            new FilePickerFileType("SmartCopy Selection") { Patterns = ["*.sc2sel"] },
            new FilePickerFileType("Text File")           { Patterns = ["*.txt"]    },
            new FilePickerFileType("M3U Playlist")        { Patterns = ["*.m3u"]    },
            new FilePickerFileType("M3U8 UTF-8 Playlist") { Patterns = ["*.m3u8"]   },
        ];
    }
}
