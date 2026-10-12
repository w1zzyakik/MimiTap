using System;
using System.Windows.Input;

namespace MimiTap;

/// <summary>
/// Физический ввод, который может «нажать» кнопку за Мими: кнопки мыши и клавиши.
/// Привязки хранятся строками (например, <c>MouseLeft</c> или <c>Key.Space</c>),
/// чтобы их было удобно писать в settings.json и сравнивать без таблиц.
/// </summary>
public static class Inputs
{
    /// <summary>Сколько слотов под свои привязки отводится в настройках.</summary>
    public const int MaxSlots = 10;

    public const string MouseLeft = "MouseLeft";
    public const string MouseRight = "MouseRight";
    public const string MouseMiddle = "MouseMiddle";
    public const string MouseX1 = "MouseX1";
    public const string MouseX2 = "MouseX2";

    private const string KeyPrefix = "Key.";

    // виртуальные коды для GetAsyncKeyState
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const int VkX1Button = 0x05;
    private const int VkX2Button = 0x06;

    public static string KeyToken(Key key) => KeyPrefix + key;

    public static bool IsKeyToken(string? token) =>
        token is not null && token.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public static bool TryParseKey(string token, out Key key)
    {
        key = Key.None;
        return IsKeyToken(token) && Enum.TryParse(token[KeyPrefix.Length..], out key);
    }

    /// <summary>Виртуальный код для передачи в <c>GetAsyncKeyState</c>; 0 — токен неизвестен.</summary>
    public static int VirtualKey(string token)
    {
        switch (token)
        {
            case MouseLeft:
                return VkLeftButton;
            case MouseRight:
                return VkRightButton;
            case MouseMiddle:
                return VkMiddleButton;
            case MouseX1:
                return VkX1Button;
            case MouseX2:
                return VkX2Button;
        }

        return TryParseKey(token, out Key key) ? KeyInterop.VirtualKeyFromKey(key) : 0;
    }

    /// <summary>Физически ли зажат сейчас этот ввод (страховка от потерянного отпускания).</summary>
    public static bool IsDown(string token)
    {
        int vk = VirtualKey(token);
        return vk != 0 && NativeMethods.IsVirtualKeyDown(vk);
    }
}
