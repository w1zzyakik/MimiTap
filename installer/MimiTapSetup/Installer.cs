using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

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

            Say("Закрываю MimiTap, если он запущен…");
            StopRunningApp(dir);

            Say($"Создаю папку {dir}");
            Directory.CreateDirectory(dir);

            Say("Копирую MimiTap.exe…");
            WritePayload(appExe);

            Say("Копирую удалялку…");
            CopySelf(uninstaller);

            Say("Создаю ярлыки в меню «Пуск»…");
            Directory.CreateDirectory(InstallPaths.StartMenuGroup);
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, "MimiTap.lnk"), appExe, "", "MimiTap — оверлей-картинка");
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, "Настройки MimiTap.lnk"), appExe, "--settings", "Настройки MimiTap");
            CreateShortcut(Path.Combine(InstallPaths.StartMenuGroup, "Удалить MimiTap.lnk"), uninstaller, "", "Удаление MimiTap");

            if (options.DesktopShortcut)
            {
                Say("Создаю значок на рабочем столе…");
                CreateShortcut(InstallPaths.Desktop, appExe, "", "MimiTap");
            }
            else
            {
                DeleteFile(InstallPaths.Desktop);
            }

            Say(options.AutoStart ? "Включаю автозапуск с Windows…" : "Выключаю автозапуск…");
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey, true))
            {
                if (run is null)
                {
                    throw new InvalidOperationException("не удалось открыть ключ автозапуска");
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

            Say("Регистрирую программу в «Программы и удаление»…");
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
                Say("Запускаю MimiTap…");
                StartApp(appExe, "");
            }

            Say("Готово.");
            return new InstallerResult(true, $"MimiTap установлен в {dir}");
        }
        catch (Exception ex)
        {
            Say($"Ошибка: {ex.Message}");
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

            Say("Закрываю MimiTap, если он запущен…");
            StopRunningApp(dir);

            Say("Удаляю ярлыки…");
            foreach (string name in new[] { "MimiTap.lnk", "Настройки MimiTap.lnk", "Удалить MimiTap.lnk" })
            {
                DeleteFile(Path.Combine(InstallPaths.StartMenuGroup, name));
            }

            DeleteFile(InstallPaths.Desktop);
            DeleteDirectoryIfEmpty(InstallPaths.StartMenuGroup);

            Say("Убираю автозапуск…");
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                run?.DeleteValue(RunValueName, false);
            }

            Say("Убираю запись из «Программы и удаление»…");
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            if (options.DeleteUserData)
            {
                Say("Удаляю настройки и картинки…");
                DeleteDirectory(InstallPaths.DataDir);
            }
            else
            {
                Say($"Настройки оставлены в {InstallPaths.DataDir}");
            }

            Say("Удаляю файлы программы…");
            ScheduleSelfDelete(dir);

            return new InstallerResult(true, "MimiTap удалён");
        }
        catch (Exception ex)
        {
            Say($"Ошибка: {ex.Message}");
            return new InstallerResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Запускает временную копию удалялки, которая сносит папку и саму себя.
    /// </summary>
    private static void ScheduleSelfDelete(string dir)
    {
        string self = Environment.ProcessPath;
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
            string me = Environment.ProcessPath;
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
            throw new InvalidOperationException("в установщике нет файла программы — сначала выполните publish");
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
        string self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
        {
            throw new InvalidOperationException("не удалось определить путь к установщику");
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

    private static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
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
