using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MimiTap;

internal static class ImageTools
{
    public static byte[] PngToIconBytes(Stream pngStream, int size = 32)
    {
        var decoder = new PngBitmapDecoder(pngStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var scaled = new TransformedBitmap(
            source,
            new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
        scaled.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        byte[] png = buffer.ToArray();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
        writer.Flush();
        return output.ToArray();
    }
}

public sealed class AppController : IDisposable
{
    private readonly SoundService _sound = new();
    private readonly GlobalMouseHook _mouseHook = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly Dispatcher _dispatcher;
    private readonly int _hotkeyId = 0xA17;

    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private OverlayWindow? _overlay;
    private TutorialWindow? _tutorial;
    private bool _hiddenBySettings;
    private bool _dragging;
    private bool _pressArmed;
    private bool _pressMoved;
    private bool _pressOnPicture;
    private int _pressX;
    private int _pressY;
    private long _pressTime;
    private const int ClickMoveThreshold = 12;
    private bool _disposed;
    private bool _hotkeyRegistered;
    private IntPtr _hotkeyHandle = IntPtr.Zero;
    private HwndSource? _hotkeySource;
    private readonly DispatcherTimer _dragTimer;
    private int _dragX;
    private int _dragY;
    private readonly StringBuilder _diag = new();
    private readonly bool _diagnostics;

    // вводы, которые сейчас зажаты и «нажимают» кнопку за Мими (для режима «держать»)
    private readonly HashSet<string> _heldTriggers = new(StringComparer.OrdinalIgnoreCase);

    public AppController()
    {
        _dispatcher = Application.Current.Dispatcher;
        _diagnostics = HasArgument("--diag");
        _dragTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _dragTimer.Tick += (_, _) => ApplyDrag();
        Settings = SettingsStore.Load();
        Loc.SetLanguage(Settings.Language);

        // реестр — источник истины для автозапуска: установщик мог добавить запись
        // до первого запуска, иначе первое же сохранение настроек её бы удалило
        Settings.AutoStart = AutoStart.IsEnabled();

        // окно настроек создаём после загрузки языка, чтобы подписи сразу были верными
        SettingsWindow = new SettingsWindow(this);
    }

    public string DiagnosticsPath => Path.Combine(SettingsStore.DataDir, "diag.log");

    public bool HookInstalled => _mouseHook.Enabled;

    public string DragDiag => _mouseHook.Enabled ? "" : "хук мыши не установлен";

    public SettingsWindow SettingsWindow { get; }

    public AppSettings Settings { get; private set; }

    public OverlayWindow? Overlay => _overlay;

    public void Start()
    {
        SettingsStore.ExtractDefaultAssets();
        _sound.EnsureDispatcher(_dispatcher);

        if (string.IsNullOrWhiteSpace(Settings.IdleImagePath))
        {
            Settings.IdleImagePath = SettingsStore.DefaultIdleImage;
        }

        if (string.IsNullOrWhiteSpace(Settings.ClickImagePath))
        {
            Settings.ClickImagePath = SettingsStore.DefaultClickImage;
        }

        Settings.Normalize();

        _overlay = new OverlayWindow(Settings, _sound);
        _overlay.SettingsRequested += ShowSettings;
        _overlay.Moved += OnOverlayMoved;
        _overlay.InputStillHeld = AnyTriggerStillHeld;
        _overlay.Loaded += (_, _) => _overlay.ApplySettings(Settings);
        _overlay.Show();

        _mouseHook.OnEvent += OnMouseEvent;
        _mouseHook.Enabled = Settings.MouseHookEnabled;
        _keyboardHook.OnEvent += OnKeyEvent;
        _keyboardHook.Enabled = NeedsKeyboardHook(Settings);

        CreateTray();
        RegisterHotKey(Settings);

        bool minimized = HasArgument("--minimized");
        if (minimized || !Settings.StartVisible)
        {
            _hiddenBySettings = true;
            _overlay.Hide();
        }

        UpdateTrayMenu();

        if (HasArgument("--settings"))
        {
            ShowSettings();
        }

        if (SelfTest.Requested())
        {
            SelfTest.Run(this);
        }
        else if (!Settings.TutorialSeen
                 && IsOverlayVisible
                 && !HasArgument("--minimized")
                 && !HasArgument("--settings"))
        {
            // обучение показываем один раз, когда оверлей уже на экране
            _dispatcher.BeginInvoke(new Action(() => ShowTutorial(false)), DispatcherPriority.ApplicationIdle);
        }
    }

    private static bool HasArgument(string name) =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private void OnOverlayMoved(Rect rect)
    {
        Settings.PositionX = rect.X;
        Settings.PositionY = rect.Y;
        Settings.HasSavedPosition = true;
    }

    public void ApplySettings(AppSettings next)
    {
        next.Normalize();
        Settings = next;
        SettingsStore.Save(next);
        AutoStart.Set(next.AutoStart);

        Loc.SetLanguage(next.Language);

        _overlay?.ApplySettings(next);
        _mouseHook.Enabled = next.MouseHookEnabled;
        _keyboardHook.Enabled = NeedsKeyboardHook(next);
        RegisterHotKey(next);

        _hiddenBySettings = !next.StartVisible;
        if (_hiddenBySettings)
        {
            _overlay?.Hide();
        }
        else if (_overlay is not null && !_overlay.IsVisible)
        {
            _overlay?.Show();
        }

        UpdateTrayMenu();
    }

    public AppSettings DefaultSettings() => Defaults();

    public static AppSettings Defaults()
    {
        var settings = new AppSettings
        {
            IdleImagePath = SettingsStore.DefaultIdleImage,
            ClickImagePath = SettingsStore.DefaultClickImage,
            Width = 220,
            Height = 220,
            Anchor = nameof(AnchorMode.Center),
            Screen = nameof(ScreenMode.Primary),
            Stretch = nameof(StretchMode.Uniform),
            HitTestMode = "Screen",
            ClickBehavior = nameof(ClickMode.Hold),
            SoundMaxSeconds = 1.0,
            ActivePreset = 0
        };

        // по умолчанию два набора: основные кадры и дополнительный пресет
        settings.Presets.Add(new ImagePreset
        {
            IdleImagePath = SettingsStore.DefaultIdleImage,
            ClickImagePath = SettingsStore.DefaultClickImage
        });
        settings.Presets.Add(new ImagePreset
        {
            IdleImagePath = SettingsStore.DefaultPreset2Idle,
            ClickImagePath = SettingsStore.DefaultPreset2Click
        });

        settings.Normalize();
        return settings;
    }

    public void PreviewImagePath(string idle, string click)
    {
        if (_overlay is null)
        {
            return;
        }

        var probe = Settings.Clone();
        probe.IdleImagePath = idle;
        probe.ClickImagePath = click;
        _overlay.ApplySettings(probe);
    }

    public void PreviewSettings(AppSettings probe)
    {
        probe.Normalize();
        _overlay?.ApplySettings(probe);
    }

    public void TestSound(AppSettings probe)
    {
        _sound.Play(probe.SoundPath, probe.Volume, probe.SoundMaxSeconds);
    }

    public void ToggleOverlay()
    {
        if (_overlay is null)
        {
            return;
        }

        if (_overlay.IsVisible)
        {
            _hiddenBySettings = true;
            _overlay.Hide();
        }
        else
        {
            _hiddenBySettings = false;
            _overlay.ApplySettings(Settings);
            _overlay.Show();
        }

        UpdateTrayMenu();
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_overlay is null)
        {
            return;
        }

        _hiddenBySettings = !visible;
        if (visible)
        {
            _overlay.ApplySettings(Settings);
            _overlay.Show();
        }
        else
        {
            _overlay.Hide();
        }

        UpdateTrayMenu();
    }

    public bool IsOverlayVisible => _overlay?.IsVisible == true;

    /// <summary>Меняет язык интерфейса на лету и обновляет зависящие от текста элементы.</summary>
    public void ApplyLanguage(string language)
    {
        Settings.Language = Loc.Canonical(language);
        Loc.SetLanguage(Settings.Language);
        SettingsStore.Save(Settings);
        UpdateTrayMenu();
        SettingsWindow.RefreshDynamicText();
    }

    /// <summary>Показывает обучение при первом запуске или принудительно из настроек.</summary>
    public void ShowTutorial(bool force)
    {
        if (!force && Settings.TutorialSeen)
        {
            return;
        }

        if (_tutorial is { IsVisible: true })
        {
            _tutorial.Activate();
            return;
        }

        _tutorial = new TutorialWindow(this);
        if (_overlay is { IsVisible: true })
        {
            _tutorial.Owner = _overlay;
        }

        _tutorial.Closed += (_, _) =>
        {
            Settings.TutorialSeen = true;
            SettingsStore.Save(Settings);
            _tutorial = null;
        };

        _tutorial.Show();
        _tutorial.Activate();
    }

    private void OnMouseEvent(MouseHookEvent kind, int x, int y)
    {
        if (_overlay is null || !_overlay.IsVisible)
        {
            return;
        }

        switch (kind)
        {
            case MouseHookEvent.LeftDown:
                HandleLeftDown(x, y);
                break;

            case MouseHookEvent.Move:
                HandleMove(x, y);
                break;

            case MouseHookEvent.LeftUp:
                HandleLeftUp(x, y);
                break;

            case MouseHookEvent.RightDown:
                // ПКМ по картинке открывает настройки, но если ПКМ назначена
                // привязкой — она «нажимает» кнопку, а не открывает окно
                if (IsArmed(Inputs.MouseRight))
                {
                    OnTriggerDown(Inputs.MouseRight);
                }
                else if (_overlay.HitTestZone(x, y))
                {
                    ShowSettings();
                }

                break;

            case MouseHookEvent.RightUp:
                OnTriggerUp(Inputs.MouseRight);
                break;

            case MouseHookEvent.MiddleDown:
                OnTriggerDown(Inputs.MouseMiddle);
                break;

            case MouseHookEvent.MiddleUp:
                OnTriggerUp(Inputs.MouseMiddle);
                break;

            case MouseHookEvent.X1Down:
                OnTriggerDown(Inputs.MouseX1);
                break;

            case MouseHookEvent.X1Up:
                OnTriggerUp(Inputs.MouseX1);
                break;

            case MouseHookEvent.X2Down:
                OnTriggerDown(Inputs.MouseX2);
                break;

            case MouseHookEvent.X2Up:
                OnTriggerUp(Inputs.MouseX2);
                break;
        }
    }

    private void HandleLeftDown(int x, int y)
    {
        if (_overlay is null)
        {
            return;
        }

        _pressX = x;
        _pressY = y;
        _pressMoved = false;
        _pressOnPicture = _overlay.HitTestZone(x, y);

        // в режиме «весь экран» клик засчитывается где угодно, но пока открыто
        // окно настроек — только по картинке, иначе каждый клик по ползункам
        // переключал бы картинку и играл звук
        bool wholeScreen = _overlay.HitTestMode == "Screen";
        bool locationOk = wholeScreen
            ? !SettingsWindow.IsVisible || _pressOnPicture
            : _overlay.HitTest(x, y);

        _pressArmed = locationOk && IsArmed(Inputs.MouseLeft);
        _pressTime = Environment.TickCount64;
        Log($"down ({x},{y}) armed={_pressArmed} onPicture={_pressOnPicture}");

        PressTrigger(Inputs.MouseLeft, _pressArmed);

        if (_pressOnPicture && Settings.DragToMove && !IsScreenCaptureForeground())
        {
            _dragging = true;
            _dragX = x;
            _dragY = y;
            _dispatcher.BeginInvoke(() => _overlay?.BeginDrag(x, y));
        }
    }

    private void HandleMove(int x, int y)
    {
        if (_pressArmed && !_pressMoved)
        {
            int dx = x - _pressX;
            int dy = y - _pressY;
            if (dx * dx + dy * dy > ClickMoveThreshold * ClickMoveThreshold)
            {
                _pressMoved = true;
            }
        }

        if (_dragging)
        {
            // события мыши приходят с частотой до 1000 Гц; очередь диспетчера
            // переполняется, поэтому запоминаем последнюю позицию и
            // применяем её раз в 16 мс
            _dragX = x;
            _dragY = y;
            if (!_dragTimer.IsEnabled)
            {
                _dragTimer.Start();
            }
        }
    }

    private void HandleLeftUp(int x, int y)
    {
        if (!_pressArmed && !_dragging)
        {
            return;
        }

        bool armed = _pressArmed;
        _pressArmed = false;
        _dragging = false;
        _dragTimer.Stop();
        long held = Environment.TickCount64 - _pressTime;
        Log($"up ({x},{y}) armed={armed} moved={_pressMoved} held={held}ms");

        _dispatcher.BeginInvoke(() =>
        {
            if (_overlay is null)
            {
                return;
            }

            ReleaseHeld(Inputs.MouseLeft);
            bool pictureMoved = _overlay.EndDrag();
            if (pictureMoved || !armed || _pressMoved)
            {
                return;
            }

            // режимы «переключить» и «показывать N мс» реагируют на отпускание
            if (_overlay.IsHoldMode)
            {
                return;
            }

            Log("click -> trigger");
            _overlay.TriggerClick();
        });
    }

    /// <summary>Нажат ли этот ввод с точки зрения настроек (или включён «реагировать на всё»).</summary>
    private bool IsArmed(string token) =>
        Settings.ReactToAllInputs
        || Settings.InputBindings.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Открыт ли сейчас инструмент скриншота («Ножницы», Snip &amp; Sketch). Если да, нажатие
    /// ЛКМ — это выделение области снимка, а не перетаскивание Мими: иначе выделение
    /// утаскивает картинку за собой, и Мими «пропадает» (а центрирование возвращает).
    /// </summary>
    private static bool IsScreenCaptureForeground()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // у упакованных (UWP) приложений окно принадлежит ApplicationFrameHost,
        // а настоящий процесс — в дочернем окне
        return IsCaptureProcess(hwnd) || IsCaptureProcess(NativeMethods.GetWindow(hwnd, NativeMethods.GW_CHILD));
    }

