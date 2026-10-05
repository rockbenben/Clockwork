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
    // 取某点所在的窗口。注意：(a) 参数是 POINT **按值**传，不是指针；
    // (b) 它跳过 WS_EX_TRANSPARENT 的窗（命中测试穿透的那些），所以手势的笔迹覆盖窗不会被当成目标。
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint gaFlags);
    private const uint GA_ROOT = 2;   // 顶层根窗口

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

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr h, uint code);

    /// <summary>硬退本进程：只给退出看门狗兜底用（见 <c>App.ArmExitWatchdog</c>）。</summary>
    //
    // 为什么是 TerminateProcess 而不是 <c>Environment.Exit</c>：Exit 走托管那一套
    //（ProcessExit、终结器、模块卸载），而它要拦的那个症状**正是托管收尾里某条线程不返回**——
    // 那时候 Exit 自己也会停在那儿，看门狗就成了摆设。TerminateProcess 是内核调用：
    // 所有线程当场结束、句柄由系统回收，不跑任何托管代码，所以它不会挂。
    //
    // 代价要说清楚：该落盘的东西一个都不会落。所以调用方必须先尽力补写（App 那边带超时），
    // 并且这是**最后**一招，不是退出流程的常规一步。互斥体句柄由系统关掉＝abandoned，
    // 接管的实例本来就有 AbandonedMutexException 那条分支（见 ClaimSingleInstance）。
    public static void TerminateSelf(uint code = 0)
    {
        try { TerminateProcess(GetCurrentProcess(), code); } catch { }
    }

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

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("kernel32.dll")] private static extern int GetCurrentProcessId();
    // 同一句 API 的变长结构版本：上面那个 `out uint` 重载只够读 TokenElevation 这种 4 字节值，
    // 而 TokenIntegrityLevel 给的是 TOKEN_MANDATORY_LABEL（里面是一个 SID 指针），必须拿缓冲区读。
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tok, int cls, IntPtr info, uint len, out uint ret);
    [DllImport("advapi32.dll")] private static extern byte GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint idx);

    /// <summary>UIPI 会不会把一次注入丢掉：目标进程的完整性级别高于我们自己时，`SendInput` 被静默丢弃。</summary>
    /// <param name="selfRid">我们的完整性 RID（SecurityMedium=0x2000 / High=0x3000…）。</param>
    /// <param name="targetRid">目标的同一格，**0 = 读不到**。</param>
    //
    // 读不到按「会拦」处理，这个取向是行为决定不是省事：答错成「送得进去」，代价是抬起已经吞了、
    // 注入又被丢——用户那一下右键**彻底消失**（正是他报的症状）；答错成「送不进去」，走的是
    // 「放行真抬起」那条既有路，最坏是多弹一次菜单。受保护进程（杀软那种）开不到 token，
    // 而它们恰恰几乎都是提权的，所以这一档猜「拦」也猜得对。
    internal static bool IntegrityBlocksInjection(uint selfRid, uint targetRid)
        => targetRid == 0 || targetRid > selfRid;

    /// <summary>此刻能不能把一次注入送进目标程序的输入队列——补发之前问的那一句。</summary>
    //
    // 为什么必须在**吞掉真抬起之前**问：补发改到补发线程排队之后，回调一 `return 1`，那条真抬起
    // 就再也还不回去了。旧写法能拿 `SendInput` 的返回值当场决定「被拒就放行真事件」，是因为注入
    // 就跑在回调里（见 MouseHook.ReplayClick）。异步化不能把这个兜底弄丢，于是把它前移成一次探测。
    //
    // 判据是完整性级别而不是「是不是管理员」，因为 UIPI 丢的正是「低 IL → 高 IL 窗口」这一档；
    // 我们自己提权时普通窗口全在下面，不会被丢。
    public static bool InputNotBlockedNow()
    {
        try
        {
            // 安全桌面（UAC 提示屏）：那个桌面上没有本进程的钩子，也没有可投的输入队列。
            // 判据用 shell 窗是否存在——它是按当前桌面查的，切到安全桌面上就问不到。
            if (GetShellWindow() == IntPtr.Zero) return false;
            if (IsElevated()) return true;
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return true;         // 没有前台窗：没有「更强的目标」，照平常走
            uint pid = GetWindowThreadProcessId(fg, out _);
            if (pid == 0 || pid == GetCurrentProcessId()) return true;
            return !IntegrityBlocksInjection(SelfIntegrityRid(), IntegrityRidOfProcess(pid));
        }
        catch { return false; }   // 问不出来就当送不进去：取向见 IntegrityBlocksInjection
    }

    private static uint _selfIntegrityRid;   // 本进程的 IL 不会变，读一次存着

    private static uint SelfIntegrityRid()
    {
        uint cached = Volatile.Read(ref _selfIntegrityRid);
        if (cached != 0) return cached;
        uint rid = TokenIntegrityRid(GetCurrentProcess());
        Volatile.Write(ref _selfIntegrityRid, rid);
        return rid;
    }

    private static uint IntegrityRidOfProcess(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
        if (h == IntPtr.Zero) return 0;
        try { return TokenIntegrityRid(h); }
        finally { try { CloseHandle(h); } catch { } }
    }

    /// <summary>前台窗口所属进程的 pid；没有前台窗时返回 0。</summary>
    //
    // **这里一个字都不许加。** 它跑在低级鼠标钩子回调里（见 HookProbe），而那条路上
    // `OpenProcess` 是已知能卡住整条鼠标链的东西（本机实测 3187ms / 6469ms，见 HookProbe 类头）。
    // 这一句是 GetForegroundWindow + GetWindowThreadProcessId，两次 user32 调用、纯本进程可答、
    // 不碰别的进程的对象，所以它留在回调里是安全的；本进程自己的 pid 也直接答掉。
    public static uint ForegroundPid()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return 0;   // 前台是自己（覆盖窗抢到了）：没有「目标」
            return pid;
        }
        catch { return 0; }
    }

    // TOKEN_MANDATORY_LABEL 的最后一个子授权就是完整性 RID（0x1000 Low / 0x2000 Medium /
    // 0x3000 High / 0x4000 System）。失败一律答 0=读不到，绝不外抛：调用它的地方在钩子回调路径上。
    private static uint TokenIntegrityRid(IntPtr hProcess)
    {
        if (!OpenProcessToken(hProcess, 0x0008, out IntPtr tok) || tok == IntPtr.Zero) return 0;
        IntPtr buf = IntPtr.Zero;
        try
        {
            uint len = 64;
            buf = Marshal.AllocHGlobal((int)len);
            if (!GetTokenInformation(tok, 25 /* TokenIntegrityLevel */, buf, len, out uint need))
            {
                // 缓冲区不够时系统会把需要的长度报回来，别一上来就赌 64 字节永远够。
                if (need == 0 || need > 4096) return 0;
                Marshal.FreeHGlobal(buf);
                len = need;
                buf = Marshal.AllocHGlobal((int)len);
                if (!GetTokenInformation(tok, 25, buf, len, out _)) return 0;
            }
            IntPtr sid = Marshal.ReadIntPtr(buf);            // LABEL 结构体只有一个 SID 指针
            if (sid == IntPtr.Zero) return 0;
            byte count = GetSidSubAuthorityCount(sid);
            if (count == 0) return 0;
            return (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        catch { return 0; }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            try { CloseHandle(tok); } catch { }
        }
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

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>主屏的缩放系数（1.0 = 100%，1.5 = 150%）。取不到时回退 1.0。</summary>
    //
    // 中键长按的拖拽容差按它换算（见 MouseHook 构造函数 / GestureCoordinator.ApplyMouseHook）：低级钩子喂进来的
    // 坐标是物理像素，而「手抖几像素算抖动」的那个像素是逻辑像素——150% 屏上 6 物理像素只有
    // 4 逻辑像素，约 1mm，按下去的那一下抖动就能把长按误判成中键拖拽，面板永远不弹。
    // 本程序声明 PerMonitorV2（见 app.manifest），GetDpiForMonitor 拿到的就是主屏真实缩放。
    // 取主屏而非逐屏，是 MouseHook 里同一笔已写明的取舍（GestureGate.MinLegForScreen 那段）：
    // 钩子回调里不值得为副屏的手感多两次 P/Invoke。
    public static double PrimaryScale()
    {
        try
        {
            var mon = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
            if (mon != IntPtr.Zero
                && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
                && dpiX >= 96) return dpiX / 96.0;
        }
        catch { }
        return 1.0;
    }

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
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;   // Win 键没有通用 VK，左右各问一次

    // 高位 = 此刻物理按下。取不到（受限令牌等）按「没按」处理：逃生口探测失败只该少一条退路，
    // 绝不能反过来把正常开机误判成安全模式、让所有人的开机清单集体不跑。
    public static bool ShiftHeld()
    {
        try { return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0; } catch { return false; }
    }

    /// <summary>此刻有修饰键（Ctrl / Shift / Alt / Win 任一）物理按着吗。</summary>
    //
    // 给鼠标钩子让路用：修饰键 + 右键是**别的工具的手势地盘**——WindowShuffle 的 Ctrl+右键拖窗、
    // Explorer 的 Shift+右键扩展菜单那一类。钩子在右键按下这一刻问到「有」就整笔放行，
    // 不扣按下、不补点击（低级钩子 return 1 会截断整条钩子链，补发的注入事件又会被对方按防回环丢掉）。
    //
    // 读的是**键盘**物理态——我们从不吞键盘，所以这一问不受 RightButtonDown 上面那条
    // 「吞了消息就别拿系统键态当自己的判据」约束：那条说的是被我们自己 return 1 吞掉的**鼠标右键**，
    // 键态表里那次按下从未发生；键盘消息我们一条都没动过。
    //
    // 取不到按「没按」处理，与 ShiftHeld 同一个方向：探测失败只该少一条豁免，
    // 不能反过来把不带修饰键的右键也全部放行、让手势功能整个消失。
    public static bool ModifierHeld()
    {
        try
        {
            return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0
                || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
                || (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
                || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        }
        catch { return false; }
    }

    // ── 窗口幽灵化（Ghost Window）──
    //
    // 一个窗口的线程超过 HungWindowTimeout（默认 5 秒）没去取消息，系统就认定它「未响应」，
    // 由 **DWM** 在原窗口的位置上盖一块自己的窗——那块灰白壳就是幽灵窗。
    //
    // **幽灵窗点得中，而且不继承我们的样式。** 它不属于本进程，我们那份 WS_EX_TRANSPARENT
    // 到不了它身上；实测证据是「某点上的窗口是谁」问回来的正是幽灵窗的句柄而不是原窗
    //（未公开 API HungWindowFromGhostWindow 存在的全部理由就是把原窗从幽灵手里换回来）。
    // 于是：本程序有一扇**铺满整块屏**的置顶覆盖窗（手势笔迹，见 Views.GestureTrailWindow）
    // → UI 线程一旦卡顿超过 5 秒（卡多久都有可能：整机忙、GC、调试器断下、别的进程把输入队列堵住）
    // → 一块铺满整块屏、点得中的幽灵窗压在最上层 → **整个桌面点不动**。
    // 而那块窗属于 dwm.exe，我们既摘不掉也关不掉——用户能试出来的唯一解法就是杀掉 dwm.exe
    //（用户报的正是这个：「偶尔手势操作会造成无法点击，关闭桌面窗口管理器才正常」）。
    //
    // 关掉本进程的幽灵化，这条路就整个不存在：任何一扇窗都不会再被 DWM 顶上一块点得中的壳。
    // 代价（进程级、一次性、**不可逆**）：UI 线程真卡住时窗口只是卡着不动，不再有那块
    // 「未响应」的壳可以拖走、可以关。那点安慰换掉的是一整台电脑点不动，而 Clockwork 本来就是
    // 托盘程序，窗口不是它的主场。
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();

    private static int _ghostDisabled;

    /// <summary>关掉本进程的窗口幽灵化。幂等、不可逆（理由见上面那段）。</summary>
    //
    // 只给「会摆出全屏覆盖窗」的那条路调（见 GestureCoordinator.ApplyMouseHook 里建笔迹窗那一处），
    // 没开手势的人一点没变。里面有闸是因为那句 P/Invoke 是单向的、而调用点每次保存配置都会走到：
    // 让它自己保证「一辈子只真发出去一次」，调用点就不必替它记这笔账。
    public static void DisableWindowGhosting()
    {
        if (System.Threading.Interlocked.Exchange(ref _ghostDisabled, 1) == 1) return;
        try { DisableProcessWindowsGhosting(); } catch { }
    }

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    // 一次注入的「ALT 按下+抬起」**成对、同批、原子**（一次 SendInput 发两个事件），排在
    // SetForegroundWindow 之前：让前台锁把紧随的抢前台当作用户刚敲过 ALT 而放行。
    // **绝不能拆成 down→SFW→up 的三明治**（坊间流传的那种写法）：Alt up 会随新激活窗口落到
    // 自家队列，旧前台程序手里留下一个没有配对 down——用户下次切回去，菜单条像被按住一样
    // 亮着（2026-09-26 用户实报「造成 alt 黏连」）。同批成对则所有队列看到的都是一闪而过的完整键。
    // 光同批还不够，还要核对返回条数（与 KeyInput.SendKeyCombo 同一套房规）：SendInput 可能只
    // 放进前几条就被拒（UIPI/安全桌面），漏了那条 up 的 down 就是又一次黏连——缺什么补什么。
    private static void AltExemption()
    {
        uint n = SendInput(2, new[] { MakeKey(VK_MENU, false), MakeKey(VK_MENU, true) }, Marshal.SizeOf(typeof(INPUT)));
        if (n < 2) ReleaseKeys(new ushort[] { VK_MENU });
    }

    /// <summary>把窗口硬提到前台。返回是否真的到了前台。</summary>
    //
    // **调用方一律走 <see cref="ForegroundNudge.Activate"/>，不要直接调这个方法。**
    // 它兜底时会用 AttachThreadInput 挂输入队列，于是**调用线程必须有消息泵**；而「找一条有泵的线程」
    // 这件事本身是有代价的（UI 线程有泵，但它是跨进程同步调用，会把 UI 线程按住甚至永久挂住）。
    // ForegroundNudge 把这份代价收进了一条专属的牺牲线程，并且给调用方加了超时。
    // 直接调它 = 把「哪条线程来承担」这个问题又交回给了每一个调用点。
    //
    // 为什么不能只调 SetForegroundWindow：Windows 有前台锁，只有"刚响应了用户输入"的进程才准抢前台。
    // 全局热键（WM_HOTKEY）算，所以热键呼出面板一路顺畅；而**低级鼠标钩子里的中键长按不算**——
    // 那条路上 SetForegroundWindow 会被降级成任务栏闪烁，面板出现了却收不到键盘，
    // 而且因为它从未激活过，「失焦即关」也永远不会触发，面板就一直挂在屏幕上。
    //
    // 通行解法是把自己的输入队列临时挂到当前前台线程上：挂上之后两个线程共享输入状态，
    // 前台锁不再挡我们。用完立刻摘掉——长期挂着会让两个进程的键盘焦点互相干扰。
    //
    // **这一句是 UI 线程上最可疑的阻塞点，所以包了一圈计时（见 Core.UiStallWatch）。**
    // 它是**跨进程同步**的：SetForegroundWindow 会向"失去激活的那个窗口"同步投递
    // WM_ACTIVATE / WM_KILLFOCUS，而调用期间本线程正挂在**前台线程的输入队列**上。
    // 于是目标程序忙（在加载、在渲染、或自己卡着）时，本线程就跟着一起等——从前调用方为了
    // 让 AttachThreadInput 有消息泵可消化那些同步消息，专门把它切到了 **UI 线程**上
    //（见 ForegroundNudge 的类头注释：那一步把 UI 线程变成了受害者）。
    // 更坏的一档：SetForegroundWindow 干脆不返回时，下面 finally 那句 detach 永远不执行，
    // 本线程就**永久挂在别人的输入队列上**，两边一起不泵——那条路正是
    // WindowManager 注释里写的「前台线程跟着一起卡死，DWM 重启才能解开」。
    // 现在它跑在 ForegroundNudge 那条牺牲线程上，所以最坏也只牺牲那一条。
    // 日志里那行 `ui thread slow: foreground-nudge took ...ms (thread=bg)` 就是它的指纹
    //（标 bg 是对的：它已经不在 UI 线程上了）。
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        long t0 = Core.UiStallWatch.Begin();
        try
        {
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;   // 有豁免时这一下就够
            // ALT 微输入豁免：敲一对完整的「按下+抬起」换一次放行，换来的是**真前台**——
            // Activated/Deactivated 照常成对触发，失焦即关是毫秒级的。务必排在 attach 之前：
            // 下面那条 AttachThreadInput 借来的是「校验完一撒手就被系统回滚」的假前台，
            // 正是 2026-09-26「半激活面板」（鼠标点得动、看门狗却没有任何一拍见过它在前台）
            // 的病灶，只能当最后兜底。
            AltExemption();
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
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
        finally { Core.UiStallWatch.End("foreground-nudge", t0); }
    }

    /// <summary>此刻前台窗口所属进程的裸名（不含 .exe / 路径）。取不到返回空串。</summary>
    //
    // 给快捷面板的「场景页」用：面板要按你**呼出它的那一刻正在用哪个程序**来决定先显示哪一页。
    // 时机是承重的——必须在面板窗口显示**之前**问，面板一显示就把前台抢走了，那时再问只会得到
    // Clockwork 自己。调用点见 PanelCoordinator.TogglePanel 的第一行。
    //
    // 全程兜住：进程可能在这两行之间退出（句柄开不出来），也可能因权限读不到名字。
    // 拿不到就当作「没有场景」，面板照常显示全局页——
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

    /// <summary>屏幕上某点（物理像素）所在的**顶层**窗口；落在本进程自己的窗上、或取不到时返回 Zero。</summary>
    //
    // 手势「当前窗口」该打谁的第一答案：右键划在哪个窗口上，就该动哪个窗口——不是前台窗口。
    // 右键按下被低级钩子吞掉（return 1），Windows 不会去激活光标下的窗；在一个**后台**窗口上起笔时，
    // 前台仍是原先那个活动窗。拿 GetForegroundWindow 当目标，关/最小化就会落到你正用着的那个窗上。
    //
    // WindowFromPoint 直接跳过 WS_EX_TRANSPARENT 的窗（命中穿透），笔迹覆盖窗天然不会中；
    // 再 GetAncestor(GA_ROOT) 取顶层（拿到的常是子窗/客户区里的控件，窗口动作要的是顶层框）。
    // 本进程那扇窗（面板/编辑器）也排除掉，口径与 ForegroundWindowOfOthers 一致。
    public static IntPtr WindowAtPoint(int x, int y)
    {
        try
        {
            var h = WindowFromPoint(new POINT { X = x, Y = y });
            if (h == IntPtr.Zero) return IntPtr.Zero;
            h = GetAncestor(h, GA_ROOT);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            GetWindowThreadProcessId(h, out uint pid);
            return pid == (uint)Environment.ProcessId ? IntPtr.Zero : h;
        }
        catch { return IntPtr.Zero; }
    }

    public static string ProcessNameForWindow(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out uint pid);
            return ProcessNameForPid(pid);
        }
        catch { return ""; }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, System.Text.StringBuilder name, ref uint size);

    // 钩子线程上复用的名字缓冲。低级钩子回调只有一个线程在跑，所以一格就够，
    // 而它要解决的正是「每次按下都 new 一个 1KB 缓冲」——见下面那条不许用组件的理由。
    [ThreadStatic] private static System.Text.StringBuilder? _nameBuf;

    /// <summary>pid → 进程名（不带目录、不带 <c>.exe</c>，与 <c>Process.ProcessName</c> 同形）；取不到返回空串。</summary>
    //
    // **为什么不是 System.Diagnostics.Process。** 这一句跑在低级鼠标钩子的回调里，那条路有
    // LowLevelHooksTimeout（默认 300ms）的硬预算，超了 Windows 会把钩子静默摘掉。旧写法
    //（Process.GetProcessById + .ProcessName）在本机量过：377 个活进程各查一次，组件 278ms、
    // 这个写法 3ms，差约 90 倍；而且组件每次都要 new 一个 Process 对象带一串字符串——现场日志里
    // 那句 `hook-proc-name took 3187ms`（等于整个回调的时长，chain=0）就是这条路被按住的样子，
    // 而它一被按住，心跳停 → 自愈判定钩子死了 → UI 线程 Join 等满 2 秒 → 屏幕上那条线冻住。
    // 判据由 ProcessNameQueryTests 钉着，包括「不许换回组件」那一条。
    //
    // 失败一律答空串、绝不外抛：回调里漏出去一个异常就是进程当场没了（见 MouseHook.CallbackCore 的 try）。
    public static string ProcessNameForPid(uint pid)
    {
        if (pid == 0) return "";
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = _nameBuf ??= new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size) || size == 0) return "";
            // flags=0 给的是 Win32 路径（C:\dir\name.exe）；flags=1 那种 \DEVICE\HarddiskVolume 形式不能用。
            var path = sb.ToString(0, (int)size);
            int slash = path.LastIndexOf('\\');
            var leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            return leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? leaf[..^4] : leaf;
        }
        catch { return ""; }
        finally { try { CloseHandle(h); } catch { } }
    }

    public static string ForegroundProcessName() => ProcessNameForWindow(GetForegroundWindow());

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(long lParam, System.Text.StringBuilder lpString, int nSize);

    // VK → 这个键在**当前键盘布局**下的名字（实测 zh-CN/美式布局：VK 0xC0 拿回 "`"；
    // 同一个物理键在德语 QWERTZ 上是死键 "^"——所以不能在代码里把 Oem3 写死成反引号）。
    // 仅供录键框美化显示：只接受**恰好一个字符**的结果；多字符名（F1、PageUp、Space…）
    // 或拿不到名字时返回 null，调用方回退到 WPF token（"Oem3"）。
    public static string? KeyNameChar(uint vk)
    {
        uint scan = MapVirtualKey(vk, 0);
        if (scan == 0) return null;
        var sb = new System.Text.StringBuilder(16);
        // lParam 的 16-23 位放扫描码；Oem 标点键都不是扩展键，无需置扩展位（bit24）。
        return GetKeyNameTextW((long)(scan << 16), sb, sb.Capacity) == 1 ? sb.ToString() : null;
    }

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
