using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace MimiTap;

/// <summary>
/// Простой движок локализации без внешних пакетов: JSON-словари лежат во встроенных
/// ресурсах (<c>Localization/Strings/&lt;код&gt;.json</c>), строки доступны по ключу.
/// Смена языка применяется на лету — разметка XAML подписана на индексатор словаря.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Поддерживаемые языки. Порядок — как в списке настроек.</summary>
    public static readonly string[] Supported = { "ru", "en", "es", "pt-BR", "uk", "pl" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, string> _fallback;
    private Dictionary<string, string> _map;
    private string _language = "en";

    private Loc()
    {
        _fallback = LoadDictionary("en");
        _map = _fallback;
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language => _language;

    /// <summary>Строка по ключу; если ключа нет — возвращается сам ключ.</summary>
    public string this[string key] => Translate(key);

    public static string T(string key) => Instance.Translate(key);

    public static string Format(string key, params object?[] args)
    {
        string template = Instance.Translate(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private string Translate(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out string? fallback) && !string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>Применяет язык (код, "auto" или пусто → язык системы).</summary>
    public static void SetLanguage(string? language) => Instance.Apply(language);

    private void Apply(string? language)
    {
        string code = Normalize(language);

        if (string.Equals(code, _language, StringComparison.OrdinalIgnoreCase)
            && !ReferenceEquals(_map, _fallback))
        {
            return;
        }

        _language = code;
        _map = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? _fallback : LoadDictionary(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Канонический код для хранения в настройках: "" означает «как в системе».</summary>
    public static string Canonical(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return Match(language) ?? string.Empty;
    }

    /// <summary>Код языка из настройки: пусто/"auto" → язык системы, неизвестный → английский.</summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return Detect();
        }

        return Match(language) ?? "en";
    }

    private static string Detect() =>
        Match(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        ?? Match(CultureInfo.CurrentUICulture.Name)
        ?? "en";

    private static string? Match(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string code = value.Trim();

        foreach (string supported in Supported)
        {
            if (string.Equals(supported, code, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        // pt / pt-PT / pt-BR → pt-BR, en-US → en и т. п.
        string two = code.Length >= 2 ? code[..2] : code;
        foreach (string supported in Supported)
        {
            if (supported.StartsWith(two, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return null;
    }

    private static Dictionary<string, string> LoadDictionary(string code)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var assembly = typeof(Loc).Assembly;
            string suffix = "." + code + ".json";
            string? name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            using Stream? stream = name is null ? null : assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return empty;
            }

            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? empty;
        }
        catch
        {
            return empty;
        }
    }
}

/// <summary>XAML-разметка <c>{loc:Tr КлючСтроки}</c> — привязка к словарю с живым обновлением.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
