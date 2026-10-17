using System;
using System.Collections.Generic;
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
    private enum StatusKind
    {
        Idle,
        Saved,
        Reset,
        Error
    }

    private readonly AppController _controller;
    private AppSettings _working = new();
    private bool _loading = true;
    private bool _allowClose;
    private string _idlePath = string.Empty;
    private string _clickPath = string.Empty;
    private StatusKind _statusKind = StatusKind.Idle;
    private string _statusError = string.Empty;

    // слоты своих привязок: фиксированная длина 10, пустая строка — свободный слот
    private readonly List<string> _bindings = new();
    private readonly Button[] _bindSlots = new Button[Inputs.MaxSlots];
    private int _captureSlot = -1;

    // пресеты: наборы кадров; _activePreset указывает, какой сейчас редактируется
    private const int MaxPresets = 20;
    private readonly List<ImagePreset> _presets = new();
    private int _activePreset;

    // цвета темы («#RRGGBB», пусто — по умолчанию) и их элементы в списке цветов
    private string _accentColor = string.Empty;
    private string _baseColor = string.Empty;
    private string _textColor = string.Empty;
    private readonly Dictionary<string, TextBox> _colorBoxes = new();
    private readonly Dictionary<string, Border> _colorSwatches = new();

    // кастомный выборщик цвета: какой ключ редактируется и текущий выбор в HSV
    private string? _pickerKey;
    private double _pickH;
    private double _pickS = 1.0;
    private double _pickV = 1.0;
    private bool _pickerSyncing;

    // готовые цвета в выборщике
    private static readonly string[] PickerPalette =
    {
        "#8B5CF6", "#A855F7", "#6366F1", "#3B82F6", "#0EA5E9", "#06B6D4",
        "#14B8A6", "#10B981", "#84CC16", "#EAB308", "#F59E0B", "#F97316",
        "#EF4444", "#EC4899", "#EDEAF5", "#9A93AC"
    };

    // liquid glass (бета): фон-стекло на всё меню — эффект, снимок стола ЗА окном и часы анимации
    private readonly LiquidGlassEffect? _glassEffect;
    private readonly System.Diagnostics.Stopwatch _glassClock = new();
    private WriteableBitmap? _glassBackdrop;
    private ImageBrush? _glassBackdropBrush;
    private bool _glassActive;
    private TimeSpan _glassLastCapture;

    // ограничитель FPS преломления: как часто обновлять снимок фона (анимация Time идёт плавно)
    private TimeSpan _glassCaptureInterval = TimeSpan.FromMilliseconds(1000.0 / 60.0);
    private double _glassCaptureScale = 1.0;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        BuildBindingsUi();
        BuildColorsUi();
        BuildPickerPresets();
        WireColorPicker();
        TitleIcon.Source = LoadPreviewImage(SettingsStore.DefaultIcon, null);
        VersionText.Text = FormatVersion();

        // liquid glass (бета): эффект мог не загрузиться (нет байткода шейдера) —
        // тогда чекбокс просто отключится, меню работает как обычно
        try
        {
            _glassEffect = new LiquidGlassEffect();
        }
        catch
        {
            _glassEffect = null;
            LiquidGlassBox.IsEnabled = false;
            GlassStrengthSlider.IsEnabled = false;
            GlassFpsBox.IsEnabled = false;
        }

        PreviewMouseMove += OnGlassMouseMove;
        IsVisibleChanged += (_, _) => UpdateGlassLayer();

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

    private static string FormatVersion()
    {
        Version? version = typeof(App).Assembly.GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Вызывается контроллером после смены языка — обновляет вычисляемые надписи.</summary>
    public void RefreshDynamicText()
    {
        UpdateOverlayButton();
        UpdateSoundInfo(_working);
        UpdateZoneInfo(CollectProbe());
        UpdatePreview();
        RefreshBindingsUi();
        RefreshPresetsUi();
        RefreshColorsUi();
        ApplyStatusText();
    }

    public void LoadFrom(AppSettings settings)
    {
        PickerPopup.IsOpen = false;
        _pickerKey = null;
        _working = settings.Clone();
        _loading = true;

        _idlePath = settings.IdleImagePath;
        _clickPath = settings.ClickImagePath;

        _presets.Clear();
        foreach (var preset in settings.Presets)
        {
            _presets.Add(new ImagePreset
            {
                Name = preset.Name,
                IdleImagePath = preset.IdleImagePath,
                ClickImagePath = preset.ClickImagePath
            });
        }

        if (_presets.Count == 0)
        {
            _presets.Add(new ImagePreset { IdleImagePath = _idlePath, ClickImagePath = _clickPath });
        }

        _activePreset = Math.Clamp(settings.ActivePreset, 0, _presets.Count - 1);
        SyncActivePresetIntoList();
        RefreshPresetsUi();

        UseImageSizeBox.IsChecked = settings.UseImageSize;
        WidthBox.Text = settings.Width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = settings.Height.ToString(CultureInfo.InvariantCulture);
        StretchBox.SelectedIndex = IndexOfTag(StretchBox, settings.Stretch);
        LockAspectBox.IsChecked = settings.LockAspect;

        ScreenBox.SelectedIndex = IndexOfTag(ScreenBox, settings.Screen);
        AnchorBox.SelectedIndex = IndexOfTag(AnchorBox, settings.Anchor);
        PosXBox.Text = settings.PositionX is double px ? px.ToString("0") : string.Empty;
        PosYBox.Text = settings.PositionY is double py ? py.ToString("0") : string.Empty;

        LanguageBox.SelectedIndex = IndexOfTag(LanguageBox, settings.Language);

        _accentColor = settings.AccentColor;
        _baseColor = settings.BaseColor;
        _textColor = settings.TextColor;
        RefreshColorsUi();

        MouseHookEnabledBox.IsChecked = settings.MouseHookEnabled;
        HitTestBox.SelectedIndex = IndexOfTag(HitTestBox, settings.HitTestMode);
        ZoneSlider.Value = settings.ClickZonePadding;
        ClickBehaviorBox.SelectedIndex = IndexOfTag(ClickBehaviorBox, settings.ClickBehavior);
        ClickDurationBox.Text = settings.ClickDurationMs.ToString(CultureInfo.InvariantCulture);
        ClickDurationBox.IsEnabled = TagOf(ClickBehaviorBox, "Toggle") == "Timed";
        DragToMoveBox.IsChecked = settings.DragToMove;

        _bindings.Clear();
        _bindings.AddRange(settings.InputBindings);
        EnsureBindingsSize();
        _captureSlot = -1;
        ReactAllBox.IsChecked = settings.ReactToAllInputs;
        RefreshBindingsUi();

        ClickThroughBox.IsChecked = settings.ClickThrough;
        NoActivateBox.IsChecked = settings.NoActivate;
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTop;
        StartVisibleBox.IsChecked = settings.StartVisible;
        OpacitySlider.Value = settings.Opacity;

        LiquidGlassBox.IsChecked = settings.LiquidGlassEnabled;
        GlassStrengthSlider.Value = settings.LiquidGlassStrength;
        GlassFpsBox.SelectedIndex = IndexOfTag(GlassFpsBox, settings.GlassRefractionFps);

        SoundEnabledBox.IsChecked = settings.SoundEnabled;
        SoundPathBox.Text = settings.SoundPath;
        VolumeSlider.Value = settings.Volume;
        MaxSecondsSlider.Value = settings.SoundMaxSeconds;

        AutoStartBox.IsChecked = settings.AutoStart;
        HotkeyEnabledBox.IsChecked = settings.HotkeyEnabled;
        HotkeyCtrlBox.IsChecked = settings.HotkeyCtrl;
        HotkeyAltBox.IsChecked = settings.HotkeyAlt;
        HotkeyShiftBox.IsChecked = settings.HotkeyShift;
        HotkeyKeyBox.SelectedIndex = Math.Max(0, IndexOfTag(HotkeyKeyBox, settings.HotkeyKey));

        WidthBox.IsEnabled = !settings.UseImageSize;
        HeightBox.IsEnabled = !settings.UseImageSize;

        _statusKind = StatusKind.Idle;
        _statusError = string.Empty;

        UpdateOverlayButton();
        UpdatePercentText();
        UpdateSoundInfo(settings);
        UpdateZoneInfo(settings);
        UpdatePreview();
        Theme.Apply(settings);
        ApplyStatusText();
        _loading = false;
        UpdateGlassLayer();
    }

    private void Collect(AppSettings target)
    {
        SyncActivePresetIntoList();
        target.IdleImagePath = _idlePath;
        target.ClickImagePath = _clickPath;
        target.Presets = new List<ImagePreset>();
        foreach (var preset in _presets)
        {
            target.Presets.Add(new ImagePreset
            {
                Name = preset.Name,
                IdleImagePath = preset.IdleImagePath,
                ClickImagePath = preset.ClickImagePath
            });
        }

        target.ActivePreset = _activePreset;
        target.UseImageSize = UseImageSizeBox.IsChecked == true;
        target.Width = ParseInt(WidthBox.Text, target.Width, 16, 10000);
        target.Height = ParseInt(HeightBox.Text, target.Height, 16, 10000);
        target.Stretch = TagOf(StretchBox, "Uniform");
        target.LockAspect = LockAspectBox.IsChecked == true;
        target.Screen = TagOf(ScreenBox, "Primary");
        target.Anchor = TagOf(AnchorBox, "Free");
        target.Language = Loc.Canonical(TagOf(LanguageBox, string.Empty));
        target.AccentColor = _accentColor;
        target.BaseColor = _baseColor;
        target.TextColor = _textColor;

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
        target.ReactToAllInputs = ReactAllBox.IsChecked == true;
        target.InputBindings = new List<string>(_bindings);
        target.ClickThrough = ClickThroughBox.IsChecked == true;
        target.NoActivate = NoActivateBox.IsChecked == true;
        target.AlwaysOnTop = AlwaysOnTopBox.IsChecked == true;
        target.StartVisible = StartVisibleBox.IsChecked == true;
        target.Opacity = Math.Round(OpacitySlider.Value, 3);
        target.LiquidGlassEnabled = LiquidGlassBox.IsChecked == true;
        target.LiquidGlassStrength = Math.Round(GlassStrengthSlider.Value, 3);
        target.GlassRefractionFps = TagOf(GlassFpsBox, "Unlimited");
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

    private AppSettings CollectProbe()
    {
        var probe = _working.Clone();
        Collect(probe);
        return probe;
    }

    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = false;
        _pickerKey = null;
        var next = _controller.Settings.Clone();
        Collect(next);

        try
        {
            _controller.ApplySettings(next);
            _working = next.Clone();
            _statusKind = StatusKind.Saved;
            _statusError = string.Empty;
        }
        catch (Exception ex)
        {
            _statusKind = StatusKind.Error;
            _statusError = ex.Message;
            SettingsStore.WriteTextFile(SettingsStore.ErrorLogPath, ex.ToString());
        }

        UpdateOverlayButton();
        ApplyStatusText();
    }

    private void ApplyStatusText()
    {
        switch (_statusKind)
        {
            case StatusKind.Saved:
                StatusText.Text = Loc.T("StatusSaved");
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentHoverBrush");
                break;
            case StatusKind.Reset:
                StatusText.Text = Loc.T("StatusReset");
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentHoverBrush");
                break;
            case StatusKind.Error:
                StatusText.Text = Loc.Format("StatusSaveFailed", _statusError);
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                break;
            default:
                StatusText.Text = Loc.T("FooterStatus");
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                break;
        }
    }

    private void UpdateOverlayButton()
    {
        bool visible = _controller.IsOverlayVisible;
        ShowOverlayButton.Content = Loc.T(visible ? "BtnHideOverlay" : "BtnShowOverlay");
    }

    private void UpdatePercentText()
    {
        string percent = Loc.T("UnitPercent");
        OpacityText.Text = $"{OpacitySlider.Value * 100:0}{percent}";
        VolumeText.Text = $"{VolumeSlider.Value * 100:0}{percent}";
        MaxSecondsText.Text = MaxSecondsSlider.Value.ToString("0.00", CultureInfo.CurrentCulture) + " " + Loc.T("UnitSeconds");
        ZoneValueText.Text = $"{ZoneSlider.Value:0} {Loc.T("UnitPx")}";
        GlassStrengthText.Text = $"{GlassStrengthSlider.Value * 100:0}{percent}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        DeactivateGlass();
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

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (SectionGeneral is null)
        {
            return;
        }

        if (sender is RadioButton { Tag: string tag })
        {
            ShowSection(tag);
        }
    }

    private void ShowSection(string tag)
    {
        SectionGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        SectionAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        SectionColors.Visibility = tag == "Colors" ? Visibility.Visible : Visibility.Collapsed;
        SectionClicks.Visibility = tag == "Clicks" ? Visibility.Visible : Visibility.Collapsed;
        SectionSound.Visibility = tag == "Sound" ? Visibility.Visible : Visibility.Collapsed;
        SectionSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;
        SectionAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox is null)
        {
            return;
        }

        _controller.ApplyLanguage(TagOf(LanguageBox, string.Empty));
        RefreshDynamicText();
    }

    private void OnShowTutorial(object sender, RoutedEventArgs e) => _controller.ShowTutorial(true);

    private void ResetAll(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            Loc.T("ConfirmReset"),
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
        _statusKind = StatusKind.Reset;
        ApplyStatusText();
    }

    private void ToggleOverlayVisibility(object sender, RoutedEventArgs e)
    {
        _controller.ToggleOverlay();
        UpdateOverlayButton();
    }

    private void PickIdleImage(object sender, RoutedEventArgs e)
    {
        var file = PickImage(_idlePath, Loc.T("DialogPickImage"));
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
        var file = PickImage(_clickPath, Loc.T("DialogPickImage"));
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

    private string? PickImage(string current, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = Loc.T("FilterImages"),
            InitialDirectory = File.Exists(current) ? Path.GetDirectoryName(current) : SettingsStore.AssetsDir
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void PickSound(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("DialogPickSound"),
            Filter = Loc.T("FilterSounds"),
            InitialDirectory = File.Exists(SoundPathBox.Text) ? Path.GetDirectoryName(SoundPathBox.Text) : null
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SoundPathBox.Text = dialog.FileName;
        _working = CollectProbe();
        UpdateSoundInfo(_working);
    }

    private void ResetSound(object sender, RoutedEventArgs e)
    {
        SoundPathBox.Text = string.Empty;
        UpdateSoundInfo(_working);
    }

    private void TestSound(object sender, RoutedEventArgs e)
    {
        _controller.TestSound(CollectProbe());
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
                ? Loc.T("SoundNone")
                : Loc.T("SoundNotFound");
            return;
        }

        var duration = SoundService.TryGetDuration(settings.SoundPath);
        string clip = settings.SoundMaxSeconds.ToString("0.00", CultureInfo.CurrentCulture);
        SoundInfoText.Text = duration is null
            ? Loc.Format("SoundFileClip", Path.GetFileName(settings.SoundPath), clip)
            : duration.Value > settings.SoundMaxSeconds + 0.001
                ? Loc.Format("SoundDurationClip", duration.Value.ToString("0.00", CultureInfo.CurrentCulture), clip)
                : Loc.Format("SoundDurationFit", duration.Value.ToString("0.00", CultureInfo.CurrentCulture));
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

        OpacityText.Text = $"{e.NewValue * 100:0}{Loc.T("UnitPercent")}";
        UpdatePreview();
    }

    // ── liquid glass (бета) ────────────────────────────────────────

    // кисти панелей, которые на время «жидкого стекла» становятся полупрозрачными:
    // иначе непрозрачные карточки/поля прятали бы фон-стекло под собой
    private static readonly string[] GlassPanelKeys =
    {
        "CardBrush", "CardHoverBrush", "FieldBrush", "ElevatedBrush",
        "ElevatedHoverBrush", "ElevatedPressedBrush", "NavActiveBrush",
        "LineBrush", "LineStrongBrush"
    };

    private void OnLiquidGlassChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateGlassLayer();
    }

    private void OnGlassStrengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlassStrengthText is null)
        {
            return;
        }

        GlassStrengthText.Text = $"{e.NewValue * 100:0}{Loc.T("UnitPercent")}";
        if (_loading)
        {
            return;
        }

        UpdateGlassLayer();
    }

    private void OnGlassFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateGlassLayer();
    }

    /// <summary>Целевой интервал обновления снимка фона и множитель даунскейла по настройке FPS.</summary>
    private void ResolveGlassCaptureTiming()
    {
        string mode = TagOf(GlassFpsBox, "Unlimited");
        int fps;
        if (!int.TryParse(mode, out fps) || fps <= 0)
        {
            // реалтайм: обновляем фон каждый кадр рендера (реальный потолок задаёт монитор ~60–144 Гц)
            _glassCaptureInterval = TimeSpan.FromMilliseconds(1000.0 / 60.0);
            _glassCaptureScale = 1.0;
            return;
        }

        _glassCaptureInterval = TimeSpan.FromMilliseconds(1000.0 / fps);

        // на слабом ПК (низкий лимит) снимаем фон в половинном разрешении —
        // CopyFromScreen + копирование в WriteableBitmap дешевеют вчетверо,
        // а размытие в шейдере маскирует потерю детализации
        _glassCaptureScale = fps <= 30 ? 0.5 : 1.0;
    }

    /// <summary>
    /// Включает/выключает фон «жидкого стекла» на всё меню: слой с шейдером накрывает окно,
    /// а анимация поверхности идёт через <see cref="CompositionTarget.Rendering"/>.
    /// </summary>
    private void UpdateGlassLayer()
    {
        if (_glassEffect is null || GlassLayer is null)
        {
            return;
        }

        bool enabled = LiquidGlassBox.IsChecked == true && IsVisible;
        if (!enabled)
        {
            DeactivateGlass();
            return;
        }

        double strength = Math.Clamp(GlassStrengthSlider.Value, 0.1, 1.0);
        _glassEffect.Refraction = 0.6 + 1.6 * strength;
        _glassEffect.Blur = 3.0 + 9.0 * strength;
        _glassEffect.Specular = 0.05 + 0.06 * strength;
        _glassEffect.Edge = 0.16 + 0.22 * strength;
        _glassEffect.Tint = BuildGlassTint();
        ResolveGlassCaptureTiming();

        GlassLayer.Effect = _glassEffect;
        GlassLayer.Visibility = Visibility.Visible;

        // окно исключается из захвата экрана — только тогда снимок его области покажет фон,
        // который ЗА окном, а не само окно (иначе стекло «залипло» бы само в себе)
        SetCaptureExclusion(true);
        ApplyGlassPanels(true);

        if (!_glassActive)
        {
            _glassActive = true;
            _glassClock.Restart();
            _glassLastCapture = TimeSpan.Zero;
            CompositionTarget.Rendering += OnGlassRendering;
        }

        CaptureBackdrop();
    }

    /// <summary>Снимает «жидкое стекло»: возвращает непрозрачные панели и захват окна.</summary>
    private void DeactivateGlass()
    {
        if (_glassActive)
        {
            _glassActive = false;
            CompositionTarget.Rendering -= OnGlassRendering;
            _glassClock.Stop();
        }

        SetCaptureExclusion(false);
        ApplyGlassPanels(false);

        if (GlassLayer is not null)
        {
            GlassLayer.Effect = null;
            GlassLayer.Visibility = Visibility.Collapsed;
        }

        _glassBackdrop = null;
        _glassBackdropBrush = null;
        if (_glassEffect is not null)
        {
            _glassEffect.Input = null;
        }
    }

    /// <summary>Анимирует поверхность стекла плавно, а снимок фона обновляет по выбранному лимиту FPS.</summary>
    private void OnGlassRendering(object? sender, EventArgs e)
    {
        if (!_glassActive || _glassEffect is null)
        {
            return;
        }

        // анимация Time не зависит от лимита: колыхание и линза остаются плавными
        _glassEffect.Time = _glassClock.Elapsed.TotalSeconds;

        // FPS преломления: фон переснимается не чаще, чем задано (реалтайм — каждый кадр)
        if (_glassClock.Elapsed - _glassLastCapture >= _glassCaptureInterval)
        {
            _glassLastCapture = _glassClock.Elapsed;
            CaptureBackdrop();
        }
    }

    /// <summary>Тон стекла: тёмный цвет темы — фон остаётся контрастным для светлого текста.</summary>
    private static Color BuildGlassTint()
    {
        Color baseColor = Color.FromRgb(0x0D, 0x0A, 0x13);
        if (Application.Current?.TryFindResource("BgBrush") is SolidColorBrush brush)
        {
            baseColor = brush.Color;
        }

        return Color.FromArgb(120, baseColor.R, baseColor.G, baseColor.B);
    }

    /// <summary>Полупрозрачные панели на время стекла: фон просвечивает, текстуры тем читаемы.</summary>
    private void ApplyGlassPanels(bool on)
    {
        if (on)
        {
            foreach (string key in GlassPanelKeys)
            {
                if (Application.Current?.TryFindResource(key) is not SolidColorBrush src)
                {
                    continue;
                }

                byte alpha = key switch
                {
                    "LineBrush" or "LineStrongBrush" => 0x59,
                    "FieldBrush" or "ElevatedPressedBrush" => 0x4D,
                    "NavActiveBrush" => 0x66,
                    _ => 0x73
                };

                var brush = new SolidColorBrush(Color.FromArgb(alpha, src.Color.R, src.Color.G, src.Color.B));
                brush.Freeze();
                Resources[key] = brush;
            }
        }
        else
        {
            foreach (string key in GlassPanelKeys)
            {
                Resources.Remove(key);
            }
        }
    }

    /// <summary>Прячет/возвращает окно в захват экрана (SetWindowDisplayAffinity).</summary>
    private void SetCaptureExclusion(bool exclude)
    {
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.SetWindowDisplayAffinity(
                handle, exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
        }
        catch
        {
            // старые Windows не знают WDA_EXCLUDEFROMCAPTURE — стекло просто захватит само окно
        }
    }

    /// <summary>
    /// Снимает область рабочего стола под окном в <see cref="_glassBackdrop"/>. Окно исключено
    /// из захвата, поэтому в снимок попадает именно то, что находится ЗА ним, — это и преломляет шейдер.
    /// </summary>
    private void CaptureBackdrop()
    {
        if (!_glassActive || _glassEffect is null)
        {
            return;
        }

        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT rect))
        {
            return;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 2 || height < 2)
        {
            return;
        }

        // ограничитель FPS может снимать фон в уменьшенном разрешении: шейдеру достаётся
        // меньше данных (лучше кэш текстур), а размытие стекла маскирует потерю детализации
        double scale = _glassCaptureScale is > 0.05 and < 1.0 ? _glassCaptureScale : 1.0;
        int capWidth = Math.Max(2, (int)Math.Round(width * scale));
        int capHeight = Math.Max(2, (int)Math.Round(height * scale));

        try
        {
            using var full = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(full))
            {
                graphics.CopyFromScreen(
                    rect.Left, rect.Top, 0, 0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            System.Drawing.Bitmap capture = full;
            System.Drawing.Bitmap? scaled = null;
            if (capWidth != width || capHeight != height)
            {
                scaled = new System.Drawing.Bitmap(capWidth, capHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(scaled))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.DrawImage(full, new System.Drawing.Rectangle(0, 0, capWidth, capHeight));
                }

                capture = scaled;
            }

            try
            {
                System.Drawing.Imaging.BitmapData data = capture.LockBits(
                    new System.Drawing.Rectangle(0, 0, capWidth, capHeight),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    if (_glassBackdrop is null || _glassBackdrop.PixelWidth != capWidth || _glassBackdrop.PixelHeight != capHeight)
                    {
                        _glassBackdrop = new WriteableBitmap(capWidth, capHeight, 96, 96, PixelFormats.Bgra32, null);
                        _glassBackdropBrush = new ImageBrush(_glassBackdrop) { Stretch = Stretch.Fill };
                        _glassEffect.Input = _glassBackdropBrush;
                    }

                    _glassBackdrop.WritePixels(
                        new Int32Rect(0, 0, capWidth, capHeight), data.Scan0, data.Stride * capHeight, data.Stride);

                    // Resolution — размер окна: радиус размытия в шейдере (texel = 1/Resolution)
                    // не «прыгает» при уменьшении самой текстуры
                    _glassEffect.Resolution = new Point(width, height);
                    GlassLayer.InvalidateVisual();
                }
                finally
                {
                    capture.UnlockBits(data);
                }
            }
            finally
            {
                scaled?.Dispose();
            }
        }
        catch
        {
            // сбой захвата не должен ронять меню — стекло покажет последний удачный снимок
        }
    }

    /// <summary>Блик стекла следует за курсором.</summary>
    private void OnGlassMouseMove(object sender, MouseEventArgs e)
    {
        if (_glassEffect is null || !_glassActive || ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        Point position = e.GetPosition(this);
        _glassEffect.Highlight = new Point(
            Math.Clamp(position.X / ActualWidth, 0.0, 1.0),
            Math.Clamp(position.Y / ActualHeight, 0.0, 1.0));
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}{Loc.T("UnitPercent")}";
        }
    }

    private void OnMaxSecondsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxSecondsText is null)
        {
            return;
        }

        MaxSecondsText.Text = e.NewValue.ToString("0.00", CultureInfo.CurrentCulture) + " " + Loc.T("UnitSeconds");
        if (!_loading)
        {
            UpdateSoundInfo(CollectProbe());
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

        var next = CollectProbe();

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
            ZoneInfoText.Text = Loc.T("ZoneWarning");
            return;
        }

        if (!settings.MouseHookEnabled)
        {
            ZoneInfoText.Text = Loc.T("ZoneHookOff");
            return;
        }

        if (screenMode)
        {
            ZoneInfoText.Text = Loc.T("ZoneScreen");
            return;
        }

        var zone = _controller.ClickZoneRect();
        ZoneInfoText.Text = Loc.Format(
            "ZoneDetails",
            settings.Width,
            settings.Height,
            zone.Width.ToString("0"),
            zone.Height.ToString("0"),
            zone.Left.ToString("0"),
            zone.Right.ToString("0"),
            zone.Top.ToString("0"),
            zone.Bottom.ToString("0"));
    }

    private void OnZoneChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoneValueText is null)
        {
            return;
        }

        ZoneValueText.Text = $"{e.NewValue:0} {Loc.T("UnitPx")}";

        if (_loading)
        {
            return;
        }

        var next = CollectProbe();
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

        var next = CollectProbe();
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

    /// <summary>Переносит текущие картинки в редактируемый пресет, чтобы правки не потерялись.</summary>
    private void SyncActivePresetIntoList()
    {
        if (_activePreset < 0 || _activePreset >= _presets.Count)
        {
            return;
        }

        _presets[_activePreset].IdleImagePath = _idlePath;
        _presets[_activePreset].ClickImagePath = _clickPath;
    }

    /// <summary>Перестраивает список пресетов: выбор, имя и удаление для каждого.</summary>
    private void RefreshPresetsUi()
    {
        if (PresetsList is null)
        {
            return;
        }

        PresetsList.Children.Clear();
        for (int i = 0; i < _presets.Count; i++)
        {
            PresetsList.Children.Add(BuildPresetRow(i));
        }

        if (AddPresetButton is not null)
        {
            AddPresetButton.IsEnabled = _presets.Count < MaxPresets;
        }
    }

    private FrameworkElement BuildPresetRow(int index)
    {
        var preset = _presets[index];

        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var select = new RadioButton
        {
            GroupName = "PresetGroup",
            IsChecked = index == _activePreset,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Style = FindResource("PresetSelect") as Style,
            Tag = index
        };
        select.Checked += OnPresetSelected;
        Grid.SetColumn(select, 0);
        row.Children.Add(select);

        var nameBox = new TextBox
        {
            Text = preset.Name ?? string.Empty,
            Tag = index,
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center
        };
        nameBox.TextChanged += OnPresetNameChanged;

        // подсказка с именем по умолчанию: видна, пока поле пустое
        var watermark = new TextBlock
        {
            Text = Loc.Format("PresetDefaultName", index + 1),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(preset.Name) ? Visibility.Visible : Visibility.Collapsed
        };
        watermark.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        nameBox.TextChanged += (_, _) =>
            watermark.Visibility = string.IsNullOrEmpty(nameBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var nameCell = new Grid();
        nameCell.Children.Add(nameBox);
        nameCell.Children.Add(watermark);
        Grid.SetColumn(nameCell, 1);
        row.Children.Add(nameCell);

        var remove = new Button
        {
            Content = Loc.T("PresetDelete"),
            Tag = index,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = _presets.Count > 1
        };
        remove.Click += RemovePreset;
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        return row;
    }

    private void OnPresetSelected(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: int index })
        {
            return;
        }

        if (index == _activePreset || index < 0 || index >= _presets.Count)
        {
            return;
        }

        SyncActivePresetIntoList();
        _activePreset = index;
        _idlePath = _presets[index].IdleImagePath;
        _clickPath = _presets[index].ClickImagePath;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        UpdatePreview();
    }

    private void OnPresetNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || sender is not TextBox { Tag: int index })
        {
            return;
        }

        if (index >= 0 && index < _presets.Count)
        {
            _presets[index].Name = ((TextBox)sender).Text;
        }
    }

    private void AddPreset(object sender, RoutedEventArgs e)
    {
        if (_presets.Count >= MaxPresets)
        {
            return;
        }

        SyncActivePresetIntoList();
        _presets.Add(new ImagePreset
        {
            IdleImagePath = SettingsStore.DefaultIdleImage,
            ClickImagePath = SettingsStore.DefaultClickImage
        });
        _activePreset = _presets.Count - 1;
        _idlePath = SettingsStore.DefaultIdleImage;
        _clickPath = SettingsStore.DefaultClickImage;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        RefreshPresetsUi();
        UpdatePreview();
    }

    private void RemovePreset(object sender, RoutedEventArgs e)
    {
        // последний пресет удалять нельзя — иначе нечего показывать
        if (_presets.Count <= 1 || sender is not Button { Tag: int index })
        {
            return;
        }

        if (index < 0 || index >= _presets.Count)
        {
            return;
        }

        SyncActivePresetIntoList();
        _presets.RemoveAt(index);

        if (_activePreset > index)
        {
            _activePreset--;
        }

        _activePreset = Math.Clamp(_activePreset, 0, _presets.Count - 1);
        _idlePath = _presets[_activePreset].IdleImagePath;
        _clickPath = _presets[_activePreset].ClickImagePath;
        _controller.PreviewImagePath(_idlePath, _clickPath);
        RefreshPresetsUi();
        UpdatePreview();
    }

    /// <summary>Строит строки настройки цветов: акцентный, основной и цвет текста.</summary>
    private void BuildColorsUi()
    {
        if (ColorsList is null)
        {
            return;
        }

        ColorsList.Children.Clear();
        _colorBoxes.Clear();
        _colorSwatches.Clear();

        ColorsList.Children.Add(BuildColorRow("ColorAccent", "Accent"));
        ColorsList.Children.Add(BuildColorRow("ColorBase", "Base"));
        ColorsList.Children.Add(BuildColorRow("ColorText", "Text"));
        RefreshColorSwatches();
    }

    private FrameworkElement BuildColorRow(string labelKey, string key)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = Loc.T(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var swatch = new Border
        {
            Width = 46,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            Tag = key
        };
        swatch.SetResourceReference(Border.BorderBrushProperty, "LineStrongBrush");
        swatch.MouseLeftButtonUp += (_, _) => OpenPicker(key, swatch);
        Grid.SetColumn(swatch, 1);
        row.Children.Add(swatch);

        var box = new TextBox
        {
            Width = 110,
            Text = GetColor(key),
            Tag = key,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        box.LostFocus += OnColorHexCommitted;
        box.KeyDown += OnColorHexKeyDown;
        Grid.SetColumn(box, 2);
        row.Children.Add(box);

        var button = new Button
        {
            Content = Loc.T("ColorChoose"),
            Tag = key,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => OpenPicker(key, button);
        Grid.SetColumn(button, 3);
        row.Children.Add(button);

        _colorBoxes[key] = box;
        _colorSwatches[key] = swatch;
        return row;
    }

    private string GetColor(string key) => key switch
    {
        "Base" => _baseColor,
        "Text" => _textColor,
        _ => _accentColor
    };

    private void SetColor(string key, string value)
    {
        switch (key)
        {
            case "Base":
                _baseColor = value;
                break;
            case "Text":
                _textColor = value;
                break;
            default:
                _accentColor = value;
                break;
        }
    }

    private static string DefaultColor(string key) => key switch
    {
        "Base" => Theme.DefaultBase,
        "Text" => Theme.DefaultText,
        _ => Theme.DefaultAccent
    };

    private void RefreshColorsUi() => BuildColorsUi();

    private void RefreshColorSwatches()
    {
        foreach (var pair in _colorSwatches)
        {
            Color color = Theme.Resolve(GetColor(pair.Key), DefaultColor(pair.Key));
            pair.Value.Background = new SolidColorBrush(color);
        }
    }

    private void OnColorHexCommitted(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: string key } box)
        {
            CommitColorHex(key, box);
        }
    }

    private void OnColorHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { Tag: string key } box)
        {
            e.Handled = true;
            CommitColorHex(key, box);
        }
    }

    private void CommitColorHex(string key, TextBox box)
    {
        string text = box.Text.Trim();
        if (text.Length == 0)
        {
            SetColor(key, string.Empty);
        }
        else if (Theme.TryParse(text, out Color color))
        {
            SetColor(key, Theme.ToHex(color));
        }
        else
        {
            // неверный ввод — возвращаем прежнее значение
            box.Text = GetColor(key);
            return;
        }

        SyncPickerFromKey(key);

        if (_loading)
        {
            return;
        }

        box.Text = GetColor(key);
        ApplyColorPreview();
    }

    /// <summary>Связывает элементы попапа-выборщика с обработчиками (один раз).</summary>
    private void WireColorPicker()
    {
        SvArea.MouseLeftButtonDown += OnSvDown;
        SvArea.MouseMove += OnSvMove;
        SvArea.MouseLeftButtonUp += OnSvUp;

        HueArea.MouseLeftButtonDown += OnHueDown;
        HueArea.MouseMove += OnHueMove;
        HueArea.MouseLeftButtonUp += OnHueUp;

        PickerHex.LostFocus += (_, _) => CommitPickerHex();
        PickerHex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitPickerHex();
            }
        };
    }

    /// <summary>Открывает попап-выборщик возле плашки/кнопки нужного цвета.</summary>
    private void OpenPicker(string key, FrameworkElement target)
    {
        _pickerKey = key;
        Color current = Theme.Resolve(GetColor(key), DefaultColor(key));
        Theme.ToHsv(current, out _pickH, out _pickS, out _pickV);
        if (_pickS < 0.0001)
        {
            // у серого/чёрного насыщенность нулевая — иначе ползунок «не двигается»
            _pickS = 1.0;
        }

        PickerPopup.PlacementTarget = target;
        PickerPopup.IsOpen = true;
        UpdatePickerVisuals(commit: false);
    }

    private void OnPickerOpened(object sender, EventArgs e) => UpdatePickerVisuals(commit: false);

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        SvArea.CaptureMouse();
        UpdateSvFrom(e.GetPosition(SvArea));
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSvFrom(e.GetPosition(SvArea));
        }
    }

    private void OnSvUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            SvArea.ReleaseMouseCapture();
        }
    }

    private void UpdateSvFrom(Point point)
    {
        double width = SvArea.Width > 0 ? SvArea.Width : 1;
        double height = SvArea.Height > 0 ? SvArea.Height : 1;
        _pickS = Math.Clamp(point.X / width, 0.0, 1.0);
        _pickV = Math.Clamp(1.0 - point.Y / height, 0.0, 1.0);
        UpdatePickerVisuals();
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        HueArea.CaptureMouse();
        UpdateHueFrom(e.GetPosition(HueArea));
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHueFrom(e.GetPosition(HueArea));
        }
    }

    private void OnHueUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            HueArea.ReleaseMouseCapture();
        }
    }

    private void UpdateHueFrom(Point point)
    {
        double width = HueArea.ActualWidth > 0 ? HueArea.ActualWidth : 1;
        _pickH = Math.Clamp(point.X / width, 0.0, 1.0) * 360.0;
        UpdatePickerVisuals();
    }

    /// <summary>Перерисовывает выборщик; при commit=true пишет цвет в настройки и превью.</summary>
    private void UpdatePickerVisuals(bool commit = true)
    {
        if (_pickerSyncing)
        {
            return;
        }

        _pickerSyncing = true;
        try
        {
            SvHue.Fill = new SolidColorBrush(Theme.FromHsv(_pickH, 1.0, 1.0));

            double sw = SvArea.Width > 0 ? SvArea.Width : 248;
            double sh = SvArea.Height > 0 ? SvArea.Height : 150;
            Canvas.SetLeft(SvThumb, _pickS * sw - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1.0 - _pickV) * sh - SvThumb.Height / 2);

            double hw = HueArea.ActualWidth > 0 ? HueArea.ActualWidth : 248;
            Canvas.SetLeft(HueThumb, _pickH / 360.0 * hw - HueThumb.Width / 2);
            Canvas.SetTop(HueThumb, (HueArea.ActualHeight - HueThumb.Height) / 2);

            Color picked = Theme.FromHsv(_pickH, _pickS, _pickV);
            PickerPreview.Background = new SolidColorBrush(picked);
            if (!PickerHex.IsKeyboardFocusWithin)
            {
                PickerHex.Text = Theme.ToHex(picked);
            }

            if (commit && _pickerKey is not null && !_loading)
            {
                string hex = Theme.ToHex(picked);
                SetColor(_pickerKey, hex);
                if (_colorBoxes.TryGetValue(_pickerKey, out TextBox? box) && !box.IsKeyboardFocusWithin)
                {
                    box.Text = hex;
                }

                ApplyColorPreview();
            }
        }
        finally
        {
            _pickerSyncing = false;
        }
    }

    private void CommitPickerHex()
    {
        if (_pickerKey is null)
        {
            return;
        }

        if (Theme.TryParse(PickerHex.Text, out Color color))
        {
            Theme.ToHsv(color, out _pickH, out _pickS, out _pickV);
            if (_pickS < 0.0001)
            {
                _pickS = 1.0;
            }

            UpdatePickerVisuals();
        }
        else
        {
            PickerHex.Text = Theme.ToHex(Theme.Resolve(GetColor(_pickerKey), DefaultColor(_pickerKey)));
        }
    }

    private void BuildPickerPresets()
    {
        if (PickerPresets is null)
        {
            return;
        }

        PickerPresets.Children.Clear();
        foreach (string hex in PickerPalette)
        {
            Color color = Theme.Resolve(hex, hex);
            var cell = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                BorderThickness = new Thickness(1)
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "LineStrongBrush");

            string captured = hex;
            cell.MouseLeftButtonUp += (_, _) => ApplyPickerColor(Theme.Resolve(captured, captured));
            PickerPresets.Children.Add(cell);
        }
    }

    private void ApplyPickerColor(Color color)
    {
        Theme.ToHsv(color, out _pickH, out _pickS, out _pickV);
        if (_pickS < 0.0001)
        {
            _pickS = 1.0;
        }

        UpdatePickerVisuals();
    }

    private void PickerSetDefault(object sender, RoutedEventArgs e)
    {
        if (_pickerKey is null)
        {
            return;
        }

        SetColor(_pickerKey, string.Empty);
        if (_colorBoxes.TryGetValue(_pickerKey, out TextBox? box))
        {
            box.Text = string.Empty;
        }

        Color fallback = Theme.Resolve(string.Empty, DefaultColor(_pickerKey));
        Theme.ToHsv(fallback, out _pickH, out _pickS, out _pickV);
        if (_pickS < 0.0001)
        {
            _pickS = 1.0;
        }

        UpdatePickerVisuals(commit: false);
        if (!_loading)
        {
            ApplyColorPreview();
        }
    }

    private void PickerDone(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = false;

    /// <summary>
    /// Самопроверка бета-эффекта «жидкое стекло» для <c>--selftest</c>: байткод шейдера загрузился,
    /// слой-стекло включился на всё окно, снят фон за окном и выключение всё вернуло назад.
    /// </summary>
    /// <remarks>
    /// Сам вывод шейдера здесь не проверяется: <see cref="RenderTargetBitmap"/> не выполняет
    /// <see cref="System.Windows.Media.Effects.ShaderEffect"/>, поэтому стекло проверяют глазами.
    /// </remarks>
    internal string SelfTestGlass()
    {
        if (_glassEffect is null)
        {
            return "effect=unavailable (байткод шейдера не загрузился)";
        }

        bool wasChecked = LiquidGlassBox.IsChecked == true;

        try
        {
            LiquidGlassBox.IsChecked = true;
            UpdateGlassLayer();

            // видимость слоя меняется только что — прогоняем разметку, чтобы узнать его реальный размер
            UpdateLayout();

            string backdrop = _glassBackdrop is null
                ? "none"
                : $"{_glassBackdrop.PixelWidth}x{_glassBackdrop.PixelHeight}";
            string result =
                $"effect=ok active={_glassActive} layer={GlassLayer.Visibility} size={GlassLayer.ActualWidth:0}x{GlassLayer.ActualHeight:0} " +
                $"refraction={_glassEffect.Refraction:0.00} blur={_glassEffect.Blur:0.0} backdrop={backdrop} " +
                $"input={(_glassEffect.Input is null ? "none" : "set")}";

            // ограничитель FPS преломления: пересчёт интервала захвата и даунскейла
            string savedFps = TagOf(GlassFpsBox, "Unlimited");
            try
            {
                GlassFpsBox.SelectedIndex = IndexOfTag(GlassFpsBox, "30");
                ResolveGlassCaptureTiming();
                string at30 = $"{_glassCaptureInterval.TotalMilliseconds:0.0}ms/{_glassCaptureScale:0.00}";
                GlassFpsBox.SelectedIndex = IndexOfTag(GlassFpsBox, "Unlimited");
                ResolveGlassCaptureTiming();
                string atMax = $"{_glassCaptureInterval.TotalMilliseconds:0.0}ms/{_glassCaptureScale:0.00}";
                result += $" fps(30={at30} unlimited={atMax})";
            }
            finally
            {
                GlassFpsBox.SelectedIndex = IndexOfTag(GlassFpsBox, savedFps);
                ResolveGlassCaptureTiming();
            }

            LiquidGlassBox.IsChecked = false;
            UpdateGlassLayer();
            result += $" off=({GlassLayer.Visibility}, capture={(_glassActive ? "on" : "off")})";

            return result;
        }
        catch (Exception ex)
        {
            return "effect FAILED: " + ex.Message;
        }
        finally
        {
            LiquidGlassBox.IsChecked = wasChecked;
            UpdateGlassLayer();
        }
    }

    internal string SelfTestColorPicker()
    {
        string savedAccent = _accentColor;
        try
        {
            OpenPicker("Accent", this);
            bool opened = PickerPopup.IsOpen;

            ApplyPickerColor(Theme.Resolve("#3366CC", "#3366CC"));
            string brush = Application.Current?.Resources["AccentBrush"] is SolidColorBrush s
                ? Theme.ToHex(s.Color)
                : "-";

            return $"open={opened} applied={_accentColor} accentBrush={brush}";
        }
        finally
        {
            _accentColor = savedAccent;
            PickerPopup.IsOpen = false;
            _controller.PreviewTheme(_controller.Settings);
        }
    }

    /// <summary>Пока попап открыт на этом ключе — подтягивает в него значение из строки.</summary>
    private void SyncPickerFromKey(string key)
    {
        if (_pickerKey != key || !PickerPopup.IsOpen)
        {
            return;
        }

        Color current = Theme.Resolve(GetColor(key), DefaultColor(key));
        Theme.ToHsv(current, out _pickH, out _pickS, out _pickV);
        if (_pickS < 0.0001)
        {
            _pickS = 1.0;
        }

        UpdatePickerVisuals(commit: false);
    }

    private void ResetColors(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = false;
        _accentColor = string.Empty;
        _baseColor = string.Empty;
        _textColor = string.Empty;
        RefreshColorsUi();

        if (!_loading)
        {
            ApplyColorPreview();
        }
    }

    private void ApplyColorPreview()
    {
        RefreshColorSwatches();
        _controller.PreviewTheme(CollectProbe());

        // при включённом стекле панели и тон берутся из темы — обновляем их под новый цвет
        if (_glassActive)
        {
            ApplyGlassPanels(true);
            if (_glassEffect is not null)
            {
                _glassEffect.Tint = BuildGlassTint();
            }
        }
    }

    /// <summary>Строит 10 слотов своих привязок.</summary>
    private void BuildBindingsUi()
    {
        BindingsGrid.Children.Clear();
        for (int i = 0; i < Inputs.MaxSlots; i++)
        {
            var button = new Button
            {
                Tag = i,
                Margin = i % 2 == 0 ? new Thickness(0, 3, 3, 3) : new Thickness(3, 3, 0, 3),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = FindResource("BaseButton") as Style
            };
            button.Click += OnBindSlotClick;
            button.PreviewMouseRightButtonUp += OnBindSlotClear;
            _bindSlots[i] = button;
            BindingsGrid.Children.Add(button);
        }
    }

    private void RefreshBindingsUi()
    {
        for (int i = 0; i < _bindSlots.Length; i++)
        {
            var button = _bindSlots[i];
            if (button is null)
            {
                continue;
            }

            if (i == _captureSlot)
            {
                button.Content = Loc.T("BindPressKey");
                button.ToolTip = Loc.T("BindCaptureEsc");
            }
            else
            {
                string token = i < _bindings.Count ? _bindings[i] : string.Empty;
                button.Content = DescribeBinding(token);
                button.ToolTip = Loc.T("BindClear");
            }
        }
    }

    private void OnBindSlotClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index })
        {
            return;
        }

        _captureSlot = index;
        RefreshBindingsUi();
    }

    /// <summary>ПКМ по слоту, когда он не в режиме захвата, очищает привязку.</summary>
    private void OnBindSlotClear(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: int index })
        {
            return;
        }

        e.Handled = true;
        EnsureBindingsSize();
        _bindings[index] = string.Empty;
        _captureSlot = -1;
        RefreshBindingsUi();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (_captureSlot < 0)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _captureSlot = -1;
            RefreshBindingsUi();
            return;
        }

        // Alt приходит как Key.System — настоящая клавиша лежит в SystemKey
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.None)
        {
            return;
        }

        SetBinding(_captureSlot, Inputs.KeyToken(key));
        _captureSlot = -1;
        RefreshBindingsUi();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        if (_captureSlot < 0)
        {
            return;
        }

        e.Handled = true;

        string? token = e.ChangedButton switch
        {
            MouseButton.Left => Inputs.MouseLeft,
            MouseButton.Right => Inputs.MouseRight,
            MouseButton.Middle => Inputs.MouseMiddle,
            MouseButton.XButton1 => Inputs.MouseX1,
            MouseButton.XButton2 => Inputs.MouseX2,
            _ => null
        };

        if (token is null)
        {
            return;
        }

        SetBinding(_captureSlot, token);
        _captureSlot = -1;
        RefreshBindingsUi();
    }

    private void SetBinding(int slot, string token)
    {
        EnsureBindingsSize();

        // один и тот же ввод не должен «нажимать» дважды — освобождаем прошлый слот
        int existing = _bindings.FindIndex(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _bindings[existing] = string.Empty;
        }

        if (slot >= 0 && slot < _bindings.Count)
        {
            _bindings[slot] = token;
        }
    }

    private void EnsureBindingsSize()
    {
        while (_bindings.Count < Inputs.MaxSlots)
        {
            _bindings.Add(string.Empty);
        }
    }

    private static string DescribeBinding(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Loc.T("BindSlotEmpty");
        }

        switch (token)
        {
            case Inputs.MouseLeft:
                return Loc.T("BindMouseLeft");
            case Inputs.MouseRight:
                return Loc.T("BindMouseRight");
            case Inputs.MouseMiddle:
                return Loc.T("BindMouseMiddle");
            case Inputs.MouseX1:
                return Loc.T("BindMouseX1");
            case Inputs.MouseX2:
                return Loc.T("BindMouseX2");
        }

        return Inputs.TryParseKey(token, out Key key) ? DescribeKey(key) : token;
    }

    private static string DescribeKey(Key key)
    {
        switch (key)
        {
            case >= Key.A and <= Key.Z:
                return key.ToString();
            case >= Key.D0 and <= Key.D9:
                return ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture);
            case >= Key.NumPad0 and <= Key.NumPad9:
                return "Num " + ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture);
            case >= Key.F1 and <= Key.F24:
                return key.ToString();
            case Key.LeftCtrl or Key.RightCtrl:
                return Loc.T("BindKeyCtrl");
            case Key.LeftAlt or Key.RightAlt:
                return Loc.T("BindKeyAlt");
            case Key.LeftShift or Key.RightShift:
                return Loc.T("BindKeyShift");
            case Key.LWin or Key.RWin:
                return Loc.T("BindKeyWin");
            case Key.Space:
                return Loc.T("BindKeySpace");
            case Key.Enter:
                return Loc.T("BindKeyEnter");
            case Key.Tab:
                return Loc.T("BindKeyTab");
            case Key.Escape:
                return Loc.T("BindKeyEscape");
            case Key.Back:
                return Loc.T("BindKeyBack");
            case Key.Delete:
                return Loc.T("BindKeyDelete");
            case Key.Insert:
                return Loc.T("BindKeyInsert");
            case Key.Home:
                return Loc.T("BindKeyHome");
            case Key.End:
                return Loc.T("BindKeyEnd");
            case Key.PageUp:
                return Loc.T("BindKeyPageUp");
            case Key.PageDown:
                return Loc.T("BindKeyPageDown");
            case Key.Up:
                return Loc.T("BindKeyUp");
            case Key.Down:
                return Loc.T("BindKeyDown");
            case Key.Left:
                return Loc.T("BindKeyLeft");
            case Key.Right:
                return Loc.T("BindKeyRight");
            case Key.CapsLock:
                return Loc.T("BindKeyCapsLock");
            case Key.NumLock:
                return Loc.T("BindKeyNumLock");
            case Key.Scroll:
                return Loc.T("BindKeyScrollLock");
            case Key.PrintScreen:
                return Loc.T("BindKeyPrintScreen");
            case Key.Pause:
                return Loc.T("BindKeyPause");
            case Key.Apps:
                return Loc.T("BindKeyMenu");
            case Key.Multiply:
                return Loc.T("BindKeyNumPadMultiply");
            case Key.Add:
                return Loc.T("BindKeyNumPadAdd");
            case Key.Subtract:
                return Loc.T("BindKeyNumPadSubtract");
            case Key.Decimal:
                return Loc.T("BindKeyNumPadDecimal");
            case Key.Divide:
                return Loc.T("BindKeyNumPadDivide");
            default:
                return key.ToString();
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
