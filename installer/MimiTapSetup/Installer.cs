using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using MimiTap;

namespace MimiTapSetup;

public sealed class InstallerOptions
{
    public string TargetDir { get; set; } = InstallPaths.DefaultDir;

    public bool DesktopShortcut { get; set; }

    public bool AutoStart { get; set; } = true;

    public bool RunAfterInstall { get; set; }

    public bool DeleteUserData { get; set; }

    public bool Quiet { get; set; }

    public static InstallerOptions FromArgs(string[] args)
    {
        var options = new InstallerOptions();

        foreach (string arg in args)
        {
            if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
            {
                string path = arg.Substring(3).Trim('"');
                if (path.Length > 0)
                {
                    options.TargetDir = path;
                }
            }
            else if (arg.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase))
            {
                options.DesktopShortcut = false;
            }
            else if (arg.Equals("/DESKTOP", StringComparison.OrdinalIgnoreCase))
            {
                options.DesktopShortcut = true;
            }
            else if (arg.Equals("/NOAUTOSTART", StringComparison.OrdinalIgnoreCase))
            {
                options.AutoStart = false;
            }
            else if (arg.Equals("/NORUN", StringComparison.OrdinalIgnoreCase))
            {
                options.RunAfterInstall = false;
            }
            else if (arg.Equals("/DELETEUSERDATA", StringComparison.OrdinalIgnoreCase))
            {
                options.DeleteUserData = true;
            }
            else if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase) || arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
            {
                options.Quiet = true;
            }
        }

        return options;
    }
}

public sealed record InstallerResult(bool Success, string Summary)
{
    public int ExitCode => Success ? 0 : 1;
}

public static class InstallPaths
{
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MimiTap");

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MimiTap");

    public static string StartMenuGroup => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "MimiTap");

    public static string Desktop => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MimiTap.lnk");

    public static string AppExeName => "MimiTap.exe";
    public static string UninstallerName => "Uninstall.exe";
}

