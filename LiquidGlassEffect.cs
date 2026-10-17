using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MimiTap;

/// <summary>
/// Эффект «жидкое стекло» (бета) для меню настроек: шейдер превращает снятый фон ЗА окном
/// в размытое, преломляющее и тонированное стекло с бликом у курсора и подсветкой кромки.
/// Байткод — <c>Shaders\LiquidGlass.ps</c> (ps_3_0, компилируется fxc из Windows SDK,
/// исходник — <c>Shaders\LiquidGlass.fx</c>).
///
/// Регистры констант обязаны совпадать с <c>register(c0…c7)</c> из шейдера:
/// Resolution → c0, Refraction → c1, Blur → c2, Tint → c3,
/// Highlight (float2) → c4, Time → c5, Specular → c6, Edge → c7.
/// Если байткод не загрузился — конструктор бросает исключение, вызывающая сторона отключает
/// функцию (пустой PixelShader в рендере роняет WPF, поэтому заглушка недопустима).
/// </summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    private static PixelShader? _shader;

    /// <summary>Фон за окном — снимок рабочего стола (sampler s0 в шейдере).</summary>
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(LiquidGlassEffect), 0);

    /// <summary>Размер слоя в пикселях — нужен шейдеру для перевода UV в пиксели.</summary>
    public static readonly DependencyProperty ResolutionProperty =
        DependencyProperty.Register(
            "Resolution", typeof(Point), typeof(LiquidGlassEffect),
            new PropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(0)));

    /// <summary>Сила преломления: насколько фон «плывёт» под стеклом.</summary>
    public static readonly DependencyProperty RefractionProperty =
        DependencyProperty.Register(
            "Refraction", typeof(double), typeof(LiquidGlassEffect),
            new PropertyMetadata(1.0, PixelShaderConstantCallback(1)));

    /// <summary>Радиус размытия фона в пикселях.</summary>
    public static readonly DependencyProperty BlurProperty =
        DependencyProperty.Register(
            "Blur", typeof(double), typeof(LiquidGlassEffect),
            new PropertyMetadata(10.0, PixelShaderConstantCallback(2)));

    /// <summary>Тон стекла: RGB — цвет, альфа — сила подмеса.</summary>
    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(
            "Tint", typeof(Color), typeof(LiquidGlassEffect),
            new PropertyMetadata(Colors.Transparent, PixelShaderConstantCallback(3)));

    /// <summary>Позиция курсора в UV (0..1) — центр блика.</summary>
    public static readonly DependencyProperty HighlightProperty =
        DependencyProperty.Register(
            "Highlight", typeof(Point), typeof(LiquidGlassEffect),
            new PropertyMetadata(new Point(0.5, 0.35), PixelShaderConstantCallback(4)));

    /// <summary>Время в секундах — анимирует лёгкое колыхание поверхности.</summary>
    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(
            "Time", typeof(double), typeof(LiquidGlassEffect),
            new PropertyMetadata(0.0, PixelShaderConstantCallback(5)));

    /// <summary>Яркость блика у курсора.</summary>
    public static readonly DependencyProperty SpecularProperty =
        DependencyProperty.Register(
            "Specular", typeof(double), typeof(LiquidGlassEffect),
            new PropertyMetadata(0.08, PixelShaderConstantCallback(6)));

    /// <summary>Яркость подсветки кромки окна.</summary>
    public static readonly DependencyProperty EdgeProperty =
        DependencyProperty.Register(
            "Edge", typeof(double), typeof(LiquidGlassEffect),
            new PropertyMetadata(0.25, PixelShaderConstantCallback(7)));

    public LiquidGlassEffect()
    {
        PixelShader = LoadShader();

        // константы нужно отправить в шейдер сразу: без UpdateShaderValue они остаются нулями,
        // пока свойство не изменят (а значения по умолчанию менять никто не будет)
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ResolutionProperty);
        UpdateShaderValue(RefractionProperty);
        UpdateShaderValue(BlurProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(HighlightProperty);
        UpdateShaderValue(TimeProperty);
        UpdateShaderValue(SpecularProperty);
        UpdateShaderValue(EdgeProperty);
    }

    public Brush? Input
    {
        get => (Brush?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public Point Resolution
    {
        get => (Point)GetValue(ResolutionProperty);
        set => SetValue(ResolutionProperty, value);
    }

    public double Refraction
    {
        get => (double)GetValue(RefractionProperty);
        set => SetValue(RefractionProperty, value);
    }

    public double Blur
    {
        get => (double)GetValue(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public Point Highlight
    {
        get => (Point)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    public double Time
    {
        get => (double)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public double Specular
    {
        get => (double)GetValue(SpecularProperty);
        set => SetValue(SpecularProperty, value);
    }

    public double Edge
    {
        get => (double)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    private static PixelShader LoadShader()
    {
        if (_shader is not null)
        {
            return _shader;
        }

        // байткод лежит в ресурсах сборки (Shaders\LiquidGlass.ps) — WPF сам его подгрузит
        _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/LiquidGlass.ps", UriKind.Absolute)
        };
        return _shader;
    }
}
