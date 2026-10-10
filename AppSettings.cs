using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace MimiTap;

public enum ScreenMode
{
    Primary,
    Virtual,
    Cursor
}

public enum AnchorMode
{
    Free,
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
    LeftCenter,
    RightCenter
}

public enum StretchMode
{
    Uniform,
    UniformToFill,
    Fill,
    None,
    Center
}

public enum ClickMode
{
    Hold,
    Timed,
    Toggle
}

public sealed class AppSettings
{
    public string IdleImagePath { get; set; } = "";
    public string ClickImagePath { get; set; } = "";
    public int Width { get; set; } = 220;
    public int Height { get; set; } = 220;
    public bool UseImageSize { get; set; }
    public bool LockAspect { get; set; } = true;
    public string Stretch { get; set; } = nameof(StretchMode.Uniform);
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public bool HasSavedPosition { get; set; }
    public string Screen { get; set; } = nameof(ScreenMode.Primary);
    public string Anchor { get; set; } = nameof(AnchorMode.Center);
    public bool AlwaysOnTop { get; set; } = true;
    public bool NoActivate { get; set; } = true;
    public bool ClickThrough { get; set; } = true;
    public bool DragToMove { get; set; }
    public bool ClickOnlyOnImage { get; set; }
    public string HitTestMode { get; set; } = "Screen";
    public int ClickZonePadding { get; set; } = 40;
    public double Opacity { get; set; } = 1.0;
    public string ClickBehavior { get; set; } = nameof(ClickMode.Hold);
    public int ClickDurationMs { get; set; } = 150;
    public bool StartVisible { get; set; } = true;
    public bool MouseHookEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public string SoundPath { get; set; } = "";
    public double Volume { get; set; } = 0.8;
    public double SoundMaxSeconds { get; set; } = 1.0;
    public bool AutoStart { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyAlt { get; set; } = true;
    public bool HotkeyShift { get; set; }
    public string HotkeyKey { get; set; } = "M";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        Width = Math.Clamp(Width, 16, 10000);
        Height = Math.Clamp(Height, 16, 10000);
        Opacity = Math.Clamp(Opacity, 0.05, 1.0);
        Volume = Math.Clamp(Volume, 0.0, 1.0);
        SoundMaxSeconds = Math.Clamp(SoundMaxSeconds, 0.05, 1.0);
        ClickDurationMs = Math.Clamp(ClickDurationMs, 20, 5000);
        ClickZonePadding = Math.Clamp(ClickZonePadding, 0, 400);
        HitTestMode = HitTestMode is "Image" or "Screen" ? HitTestMode : "Window";
        ClickOnlyOnImage = HitTestMode == "Image";
        if (HasSavedPosition && (PositionX is null || PositionY is null))
        {
            HasSavedPosition = false;
        }

        if (!HasSavedPosition)
        {
            PositionX = null;
            PositionY = null;
        }

        if (string.IsNullOrWhiteSpace(HotkeyKey))
        {
            HotkeyKey = "M";
        }
    }
}

public static class SettingsStore
{
    private static readonly UTF8Encoding Utf8Bom = new(true);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MimiTap");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string AssetsDir => Path.Combine(DataDir, "assets");
    public static string ErrorLogPath => Path.Combine(DataDir, "error.log");

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(SettingsPath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch
        {
            settings = new AppSettings();
        }

        settings.Normalize();
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalize();

        if (settings.PositionX is double x && !double.IsFinite(x))
        {
            settings.PositionX = null;
        }

        if (settings.PositionY is double y && !double.IsFinite(y))
        {
            settings.PositionY = null;
        }

        if (settings.PositionX is null || settings.PositionY is null)
        {
            settings.HasSavedPosition = false;
        }

        Directory.CreateDirectory(DataDir);

        string json;
        try
        {
            json = JsonSerializer.Serialize(settings, Json);
        }
        catch (Exception ex)
        {
            WriteTextFile(ErrorLogPath, "Settings serialization failed:\n" + ex);
            throw new InvalidOperationException("Не удалось подготовить настройки к сохранению.", ex);
        }

        string temp = SettingsPath + ".tmp";

        try
        {
            File.WriteAllText(temp, json, Utf8Bom);
            File.Move(temp, SettingsPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.WriteAllText(SettingsPath, json, Utf8Bom);
            }
            catch (Exception inner)
            {
                WriteTextFile(ErrorLogPath, "Settings write failed:\n" + inner);
                throw new InvalidOperationException("Не удалось записать settings.json.", inner);
            }
        }
    }

    public static void WriteTextFile(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DataDir);
            File.WriteAllText(path, content, Utf8Bom);
        }
        catch
        {
        }
    }

    public static void ExtractDefaultAssets()
    {
        Directory.CreateDirectory(AssetsDir);
        Extract("pack://application:,,,/Assets/1.png", Path.Combine(AssetsDir, "1.png"));
        Extract("pack://application:,,,/Assets/2.png", Path.Combine(AssetsDir, "2.png"));

        // иконку обновляем, если она отличается от вшитой: иначе после замены
        // иконки в новой версии трей продолжал бы показывать старую
        Extract("pack://application:,,,/Assets/MimiTap.ico", Path.Combine(AssetsDir, "MimiTap.ico"), replaceIfDifferent: true);
    }

    public static string DefaultIdleImage => Path.Combine(AssetsDir, "1.png");
    public static string DefaultClickImage => Path.Combine(AssetsDir, "2.png");
    public static string DefaultIcon => Path.Combine(AssetsDir, "MimiTap.ico");

    private static void Extract(string packUri, string target, bool replaceIfDifferent = false)
    {
        if (File.Exists(target) && !replaceIfDifferent)
        {
            return;
        }

        try
        {
            var uri = new Uri(packUri, UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info is null)
            {
                return;
            }

            using var input = info.Stream;
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            byte[] data = memory.ToArray();

            if (File.Exists(target) &&
                new FileInfo(target).Length == data.Length &&
                File.ReadAllBytes(target).AsSpan().SequenceEqual(data))
            {
                return;
            }

            File.WriteAllBytes(target, data);
        }
        catch
        {
        }
    }
}