    private static bool IsCaptureProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            return name.Contains("SnippingTool", StringComparison.OrdinalIgnoreCase)
                || name.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SnipAndSketch", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Мими нажимает кнопку на вводе из привязок; в режиме «держать» — пока ввод зажат.</summary>
    private void PressTrigger(string token, bool armed)
    {
        if (!armed || _overlay is null)
        {
            return;
        }

        _heldTriggers.Add(token);
        if (_overlay.IsHoldMode)
        {
            // 2.png показывается сразу на нажатии и держится, пока ввод зажат
            _overlay.SetPressed(true);
        }
    }

    private void OnTriggerDown(string token)
    {
        // пока открыто окно настроек, мышь не трогаем: пользователь настраивает,
        // а не играет (клавиатуру отсекает OnKeyEvent)
        if (SettingsWindow.IsVisible && !Inputs.IsKeyToken(token))
        {
            return;
        }

        PressTrigger(token, IsArmed(token));
    }

    private void OnTriggerUp(string token)
    {
        bool wasHeld = _heldTriggers.Contains(token);
        ReleaseHeld(token);
        if (wasHeld && _overlay is { IsHoldMode: false })
        {
            // «показывать N мс» и «переключить» срабатывают на отпускание
            _overlay.TriggerClick();
        }
    }