public static class Installer
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MimiTap";
    private const string RunValueName = "MimiTap";

    public static InstallerResult RunInstall(InstallerOptions options, Action<string>? log)
    {
        var steps = new List<string>();

        void Say(string message)
        {
            steps.Add(message);
            log?.Invoke(message);
        }

        try
        {
            string dir = Path.GetFullPath(options.TargetDir);
            string appExe = Path.Combine(dir, InstallPaths.AppExeName);
            string uninstaller = Path.Combine(dir, InstallPaths.UninstallerName);

            Say(Loc.T("InstallerClosingApp"));
            StopRunningApp(dir);

            Say(Loc.Format("InstallerCreatingFolder", dir));
            Directory.CreateDirectory(dir);

            Say(Loc.T("InstallerCopyingExe"));
            WritePayload(appExe);

            Say(Loc.T("InstallerCopyingUninstaller"));
            CopySelf(uninstaller);

            Say(Loc.T("InstallerShortcuts"));
            Directory.CreateDirectory(InstallPaths.StartMenuGroup);
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, "MimiTap.lnk"), appExe, "", Loc.T("InstallerShortcutAppDesc"));
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, Loc.T("InstallerShortcutSettings") + ".lnk"), appExe, "--settings", Loc.T("InstallerShortcutSettingsDesc"));
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, Loc.T("InstallerShortcutUninstall") + ".lnk"), uninstaller, "", Loc.T("InstallerShortcutUninstallDesc"));

            if (options.DesktopShortcut)
            {
                Say(Loc.T("InstallerDesktopShortcut"));
                CreateShortcut(InstallPaths.Desktop, appExe, "", Loc.T("InstallerShortcutAppDesc"));
            }
            else
            {
                DeleteFile(InstallPaths.Desktop);
            }

            Say(Loc.T(options.AutoStart ? "InstallerAutostartOn" : "InstallerAutostartOff"));
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey, true))
            {
                if (run is null)
                {
                    throw new InvalidOperationException(Loc.T("InstallerErrRunKey"));
                }

                if (options.AutoStart)
                {
                    run.SetValue(RunValueName, $"\"{appExe}\" --minimized", RegistryValueKind.String);
                }
                else
                {
                    run.DeleteValue(RunValueName, false);
                }
            }

            Say(Loc.T("InstallerRegistering"));
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey, true))
            {
                key?.SetValue("DisplayName", "MimiTap", RegistryValueKind.String);
                key?.SetValue("DisplayVersion", App.AppVersion, RegistryValueKind.String);
                key?.SetValue("Publisher", "MimiTap", RegistryValueKind.String);
                key?.SetValue("DisplayIcon", appExe, RegistryValueKind.String);
                key?.SetValue("InstallLocation", dir, RegistryValueKind.String);
                key?.SetValue("UninstallString", $"\"{uninstaller}\"", RegistryValueKind.String);
                key?.SetValue("QuietUninstallString", $"\"{uninstaller}\" /S", RegistryValueKind.String);
                key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = 0;
                try
                {
                    size = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length) / 1024;
                }
                catch
                {
                }

                key?.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
            }

            if (options.RunAfterInstall)
            {
                Say(Loc.T("InstallerLaunching"));
                StartApp(appExe, "");
            }

            Say(Loc.T("InstallerFinished"));
            return new InstallerResult(true, Loc.Format("InstallerInstalledTo", dir));
        }
        catch (Exception ex)
        {
            Say(Loc.Format("InstallerErrorLine", ex.Message));
            return new InstallerResult(false, ex.Message);
        }
    }

    public static InstallerResult RunUninstall(InstallerOptions options, Action<string>? log)
    {
        var steps = new List<string>();

        void Say(string message)
        {
            steps.Add(message);
            log?.Invoke(message);
        }

        try
        {
            string dir = Path.GetFullPath(options.TargetDir);

            Say(Loc.T("InstallerClosingApp"));
            StopRunningApp(dir);

            Say(Loc.T("InstallerRemovingShortcuts"));
            // удаляем всю группу «Пуск» целиком: имена ярлыков зависят от языка
            DeleteDirectory(InstallPaths.StartMenuGroup);
            DeleteFile(InstallPaths.Desktop);

            Say(Loc.T("InstallerRemovingAutostart"));
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                run?.DeleteValue(RunValueName, false);
            }

            Say(Loc.T("InstallerRemovingRegistry"));
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            if (options.DeleteUserData)
            {
                Say(Loc.T("InstallerRemovingData"));
                DeleteDirectory(InstallPaths.DataDir);
            }
            else
            {
                Say(Loc.Format("InstallerDataKept", InstallPaths.DataDir));
            }

            Say(Loc.T("InstallerRemovingFiles"));
            ScheduleSelfDelete(dir);

            return new InstallerResult(true, Loc.T("InstallerUninstalled"));
        }
        catch (Exception ex)
        {
            Say(Loc.Format("InstallerErrorLine", ex.Message));
            return new InstallerResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Запускает временную копию удалялки, которая сносит папку и саму себя.
    /// </summary>
    private static void ScheduleSelfDelete(string dir)
    {
        string? self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
        {
            return;
        }

        string temp = Path.Combine(Path.GetTempPath(), $"MimiTapUninstall-{Guid.NewGuid():N}.exe");
        File.Copy(self, temp, true);

        var info = new ProcessStartInfo
        {
            FileName = temp,
            Arguments = $"--delete \"{dir}\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetTempPath()
        };

        Process.Start(info);
    }

    /// <summary>
    /// Служебный режим: удаляет папку установки и затем сам себя.
    /// </summary>
    public static int DeleteLeftovers(string dir)
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Thread.Sleep(400);
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }

                break;
            }
            catch
            {
                // файл мог быть ещё занят — пробуем ещё раз
            }
        }

        try
        {
            string? me = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(me) && File.Exists(me))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ping -n 3 127.0.0.1 > nul & del /f /q \"{me}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch
        {
        }

        return 0;
    }

    private static void WritePayload(string target)
    {
        Assembly assembly = typeof(Installer).Assembly;
        using Stream? source = assembly.GetManifestResourceStream(App.PayloadResourceName);
        if (source is null)
        {
            throw new InvalidOperationException(Loc.T("InstallerErrNoPayload"));
        }

        string temp = target + ".new";
        using (var output = File.Create(temp))
        {
            source.CopyTo(output);
        }

        // файл мог быть запущен — снимаем блокировку через замену
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        File.Move(temp, target, true);
    }

    private static void CopySelf(string target)
    {
        string? self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
        {
            throw new InvalidOperationException(Loc.T("InstallerErrNoSelf"));
        }

        File.Copy(self, target, true);
    }

    public static void StopRunningApp(string dir)
    {
        foreach (Process process in Process.GetProcessesByName("MimiTap"))
        {
            try
            {
                string path = process.MainModule?.FileName ?? "";
                if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(4000);
                }
            }
            catch
            {
                // процесс другого пользователя или уже завершился
            }
        }
    }

    public static void StartApp(string exe, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? exe
            });
        }
        catch
        {
        }
    }

    public static void CreateShortcut(string linkPath, string target, string arguments, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            object? instance = Activator.CreateInstance(shellType);
            if (instance is null)
            {
                return;
            }

            Type linkType = instance.GetType();
            object link = linkType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, instance, new object[] { linkPath })!;

            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) ?? target });
            linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { $"{target},0" });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
            if (arguments.Length > 0)
            {
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments });
            }

            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch
        {
            // без ярлыков установка всё равно должна пройти
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
