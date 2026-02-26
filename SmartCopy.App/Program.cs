using Avalonia;
using System;

namespace SmartCopy.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [System.STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("Available resources in UI Assembly:");
        foreach (var res in typeof(SmartCopy.UI.Views.MainWindow).Assembly.GetManifestResourceNames())
        {
            Console.WriteLine("  " + res);
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
