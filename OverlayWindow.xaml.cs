using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MimiTap;

public partial class OverlayWindow : Window
{
    private const int MaskSize = 256;

    private AppSettings _settings;
    private readonly SoundService _sound;
    private readonly DispatcherTimer _restoreTimer;
    private readonly DispatcherTimer _holdWatch;

    private BitmapSource? _idleImage;
    private BitmapSource? _clickImage;
    private string? _loadedIdlePath;
    private string? _loadedClickPath;

    private byte[]? _alphaMask;
    private int _maskW;
    private int _maskH;
    private string? _maskKey;

    private bool _showingClick;
    private bool _ready;

    private bool _dragging;
    private Point _dragAnchorDip;
    private double _dragAnchorLeft;
    private double _dragAnchorTop;
    private bool _dragMoved;

    public OverlayWindow(AppSettings settings, SoundService sound)
    {
        _settings = settings;
        _sound = sound;

        InitializeComponent();

        _restoreTimer = new DispatcherTimer();
        _restoreTimer.Tick += (_, _) => RestoreIdle();

        _holdWatch = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _holdWatch.Interval = TimeSpan.FromMilliseconds(250);
        _holdWatch.Tick += (_, _) => WatchHold();

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            _restoreTimer.Stop();
            _holdWatch.Stop();
        };
    }

    public event Action? SettingsRequested;
    public event Action<Rect>? Moved;

    public string HitTestMode => _settings.HitTestMode;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _ready = true;
        ApplyExStyle(_settings.ClickThrough, _settings.NoActivate);
    }

    public void ApplySettings(AppSettings settings)
    {
        bool sourceChanged =
            !string.Equals(_loadedIdlePath, settings.IdleImagePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_loadedClickPath, settings.ClickImagePath, StringComparison.OrdinalIgnoreCase) ||
            _idleImage is null || _clickImage is null;

        double previousLeft = Left;
        double previousTop = Top;

        _settings = settings;

        if (sourceChanged)
        {
            LoadImages();
        }

        ApplyExStyle(settings.ClickThrough, settings.NoActivate);
        Topmost = settings.AlwaysOnTop;
        Opacity = settings.Opacity;
        ImageHost.Stretch = ParseStretch(settings.Stretch);

        if (settings.UseImageSize && _idleImage is not null)
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double scale = dpi > 0.1 ? dpi : 1.0;
            Width = Math.Max(1, Math.Round(_idleImage.PixelWidth / scale));
            Height = Math.Max(1, Math.Round(_idleImage.PixelHeight / scale));
        }
        else
        {
            Width = settings.Width;
            Height = settings.Height;
        }

        ApplyPosition(previousLeft, previousTop);
        RefreshImage();
        InvalidateMask();
    }

    private void ApplyPosition(double fallbackX, double fallbackY)
    {
        var screen = ResolveScreen();

        if (_settings.HasSavedPosition
            && _settings.PositionX is double savedX
            && _settings.PositionY is double savedY
            && double.IsFinite(savedX)
            && double.IsFinite(savedY))
        {
            Left = savedX;
            Top = savedY;
            return;
        }

        switch (_settings.Anchor)
        {
            case nameof(AnchorMode.Center):
            case nameof(AnchorMode.TopCenter):
            case nameof(AnchorMode.BottomCenter):
            case nameof(AnchorMode.LeftCenter):
            case nameof(AnchorMode.RightCenter):
            case nameof(AnchorMode.TopLeft):
            case nameof(AnchorMode.TopRight):
            case nameof(AnchorMode.BottomLeft):
            case nameof(AnchorMode.BottomRight):
                PlaceByAnchor(_settings.Anchor, screen, out double x, out double y);
                Left = x;
                Top = y;
                break;
            default:
                Left = double.IsFinite(fallbackX) ? fallbackX : screen.Left + 40;
                Top = double.IsFinite(fallbackY) ? fallbackY : screen.Top + 40;
                break;
        }
    }

    private void PlaceByAnchor(string anchor, Rect screen, out double x, out double y)
    {
        double w = Width;
        double h = Height;

        switch (anchor)
        {
            case nameof(AnchorMode.TopLeft):
                x = screen.Left + 24;
                y = screen.Top + 24;
                break;
            case nameof(AnchorMode.TopRight):
                x = screen.Right - w - 24;
                y = screen.Top + 24;
                break;
            case nameof(AnchorMode.BottomLeft):
                x = screen.Left + 24;
                y = screen.Bottom - h - 24;
                break;
            case nameof(AnchorMode.BottomRight):
                x = screen.Right - w - 24;
                y = screen.Bottom - h - 24;
                break;
            case nameof(AnchorMode.TopCenter):
                x = screen.Left + (screen.Width - w) / 2;
                y = screen.Top + 24;
                break;
            case nameof(AnchorMode.BottomCenter):
                x = screen.Left + (screen.Width - w) / 2;
                y = screen.Bottom - h - 24;
                break;
            case nameof(AnchorMode.LeftCenter):
                x = screen.Left + 24;
                y = screen.Top + (screen.Height - h) / 2;
                break;
            case nameof(AnchorMode.RightCenter):
                x = screen.Right - w - 24;
                y = screen.Top + (screen.Height - h) / 2;
                break;
            default:
                x = screen.Left + (screen.Width - w) / 2;
                y = screen.Top + (screen.Height - h) / 2;
                break;
        }
    }

    private Rect ResolveScreen()
    {
        var mode = Enum.TryParse<ScreenMode>(_settings.Screen, out var parsed) ? parsed : ScreenMode.Primary;

        if (mode == ScreenMode.Virtual)
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        if (mode == ScreenMode.Cursor)
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            return ScreenBounds.FromPixels(cursor.X, cursor.Y);
        }

        return ScreenBounds.Primary();
    }

    public void LoadImages()
    {
        _idleImage = LoadBitmap(_settings.IdleImagePath, "pack://application:,,,/Assets/1.png");
        _clickImage = LoadBitmap(_settings.ClickImagePath, "pack://application:,,,/Assets/2.png");
        _loadedIdlePath = _settings.IdleImagePath;
        _loadedClickPath = _settings.ClickImagePath;
        _maskKey = null;
    }

    private static BitmapSource? LoadBitmap(string? path, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = new BitmapImage();
                    loaded.BeginInit();
                    loaded.CacheOption = BitmapCacheOption.OnLoad;
                    loaded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    loaded.UriSource = new Uri(Path.GetFullPath(path));
                    loaded.EndInit();
                    loaded.Freeze();
                    return loaded;
                }
            }
            catch
            {
            }
        }

        try
        {
            var embedded = new BitmapImage();
            embedded.BeginInit();
            embedded.UriSource = new Uri(fallback, UriKind.Absolute);
            embedded.CacheOption = BitmapCacheOption.OnLoad;
            embedded.EndInit();
            embedded.Freeze();
            return embedded;
        }
        catch
        {
            return null;
        }
    }

    private static Stretch ParseStretch(string value) => Enum.TryParse<StretchMode>(value, out var mode)
        ? mode switch
        {
            StretchMode.Uniform => Stretch.Uniform,
            StretchMode.UniformToFill => Stretch.UniformToFill,
            StretchMode.Fill => Stretch.Fill,
            StretchMode.None => Stretch.None,
            StretchMode.Center => Stretch.None,
            _ => Stretch.Uniform
        }
        : Stretch.Uniform;

    public void RefreshImage()
    {
        ImageHost.Source = _showingClick ? (_clickImage ?? _idleImage) : _idleImage;
        _maskKey = null;
    }

    /// <summary>Имя файла картинки, которая показана сейчас.</summary>
    public string CurrentImageName
    {
        get
        {
            string path = _showingClick
                ? (string.IsNullOrWhiteSpace(_settings.ClickImagePath) ? "2.png" : _settings.ClickImagePath)
                : (string.IsNullOrWhiteSpace(_settings.IdleImagePath) ? "1.png" : _settings.IdleImagePath);
            return Path.GetFileName(path);
        }
    }

    public void ShowIdle()
    {
        if (!_showingClick)
        {
            return;
        }

        _showingClick = false;
        _restoreTimer.Stop();
        RefreshImage();
    }

    public void ShowClick()
    {
        _showingClick = true;
        RefreshImage();
    }

    public void TriggerClick()
    {
        bool hold = IsHoldMode;

        if (hold)
        {
            // 2.png держится, пока зажата кнопка; отпускание обрабатывает SetPressed(false)
            SetPressed(true);
            return;
        }

        bool toggle = Enum.TryParse<ClickMode>(_settings.ClickBehavior, out var mode) && mode == ClickMode.Toggle;

        if (toggle)
        {
            _showingClick = !_showingClick;
            _restoreTimer.Stop();
        }
        else
        {
            _showingClick = true;
            _restoreTimer.Stop();
            _restoreTimer.Interval = TimeSpan.FromMilliseconds(_settings.ClickDurationMs);
            _restoreTimer.Start();
        }

        RefreshImage();

        if (_settings.SoundEnabled)
        {
            _sound.Play(_settings.SoundPath, _settings.Volume, _settings.SoundMaxSeconds);
        }
    }

    public bool IsHoldMode => Enum.TryParse<ClickMode>(_settings.ClickBehavior, out var mode) && mode == ClickMode.Hold;

    public bool IsPressed { get; private set; }

    /// <summary>
    /// Состояние «кнопка зажата»: пока она зажата — 2.png, как только отпущена — 1.png.
    /// </summary>
    public void SetPressed(bool pressed)
    {
        if (!IsHoldMode)
        {
            return;
        }

        if (pressed == IsPressed)
        {
            return;
        }

        IsPressed = pressed;

        if (pressed)
        {
            PressedSince = Environment.TickCount64;
            _restoreTimer.Stop();
            ShowClick();

            if (_settings.SoundEnabled)
            {
                _sound.Play(_settings.SoundPath, _settings.Volume, _settings.SoundMaxSeconds);
            }
        }
        else
        {
            LastHoldMs = Environment.TickCount64 - PressedSince;
            ShowIdle();
        }

        _holdWatch.Stop();
        _holdWatch.Start();
        PressedChanged?.Invoke(IsPressed);
    }

    public long PressedSince { get; private set; }

    public long LastHoldMs { get; private set; }

    public event Action<bool>? PressedChanged;

    /// <summary>
    /// Страховка на случай потерянного события отпускания: если кнопка физически
    /// уже не зажата, возвращаем 1.png.
    /// </summary>
    private void WatchHold()
    {
        if (!IsHoldMode || !IsPressed)
        {
            _holdWatch.Stop();
            return;
        }

        if (!NativeMethods.IsLeftButtonDown())
        {
            SetPressed(false);
            _holdWatch.Stop();
        }
    }

    private void RestoreIdle()
    {
        _restoreTimer.Stop();
        if (!_showingClick)
        {
            return;
        }

        _showingClick = false;
        RefreshImage();
    }

    public void SaveCurrentPosition()
    {
        _settings.PositionX = Left;
        _settings.PositionY = Top;
        _settings.HasSavedPosition = true;
    }

    public void CenterOnScreen()
    {
        var screen = ResolveScreen();
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top + (screen.Height - Height) / 2;
        SaveCurrentPosition();
        Moved?.Invoke(new Rect(Left, Top, Width, Height));
    }

    private void ApplyExStyle(bool clickThrough, bool noActivate)
    {
        if (!_ready)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt32();
        style |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
        style |= noActivate ? NativeMethods.WS_EX_NOACTIVATE : 0;
        style = clickThrough
            ? style | NativeMethods.WS_EX_TRANSPARENT
            : style & ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    public bool HitTest(double screenX, double screenY)
    {
        if (_settings.HitTestMode == "Screen")
        {
            return IsVisible;
        }

        return HitTestZone(screenX, screenY);
    }

    public bool HitTestZone(double screenX, double screenY)
    {
        if (!IsVisible || Width <= 0 || Height <= 0)
        {
            return false;
        }

        Point local;
        try
        {
            local = PointFromScreen(new Point(screenX, screenY));
        }
        catch
        {
            return false;
        }

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        double padding = Math.Max(0, _settings.ClickZonePadding);

        LastHitTestInfo = $"mode={_settings.HitTestMode} screen=({screenX:0},{screenY:0}) local=({local.X:0.##},{local.Y:0.##}) " +
                          $"size=({w:0},{h:0}) left={Left:0.##} top={Top:0.##} pad={padding:0}";

        // зона клика: прямоугольник окна, расширенный на padding во все стороны
        if (local.X < -padding || local.Y < -padding || local.X >= w + padding || local.Y >= h + padding)
        {
            return false;
        }

        bool insideWindow = local.X >= 0 && local.Y >= 0 && local.X < w && local.Y < h;

        if (!insideWindow)
        {
            return true;
        }

        if (_settings.HitTestMode != "Image")
        {
            return true;
        }

        var mask = GetAlphaMask();
        if (mask is null)
        {
            return true;
        }

        int mx = (int)(local.X / w * _maskW);
        int my = (int)(local.Y / h * _maskH);
        mx = Math.Clamp(mx, 0, _maskW - 1);
        my = Math.Clamp(my, 0, _maskH - 1);
        return mask[my * _maskW + mx] > 16;
    }

    public string LastHitTestInfo { get; private set; } = "";

    public Rect ClickZoneRect()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        double padding = Math.Max(0, _settings.ClickZonePadding);

        Point topLeft = PointToScreen(new Point(0, 0));
        return new Rect(
            topLeft.X - padding,
            topLeft.Y - padding,
            w + padding * 2,
            h + padding * 2);
    }

    private byte[]? GetAlphaMask()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        string key = $"{_showingClick}|{_loadedClickPath}|{_loadedIdlePath}|{w:0}|{h:0}|{_settings.Stretch}";

        if (_alphaMask is not null && _maskKey == key)
        {
            return _alphaMask;
        }

        var source = _showingClick ? _clickImage : _idleImage;
        if (source is null || w < 1 || h < 1)
        {
            _alphaMask = null;
            _maskKey = key;
            return null;
        }

        _maskW = MaskSize;
        _maskH = Math.Max(1, (int)Math.Round(MaskSize * h / w));
        _maskKey = key;

        double dpiX = source.DpiX > 1 ? source.DpiX : 96;
        double dpiY = source.DpiY > 1 ? source.DpiY : 96;
        double imageW = source.PixelWidth * 96.0 / dpiX;
        double imageH = source.PixelHeight * 96.0 / dpiY;

        StretchMode mode = Enum.TryParse<StretchMode>(_settings.Stretch, true, out var parsed)
            ? parsed
            : StretchMode.Uniform;
        (double dx, double dy, double dw, double dh) = DestinationRect(mode, imageW, imageH, w, h);

        var scaled = new FormatConvertedBitmap(
            new TransformedBitmap(source, new ScaleTransform((double)MaskSize / source.PixelWidth, (double)MaskSize / source.PixelHeight)),
            PixelFormats.Bgra32,
            null,
            0);
        scaled.Freeze();

        var pixels = new byte[MaskSize * MaskSize * 4];
        scaled.CopyPixels(pixels, MaskSize * 4, 0);

        var mask = new byte[_maskW * _maskH];
        for (int my = 0; my < _maskH; my++)
        {
            double wy = (my + 0.5) / _maskH * h;
            for (int mx = 0; mx < _maskW; mx++)
            {
                double wx = (mx + 0.5) / _maskW * w;

                if (wx < dx || wx > dx + dw || wy < dy || wy > dy + dh)
                {
                    continue;
                }

                double u = (wx - dx) / dw;
                double v = (wy - dy) / dh;
                int px = Math.Clamp((int)(u * (MaskSize - 1)), 0, MaskSize - 1);
                int py = Math.Clamp((int)(v * (MaskSize - 1)), 0, MaskSize - 1);
                mask[my * _maskW + mx] = pixels[(py * MaskSize + px) * 4 + 3];
            }
        }

        _alphaMask = mask;
        return mask;
    }

    private static (double dx, double dy, double dw, double dh) DestinationRect(
        StretchMode mode,
        double imageW,
        double imageH,
        double w,
        double h)
    {
        if (imageW <= 0 || imageH <= 0)
        {
            return (0, 0, w, h);
        }

        switch (mode)
        {
            case StretchMode.Fill:
                return (0, 0, w, h);
            case StretchMode.UniformToFill:
            {
                double s = Math.Max(w / imageW, h / imageH);
                double dw = imageW * s;
                double dh = imageH * s;
                return ((w - dw) / 2, (h - dh) / 2, dw, dh);
            }
            case StretchMode.None:
            case StretchMode.Center:
                return ((w - imageW) / 2, (h - imageH) / 2, imageW, imageH);
            default:
            {
                double s = Math.Min(w / imageW, h / imageH);
                double dw = imageW * s;
                double dh = imageH * s;
                return ((w - dw) / 2, (h - dh) / 2, dw, dh);
            }
        }
    }

    private void InvalidateMask()
    {
        _maskKey = null;
    }

    public void BeginDrag(int screenX, int screenY)
    {
        if (!_settings.DragToMove)
        {
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragAnchorLeft = Left;
        _dragAnchorTop = Top;
        _dragAnchorDip = PointFromScreen(new Point(screenX, screenY));
    }

    private void ScreenPoint(MouseButtonEventArgs e, out int x, out int y)
    {
        Point screen = PointToScreen(e.GetPosition(this));
        x = (int)screen.X;
        y = (int)screen.Y;
    }

    public void ContinueDrag(int screenX, int screenY)
    {
        if (!_dragging)
        {
            return;
        }

        Point current = PointFromScreen(new Point(screenX, screenY));
        double dx = current.X - _dragAnchorDip.X;
        double dy = current.Y - _dragAnchorDip.Y;

        if (!_dragMoved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
        {
            return;
        }

        _dragMoved = true;
        Left = _dragAnchorLeft + dx;
        Top = _dragAnchorTop + dy;
    }

    public bool EndDrag()
    {
        if (!_dragging)
        {
            return false;
        }

        _dragging = false;
        bool moved = _dragMoved;
        _dragMoved = false;

        if (moved)
        {
            SaveCurrentPosition();
            Moved?.Invoke(new Rect(Left, Top, Width, Height));
        }

        return moved;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        ScreenPoint(e, out int sx, out int sy);
        BeginDrag(sx, sy);

        if (IsHoldMode && !_dragging)
        {
            SetPressed(true);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            SetPressed(false);
            return;
        }

        Point screen = PointToScreen(e.GetPosition(this));
        ContinueDrag((int)screen.X, (int)screen.Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();

        bool hold = IsHoldMode;
        SetPressed(false);

        if (EndDrag())
        {
            e.Handled = true;
            return;
        }

        if (!hold)
        {
            TriggerClick();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        SettingsRequested?.Invoke();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Hide();
        }
        else if (e.Key == Key.F5)
        {
            SettingsRequested?.Invoke();
        }
    }
}

internal static class ScreenBounds
{
    internal static Rect Primary() =>
        ToRect(System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]);

    internal static Rect FromPixels(int x, int y) =>
        ToRect(System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)));

    private static Rect ToRect(System.Windows.Forms.Screen screen) =>
        new(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
}