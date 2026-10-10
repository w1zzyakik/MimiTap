using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MimiTap;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private AppSettings _working = new();
    private bool _loading = true;
    private bool _allowClose;
    private string _idlePath = string.Empty;
    private string _clickPath = string.Empty;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        TitleIcon.Source = LoadPreviewImage(SettingsStore.DefaultIcon, null);
        Loaded += (_, _) =>
        {
            if (_loading)
            {
                _loading = false;
            }
        };
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void LoadFrom(AppSettings settings)
    {
        _working = settings.Clone();
        _loading = true;

        _idlePath = settings.IdleImagePath;
        _clickPath = settings.ClickImagePath;
        UseImageSizeBox.IsChecked = settings.UseImageSize;
        WidthBox.Text = settings.Width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = settings.Height.ToString(CultureInfo.InvariantCulture);
        StretchBox.SelectedIndex = IndexOfTag(StretchBox, settings.Stretch);
        LockAspectBox.IsChecked = settings.LockAspect;

        ScreenBox.SelectedIndex = IndexOfTag(ScreenBox, settings.Screen);
        AnchorBox.SelectedIndex = IndexOfTag(AnchorBox, settings.Anchor);
        PosXBox.Text = settings.PositionX is double px ? px.ToString("0") : string.Empty;
        PosYBox.Text = settings.PositionY is double py ? py.ToString("0") : string.Empty;

        MouseHookEnabledBox.IsChecked = settings.MouseHookEnabled;
        HitTestBox.SelectedIndex = IndexOfTag(HitTestBox, settings.HitTestMode);
        ZoneSlider.Value = settings.ClickZonePadding;
        ClickBehaviorBox.SelectedIndex = IndexOfTag(ClickBehaviorBox, settings.ClickBehavior);
        ClickDurationBox.Text = settings.ClickDurationMs.ToString(CultureInfo.InvariantCulture);
        ClickDurationBox.IsEnabled = TagOf(ClickBehaviorBox, "Toggle") == "Timed";
        DragToMoveBox.IsChecked = settings.DragToMove;
        ClickThroughBox.IsChecked = settings.ClickThrough;
        NoActivateBox.IsChecked = settings.NoActivate;
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTop;
        StartVisibleBox.IsChecked = settings.StartVisible;
        OpacitySlider.Value = settings.Opacity;
        OpacityText.Text = $"{settings.Opacity * 100:0}%";

        SoundEnabledBox.IsChecked = settings.SoundEnabled;
        SoundPathBox.Text = settings.SoundPath;
        VolumeSlider.Value = settings.Volume;
        VolumeText.Text = $"{settings.Volume * 100:0}%";
        MaxSecondsSlider.Value = settings.SoundMaxSeconds;
        MaxSecondsText.Text = settings.SoundMaxSeconds.ToString("0.00") + " с";

        AutoStartBox.IsChecked = settings.AutoStart;
        HotkeyEnabledBox.IsChecked = settings.HotkeyEnabled;
        HotkeyCtrlBox.IsChecked = settings.HotkeyCtrl;
        HotkeyAltBox.IsChecked = settings.HotkeyAlt;
        HotkeyShiftBox.IsChecked = settings.HotkeyShift;
        HotkeyKeyBox.SelectedIndex = Math.Max(0, IndexOfTag(HotkeyKeyBox, settings.HotkeyKey));

        ShowOverlayButton.Content = settings.StartVisible ? "Скрыть картинку" : "Показать картинку";

        WidthBox.IsEnabled = !settings.UseImageSize;
        HeightBox.IsEnabled = !settings.UseImageSize;

        UpdateSoundInfo(settings);
        UpdatePreview();
        UpdateZoneInfo(settings);
        _loading = false;
        StatusText.Text = "Настройки сохраняются в %AppData%\\MimiTap\\settings.json";
    }

    private void Collect(AppSettings target)
    {
        target.IdleImagePath = _idlePath;
        target.ClickImagePath = _clickPath;
        target.UseImageSize = UseImageSizeBox.IsChecked == true;
        target.Width = ParseInt(WidthBox.Text, target.Width, 16, 10000);
        target.Height = ParseInt(HeightBox.Text, target.Height, 16, 10000);
        target.Stretch = TagOf(StretchBox, "Uniform");
        target.LockAspect = LockAspectBox.IsChecked == true;
        target.Screen = TagOf(ScreenBox, "Primary");
        target.Anchor = TagOf(AnchorBox, "Free");

        bool hasX = double.TryParse(PosXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double px);
        bool hasY = double.TryParse(PosYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double py);
        target.HasSavedPosition = hasX && hasY;
        if (target.HasSavedPosition)
        {
            target.PositionX = px;
            target.PositionY = py;
        }

        target.MouseHookEnabled = MouseHookEnabledBox.IsChecked == true;
        target.HitTestMode = TagOf(HitTestBox, "Window");
        target.ClickZonePadding = (int)Math.Round(ZoneSlider.Value);
        target.ClickBehavior = TagOf(ClickBehaviorBox, "Toggle");
        target.ClickDurationMs = ParseInt(ClickDurationBox.Text, 150, 20, 5000);
        target.DragToMove = DragToMoveBox.IsChecked == true;
        target.ClickThrough = ClickThroughBox.IsChecked == true;
        target.NoActivate = NoActivateBox.IsChecked == true;
        target.AlwaysOnTop = AlwaysOnTopBox.IsChecked == true;
        target.StartVisible = StartVisibleBox.IsChecked == true;
        target.Opacity = Math.Round(OpacitySlider.Value, 3);
        target.SoundEnabled = SoundEnabledBox.IsChecked == true;
        target.SoundPath = SoundPathBox.Text.Trim();
        target.Volume = Math.Round(VolumeSlider.Value, 3);
        target.SoundMaxSeconds = Math.Round(Math.Min(1.0, MaxSecondsSlider.Value), 3);
        target.AutoStart = AutoStartBox.IsChecked == true;
        target.HotkeyEnabled = HotkeyEnabledBox.IsChecked == true;
        target.HotkeyCtrl = HotkeyCtrlBox.IsChecked == true;
        target.HotkeyAlt = HotkeyAltBox.IsChecked == true;
        target.HotkeyShift = HotkeyShiftBox.IsChecked == true;
        target.HotkeyKey = TagOf(HotkeyKeyBox, "M");
        target.Normalize();
    }

    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        var next = _controller.Settings.Clone();
        Collect(next);

        try
        {
            _controller.ApplySettings(next);
            _working = next.Clone();
            ShowOverlayButton.Content = next.StartVisible ? "Скрыть картинку" : "Показать картинку";
            StatusText.Text = "Сохранено";
            StatusText.Foreground = FindResource("AccentBrush") as Brush;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось сохранить: " + ex.Message;
            StatusText.Foreground = FindResource("DangerBrush") as Brush;
            SettingsStore.WriteTextFile(SettingsStore.ErrorLogPath, ex.ToString());
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void HideToTray(object sender, RoutedEventArgs e)
    {
        Hide();

        if (!_controller.IsOverlayVisible)
        {
            _controller.SetOverlayVisible(true);
        }
    }

    private void ExitApp(object sender, RoutedEventArgs e) => _controller.Exit();

    private void ResetAll(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Сбросить все настройки к значениям по умолчанию?",
            "MimiTap",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var next = _controller.DefaultSettings();
        _controller.ApplySettings(next);
        LoadFrom(next);
        StatusText.Text = "Сброшено к значениям по умолчанию";
    }

    private void ToggleOverlayVisibility(object sender, RoutedEventArgs e)
    {
        _controller.ToggleOverlay();
        ShowOverlayButton.Content = _controller.IsOverlayVisible ? "Скрыть картинку" : "Показать картинку";
    }

    private void PickIdleImage(object sender, RoutedEventArgs e)
    {
        var file = PickImage(_idlePath);
        if (file is null)
        {
            return;
        }

        _idlePath = file;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        UpdatePreview();
    }

    private void PickClickImage(object sender, RoutedEventArgs e)
    {
        var file = PickImage(_clickPath);
        if (file is null)
        {
            return;
        }

        _clickPath = file;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        UpdatePreview();
    }

    private void ResetIdleImage(object sender, RoutedEventArgs e)
    {
        _idlePath = SettingsStore.DefaultIdleImage;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        UpdatePreview();
    }

    private void ResetClickImage(object sender, RoutedEventArgs e)
    {
        _clickPath = SettingsStore.DefaultClickImage;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        UpdatePreview();
    }

    private string? PickImage(string current)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы (*.*)|*.*",
            InitialDirectory = File.Exists(current) ? Path.GetDirectoryName(current) : SettingsStore.AssetsDir
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void PickSound(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите звук нажатия",
            Filter = "Звуки (*.wav;*.mp3;*.m4a;*.wma;*.aac;*.ogg)|*.wav;*.mp3;*.m4a;*.wma;*.aac;*.ogg|Все файлы (*.*)|*.*",
            InitialDirectory = File.Exists(SoundPathBox.Text) ? Path.GetDirectoryName(SoundPathBox.Text) : null
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SoundPathBox.Text = dialog.FileName;
        var next = _working.Clone();
        Collect(next);
        _working = next;
        UpdateSoundInfo(next);
    }

    private void ResetSound(object sender, RoutedEventArgs e)
    {
        SoundPathBox.Text = string.Empty;
        UpdateSoundInfo(_working);
    }

    private void TestSound(object sender, RoutedEventArgs e)
    {
        var next = _working.Clone();
        Collect(next);
        _controller.TestSound(next);
    }

    private void UpdateSoundInfo(AppSettings settings)
    {
        if (SoundInfoText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.SoundPath) || !File.Exists(settings.SoundPath))
        {
            SoundInfoText.Text = string.IsNullOrWhiteSpace(settings.SoundPath)
                ? "Звук не выбран — будет тишина."
                : "Файл звука не найден";
            return;
        }

        var duration = SoundService.TryGetDuration(settings.SoundPath);
        SoundInfoText.Text = duration is null
            ? $"Файл: {Path.GetFileName(settings.SoundPath)} (обрезка до {settings.SoundMaxSeconds:0.00} с)"
            : duration.Value > settings.SoundMaxSeconds + 0.001
                ? $"Длительность {duration.Value:0.00} с — обрезается до {settings.SoundMaxSeconds:0.00} с"
                : $"Длительность {duration.Value:0.00} с — укладывается в лимит";
    }

    private void OnSizeModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        bool enabled = UseImageSizeBox.IsChecked != true;
        WidthBox.IsEnabled = enabled;
        HeightBox.IsEnabled = enabled;
        UpdatePreview();
    }

    private void OnSizeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || LockAspectBox.IsChecked != true)
        {
            return;
        }

        if (!int.TryParse(WidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) || width <= 0)
        {
            return;
        }

        double ratio = _working.Height / (double)Math.Max(1, _working.Width);
        if (sender == WidthBox)
        {
            HeightBox.Text = Math.Clamp((int)Math.Round(width * ratio), 16, 10000).ToString(CultureInfo.InvariantCulture);
        }
        else if (int.TryParse(HeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) && height > 0)
        {
            WidthBox.Text = Math.Clamp((int)Math.Round(height / Math.Max(0.01, ratio)), 16, 10000).ToString(CultureInfo.InvariantCulture);
        }
    }

    private void OnStretchChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void OnAnchorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (TagOf(AnchorBox, "Free") != "Free")
        {
            PosXBox.Text = string.Empty;
            PosYBox.Text = string.Empty;
        }
    }

    private void OnClickModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClickDurationBox is null)
        {
            return;
        }

        ClickDurationBox.IsEnabled = !_loading && TagOf(ClickBehaviorBox, "Toggle") == "Timed";
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || OpacityText is null)
        {
            return;
        }

        OpacityText.Text = $"{e.NewValue * 100:0}%";
        UpdatePreview();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}%";
        }
    }

    private void OnMaxSecondsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxSecondsText is null)
        {
            return;
        }

        MaxSecondsText.Text = e.NewValue.ToString("0.00") + " с";
        if (!_loading)
        {
            UpdateSoundInfo(_working);
        }
    }

    private void OnPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void SaveCurrentPosition(object sender, RoutedEventArgs e)
    {
        _controller.SaveOverlayPosition();
        var rect = _controller.OverlayPosition();
        PosXBox.Text = rect.X.ToString("0");
        PosYBox.Text = rect.Y.ToString("0");
    }

    private void CenterOverlay(object sender, RoutedEventArgs e)
    {
        _controller.CenterOverlay();
        var rect = _controller.OverlayPosition();
        PosXBox.Text = rect.X.ToString("0");
        PosYBox.Text = rect.Y.ToString("0");
    }

    private void ClearSavedPosition(object sender, RoutedEventArgs e)
    {
        PosXBox.Text = string.Empty;
        PosYBox.Text = string.Empty;
    }

    private void UpdatePreview()
    {
        if (PreviewImage is null)
        {
            return;
        }

        var next = _working.Clone();
        Collect(next);

        bool showClick = PreviewClickBox.IsChecked == true;
        string source = showClick ? next.ClickImagePath : next.IdleImagePath;
        string fallback = showClick ? "pack://application:,,,/Assets/2.png" : "pack://application:,,,/Assets/1.png";

        PreviewImage.Source = LoadPreviewImage(source, fallback);
        PreviewImage.Stretch = next.Stretch switch
        {
            "UniformToFill" => Stretch.UniformToFill,
            "Fill" => Stretch.Fill,
            "None" => Stretch.None,
            "Center" => Stretch.None,
            _ => Stretch.Uniform
        };
        PreviewImage.Opacity = next.Opacity;
    }

    private void UpdateZoneInfo(AppSettings settings)
    {
        if (ZoneInfoText is null)
        {
            return;
        }

        bool screenMode = settings.HitTestMode == "Screen";
        ZoneSlider.IsEnabled = !screenMode;

        if (!_controller.HookInstalled && settings.MouseHookEnabled)
        {
            ZoneInfoText.Text = "ВНИМАНИЕ: глобальный хук мыши не установлен — клики не отслеживаются.";
            return;
        }

        if (!settings.MouseHookEnabled)
        {
            ZoneInfoText.Text = "Хук мыши выключен: клики обрабатывает только сама картинка (если кликабельность не отключена).";
            return;
        }

        if (screenMode)
        {
            ZoneInfoText.Text = "Клик засчитывается в любом месте экрана.";
            return;
        }

        var zone = _controller.ClickZoneRect();
        ZoneInfoText.Text = $"Картинка {settings.Width}×{settings.Height} px, зона клика {zone.Width:0}×{zone.Height:0} px " +
                            $"(X: {zone.Left:0}…{zone.Right:0}, Y: {zone.Top:0}…{zone.Bottom:0}). " +
                            "Клик по «воздуху» вокруг картинки тоже переключает её.";
    }

    private void OnZoneChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoneValueText is null)
        {
            return;
        }

        ZoneValueText.Text = $"{e.NewValue:0} px";

        if (_loading)
        {
            return;
        }

        var next = _working.Clone();
        Collect(next);
        next.ClickZonePadding = (int)Math.Round(e.NewValue);
        _controller.PreviewSettings(next);
        UpdateZoneInfo(next);
    }

    private void OnHitTestChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var next = _working.Clone();
        Collect(next);
        _controller.PreviewSettings(next);
        UpdateZoneInfo(next);
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static BitmapSource? LoadPreviewImage(string? path, string? packUri)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(Path.GetFullPath(path));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
            }
        }

        if (string.IsNullOrWhiteSpace(packUri))
        {
            return null;
        }

        try
        {
            var embedded = new BitmapImage();
            embedded.BeginInit();
            embedded.UriSource = new Uri(packUri, UriKind.Absolute);
            embedded.EndInit();
            embedded.Freeze();
            return embedded;
        }
        catch
        {
            return null;
        }
    }

    private static int ParseInt(string text, int fallback, int min, int max) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static string TagOf(ComboBox box, string fallback) =>
        box.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : fallback;

    private static int IndexOfTag(ComboBox box, string tag)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem item && item.Tag as string == tag)
            {
                return i;
            }
        }

        return 0;
    }
}