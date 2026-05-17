using System.Runtime.InteropServices;

namespace DotNetAgents.ComputerUse.Windows;

internal static partial class WindowsInputNative
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int WHEEL_DELTA = 120;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public static void MouseClick(int x, int y, bool rightButton)
    {
        (int nx, int ny) = ToNormalized(x, y);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE);
        var down = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        var up = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
        SendMouseInput(nx, ny, 0, down);
        SendMouseInput(nx, ny, 0, up);
    }

    public static void MouseDoubleClick(int x, int y)
    {
        (int nx, int ny) = ToNormalized(x, y);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_LEFTDOWN);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_LEFTUP);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_LEFTDOWN);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_LEFTUP);
    }

    public static void MouseScroll(int x, int y, bool vertical, int delta)
    {
        (int nx, int ny) = ToNormalized(x, y);
        SendMouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE);
        int amount = Math.Clamp(delta * WHEEL_DELTA, -32768, 32767);
        SendMouseInput(nx, ny, amount, vertical ? MOUSEEVENTF_WHEEL : MOUSEEVENTF_HWHEEL);
    }

    public static void MouseDrag(int fromX, int fromY, int toX, int toY)
    {
        (int nfx, int nfy) = ToNormalized(fromX, fromY);
        (int ntx, int nty) = ToNormalized(toX, toY);
        SendMouseInput(nfx, nfy, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE);
        SendMouseInput(nfx, nfy, 0, MOUSEEVENTF_LEFTDOWN);
        SendMouseInput(ntx, nty, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE);
        SendMouseInput(ntx, nty, 0, MOUSEEVENTF_LEFTUP);
    }

    private static (int nx, int ny) ToNormalized(int x, int y)
    {
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        int nx = (int)((x * 65535.0) / Math.Max(1, screenW));
        int ny = (int)((y * 65535.0) / Math.Max(1, screenH));
        return (nx, ny);
    }

    private static void SendMouseInput(int x, int y, int mouseData, uint dwFlags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    mouseData = mouseData,
                    dwFlags = dwFlags
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyDown(ushort vk)
    {
        SendKeyInput(vk, keyUp: false);
    }

    public static void SendKeyUp(ushort vk)
    {
        SendKeyInput(vk, keyUp: true);
    }

    private static void SendKeyInput(ushort wVk, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = wVk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern short VkKeyScan(char ch);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
