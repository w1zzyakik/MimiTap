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

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        BuildBindingsUi();
        TitleIcon.Source = LoadPreviewImage(SettingsStore.DefaultIcon, null);
        VersionText.Text = FormatVersion();
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
        ApplyStatusText();
    }

    public void LoadFrom(AppSettings settings)
    {
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
        ApplyStatusText();
        _loading = false;
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
                StatusText.Foreground = FindResource("AccentHoverBrush") as Brush;
                break;
            case StatusKind.Reset:
                StatusText.Text = Loc.T("StatusReset");
                StatusText.Foreground = FindResource("AccentHoverBrush") as Brush;
                break;
            case StatusKind.Error:
                StatusText.Text = Loc.Format("StatusSaveFailed", _statusError);
                StatusText.Foreground = FindResource("DangerBrush") as Brush;
                break;
            default:
                StatusText.Text = Loc.T("FooterStatus");
                StatusText.Foreground = FindResource("MutedBrush") as Brush;
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
            Foreground = FindResource("MutedBrush") as Brush,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(preset.Name) ? Visibility.Visible : Visibility.Collapsed
        };
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
