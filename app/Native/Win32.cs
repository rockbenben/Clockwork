using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clockwork.Native;

// Win32 P/Invoke 封装。
// 预编译后不再有运行时 csc / 受限令牌降级问题——原 Confirm-Win32Available 那套整体废弃。
public static class Win32
{
    public const uint WM_CLOSE = 0x0010;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public int type; public InputUnion U; }

    private static INPUT MakeKey(ushort vk, bool up)
    {
        var inp = new INPUT { type = 1 }; // INPUT_KEYBOARD
        inp.U.ki.wVk = vk;
        inp.U.ki.dwFlags = up ? 2u : 0u; // KEYEVENTF_KEYUP
        return inp;
    }

    // 官方推荐路径：整个组合（修饰键按下→主键按下/抬起→修饰键逆序抬起）一次 SendInput 原子注入。
    // 返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝）。
    public static uint SendCombo(ushort[] mods, ushort vk)
    {
        var list = new List<INPUT>();
        foreach (var m in mods) list.Add(MakeKey(m, false));
        list.Add(MakeKey(vk, false));
        list.Add(MakeKey(vk, true));
        for (int i = mods.Length - 1; i >= 0; i--) list.Add(MakeKey(mods[i], true));
        var arr = list.ToArray();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    // 部分注入的善后：给每个键补发抬起事件，防止修饰键被卡在按下态。
    public static void ReleaseKeys(ushort[] vks)
    {
        var list = new List<INPUT>();
        foreach (var k in vks) list.Add(MakeKey(k, true));
        var arr = list.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    // 目标进程的所有可见顶层窗口句柄。入参须为裸进程名（调用方先 StepHelpers.ToProcessName 归一）。
    public static IntPtr[] WindowsForProcess(string procName)
    {
        var pids = new HashSet<uint>();
        foreach (var pr in Process.GetProcessesByName(procName)) pids.Add((uint)pr.Id);
        var list = new List<IntPtr>();
        EnumWindows((h, p) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pids.Contains(pid)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
}