    /// <summary>Убирает ввод из зажатых; в режиме «держать» возвращает 1.png, если ничего не осталось.</summary>
    private void ReleaseHeld(string token)
    {
        if (!_heldTriggers.Remove(token) || _overlay is null)
        {
            return;
        }

        if (_overlay.IsHoldMode && _heldTriggers.Count == 0)
        {
            _overlay.SetPressed(false);
        }
    }

    private void OnKeyEvent(bool down, int vk)
    {
        if (_overlay is null || !_overlay.IsVisible)
        {
            return;
        }

        // пока открыто окно настроек, клавиатура набирает текст в полях — не мешаем
        if (SettingsWindow.IsActive)
        {
            return;
        }

        var key = KeyInterop.KeyFromVirtualKey(vk);
        if (key == Key.None)
        {
            return;
        }

        if (down)
        {
            OnTriggerDown(Inputs.KeyToken(key));
        }
        else
        {
            OnTriggerUp(Inputs.KeyToken(key));
        }
    }

    /// <summary>
    /// Страховка для оверлея: если все зажатые вводы физически отпущены, значит
    /// событие отпускания потерялось и 2.png пора вернуть на 1.png.
    /// </summary>
    private bool AnyTriggerStillHeld()
    {
        if (_heldTriggers.Count == 0)
        {
            return false;
        }

        if (_heldTriggers.Any(Inputs.IsDown))
        {
            return true;
        }

        _heldTriggers.Clear();
        return false;
    }

