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

    // 放一个 .wav。走 winmm 的 PlaySound 而不是 System.Media.SoundPlayer：
    // SoundPlayer.Play() 是异步的，而它把 wav 读进**托管内存**再让系统从那块内存播——
    // 那块内存必须活到播完为止。于是 `using var p = new SoundPlayer(...); p.Play();` 这种写法
    // 会在声音还没响完时就把它释放掉，表现是「有时候响、有时候只响半截」，且完全看运气。
    // SND_FILENAME 让**系统自己去读那个文件**，这边一行代码都不用替它保命。
    public const uint SND_ASYNC = 0x0001;      // 立刻返回，别把动作卡在这一步
    public const uint SND_FILENAME = 0x00020000;
    public const uint SND_NODEFAULT = 0x0002;  // 放不出来就安静地什么都不放，别退回系统的「哔」
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool PlaySound(string? name, IntPtr mod, uint flags);

    // 「此刻 Shift 按着吗」——开机清单的逃生口用（见 App.SkipStartupReason）。
    // 用 GetAsyncKeyState 而不是 WPF 的 Keyboard.Modifiers：后者读的是本线程输入队列的状态，
    // 而这一次询问发生在进程刚起来、还没有任何窗口拿到输入焦点的时候，那时它一律返回 None。
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr tok, int cls, out uint info, uint len, out uint ret);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);

    /// <summary>本进程是不是提权运行的。</summary>
    //
    // 低级鼠标钩子在某些机器上收不到输入，而「有没有提权」是最先要排除的一格——
    // 报出来比让用户去猜强。
    public static bool IsElevated()
    {
        var tok = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out tok)) return false;
            bool ok = GetTokenInformation(tok, 20, out uint elevated, 4, out _);   // TokenElevation
            return ok && elevated != 0;
        }
        catch { return false; }
        // OpenProcessToken 拿到的是一个**真句柄**，得还。GetCurrentProcess 那个是伪句柄、不用还，
        // 两者长得一样，是这一类泄漏的常见来路。
        // 现在只有 --hookprobe 调一次、进程随即退出，所以泄漏一个句柄看不出影响——
        // 但这个方法的名字听起来随时可以调，下一个调用点很可能在一个循环里。
        finally { if (tok != IntPtr.Zero) try { CloseHandle(tok); } catch { } }
    }

    /// <summary>光标此刻在哪（物理像素）。取不到时返回 (0,0)。</summary>
    //
    // 用来给「鼠标钩子还活着吗」做旁证：钩子的心跳靠鼠标动才有，
    // 光看心跳的话「久坐不动」和「钩子掉了」分不开。位置动过而心跳没动，才是真掉了。
    public static (int X, int Y) CursorPos()
    {
        try { return GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0); } catch { return (0, 0); }
    }

    /// <summary>主屏宽度（物理像素）。取不到时返回 0，调用方自己兜底。</summary>
    //
    // 手势的最小笔画长度按它算（见 GestureGate.MinLegForScreen）。用 GetSystemMetrics 而不是
    // WPF 的 SystemParameters：后者给的是 DIP，而钩子喂进来的坐标是物理像素，两者在缩放屏上差一截。
    // 取 SM_CXSCREEN（主屏）而不是 SM_CXVIRTUALSCREEN（所有屏之和）：接第二块屏不该让手势变难画。
    public static int PrimaryScreenWidth()
    {
        try { return GetSystemMetrics(0); } catch { return 0; }   // SM_CXSCREEN
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>右键的**物理**键态。仅供 --hookprobe 诊断用——为什么，见下面。</summary>
    //
    // 曾经这段注释挂在 GetCursorPos 上，写的是「手势的状态机只认按下/抬起两条消息，
    // 而抬起是可能丢的（抬手那一刻 UAC 安全桌面弹出来），这一问是权威的：它读的是
    // 物理键态，不依赖有没有收到那条消息」。
    //
    // **在手势那条路上，那句话是错的，而且错得彻底。** 钩子为了判意图先把 WM_RBUTTONDOWN
    // 吞掉了（return 1），那条消息就再没进入系统的输入处理——于是键态表里右键从头到尾
    // 没按下过，这一问永远答「没按」。MouseHook 那边实测：吞掉之后的 486 次移动里，
    // 答「按着」0 次。拿它当判据的那一版，每一笔手势都在第一次移动时被自己作废，
    // 一个采样点都攒不下，屏幕上连轨迹都不会出现。
    //
    // 教训一句话：**吞了别人的消息，就不能再拿系统状态当自己的判据。**
    // 手势那边改用自己收到按下的时刻（MouseHook 里的 _beatRDown），不依赖任何被我们动过的
    // 系统状态。
    //
    // 它现在唯一的调用方正是 DevChecks 里那个探针（--hookprobe）：探针**故意**吞一次按下，
    // 再数这一问的答案，为的就是把上面那个事实量出来。别把它用回判据里去。
    public static bool RightButtonDown()
    {
        try { return (GetAsyncKeyState(0x02) & 0x8000) != 0; } catch { return false; }   // VK_RBUTTON
    }
    private const int VK_SHIFT = 0x10;

    // 高位 = 此刻物理按下。取不到（受限令牌等）按「没按」处理：逃生口探测失败只该少一条退路，
    // 绝不能反过来把正常开机误判成安全模式、让所有人的开机清单集体不跑。
    public static bool ShiftHeld()
    {
        try { return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0; } catch { return false; }
    }

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>把窗口硬提到前台。返回是否真的到了前台。</summary>
    //
    // 为什么不能只调 SetForegroundWindow：Windows 有前台锁，只有"刚响应了用户输入"的进程才准抢前台。
    // 全局热键（WM_HOTKEY）算，所以热键呼出面板一路顺畅；而**低级鼠标钩子里的中键长按不算**——
    // 那条路上 SetForegroundWindow 会被降级成任务栏闪烁，面板出现了却收不到键盘，
    // 而且因为它从未激活过，「失焦即关」也永远不会触发，面板就一直挂在屏幕上。
    //
    // 通行解法是把自己的输入队列临时挂到当前前台线程上：挂上之后两个线程共享输入状态，
    // 前台锁不再挡我们。用完立刻摘掉——长期挂着会让两个进程的键盘焦点互相干扰。
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;   // 有豁免时这一下就够
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint theirs = GetWindowThreadProcessId(fg, out _);
            uint mine = GetCurrentThreadId();
            if (theirs == 0 || theirs == mine) return false;
            if (!AttachThreadInput(mine, theirs, true)) return false;
            try
            {
                SetForegroundWindow(hwnd);
                return GetForegroundWindow() == hwnd;
            }
            finally { AttachThreadInput(mine, theirs, false); }
        }
        catch { return false; }
    }

    /// <summary>此刻前台窗口所属进程的裸名（不含 .exe / 路径）。取不到返回空串。</summary>
    //
    // 给快捷面板的「场景页」用：面板要按你**呼出它的那一刻正在用哪个程序**来决定先显示哪一页。
    // 时机是承重的——必须在面板窗口显示**之前**问，面板一显示就把前台抢走了，那时再问只会得到
    // Clockwork 自己。调用点见 App.TogglePanel 的第一行。
    //
    // 全程兜住：进程可能在这两行之间退出（GetProcessById 抛 ArgumentException），
    // 也可能因权限读不到名字。拿不到就当作「没有场景」，面板照常显示全局页——
    // 一个读不到名字的前台窗口不该让面板开不出来。
    /// <summary>前台窗口句柄；没有前台、或前台是 Clockwork 自己时返回 Zero。</summary>
    //
    // 「排除自己」不是洁癖：主窗口里点「运行这一步」试跑一条「关闭当前窗口」时前台正是主窗口，
    // 不排除的话那一下关掉的是 Clockwork —— 用户按下的是「试一试」，得到的是程序消失。
    public static IntPtr ForegroundWindowOfOthers()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(h, out uint pid);
        return pid == (uint)Environment.ProcessId ? IntPtr.Zero : h;
    }

    public static string ForegroundProcessName()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName ?? "";
        }
        catch { return ""; }
    }

    // 设备变更广播：U 盘等卷插入时 Windows 向所有顶层窗口发 WM_DEVICECHANGE，不需要事先注册。
    // 只认「卷到达」（DBT_DEVTYP_VOLUME）——设备接口层的到达通知（打印机、摄像头、蓝牙适配器）
    // 混进来会让「U 盘插入时」在插耳机时也跑，那不是这个触发承诺的事。
    public const int WM_DEVICECHANGE = 0x0219;
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVTYP_VOLUME = 2;

    // lParam 指向的 DEV_BROADCAST_HDR：前两个 int 是 size 与 devicetype，只需要后者。
    // 结构体在别人的内存里，读越界会直接进程崩溃——故整体兜住，读不出来当成「不是卷」。
    public static bool IsVolumeArrival(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return false;
        try { return Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_VOLUME; }
        catch { return false; }
    }
    // 窗口筛选用（见 WindowsForProcess）。GWL_EXSTYLE 是 32 位样式值，x64 上 GetWindowLong 即可，无需 Ptr 版。
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    /// <summary>剪贴板内容的版本号；每次有人写入就 +1。用来判断「刚才那次复制到底成没成」，
    /// 不必先清空再看有没有东西——清空是破坏性的，而复制失败时那一下就白破坏了。</summary>
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();

    // 置顶用。HWND_TOPMOST/-1 与 HWND_NOTOPMOST/-2 是 SetWindowPos 的伪句柄，不是真窗口。
    public const int WS_EX_TOPMOST = 0x8;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
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
    private const uint MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100;
    private const uint XBUTTON1 = 1, XBUTTON2 = 2;   // 侧键：后退 / 前进
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
    // notches>0 向上/向右，<0 向下/向左（与 Windows 的 mouseData 符号约定一致）。返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝，与 SendCombo 同）。
    public static uint SendWheel(ushort[] mods, int notches, bool horizontal = false)
    {
        var list = new List<INPUT>();
        foreach (var m in mods) list.Add(MakeKey(m, false));
        list.Add(MakeMouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, unchecked((uint)(WHEEL_DELTA * notches))));
        for (int i = mods.Length - 1; i >= 0; i--) list.Add(MakeKey(mods[i], true));
        var arr = list.ToArray();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    private static INPUT MakeMouse(uint flags, uint data = 0)
        => new() { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { mouseData = data, dwFlags = flags } } };

    // 鼠标按键：修饰键按下 → 按下/抬起 ×clicks → 修饰键逆序抬起，同样一次原子注入。
    // 不带坐标：SendInput 的按键事件作用在指针当前位置，本程序不挪指针（见 KeyCombo.Mouse 的注释）。
    // 双击靠同一批里连发两组按下/抬起——同一次 SendInput 内的时间差远小于系统双击间隔，稳定成对。
    // 侧键(Back/Forward)的按下与抬起都要带 mouseData 指明是哪个 X 键，漏了会被当成未知 X 键丢弃。
    // 返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝，与 SendCombo/SendWheel 同）。
    // button 取 Core.MouseButton 的**数值**（Left=1 Right=2 Middle=3 Back=4 Forward=5）。
    // 有意不直接收那个枚举：Win32 这层是薄薄一层 P/Invoke，不该反向依赖模型层。
    // 代价是这份对应关系靠数值隐式对齐——枚举一旦重排就会静默错位（右键变中键这种），
    // 故由 Win32MouseButtonMappingTests 把两边的数值钉死。
    public static uint SendMouseButton(ushort[] mods, int button, int clicks)
    {
        (uint down, uint up, uint data) = button switch
        {
            1 => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0u),
            2 => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0u),
            3 => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0u),
            4 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            _ => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
        };
        var list = new List<INPUT>();
        foreach (var m in mods) list.Add(MakeKey(m, false));
        for (int i = 0; i < Math.Max(1, clicks); i++) { list.Add(MakeMouse(down, data)); list.Add(MakeMouse(up, data)); }
        for (int i = mods.Length - 1; i >= 0; i--) list.Add(MakeKey(mods[i], true));
        var arr = list.ToArray();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>本程序自己注入的鼠标事件的签名，写在 dwExtraInfo 里。
    /// 低级鼠标钩子（<see cref="MouseHook"/>）靠它认出自己补发的那些事件并原样放行——
    /// 不认的话，「短按补发一次真中键」会被自己的钩子再吞一次，中键从此彻底点不出来。
    /// 取一个不像句柄/指针的常数即可，别人用同一个值的概率可以忽略。</summary>
    public static readonly IntPtr InjectTag = new(0x434C4B57);   // 'CLKW'

    /// <summary>补发一次中键按下（拖拽救援）。带 <see cref="InjectTag"/> 签名。</summary>
    public static uint SendMiddleDownTagged() => SendTagged(MOUSEEVENTF_MIDDLEDOWN);

    /// <summary>补发一次中键抬起，给 <see cref="SendMiddleDownTagged"/> 收尾。带 <see cref="InjectTag"/> 签名。</summary>
    public static uint SendMiddleUpTagged() => SendTagged(MOUSEEVENTF_MIDDLEUP);

    /// <summary>补发一次右键按下（按住不动，把扣住的按下还给系统）。带 <see cref="InjectTag"/> 签名。</summary>
    public static uint SendRightDownTagged() => SendTagged(MOUSEEVENTF_RIGHTDOWN);

    /// <summary>补发一次右键抬起，给 <see cref="SendRightDownTagged"/> 收尾。带 <see cref="InjectTag"/> 签名。</summary>
    public static uint SendRightUpTagged() => SendTagged(MOUSEEVENTF_RIGHTUP);

    private static uint SendTagged(params uint[] flags)
    {
        var arr = new INPUT[flags.Length];
        for (int i = 0; i < flags.Length; i++)
            arr[i] = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags[i], dwExtraInfo = InjectTag } },
            };
        try { return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT))); }
        catch { return 0; }
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
