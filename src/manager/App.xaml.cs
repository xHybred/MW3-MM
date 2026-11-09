using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MW3ModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Init();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception.", e.Exception);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
