// LiquidGlass.fx — эффект «жидкое стекло» для окна MimiTap.
//
// Вход (sampler s0) — снимок рабочего стола ПОД окном: окно исключается из захвата
// (SetWindowDisplayAffinity / WDA_EXCLUDEFROMCAPTURE), поэтому в снимок попадает именно
// то, что находится за ним. Шейдер размывает, преломляет и тонирует этот фон, добавляет
// блик у курсора и подсветку кромки — так всё меню выглядит как жидкое стекло.
//
// Компиляция (fxc из Windows SDK):
//   fxc /T ps_3_0 /E main /Fo Shaders\LiquidGlass.ps Shaders\LiquidGlass.fx
//
// Регистры обязаны совпадать с LiquidGlassEffect.cs.
sampler2D Input : register(s0);

float2 Resolution : register(c0);  // размер элемента в пикселях
float  Refraction : register(c1);  // сила преломления
float  Blur       : register(c2);  // радиус размытия фона в пикселях
float4 Tint       : register(c3);  // тон стекла (rgb + alpha-сила подмеса)
float2 Highlight  : register(c4);  // позиция курсора в UV (0..1)
float  Time       : register(c5);  // время: лёгкое «колыхание» поверхности
float  Specular   : register(c6);  // сила блика у курсора
float  Edge       : register(c7);  // сила подсветки кромки окна

// псевдослучайный шум — «матовость» стекла
float hash21(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 texel = 1.0 / Resolution;
    float aspect = Resolution.x / max(1.0, Resolution.y);

    // ── «колыхание» поверхности: несколько несоизмеримых волн дают живой сдвиг ──
    float2 wob;
    wob.x = sin(uv.y * 8.5 + Time * 0.75)
          + 0.55 * sin(uv.y * 17.0 - Time * 0.47)
          + 0.35 * sin((uv.x + uv.y) * 6.0 + Time * 0.33);
    wob.y = cos(uv.x * 7.5 - Time * 0.68)
          + 0.55 * cos(uv.x * 15.5 + Time * 0.41)
          + 0.35 * cos((uv.x - uv.y) * 6.5 - Time * 0.29);

    // ── линза у курсора: под «каплей» фон слегка наезжает ──
    float2 hd = (uv - Highlight) * float2(aspect, 1.0);
    float hlen = length(hd);
    float2 hdir = hlen > 1e-5 ? hd / hlen : float2(0.0, 0.0);
    float hfall = exp(-hlen * hlen * 24.0);

    // ── стекло толще у кромок: лёгкое «закругление» по краям окна ──
    float2 ec = uv - 0.5;
    float2 edgeVec = ec * 2.0;
    float edgeMag = pow(saturate(length(edgeVec)), 3.0);

    float2 offset = wob * (0.0026 * Refraction);
    offset += hdir * (hfall * 0.030 * Refraction);
    offset += edgeVec * (edgeMag * 0.009 * Refraction);

    float2 baseUv = uv + offset;

    // ── размытие: круглый диск, спираль золотого угла (12 отсчётов) ──
    float radiusPx = max(1.0, Blur);
    float3 frosted = 0.0;
    float wsum = 0.0;
    [unroll]
    for (int i = 0; i < 12; i++)
    {
        float fi = (float)i;
        float a = fi * 2.39996323;
        float rr = sqrt((fi + 0.5) / 12.0);
        float2 sp = float2(cos(a), sin(a)) * (rr * radiusPx) * texel;
        float w = 1.0 - rr * 0.55;
        frosted += tex2D(Input, baseUv + sp).rgb * w;
        wsum += w;
    }
    frosted /= wsum;

    // ── хроматическая аберрация: каналы чуть расходятся к краю ──
    float2 ca = float2(0.0026, 0.0011) * Refraction * (0.4 + edgeMag);
    float3 sharp;
    sharp.r = tex2D(Input, baseUv + ca).r;
    sharp.g = tex2D(Input, baseUv).g;
    sharp.b = tex2D(Input, baseUv - ca).b;
    float3 col = lerp(frosted, sharp, 0.30);

    // ── насыщенность, тон, лёгкое затемнение к краям ──
    float lum = dot(col, float3(0.299, 0.587, 0.114));
    col = saturate(lerp(col, lum.xxx, -0.18));
    col = lerp(col, Tint.rgb, Tint.a);

    float2 q = abs(uv - 0.5) * 2.0;
    float vig = 1.0 - smoothstep(0.45, 1.15, length(q));
    col *= lerp(0.70, 1.0, vig);

    // ── блик у курсора ──
    col += hfall * Specular;

    // ── подсветка кромки: верх/лево ярче (свет сверху), низ/право мягче ──
    float2 edgeDist = min(uv, 1.0 - uv);
    float rimD = min(edgeDist.x, edgeDist.y);
    float rim = 1.0 - smoothstep(0.0, 0.008, rimD);
    float side = saturate(1.0 - (uv.x + uv.y) * 0.5);
    col += rim * Edge * (0.35 + 0.65 * side);

    // тонкая тёмная линия у самой кромки — «граница стекла»
    float border = 1.0 - smoothstep(0.0, 0.0022, rimD);
    col *= 1.0 - border * 0.22;

    // ── зерно ──
    col += (hash21(uv * Resolution + Time) - 0.5) * 0.014;

    return float4(saturate(col), 1.0);
}
