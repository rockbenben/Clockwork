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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);   // 复核「关闭」是否生效也要用
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);   // 复核「最大化」是否真的生效
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    // 窗口筛选用（见 WindowsForProcess）。GWL_EXSTYLE 是 32 位样式值，x64 上 GetWindowLong 即可，无需 Ptr 版。
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public int type; public InputUnion U; }

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const int WHEEL_DELTA = 120;   // 一格滚轮的标准刻度

    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D, VK_TAB = 0x09;

    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

    // 需要 KEYEVENTF_EXTENDEDKEY 的键：方向键 / 导航键 / 右 Ctrl / 右 Alt / NumLock / PrintScreen / 小键盘除号。
    // 少这个标志时 Windows 不会替你补：只读 wParam 虚拟键的普通 Win32/WPF/WinForms 应用无所谓（所以日常
    // 组合键一直是好的），但读扫描码或扩展位的消费者会收错——远程桌面 / 虚拟机客户端 / DirectInput 游戏 /
    // 部分终端会把方向键当成小键盘键，因为 MapVirtualKey(VK_LEFT) 得到的正是小键盘 4 的扫描码 0x4B。
    private static readonly ushort[] ExtendedVks =
        { 0x25, 0x26, 0x27, 0x28, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E, 0xA3, 0xA5, 0x90, 0x2C, 0x6F };

    private static INPUT MakeKey(ushort vk, bool up)
    {
        var inp = new INPUT { type = 1 }; // INPUT_KEYBOARD
        inp.U.ki.wVk = vk;
        inp.U.ki.wScan = (ushort)MapVirtualKey(vk, 0);   // MAPVK_VK_TO_VSC：wScan 留 0 时下游拿到的是空扫描码
        uint flags = up ? KEYEVENTF_KEYUP : 0u;
        if (Array.IndexOf(ExtendedVks, vk) >= 0) flags |= KEYEVENTF_EXTENDEDKEY;
        inp.U.ki.dwFlags = flags;
        return inp;
    }

    // 字面字符的注入事件：wVk=0、字符本身放 wScan、走 KEYEVENTF_UNICODE。
    private static INPUT MakeUnicode(char ch, bool up)
    {
        var inp = new INPUT { type = 1 };
        inp.U.ki.wVk = 0;
        inp.U.ki.wScan = ch;
        inp.U.ki.dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u);
        return inp;
    }

    // 逐字注入字面文本，返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝）。
    //
    // 走 KEYEVENTF_UNICODE 而不是虚拟键路径，是因为后者必须先把每个字符映射成「哪个键 + 要不要 Shift」，
    // 于是整段文本都要过当前键盘布局，并且会被输入法接管：目标窗口处于中文输入状态时，注入的
    // "hello" 会被整段当成拼音吃进候选框，一个字符都进不到输入框里——而随后的回车还可能把候选词上屏。
    // Unicode 注入把字符直接交给目标窗口，绕开布局与输入法，也不再受 ANSI 代码页限制。
    //
    // 换行与 Tab 例外，仍按真键发：它们不是"字符"，Unicode 注入进去多数应用不认，
    // 而「换行=回车、Tab 生效」是这个功能对用户的明确承诺。\r\n 只发一次回车。
    public static uint SendUnicodeText(string text)
    {
        var list = new List<INPUT>();
        foreach (var ch in text ?? "")
        {
            switch (ch)
            {
                case '\r': continue;
                case '\n': list.Add(MakeKey(VK_RETURN, false)); list.Add(MakeKey(VK_RETURN, true)); break;
                case '\t': list.Add(MakeKey(VK_TAB, false)); list.Add(MakeKey(VK_TAB, true)); break;
                // 代理对（emoji 等）按 UTF-16 码元逐个发，高位低位各一次——这正是 Unicode 注入的规定用法。
                default: list.Add(MakeUnicode(ch, false)); list.Add(MakeUnicode(ch, true)); break;
            }
        }
        if (list.Count == 0) return 0;
        var arr = list.ToArray();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
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

    // 滚轮：修饰键按下 → 一格滚轮 → 修饰键逆序抬起，与 SendCombo 同样一次原子注入
    //（Ctrl+滚轮缩放靠的就是"滚的那一刻 Ctrl 确实按着"，拆成两次 SendInput 中间可能被别的输入插进来）。
    // 一次只发一格：mouseData 填 120*N 理论上等于 N 格，但不少应用只按"来了一条消息"处理、仍只走一步；
    // 发 N 条独立事件才是普遍兼容的做法——次数交给步骤自带的「重复次数」，这里不自己造循环。
    // notches>0 向上（远离用户），<0 向下。返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝，与 SendCombo 同）。
    public static uint SendWheel(ushort[] mods, int notches)
    {
        var list = new List<INPUT>();
        foreach (var m in mods) list.Add(MakeKey(m, false));
        list.Add(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)(WHEEL_DELTA * notches)), dwFlags = MOUSEEVENTF_WHEEL } },
        });
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
    //
    // 光靠 IsWindowVisible 远远不够：进程的隐藏辅助窗口同样是「可见的顶层窗口」。本机实测，
    // explorer 会返回 16 个句柄而其中只有 3 个是真的资源管理器窗口（CabinetWClass），其余是任务栏
    // Shell_TrayWnd、桌面 Progman、以及一堆 ThumbnailDeviceHelperWnd；pwsh 返回 9 个句柄、真窗口为零，
    // 全是 PseudoConsoleWindow。后果不是"多算了几个"：「关闭 explorer」会把 WM_CLOSE 广播到任务栏和桌面上，
    // 「最小化」会去最小化任务栏，而 SetForeground 取 hs[0]——EnumWindows 是 Z 序自顶向下，
    // hs[0] 恰恰是那些带 WS_EX_NOACTIVATE 的辅助窗口，于是"带到最前"必然失败。
    // 三条过滤对应三种辅助窗口，缺一不可：
    //   owner==0        —— 排除属主窗口（对话框/工具浮窗，PseudoConsoleWindow 正是这类）
    //   非 TOOLWINDOW   —— 工具窗按定义就不是任务栏上那个"应用窗口"
    //   标题非空        —— 挡住 Progman / 挂起的 ApplicationFrameWindow 这类无标题壳窗口
    // 宁可严：全被滤掉时调用方如实报「找不到窗口」，也好过把动作打在任务栏上。
    public static IntPtr[] WindowsForProcess(string procName)
    {
        var pids = new HashSet<uint>();
        foreach (var pr in Process.GetProcessesByName(procName)) { pids.Add((uint)pr.Id); pr.Dispose(); }
        var list = new List<IntPtr>();
        EnumWindows((h, p) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains(pid)) return true;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (GetWindowTextLength(h) <= 0) return true;
            list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
}
