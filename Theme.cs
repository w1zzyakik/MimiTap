using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MimiTap;

/// <summary>
/// Цветовая тема приложения. Пользователь задаёт три цвета — акцентный, основной и
/// цвет текста; остальные оттенки (фон, карточки, поля, линии, кнопки) выводятся из
/// основного. Значения хранятся в настройках как «#RRGGBB», пустая строка — значение
/// по умолчанию из <c>App.xaml</c>.
///
/// Кисти лежат в ресурсах приложения и не заморожены, поэтому применение сводится к
/// смене <see cref="SolidColorBrush.Color"/> — уже открытые окна перекрашиваются на лету.
/// </summary>
public static class Theme
{
    public const string DefaultAccent = "#8B5CF6";
    public const string DefaultBase = "#0D0A13";
    public const string DefaultText = "#EDEAF5";

    private static Dictionary<string, Color>? _defaults;

    /// <summary>Применяет цвета из настроек к ресурсам приложения.</summary>
    public static void Apply(AppSettings settings)
    {
        bool hasAccent = TryParse(settings.AccentColor, out Color accent);
        bool hasBase = TryParse(settings.BaseColor, out Color baseColor);
        bool hasText = TryParse(settings.TextColor, out Color text);

        Dictionary<string, Color> defaults = Defaults;

        Color effectiveBase = hasBase ? baseColor : defaults["BgBrush"];
        Color effectiveText = hasText ? text : defaults["TextBrush"];

        // акцент и его производные (наведение светлее тёмного акцента и наоборот)
        Color accentColor = hasAccent ? accent : defaults["AccentBrush"];
        if (hasAccent)
        {
            bool light = Luminance(accent) > 0.62;
            Set("AccentBrush", accent);
            Set("AccentHoverBrush", Lighten(accent, light ? -0.16 : 0.16));
            Set("AccentPressedBrush", Lighten(accent, -0.12));
        }
        else
        {
            Set("AccentBrush", accentColor);
            Set("AccentHoverBrush", defaults["AccentHoverBrush"]);
            Set("AccentPressedBrush", defaults["AccentPressedBrush"]);
        }

        // основной цвет и выведенные из него оттенки
        if (hasBase)
        {
            Set("BgBrush", effectiveBase);
            Set("FieldBrush", Lighten(effectiveBase, 0.018));
            Set("CardBrush", Lighten(effectiveBase, 0.035));
            Set("CardHoverBrush", Lighten(effectiveBase, 0.058));
            Set("ElevatedPressedBrush", Lighten(effectiveBase, 0.055));
            Set("NavActiveBrush", Lighten(effectiveBase, 0.080));
            Set("ElevatedBrush", Lighten(effectiveBase, 0.096));
            Set("ElevatedHoverBrush", Lighten(effectiveBase, 0.140));
            Set("LineBrush", Lighten(effectiveBase, 0.118));
            Set("LineStrongBrush", Lighten(effectiveBase, 0.238));
            Set("KnobBrush", Lighten(effectiveBase, 0.688));
        }
        else
        {
            foreach (string key in BaseKeys)
            {
                Set(key, defaults[key]);
            }
        }

        // текст и приглушённый текст
        Set("TextBrush", effectiveText);
        Set("MutedBrush", hasBase || hasText
            ? Mix(effectiveBase, effectiveText, 0.60)
            : defaults["MutedBrush"]);
    }

    /// <summary>Проверяет строку цвета; пустое значение считается валидным («по умолчанию»).</summary>
    public static bool IsValid(string? text) =>
        string.IsNullOrWhiteSpace(text) || TryParse(text, out _);

    /// <summary>Приводит цвет к виду «#RRGGBB»; пустое или неверное значение — пустая строка.</summary>
    public static string Normalize(string? text) =>
        TryParse(text, out Color color) ? ToHex(color) : string.Empty;

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Разбирает «#RGB», «#RRGGBB» (решётка необязательна).</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6 ||
            !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint raw))
        {
            return false;
        }

        color = Color.FromRgb((byte)(raw >> 16), (byte)(raw >> 8), (byte)raw);
        return true;
    }

    /// <summary>Цвет из настроек или значение по умолчанию, если строка пустая/неверная.</summary>
    public static Color Resolve(string? text, string fallbackHex) =>
        TryParse(text, out Color color)
            ? color
            : TryParse(fallbackHex, out Color fallback) ? fallback : Colors.MediumPurple;

    private static readonly string[] BaseKeys =
    {
        "BgBrush", "FieldBrush", "CardBrush", "CardHoverBrush", "ElevatedPressedBrush",
        "NavActiveBrush", "ElevatedBrush", "ElevatedHoverBrush", "LineBrush", "LineStrongBrush",
        "KnobBrush"
    };

    private static Dictionary<string, Color> Defaults
    {
        get
        {
            if (_defaults is not null)
            {
                return _defaults;
            }

            var map = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (string key in AllKeys)
            {
                map[key] = ReadBrushColor(key) ?? Colors.Transparent;
            }

            _defaults = map;
            return map;
        }
    }

    private static readonly string[] AllKeys =
    {
        "BgBrush", "FieldBrush", "CardBrush", "CardHoverBrush", "ElevatedPressedBrush",
        "NavActiveBrush", "ElevatedBrush", "ElevatedHoverBrush", "LineBrush", "LineStrongBrush",
        "KnobBrush", "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "TextBrush", "MutedBrush"
    };

    private static Color? ReadBrushColor(string key) =>
        Application.Current?.Resources[key] is SolidColorBrush brush ? brush.Color : null;

    private static void Set(string key, Color color)
    {
        ResourceDictionary? resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        if (resources[key] is SolidColorBrush existing)
        {
            if (!existing.IsFrozen)
            {
                existing.Color = color;
                return;
            }
        }

        // запасной путь: кисть заморожена или её нет — подменяем ресурс целиком
        var replacement = new SolidColorBrush(color);
        replacement.Freeze();
        resources[key] = replacement;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromRgb(
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);

    /// <summary>Меняет светлоту в HSL: больше нуля — светлее, меньше — темнее.</summary>
    private static Color Lighten(Color color, double amount)
    {
        ToHsl(color, out double h, out double s, out double l);
        return FromHsl(h, s, Math.Clamp(l + amount, 0.0, 1.0));
    }

    private static void ToHsl(Color color, out double h, out double s, out double l)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta < 1e-9)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (max == r)
        {
            h = (g - b) / delta + (g < b ? 6.0 : 0.0);
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2.0;
        }
        else
        {
            h = (r - g) / delta + 4.0;
        }

        h *= 60.0;
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double hp = ((h % 360.0) + 360.0) % 360.0 / 60.0;
        double x = c * (1.0 - Math.Abs(hp % 2.0 - 1.0));
        double m = l - c / 2.0;

        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);

    /// <summary>RGB → HSV. <paramref name="h"/> в градусах (0–360), s и v — 0–1.</summary>
    public static void ToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta < 1e-9)
        {
            h = 0;
            return;
        }

        if (max == r)
        {
            h = ((g - b) / delta) % 6.0;
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2.0;
        }
        else
        {
            h = (r - g) / delta + 4.0;
        }

        h *= 60.0;
        if (h < 0)
        {
            h += 360.0;
        }
    }

    /// <summary>HSV → RGB.</summary>
    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        double c = v * s;
        double hp = h / 60.0;
        double x = c * (1.0 - Math.Abs(hp % 2.0 - 1.0));
        double m = v - c;

        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }
}
