using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SmartCopy.Core.Logging;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Workflows;
using SmartCopy.UI.Views;
using SmartCopy.UI.Views.Workflows;

namespace SmartCopy.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                CrashLogger.Write(e.Exception, "UIThread");
                ShowErrorDialog(desktop.MainWindow, e.Exception);
                e.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void ShowErrorDialog(Window? owner, Exception ex)
    {
        try
        {
            var dialog = new ConfirmDialog
            {
                DataContext = new ConfirmDialogViewModel
                {
                    Title = "Unexpected Error",
                    Message = $"An unexpected error occurred:\n\n{ex.Message}\n\nA crash report has been saved. The application will attempt to continue.",
                    ConfirmText = "OK",
                    CancelText = ""
                }
            };
            if (owner is not null)
                await dialog.ShowDialog<bool?>(owner);
        }
        catch
        {
            // Dialog itself failed — crash already logged.
        }
    }
}