    private static bool NeedsKeyboardHook(AppSettings settings) =>
        settings.MouseHookEnabled
        && (settings.ReactToAllInputs || settings.InputBindings.Any(Inputs.IsKeyToken));

    private void ApplyDrag()
    {
        if (!_dragging || _overlay is null)
        {
            _dragTimer.Stop();
            return;
        }

        _overlay.ContinueDrag(_dragX, _dragY);
    }

    private void Log(string message)
    {
        if (!_diagnostics)
        {
            return;
        }

        _diag.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        if (_diag.Length > 64 * 1024)
        {
            SettingsStore.WriteTextFile(DiagnosticsPath, _diag.ToString());
            _diag.Clear();
        }
    }

    public void SaveOverlayPosition()
    {
        _overlay?.SaveCurrentPosition();
        SettingsStore.Save(Settings);
    }

    public void CenterOverlay()
    {
        if (_overlay is null)
        {
            return;
        }

        if (!_overlay.IsVisible)
        {
            _hiddenBySettings = false;
            _overlay.Show();
        }

        _overlay.ApplySettings(Settings);
        _overlay.CenterOnScreen();
        SettingsStore.Save(Settings);
    }

    public Rect OverlayPosition()
    {
        if (_overlay is not null && _overlay.IsVisible)
        {
            return new Rect(_overlay.Left, _overlay.Top, _overlay.Width, _overlay.Height);
        }

        return new Rect(Settings.PositionX ?? 0, Settings.PositionY ?? 0, Settings.Width, Settings.Height);
    }

