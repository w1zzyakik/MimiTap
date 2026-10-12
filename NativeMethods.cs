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

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;

    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MBUTTONUP = 0x0208;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_XBUTTONDOWN = 0x020B;
    internal const int WM_XBUTTONUP = 0x020C;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    // сообщения, после которых слоёное прозрачное окно стоит перерисовать:
    // смена разрешения/DPI, переключение режима композиции, смена системных настроек
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    internal const int WM_DPICHANGED = 0x02E0;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // окно на переднем плане и его процесс — нужны, чтобы не перетаскивать Мими,
    // когда открыт инструмент скриншота (иначе выделение области утаскивает картинку)
    internal const uint GW_CHILD = 5;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookExKeyboard(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>Зажат ли прямо сейчас ввод с этим виртуальным кодом.</summary>
    internal static bool IsVirtualKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Состояние левой кнопки мыши прямо сейчас. Нужен как страховка: если событие
    /// отпускания потерялось, картинка не должна навсегда зависнуть в состоянии 2.png.
    /// </summary>
    internal static bool IsLeftButtonDown() => IsVirtualKeyDown(0x01);

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
    RightUp,
    MiddleDown,
    MiddleUp,
    X1Down,
    X1Up,
    X2Down,
    X2Up,
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
                NativeMethods.WM_MOUSEMOVE => MouseHookEvent.Move,
                NativeMethods.WM_LBUTTONDOWN => MouseHookEvent.LeftDown,
                NativeMethods.WM_LBUTTONUP => MouseHookEvent.LeftUp,
                NativeMethods.WM_RBUTTONDOWN => MouseHookEvent.RightDown,
                NativeMethods.WM_RBUTTONUP => MouseHookEvent.RightUp,
                NativeMethods.WM_MBUTTONDOWN => MouseHookEvent.MiddleDown,
                NativeMethods.WM_MBUTTONUP => MouseHookEvent.MiddleUp,
                NativeMethods.WM_MOUSEWHEEL => MouseHookEvent.Wheel,
                _ => null
            };

            int x = 0;
            int y = 0;

            if (msg is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
            {
                // какая именно боковая кнопка, лежит в старшем слове mouseData
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int button = (data.mouseData >> 16) & 0xFFFF;
                bool second = button == 2;
                kind = msg == NativeMethods.WM_XBUTTONDOWN
                    ? (second ? MouseHookEvent.X2Down : MouseHookEvent.X1Down)
                    : (second ? MouseHookEvent.X2Up : MouseHookEvent.X1Up);
                x = data.dx;
                y = data.dy;
            }
            else if (kind.HasValue)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                x = data.dx;
                y = data.dy;
            }

            if (kind.HasValue)
            {
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

internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _enabled;

    /// <summary>Нажатие (true) или отпускание (false) и виртуальный код клавиши.</summary>
    public event Action<bool, int>? OnEvent;

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

    public GlobalKeyboardHook()
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
            _hook = NativeMethods.SetWindowsHookExKeyboard(
                NativeMethods.WH_KEYBOARD_LL,
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

            bool? down = msg switch
            {
                NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => true,
                NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => false,
                _ => null
            };

            if (down.HasValue)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                try
                {
                    OnEvent?.Invoke(down.Value, (int)data.vkCode);
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
