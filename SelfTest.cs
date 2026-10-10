using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MimiTap;

public static class SelfTest
{
    public static bool Requested()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async void Run(AppController controller)
    {
        var report = new StringBuilder();
        report.AppendLine("MimiTap self test");
        report.AppendLine($"utc: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

        try
        {
            RunChecks(controller, report);
        }
        catch (Exception ex)
        {
            report.AppendLine("UNEXPECTED: " + ex);
        }

        SettingsStore.WriteTextFile(Path.Combine(SettingsStore.DataDir, "selftest.log"), report.ToString());
        await Task.Delay(400);
        Application.Current.Shutdown();
    }

    private static void RunChecks(AppController controller, StringBuilder report)
    {
        var settings = controller.Settings;

        report.AppendLine($"idle image: {settings.IdleImagePath} exists={File.Exists(settings.IdleImagePath)}");
        report.AppendLine($"click image: {settings.ClickImagePath} exists={File.Exists(settings.ClickImagePath)}");
        report.AppendLine($"data dir: {SettingsStore.DataDir}");

        CheckAssets(report);
        CheckOverlay(controller, report);
        CheckHoldBehavior(controller, report);
        CheckPositionSave(controller, report);
        CheckSettingsWindow(controller, report);
        CheckSound(controller, report);

        report.AppendLine($"autostart registered: {AutoStart.IsEnabled()}");
        report.AppendLine($"mouse hook enabled: {settings.MouseHookEnabled}");
        report.AppendLine($"hit test mode: {settings.HitTestMode}");
    }

    private static void CheckAssets(StringBuilder report)
    {
        foreach (string path in new[] { SettingsStore.DefaultIdleImage, SettingsStore.DefaultClickImage, SettingsStore.DefaultIcon })
        {
            var info = new FileInfo(path);
            report.AppendLine($"asset {Path.GetFileName(path)}: exists={info.Exists} size={(info.Exists ? info.Length : 0)}");
        }
    }

    private static void CheckOverlay(AppController controller, StringBuilder report)
    {
        var overlay = controller.Overlay;
        if (overlay is null)
        {
            report.AppendLine("overlay: MISSING");
            return;
        }

        report.AppendLine($"overlay: visible={overlay.IsVisible} left={overlay.Left:0} top={overlay.Top:0} w={overlay.Width:0} h={overlay.Height:0}");
        report.AppendLine($"hit test mode: {controller.Settings.HitTestMode} padding={controller.Settings.ClickZonePadding} hook={controller.HookInstalled}");

        var zone = overlay.ClickZoneRect();
        report.AppendLine($"click zone: x={zone.Left:0}..{zone.Right:0} y={zone.Top:0}..{zone.Bottom:0} ({zone.Width:0}x{zone.Height:0})");

        double inset = 6;
        double pad = Math.Max(0, controller.Settings.ClickZonePadding);

        report.AppendLine($"hit centre            : {overlay.HitTest(Cx(zone), Cy(zone))}  {overlay.LastHitTestInfo}");
        report.AppendLine($"hit top-left corner   : {overlay.HitTest(zone.Left + inset, zone.Top + inset)}  {overlay.LastHitTestInfo}");
        report.AppendLine($"hit bottom-right corner: {overlay.HitTest(zone.Right - inset, zone.Bottom - inset)}  {overlay.LastHitTestInfo}");
        report.AppendLine($"hit far outside       : {overlay.HitTest(-4000, -4000)}  {overlay.LastHitTestInfo}");

        if (pad > 0)
        {
            double ring = pad / 2;
            report.AppendLine($"hit in ring (air)     : {overlay.HitTest(zone.Left + ring, zone.Top + ring)}  {overlay.LastHitTestInfo}");
            report.AppendLine($"hit beyond ring       : {overlay.HitTest(zone.Left - ring - 10, zone.Top - ring - 10)}  {overlay.LastHitTestInfo}");
        }

        var windowMode = controller.Settings.Clone();
        windowMode.HitTestMode = "Window";
        windowMode.ClickZonePadding = 0;
        controller.PreviewSettings(windowMode);
        report.AppendLine($"window mode, no padding, corner inside: {overlay.HitTest(zone.Left + pad + 5, zone.Top + pad + 5)}  {overlay.LastHitTestInfo}");
        report.AppendLine($"window mode, no padding, centre      : {overlay.HitTest(Cx(zone), Cy(zone))}  {overlay.LastHitTestInfo}");

        var imageMode = controller.Settings.Clone();
        imageMode.HitTestMode = "Image";
        imageMode.ClickZonePadding = 0;
        controller.PreviewSettings(imageMode);
        report.AppendLine($"image mode, no padding, corner        : {overlay.HitTest(zone.Left + pad + 5, zone.Top + pad + 5)}  {overlay.LastHitTestInfo}");

        var screenMode = controller.Settings.Clone();
        screenMode.HitTestMode = "Screen";
        screenMode.ClickZonePadding = 0;
        controller.PreviewSettings(screenMode);
        report.AppendLine($"screen mode, far from picture         : {overlay.HitTest(-4000, -4000)}  mode={overlay.HitTestMode}");
        report.AppendLine($"screen mode, zone still works         : {overlay.HitTest(zone.Left + 5, zone.Top + 5)}  mode={overlay.HitTestMode}");
        report.AppendLine($"zone test while screen mode (drag)    : {overlay.HitTestZone(-4000, -4000)} / {overlay.HitTestZone(Cx(zone), Cy(zone))}");

        controller.PreviewSettings(controller.Settings);
    }

    private static void CheckHoldBehavior(AppController controller, StringBuilder report)
    {
        var overlay = controller.Overlay;
        if (overlay is null)
        {
            report.AppendLine("hold behaviour: overlay MISSING");
            return;
        }

        var original = controller.Settings.Clone();
        var hold = controller.Settings.Clone();
        hold.ClickBehavior = nameof(ClickMode.Hold);
        controller.PreviewSettings(hold);

        try
        {
            report.AppendLine($"click mode: {controller.Settings.ClickBehavior} -> {hold.ClickBehavior}");
            overlay.SetPressed(false);
            report.AppendLine($"idle shows             : {overlay.CurrentImageName} pressed={overlay.IsPressed}");

            overlay.SetPressed(true);
            report.AppendLine($"on press shows         : {overlay.CurrentImageName} pressed={overlay.IsPressed}");

            // держим 400 мс; поток UI занят, поэтому таймер-страховка не сработает
            Thread.Sleep(400);
            report.AppendLine($"still held after 400ms: {overlay.CurrentImageName} pressed={overlay.IsPressed}");

            overlay.SetPressed(false);
            report.AppendLine($"on release shows       : {overlay.CurrentImageName} pressed={overlay.IsPressed} heldMs={overlay.LastHoldMs} (ok: {overlay.LastHoldMs >= 350})");

            // проверка страховки: если событие отпускания потерялось, картинка
            // не должна остаться в 2.png
            overlay.SetPressed(true);
            PumpDispatcher(overlay);
            report.AppendLine($"safety release (no LKM): {overlay.CurrentImageName} pressed={overlay.IsPressed} (ok: {!overlay.IsPressed})");
        }
        finally
        {
            overlay.SetPressed(false);
            controller.PreviewSettings(original);
        }
    }

    private static void PumpDispatcher(DispatcherObject target)
    {
        for (int i = 0; i < 20; i++)
        {
            target.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }

    private static int Cx(Rect rect) => (int)(rect.Left + rect.Width / 2);
    private static int Cy(Rect rect) => (int)(rect.Top + rect.Height / 2);

    private static void CheckPositionSave(AppController controller, StringBuilder report)
    {
        var original = controller.Settings.Clone();

        try
        {
            var next = controller.Settings.Clone();
            next.Anchor = nameof(AnchorMode.Free);
            next.Screen = nameof(ScreenMode.Primary);
            next.PositionX = 321;
            next.PositionY = 123;
            next.HasSavedPosition = true;

            controller.ApplySettings(next);

            var reloaded = SettingsStore.Load();
            report.AppendLine(
                $"position save: stored=({Format(reloaded.PositionX)},{Format(reloaded.PositionY)}) " +
                $"hasSaved={reloaded.HasSavedPosition} anchor={reloaded.Anchor}");

            var bytes = File.ReadAllBytes(SettingsStore.SettingsPath);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            report.AppendLine($"settings.json: bom={bom} bytes={bytes.Length}");

            var overlay = controller.Overlay;
            if (overlay is not null)
            {
                report.AppendLine($"overlay applied position: left={overlay.Left:0} top={overlay.Top:0} expected=(321,123)");
            }

            controller.SaveOverlayPosition();
            report.AppendLine($"SaveOverlayPosition ok: ({Format(controller.Settings.PositionX)},{Format(controller.Settings.PositionY)})");
            controller.CenterOverlay();
            report.AppendLine($"CenterOverlay ok: left={controller.Overlay?.Left:0} top={controller.Overlay?.Top:0}");

            var centered = controller.Settings.Clone();
            centered.Anchor = nameof(AnchorMode.Center);
            centered.HasSavedPosition = false;
            controller.ApplySettings(centered);
            var afterAnchor = SettingsStore.Load();
            report.AppendLine($"anchor save ok: anchor={afterAnchor.Anchor} hasSaved={afterAnchor.HasSavedPosition} jsonValid=true");
        }
        catch (Exception ex)
        {
            report.AppendLine("position save FAILED: " + ex.Message);
        }
        finally
        {
            controller.ApplySettings(original);
        }
    }

    private static void CheckSettingsWindow(AppController controller, StringBuilder report)
    {
        var window = controller.SettingsWindow;

        try
        {
            window.LoadFrom(controller.Settings);
            window.Show();
            report.AppendLine($"settings window: shown={window.IsVisible} size={window.Width:0}x{window.Height:0}");
            window.Hide();
            report.AppendLine($"settings window hide-to-tray: hidden={!window.IsVisible} app still running=true");
        }
        catch (Exception ex)
        {
            report.AppendLine("settings window FAILED: " + ex);
        }
    }

    private static void CheckSound(AppController controller, StringBuilder report)
    {
        var settings = controller.Settings;

        if (string.IsNullOrWhiteSpace(settings.SoundPath) || !File.Exists(settings.SoundPath))
        {
            report.AppendLine("sound: not configured");
            return;
        }

        var header = SoundService.TryGetDuration(settings.SoundPath);
        report.AppendLine($"sound file: {Path.GetFileName(settings.SoundPath)} header={(header is null ? "unknown" : header.Value.ToString("0.000", CultureInfo.InvariantCulture) + " s")}");
        report.AppendLine($"sound limit: {settings.SoundMaxSeconds.ToString("0.000", CultureInfo.InvariantCulture)} s");
        controller.TestSound(settings);
        report.AppendLine("sound playback requested");
    }

    private static string Format(double? value) =>
        value is null ? "null" : value.Value.ToString("0", CultureInfo.InvariantCulture);
}