using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MimiTapSetup;

public partial class MainWindow : Window
{
    private readonly InstallerOptions _options;
    private readonly bool _uninstall;
    private bool _busy;

    public MainWindow(InstallerOptions options, bool uninstall)
    {
        InitializeComponent();

        _options = options;
        _uninstall = uninstall;

        Closed += (_, _) =>
        {
            if (!_busy)
            {
                Application.Current.Shutdown();
            }
        };

        // мягкое появление
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        StartPulse();

        if (_uninstall)
        {
            SubtitleText.Text = "Удаление";
            PathText.Text = options.TargetDir;
            RemoveDataBox.Visibility = Visibility.Visible;
            AutoStartBox.Visibility = Visibility.Collapsed;
            DesktopBox.Visibility = Visibility.Collapsed;
            RunBox.Visibility = Visibility.Collapsed;
            BrowseButton.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Удалить";
            PrimaryButton.Style = (Style)FindResource("DangerButton");
            FooterHint.Text = "Программа будет удалена, ярлыки и автозапуск — сняты.";
            DoneTitle.Text = "MimiTap удалён";
        }
        else
        {
            PathText.Text = options.TargetDir;
            DesktopBox.IsChecked = options.DesktopShortcut;
            AutoStartBox.IsChecked = options.AutoStart;
            RunBox.IsChecked = options.RunAfterInstall;
            FooterHint.Text = "Установка не требует прав администратора.";
        }
    }

    private void StartPulse()
    {
        // линейный градиент без вращения во избежание проблем с Transform в одном файле
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Папка установки MimiTap",
            SelectedPath = Directory.Exists(_options.TargetDir) ? _options.TargetDir : InstallPaths.DefaultDir,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string selected = dialog.SelectedPath;
            if (!selected.EndsWith("MimiTap", StringComparison.OrdinalIgnoreCase))
            {
                selected = Path.Combine(selected, "MimiTap");
            }

            _options.TargetDir = selected;
            PathText.Text = selected;
        }
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            Close();
        }
    }

    private async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SecondaryButton.IsEnabled = false;
        PrimaryButton.IsEnabled = false;

        if (_uninstall)
        {
            _options.DeleteUserData = RemoveDataBox.IsChecked == true;
        }
        else
        {
            _options.AutoStart = AutoStartBox.IsChecked == true;
            _options.DesktopShortcut = DesktopBox.IsChecked == true;
            _options.RunAfterInstall = RunBox.IsChecked == true;
        }

        ShowProgress(_uninstall ? "Удаление…" : "Установка…");
        LogText.Text = "";

        Action<string> report = line => Dispatcher.Invoke(() => AppendLog(line));

        InstallerResult result = await Task.Run(() => _uninstall
            ? Installer.RunUninstall(_options, report)
            : Installer.RunInstall(_options, report));

        if (!result.Success)
        {
            ShowProgress("Ошибка");
            DoneTitle.Text = "Не получилось";
            DoneText.Text = result.Summary;
            PrimaryButton.Content = "Закрыть";
            PrimaryButton.IsEnabled = true;
            SecondaryButton.Visibility = Visibility.Collapsed;
            FooterHint.Text = "Подробности — в окне установки выше.";
            return;
        }

        if (!_uninstall && _options.RunAfterInstall)
        {
            Installer.StartApp(Path.Combine(_options.TargetDir, InstallPaths.AppExeName), "");
        }

        ShowDone(result.Summary);
    }

    private void AppendLog(string line)
    {
        LogText.Text += line + Environment.NewLine;
    }

    private void ShowProgress(string step)
    {
        IntroPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.Visibility = Visibility.Collapsed;
        FooterHint.Text = "Не закрывайте окно.";
        StepText.Text = step;
    }

    private void ShowDone(string summary)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = "Готово";
        PrimaryButton.IsEnabled = true;
        FooterHint.Text = summary;
        DoneText.Text = summary + Environment.NewLine +
                        (_uninstall ? "" : $"Настройки: {InstallPaths.DataDir}");
        _busy = false;
    }
}
