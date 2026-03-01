using Avalonia.Controls;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class PreviewView : Window
{
    private static readonly string[] _markdownPatterns = new[] { "*.md" };
    private static readonly string[] _textPatterns = new[] { "*.txt" };
    private static readonly string[] _allPatterns = new[] { "*.*" };

    private static readonly Avalonia.Platform.Storage.FilePickerFileType[] _fileTypeChoices = new[]
    {
        new Avalonia.Platform.Storage.FilePickerFileType("Markdown Documents") { Patterns = _markdownPatterns },
        new Avalonia.Platform.Storage.FilePickerFileType("Text Documents") { Patterns = _textPatterns },
        new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = _allPatterns }
    };

    private PreviewViewModel? _viewModel;

    public PreviewView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.RunRequested -= OnRunRequested;
                _viewModel.CancelRequested -= OnCancelRequested;
                _viewModel.SaveReportRequested -= OnSaveReportRequested;
            }

            _viewModel = DataContext as PreviewViewModel;
            if (_viewModel is not null)
            {
                _viewModel.RunRequested += OnRunRequested;
                _viewModel.CancelRequested += OnCancelRequested;
                _viewModel.SaveReportRequested += OnSaveReportRequested;
            }
        };
    }

    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void OnRunRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnCancelRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(false));
    }

    private async Task OnSaveReportRequested(string markdown)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Preview Report",
            DefaultExtension = ".md",
            SuggestedFileName = $"SmartCopy2026-Preview-{System.DateTime.Now:yyyyMMdd-HHmmss}.md",
            FileTypeChoices = _fileTypeChoices
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(markdown);
        }
    }
}