    public void ShowSettings()
    {
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                SettingsWindow.LoadFrom(Settings);
            }
            catch
            {
            }

            if (!SettingsWindow.IsVisible)
            {
                SettingsWindow.Show();
            }

            SettingsWindow.Activate();
        });
    }

    private void CreateTray()
    {
        var icon = LoadTrayIcon();
        _trayMenu = new System.Windows.Forms.ContextMenuStrip();
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "MimiTap",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.DoubleClick += (_, _) => ToggleOverlay();
    }

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            if (File.Exists(SettingsStore.DefaultIcon))
            {
                return new System.Drawing.Icon(SettingsStore.DefaultIcon);
            }

            if (File.Exists(SettingsStore.DefaultIdleImage))
            {
                using var stream = File.OpenRead(SettingsStore.DefaultIdleImage);
                string temp = Path.Combine(Path.GetTempPath(), "mimitap_tray_" + Guid.NewGuid().ToString("N") + ".ico");
                File.WriteAllBytes(temp, ImageTools.PngToIconBytes(stream));
                return new System.Drawing.Icon(temp);
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void UpdateTrayMenu()
    {
        if (_trayMenu is null || _tray is null)
        {
            return;
        }

        _trayMenu.Items.Clear();
        bool visible = IsOverlayVisible;
        _trayMenu.Items.Add(Loc.T(visible ? "TrayHidePicture" : "TrayShowPicture"), null, (_, _) => ToggleOverlay());
        _trayMenu.Items.Add(Loc.T("TrayShowIdle"), null, (_, _) => _dispatcher.BeginInvoke(() => _overlay?.ShowIdle()));
        _trayMenu.Items.Add(Loc.T("TrayShowClick"), null, (_, _) => _dispatcher.BeginInvoke(() => _overlay?.ShowClick()));
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(Loc.T("TraySettings"), null, (_, _) => ShowSettings());
        _trayMenu.Items.Add(Loc.T("BtnTutorial"), null, (_, _) => ShowTutorial(true));
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(Loc.T("TrayExit"), null, (_, _) => Exit());
        _tray.Text = Loc.T(visible ? "TrayTipShown" : "TrayTipHidden");
    }

    public void Exit()
    {
        _tray?.Visible = false;
        _dragTimer.Stop();
        SettingsWindow.AllowClose();
        FlushDiag();
        Application.Current.Shutdown();
    }

    public void FlushDiag()
    {
        if (_diagnostics && _diag.Length > 0)
        {
            SettingsStore.WriteTextFile(DiagnosticsPath, _diag.ToString());
        }
    }

    public Rect ClickZoneRect()
    {
        if (_overlay is not null && _overlay.IsVisible)
        {
            return _overlay.ClickZoneRect();
        }

        var rect = OverlayPosition();
        double padding = Math.Max(0, Settings.ClickZonePadding);
        return new Rect(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);
    }

    private void RegisterHotKey(AppSettings settings)
    {
        var handle = new WindowInteropHelper(SettingsWindow).EnsureHandle();
        _hotkeySource ??= HwndSource.FromHwnd(handle);
        if (_hotkeySource is not null && !_hotkeySource.IsDisposed)
        {
            _hotkeySource.RemoveHook(OnWindowMessage);
            _hotkeySource.AddHook(OnWindowMessage);
        }

        if (_hotkeyRegistered && _hotkeyHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hotkeyHandle, _hotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkeyHandle = handle;

        if (!settings.HotkeyEnabled)
        {
            return;
        }

        uint modifiers = 0;
        if (settings.HotkeyCtrl)
        {
            modifiers |= NativeMethods.MOD_CONTROL;
        }

        if (settings.HotkeyAlt)
        {
            modifiers |= NativeMethods.MOD_ALT;
        }

        if (settings.HotkeyShift)
        {
            modifiers |= NativeMethods.MOD_SHIFT;
        }

        if (modifiers == 0)
        {
            modifiers = NativeMethods.MOD_ALT;
        }

        if (!Enum.TryParse<Key>(settings.HotkeyKey, true, out var key))
        {
            key = Key.M;
        }

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _hotkeyRegistered = NativeMethods.RegisterHotKey(handle, _hotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            handled = true;
            ToggleOverlay();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dragTimer.Stop();
        FlushDiag();
        _mouseHook.OnEvent -= OnMouseEvent;
        _mouseHook.Enabled = false;
        _mouseHook.Dispose();
        _keyboardHook.OnEvent -= OnKeyEvent;
        _keyboardHook.Enabled = false;
        _keyboardHook.Dispose();
        _sound.Dispose();
        if (_hotkeyRegistered && _hotkeyHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hotkeyHandle, _hotkeyId);
            _hotkeyRegistered = false;
        }

        _tray?.Dispose();
        _trayMenu?.Dispose();
        _tray = null;
        _trayMenu = null;
    }
}