using System;
using System.Threading.Tasks;
using Avalonia;
using SmartCopy.Core.Logging;

namespace SmartCopy.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [System.STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLogger.Write(e.ExceptionObject as Exception, "AppDomain");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLogger.Write(e.Exception, "UnobservedTask");
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
