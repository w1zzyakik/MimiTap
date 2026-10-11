using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MimiTap;

public partial class App : Application
{
    private Mutex? _mutex;

    internal static AppController? Instance { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "MimiTap.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Loc.SetLanguage(SettingsStore.Load().Language);
            MessageBox.Show(Loc.T("AppAlreadyRunning"), "MimiTap", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show(
                args.Exception.Message + Loc.T("AppErrorDetails") + SettingsStore.ErrorLogPath,
                Loc.T("AppErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        Instance = new AppController();
        Instance.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        Instance?.Dispose();
        Instance = null;
        base.OnExit(e);
    }

    private static void Log(Exception exception)
    {
        SettingsStore.WriteTextFile(SettingsStore.ErrorLogPath, exception.ToString());
    }
}