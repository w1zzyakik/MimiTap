using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MimiTapSetup;

public sealed partial class App : Application
{
    public static string PayloadResourceName => "Payload.MimiTap.exe";

    public static string AppVersion =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args.ToArray();

        // служебный режим: удалить папку и сам себя (вызывается из удалялки)
        int deleteIndex = Array.FindIndex(args, a => string.Equals(a, "--delete", StringComparison.OrdinalIgnoreCase));
        if (deleteIndex >= 0)
        {
            string dir = deleteIndex + 1 < args.Length ? args[deleteIndex + 1] : "";
            Shutdown(Installer.DeleteLeftovers(dir));
            return;
        }

        bool silent = args.Any(a =>
            string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        var options = InstallerOptions.FromArgs(args);

        // Uninstall.exe лежит в папке установки — берём её оттуда
        string selfDir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
        if (selfDir is { Length: > 0 } &&
            string.Equals(Path.GetFileName(Environment.ProcessPath ?? ""), InstallPaths.UninstallerName, StringComparison.OrdinalIgnoreCase) &&
            !args.Any(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)))
        {
            options.TargetDir = selfDir;
        }

        bool uninstall = args.Any(a =>
            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-u", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));

        bool forceInstall = args.Any(a =>
            string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/install", StringComparison.OrdinalIgnoreCase));

        // удалялка в Program Files/Uninstall.exe по умолчанию удаляет, а не ставит
        string selfName = Path.GetFileName(Environment.ProcessPath ?? "");
        bool isUninstaller = selfName.Equals(InstallPaths.UninstallerName, StringComparison.OrdinalIgnoreCase);

        if (uninstall || (isUninstaller && !forceInstall))
        {
            uninstall = true;
        }

        if (silent)
        {
            Shutdown(uninstall ? Installer.RunUninstall(options, null).ExitCode : Installer.RunInstall(options, null).ExitCode);
            return;
        }

        var window = new MainWindow(options, uninstall);
        MainWindow = window;
        window.Show();
    }
}
