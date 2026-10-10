using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MimiTap;

public sealed class SoundService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _stopTimer;
    private Dispatcher? _dispatcher;
    private double _volume = 0.8;
    private string? _currentSource;

public SoundService()
    {
        _stopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stopTimer.Tick += (_, _) => Stop();
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaOpened += OnMediaOpenedCore;
        _player.MediaFailed += OnMediaFailedCore;
    }

public string? CurrentSource => _currentSource;

    public double DurationSeconds { get; private set; }

    public event EventHandler? MediaOpened;

    public event EventHandler? MediaFailed;

    private void OnMediaOpenedCore(object? sender, EventArgs e)
    {
        try
        {
            DurationSeconds = _player.NaturalDuration.TimeSpan.TotalSeconds;
        }
        catch
        {
            DurationSeconds = 0;
        }

        MediaOpened?.Invoke(this, e);
    }

    private void OnMediaFailedCore(object? sender, EventArgs e)
    {
        Stop();
        MediaFailed?.Invoke(this, e);
    }

    public void EnsureDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Play(string? path, double volume, double maxSeconds)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        EnsureOnDispatcher();

        try
        {
var full = Path.GetFullPath(path);
            if (_currentSource != full)
            {
                _player.Stop();
                _player.Open(new Uri(full));
                _currentSource = full;
                _player.Volume = _volume;
                DurationSeconds = 0;
            }

            _player.Volume = Math.Clamp(volume, 0.0, 1.0);
            _volume = _player.Volume;
            _stopTimer.Stop();
            _stopTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(maxSeconds, 0.05, 1.0));
            _player.Play();
            _stopTimer.Start();
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        _stopTimer.Stop();
        try
        {
            _player.Stop();
        }
        catch
        {
        }
    }

    public static double? TryGetDuration(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return WavDuration(path);
        }
        catch
        {
            return null;
        }
    }

    private static double? WavDuration(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);

        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            return null;
        }

        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            return null;
        }

        int byteRate = 0;
        long dataSize = 0;

        while (fs.Position + 8 <= fs.Length)
        {
            string id = new(reader.ReadChars(4));
            uint size = reader.ReadUInt32();
            long next = fs.Position + size + (size % 2);

            if (id == "fmt ")
            {
                reader.ReadInt16();
                reader.ReadInt16();
                reader.ReadUInt32();
                byteRate = reader.ReadInt32();
            }
            else if (id == "data")
            {
                dataSize = size;
            }

            if (byteRate > 0 && dataSize > 0)
            {
                return dataSize / (double)byteRate;
            }

            fs.Position = next;
        }

        return null;
    }

    private void EnsureOnDispatcher()
    {
        var dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return;
        }

        dispatcher.Invoke(() => { });
    }

    public void Dispose()
    {
        _stopTimer.Stop();
        _player.Close();
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("MimiTap") is string value && value.Contains("MimiTap.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "MimiTap.exe";
                key.SetValue("MimiTap", $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue("MimiTap", false);
            }
        }
        catch
        {
        }
    }

    public static bool WasMinimizedLaunch()
    {
        try
        {
            return Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}