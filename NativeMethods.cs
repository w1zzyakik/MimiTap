using System;
using System.Runtime.InteropServices;

namespace MimiTap;

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public int dx;
    public int dy;
    public int mouseData;
    public int flags;
    public int time;
    public IntPtr dwExtraInfo;
}

internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int WH_MOUSE_LL = 14;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MOUSEWHEEL = 0x020A;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

[DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
private static extern short GetAsyncKeyState(int vKey);

/// <summary>
/// Состояние левой кнопки мыши прямо сейчас. Нужен как страховка: если событие
/// отпускания потерялось, картинка не должна навсегда зависнуть в состоянии 2.png.
/// </summary>
internal static bool IsLeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;


    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    internal static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }
        else
        {
            SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
        }
    }
}

internal enum MouseHookEvent
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    MiddleDown,
    Wheel
}

internal sealed class GlobalMouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _enabled;

    public event Action<MouseHookEvent, int, int>? OnEvent;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
            {
                return;
            }

            if (value)
            {
                Install();
            }
            else
            {
                Uninstall();
            }
        }
    }

    public GlobalMouseHook()
    {
        _proc = HookProc;
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            using var module = process.MainModule;
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _proc,
                NativeMethods.GetModuleHandle(module?.ModuleName),
                0);
            _enabled = _hook != IntPtr.Zero;
        }
        catch
        {
            _enabled = false;
        }
    }

    private void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _enabled = false;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            MouseHookEvent? kind = msg switch
            {
                NativeMethods.WM_LBUTTONDOWN => MouseHookEvent.LeftDown,
                NativeMethods.WM_LBUTTONUP => MouseHookEvent.LeftUp,
                NativeMethods.WM_RBUTTONDOWN => MouseHookEvent.RightDown,
                NativeMethods.WM_MBUTTONDOWN => MouseHookEvent.MiddleDown,
                NativeMethods.WM_MOUSEMOVE => MouseHookEvent.Move,
                _ => null
            };

            if (kind.HasValue)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int x = data.dx;
                int y = data.dy;

                try
                {
                    OnEvent?.Invoke(kind.Value, x, y);
                }
                catch
                {
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.KeepAlive(_proc);
    }
}