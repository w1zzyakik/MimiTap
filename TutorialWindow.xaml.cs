using System;
using System.Windows;
using System.Windows.Input;

namespace MimiTap;

/// <summary>
/// Обучение при первом запуске: затемняет экран и показывает пошаговые выноски.
/// На шагах про Мими подсвечивает саму картинку, дальше — просто подсказки по центру.
/// </summary>
public partial class TutorialWindow : Window
{
    private static readonly (string Title, string Text, bool Highlight)[] Steps =
    {
        ("TutorialStep1Title", "TutorialStep1Text", true),
        ("TutorialStep2Title", "TutorialStep2Text", true),
        ("TutorialStep3Title", "TutorialStep3Text", false),
        ("TutorialStep4Title", "TutorialStep4Text", false),
        ("TutorialStep5Title", "TutorialStep5Text", false)
    };

    private readonly AppController _controller;
    private int _step;

    public TutorialWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        KeyDown += OnKeyDown;
        Loaded += (_, _) => Render();
        SizeChanged += (_, _) => PositionCallout();
    }

    private void Render()
    {
        (string title, string text, bool highlight) = Steps[_step];

        StepLabel.Text = $"{_step + 1} / {Steps.Length}";
        CalloutTitle.Text = Loc.T(title);
        CalloutText.Text = Loc.T(text);

        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = _step == Steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = Loc.T(_step == Steps.Length - 1 ? "TutorialDone" : "TutorialNext");
        Highlight.Visibility = highlight && _controller.IsOverlayVisible ? Visibility.Visible : Visibility.Collapsed;

        UpdateLayout();
        PositionCallout();
    }

    private void PositionCallout()
    {
        if (Callout is null)
        {
            return;
        }

        double cardW = Callout.ActualWidth > 0 ? Callout.ActualWidth : Callout.Width;
        double cardH = Callout.ActualHeight > 0 ? Callout.ActualHeight : 240;
        const double margin = 18;

        // Math.Clamp падает, если минимум больше максимума, поэтому считаем явно
        double maxX = Math.Max(margin, Width - cardW - margin);
        double maxY = Math.Max(margin, Height - cardH - margin);

        bool highlight = Steps[_step].Highlight && _controller.IsOverlayVisible;

        if (highlight)
        {
            Rect overlay = _controller.OverlayPosition();
            double localX = overlay.Left - Left;
            double localY = overlay.Top - Top;

            Highlight.Margin = new Thickness(localX - 10, localY - 10, 0, 0);
            Highlight.Width = overlay.Width + 20;
            Highlight.Height = overlay.Height + 20;

            double x = localX + overlay.Width / 2 - cardW / 2;
            double y = localY + overlay.Height + 20;
            if (y + cardH > Height - margin)
            {
                y = localY - cardH - 20;
            }

            Callout.Margin = new Thickness(Math.Clamp(x, margin, maxX), Math.Clamp(y, margin, maxY), 0, 0);
        }
        else
        {
            Callout.Margin = new Thickness((Width - cardW) / 2, (Height - cardH) / 2, 0, 0);
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step >= Steps.Length - 1)
        {
            Finish();
            return;
        }

        _step++;
        Render();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            return;
        }

        _step--;
        Render();
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Finish();
                break;
            case Key.Right:
            case Key.Enter:
            case Key.Space:
                OnNext(this, new RoutedEventArgs());
                break;
            case Key.Left:
                OnBack(this, new RoutedEventArgs());
                break;
        }
    }

    private void Finish() => Close();
}
