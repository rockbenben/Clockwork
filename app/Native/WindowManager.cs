using System.Threading;
using Clockwork.Core;
using Clockwork.I18n;
using WinSendKeys = System.Windows.Forms.SendKeys;

namespace Clockwork.Native;

public sealed class WaitResult
{
    public bool Present { get; init; }
    public int WaitedMs { get; init; }
}

// 一次窗口动作的结局。四种「没做成」必须分开，因为它们把用户指向完全不同的排查方向：
// 进程压根没跑 / 窗口在但动作被拒 / 动作名写错了 / 用户自己按了急停。
public enum WindowOutcome
{
    Ok,             // 复核过，确实生效了
    NoWindow,       // 没找到目标窗口（进程未运行 / 窗口还没出来）
    Failed,         // 窗口找到了，但动作没生效（前台锁定、提权窗口拒收、应用弹框挡住关闭）
    UnknownAction,  // 动作名不认识（手改 json 写错）
    // 动作名认得、但这个**目标**不开放它（「当前窗口」+ sendkey / activate）。
    // 与 UnknownAction 分开是因为两句话要指向不同的东西：那一条该去查动作名拼错没有，
    // 这一条该去换目标或换步骤类型。合成一格的话，编辑器里就点得出来的这个组合
    // 会报「不认识的窗口动作：activate」——指着一个完全合法的动作说不认识。
    NotForCurrentWindow,
    Cancelled,      // 急停或本次运行被取消——不是故障，静默
}

// 窗口动作、等待、置前台、文本/登录注入。
// 活交互（真实窗口/前台/注入）不单测；仅 WaitAppWindow 注入探针/睡眠可测。
public static class WindowManager
{
    // 目标进程的可见顶层窗口句柄（先把进程标识归一为裸名，与编辑器保存口径一致）。
    /// <summary>进程名填这个 = 当前窗口。</summary>
    //
    // **为什么是一个记号而不是「留空」。** 留空曾经的含义是「匹配不到任何窗口」：
    // GetProcessesByName("") 返回空数组，于是那一步什么也不做——而 close 在找不到窗口时
    // 走的是幂等分支，记 ✓ 且**不告警**。旧编辑器保存 window 步骤时并不拦空进程名，
    // 所以盘上完全可能躺着一条 {"kind":"window","action":"close","process":""} 的惰性步骤。
    // 若把「留空」改判成「当前窗口」，那条步骤会在开机清单里去关掉用户当时正看着的窗口，
    // 而且没有任何迁移能把它和「用户新配的当前窗口」区分开——两者在盘上一模一样。
    // 所以当前窗口要有自己的写法：老配置的含义原样不动，新意图必须显式写出来。
    public const string CurrentWindow = "*";

    public static bool IsCurrentWindow(string? process) => (process ?? "").Trim() == CurrentWindow;

    // 手势尤其需要「当前窗口」——右键划在哪个窗口上，动作就该落在哪个窗口，事先没有进程名可填。
    //
    // **空进程名一律不匹配任何窗口**，这一条要显式挡住，不能指望它「自然为空」：
    // GetProcessesByName("") 并不是返回零个进程——名字读不出来的那些（受保护进程、
    // 权限不足的）会match上，本机实测由此得到 **19 个真实窗口**。也就是说盘上一条
    // {"kind":"window","action":"close","process":""} 的步骤，会去关掉那 19 个窗口，
    // 而它看起来只是「有个字段没填」。旧编辑器又不拦空进程名，所以这条路是真实可达的。
    public static IntPtr[] Handles(string process)
    {
        if (IsCurrentWindow(process))
        {
            // **窗口管理类「当前窗口」打的是手势起笔那个窗，不是前台窗，更不能在这儿现读。**
            // 右键按下被低级钩子吞掉、系统不会激活光标下的窗，在后台窗口上起笔时前台仍是另一个窗；
            // 而动作在后台线程跑，读到这一句之前还隔着 Task 调度、WaitAppWindow 轮询、动作组里抢前台的
            // 前一步。起笔窗口在按下那一刻解析（_gestureOrigin，见 Win32.WindowAtPoint），运行开始时
            // 由 MarkRunBaseline 记下。它还有效就用它；失效（窗口被关）才退回触发时刻的前台 baseline，
            // 再不行才现读。裁决抽成纯函数（ResolveCurrentWindowTarget），这条取舍是活交互、单测够不着 Win32。
            var h = ResolveCurrentWindowTarget(_gestureOrigin, _foregroundBaseline,
                                               Win32.IsWindowVisible, Win32.ForegroundWindowOfOthers);
            return h != IntPtr.Zero ? new[] { h } : Array.Empty<IntPtr>();
        }
        var name = StepHelpers.ToProcessName(process);
        if (name.Length == 0) return Array.Empty<IntPtr>();
        return Win32.WindowsForProcess(name);
    }

    // 「当前窗口」该落到哪个句柄：起笔窗口（手势划在谁身上）优先，其次触发时刻记下的前台 baseline，
    // 两者都失效（窗口已关 / 非手势触发没有起笔窗口）才退回此刻现读的前台。
    // 纯函数、探针可注入——这三者不一致时取谁，是这个取舍的全部承重点，必须有断言钉着。
    internal static IntPtr ResolveCurrentWindowTarget(IntPtr gestureOrigin, IntPtr baseline,
                                                       Func<IntPtr, bool> stillVisible, Func<IntPtr> liveForegroundOfOthers)
    {
        if (gestureOrigin != IntPtr.Zero && stillVisible(gestureOrigin)) return gestureOrigin;
        if (baseline != IntPtr.Zero && stillVisible(baseline)) return baseline;
        return liveForegroundOfOthers();
    }

    // 目标进程的某个窗口当前是否真的在前台。
    public static bool IsForeground(string process)
    {
        var fg = Win32.GetForegroundWindow();
        foreach (var h in Handles(process)) if (h == fg) return true;
        return false;
    }

    // 尝试把目标窗口提到前台；仅当它确实到了前台才返回 true（SetForegroundWindow 常因前台锁定失败，必须复核）。
    public static bool SetForeground(string process)
    {
        var hs = Handles(process);
        if (hs.Length == 0) return false;
        // 当前窗口本来就在前台：既不用抢，也别白等那两次 120ms——手势要的就是抬手即到。
        if (IsCurrentWindow(process)) return true;
        // 最小化窗口 SetForegroundWindow 后仍最小化 → 先还原再置前台。
        if (Win32.IsIconic(hs[0])) { Win32.ShowWindow(hs[0], Win32.SW_RESTORE); Thread.Sleep(120); }
        // 走 ForceForeground 而不是裸 SetForegroundWindow：这里的调用大量来自手势 / 热键动作组
        // 与「已运行则激活」捷径，那些来源没有前台豁免，裸调只会被降级成任务栏闪烁。
        // ForceForeground 在被拒时用 AttachThreadInput 挂到前台线程的输入队列上补救（同面板那条路）。
        //
        // **必须由一条有消息泵的线程来调。** ForceForeground 内部 AttachThreadInput 把调用线程的输入队列
        // 与前台线程共享，挂上之后 SetForegroundWindow 内部投递的同步消息（WM_ACTIVATE / WM_KILLFOCUS
        // 等）需要调用线程 pump 消息才能消化。本方法被 StepRunner 从 Task.Run 后台线程调用，
        // 那条线程没有消息泵——挂上后那些同步消息无人接收，整条 attach 链死结，前台线程跟着一起卡死，
        // DWM 重启才能解开（Raymond Chen 2008、Alois Kraus 2018 实证）。
        //
        // **这条要求现在由 ForegroundNudge 那条专属泵线程满足，不再是「切到 UI 线程」。**
        // 原先这里写的是 `disp.Invoke(() => Win32.ForceForeground(hs[0]))`——把 UI 线程当成了那条泵。
        // 泵是有了，代价却转移到了最不该承担它的地方：那是**跨进程同步**调用，目标程序一忙
        //（在加载、在渲染、或自己卡着）UI 线程就跟着一起等；SetForegroundWindow 干脆不返回时，
        // detach 那句永远不执行，UI 线程**永久挂在别人的输入队列上**。而 UI 线程卡过 5 秒，
        // DWM 就会在全屏笔迹窗上盖一块点得中的幽灵窗，整个桌面点不动。
        // 那 4 个原本就在 UI 线程上的调用点（App.ShowMain / DialogForeground / QuickPanelWindow /
        // 看门狗）也一并走这条泵线程：它们要的是「抢到了吗」这个答案，不是「在 UI 线程上抢」。
        ForegroundNudge.Activate(hs[0]);
        Thread.Sleep(120);
        return IsForeground(process);
    }

    // 轮询等待某窗口出现：probe 真即走；最多等 timeoutSeconds 秒（0=只探一次）。探针/睡眠可注入便于测试。
    // cancel：本次运行的取消闸（动作组传入；开机清单等无闸路径传 null=只认全局急停）。
    public static WaitResult WaitAppWindow(int timeoutSeconds, int pollMs = 500, Func<bool>? probe = null, Action<int>? sleeper = null, RunCancel? cancel = null)
    {
        probe ??= () => false;
        sleeper ??= ms => Thread.Sleep(ms);
        if (pollMs < 1) pollMs = 500;
        // 封顶 24h 再 *1000：无上限的大值 *1000 会越界溢成负数 → maxWaitMs=0 → 只探一次就当窗口不存在、直接跳过等待。
        int maxWaitMs = Math.Clamp(timeoutSeconds, 0, 86_400) * 1000;
        bool present = false;
        int waited = 0;
        while (true)
        {
            try { present = probe(); } catch { present = false; }
            if (present) break;                       // 窗口出现即走
            if (waited >= maxWaitMs) break;           // 封顶：放弃
            if (RunCancel.Stopped(cancel)) break;     // 急停 或 本次运行被取消：不再干等
            sleeper(pollMs);
            waited += pollMs;
        }
        return new WaitResult { Present = present, WaitedMs = waited };
    }

    // 活：置前台+复核+发键，逐次重试至 timeoutSec。带不到前台就不发（绝不误发到别处）。
    // cancel：本次运行的取消闸。这里必须认它——用户按热键取消动作组后，若本方法还在重试，
    // 几秒后仍会把目标窗口拽到前台并把按键/文本打进去，取消就成了一句空话。
    public static bool WindowLogin(string process, string sendKey = "{ENTER}", int timeoutSec = 8, bool literal = false, RunCancel? cancel = null)
    {
        var deadline = DateTime.Now.AddSeconds(timeoutSec);
        while (DateTime.Now < deadline)
        {
            if (RunCancel.Stopped(cancel)) return false;   // 急停 或 本次运行被取消：等窗口/重试期间收到即弃发
            bool got = InjectionLock.Enter();
            try
            {
                if (SetForeground(process))
                {
                    Thread.Sleep(200);
                    if (IsForeground(process))   // 200ms 后焦点可能又被抢走 → 再复核
                    {
                        // 字面文本走 Unicode 注入（绕开输入法与键盘布局，见 Win32.SendUnicodeText）；
                        // 按键组合仍走 SendKeys——这个字段收的是 SendKeys **序列**（{ENTER}{TAB} 这种可以多个），
                        // 不是单个组合键，换不成 Win32.SendCombo。
                        // 代价要说清：SendKeys 的注入标志不归我们控制，所以 Win32.MakeKey 那套扩展键修正
                        // （方向键/Home/End 在远程桌面、虚拟机、DirectInput 里不被收成小键盘键）**覆盖不到这条路**。
                        // 热键与「发送按键」步骤走的是 KeyInput.SendKeyCombo → Win32.SendCombo，那条是修好的。
                        if (literal) Win32.SendUnicodeText(sendKey);
                        else WinSendKeys.SendWait(KeyCombo.ToSendKeysSequence(sendKey));
                        return true;
                    }
                }
            }
            finally { InjectionLock.Exit(got); }
            // 重试间隔走可中断延时（原来是死睡 500ms）：取消/急停当场醒，不用等这一觉睡完。
            if (!RunCancel.Sleep(cancel, 500)) return false;
        }
        return false;
    }

    // 活：逐字输入字面文本。process 空=发给当前焦点窗口；填了则先带到前台、复核在前台再输入。
    public static ActionResult SendText(string text, string process = "", RunCancel? cancel = null)
    {
        if (string.IsNullOrEmpty(text)) return ActionResult.Empty;
        if (!string.IsNullOrEmpty(process))
        {
            if (WindowLogin(process, text, 8, literal: true, cancel)) return ActionResult.Unver();
            // 急停/取消返回 false 时不误报「未能带到最前」——那是用户停的。
            if (!RunCancel.Stopped(cancel)) return ActionResult.Warn("Warn_TextSendFail", process);
            return ActionResult.Empty;
        }
        bool got = InjectionLock.Enter();
        try { Win32.SendUnicodeText(text); } finally { InjectionLock.Exit(got); }
        return ActionResult.Unver();
    }

    // 「恢复活动窗口」的基准：这一次运行**开始时**前台是哪个窗口。
    //
    // 为什么要一个基准，而不是「回到上一个前台」：这里几乎每个窗口动作都先 SetForeground(目标)
    // 再动手，所以一组「最小化 Slack、最小化 Discord」跑完，焦点落在哪儿全看 Windows 的心情。
    // 而用户想回去的是**他触发那一刻正在用的那个窗口**——那是运行开始前的前台，
    // 不是中途被抢来抢去的任何一个。
    //
    // ponytail: 一个静态字段，同时跑两个动作组会互相覆盖。做成 per-run 上下文要把它一路穿过
    // StepRunner / ActionGroupRunner 的签名；等真有人同时跑两组、两组还都用了这一步再说。
    private static IntPtr _foregroundBaseline;
    // 这一次运行若是**手势**触发，起笔点所在的顶层窗口；非手势触发为 Zero。
    // 窗口管理类「当前窗口」优先打它（手势划在谁身上就动谁，而不是动前台那个窗——按下被钩子吞掉，
    // 在后台窗口上起笔时前台仍是另一个窗）。与 _foregroundBaseline 一样是 per-run 的静态记账，
    // 每次运行开始由 MarkRunBaseline 覆写，所以不会跨运行串味；同时跑两个手势仍会互相覆盖，
    // 同 _foregroundBaseline 那条 ponytail 取舍，不另做 per-run 上下文。
    private static IntPtr _gestureOrigin;

    /// <summary>记下此刻的前台窗口，供之后的「恢复活动窗口」用。每次运行开始时在 UI 线程调一次。</summary>
    //
    // 走 ForegroundWindowOfOthers 而不是 GetForegroundWindow：前台是 Clockwork 自己时
    //（在编辑器里点「运行这一步」试跑就是这种情况）没有可回去的地方，记下来只会让那一步
    // 把编辑器又拽回前台——用户按的是「试一试」，得到的是窗口跳来跳去。
    public static void MarkForegroundBaseline() => MarkRunBaseline(IntPtr.Zero);

    /// <summary>运行开始时在 UI 线程记账：前台 baseline 始终记；手势触发额外记起笔窗口。</summary>
    //
    // 两个句柄在同一次调用里一起落定，免得「baseline 已更新、起笔窗口还是上一笔」的中间态。
    // gestureOrigin 传 Zero 即退化成纯前台 baseline（面板格子 / 热键 / 开机清单这些没有起笔点的路）。
    public static void MarkRunBaseline(IntPtr gestureOrigin)
    {
        _foregroundBaseline = Win32.ForegroundWindowOfOthers();
        _gestureOrigin = gestureOrigin;
    }

    // 回到基准那个窗口。最小化了的先还原——「恢复活动窗口」要的是能接着用，不是让它在任务栏上亮一下。
    private static WindowOutcome RestoreForeground()
    {
        var h = _foregroundBaseline;
        // 没有基准 = 触发那一刻前台就是 Clockwork 自己，或者压根没有前台窗口。
        // 「没有可回去的地方」和「回去失败了」是两回事，措辞在 StepRunner 那边分开。
        if (h == IntPtr.Zero || !Win32.IsWindowVisible(h)) return WindowOutcome.NoWindow;
        bool got = InjectionLock.Enter();
        try
        {
            if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
            Win32.SetForegroundWindow(h);
        }
        finally { InjectionLock.Exit(got); }
        Thread.Sleep(120);
        // 复核：SetForegroundWindow 常因前台锁定失败（同 SetForeground 那条），返回 true 不算数。
        return Win32.GetForegroundWindow() == h ? WindowOutcome.Ok : WindowOutcome.Failed;
    }

    // 「做成了没有」的复核轮询步长。复核本身借用 WaitAppWindow（同文件、有单测的那一份轮询实现）：
    // 它逐条做的就是这件事——探谓词、吞谓词异常、每轮查取消、超时返回假，没必要再写第二份。
    private const int VerifyStepMs = 100;

    // 活：统一的窗口动作——先激活/定位目标窗口，再执行操作，然后**复核是否真的做成了**。
    // 原来这里返回的是「操作了几个窗口」，把三件不同的事压成了同一个数字：没找到窗口 / 找到了但没做成 /
    // 动作名不认识。后果各不相同也各自要紧：close 数的是「发了几条 WM_CLOSE」而不是「关掉了几个」，
    // 于是它永远不可能报失败——应用弹「未保存」把关闭挡下来、或目标是提权窗口被 UIPI 拒收，日志照记 ✓，
    // 后面的锁屏/关机步骤还接着跑；activate 把「窗口存在」当成「已经到前台」，而 SetForeground 明明
    // 复核过并返回了 bool（见其注释：前台锁定常导致失败，必须复核）；手改 json 写错动作名则被报成
    // 「进程未运行」，把人指向完全错误的方向。故改成返回结局枚举，由引擎分别措辞。
    public static WindowOutcome WindowAction(string process, string op, string sendKey = "{ENTER}", int waitForWindowSeconds = 0, int postWindowDelaySeconds = 0, RunCancel? cancel = null)
    {
        // 「恢复活动窗口」先接住：它不看 process（那个参数对它没有意义），
        // 也不必等窗口出现——目标是一个早就记下来的句柄。
        if (op == "restore") return RestoreForeground();
        // 「当前窗口」只开放窗口状态那几个动作。
        // activate 对当前窗口是空操作；而 sendkey 更不能开——它的安全性建立在
        // 「抢到前台、200ms 后再复核一次焦点没被别人偷走」上，而「当前窗口」的复核
        // 恒等于拿前台跟前台自己比，永远为真，那句复核就成了摆设，
        // 于是那串按键（常常是密码）会打进任何一个碰巧抢走焦点的窗口。
        // 要往当前窗口发键，用「发送按键」步骤——它本来就是干这个的。
        if (IsCurrentWindow(process) && op is "sendkey" or "activate") return WindowOutcome.NotForCurrentWindow;
        if (op == "sendkey")
        {
            int to = waitForWindowSeconds > 0 ? waitForWindowSeconds : 8;
            if (WindowLogin(process, sendKey, to, cancel: cancel)) return WindowOutcome.Ok;
            return RunCancel.Stopped(cancel) ? WindowOutcome.Cancelled : WindowOutcome.Failed;
        }
        if (op is not ("close" or "minimize" or "maximize" or "activate" or "topmost")) return WindowOutcome.UnknownAction;

        // 等窗口出现（N=0 只探一次=早退语义）。activate 也要等：慢启动窗口没出来就 activate=空跑。
        var w = WaitAppWindow(waitForWindowSeconds, 500, () => Handles(process).Length > 0, cancel: cancel);
        if (!w.Present) return RunCancel.Stopped(cancel) ? WindowOutcome.Cancelled : WindowOutcome.NoWindow;
        // 窗口已在 → 出现后延迟（登录/主窗切换就绪）再动手；急停或取消打断延迟则不再动手。
        if (postWindowDelaySeconds > 0 && !RunCancel.Sleep(cancel, postWindowDelaySeconds * 1000L)) return WindowOutcome.Cancelled;

        // 句柄只枚举这一次。Handles() 是全进程快照（GetProcessesByName）+ EnumWindows 遍历全部顶层窗口，
        // 单次 5~20ms；若让复核轮询每 100ms 重新枚举一遍，一个窗口步骤最多要多付 9 次，还要乘以 Repeat。
        // 最小化/最大化不会让句柄失效，关闭则正好靠句柄失效来判定，所以全程用这一份快照就够。
        var hs = Handles(process);
        if (hs.Length == 0) return WindowOutcome.NoWindow;

        Func<bool> done;
        bool got = InjectionLock.Enter();
        try
        {
            switch (op)
            {
                case "close":
                    SetForeground(process); Thread.Sleep(120);
                    foreach (var h in hs) Win32.PostMessage(h, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    // PostMessage 只保证投递，唯一能证实的办法是回头看这些窗口还在不在。
                    done = () => hs.Any(h => !Win32.IsWindowVisible(h));
                    break;
                case "minimize":
                    SetForeground(process); Thread.Sleep(120);
                    foreach (var h in hs) Win32.ShowWindow(h, Win32.SW_MINIMIZE);
                    // 「至少有一个到位」而非「全部到位」：固定尺寸/无最小化框的窗口本就动不了，
                    // 要求全部到位会把这类正常情况报成失败。
                    done = () => hs.Any(Win32.IsIconic);
                    break;
                case "maximize":
                    SetForeground(process); Thread.Sleep(120);
                    foreach (var h in hs) Win32.ShowWindow(h, Win32.SW_MAXIMIZE);
                    done = () => hs.Any(Win32.IsZoomed);
                    break;
                // 置顶是**开关**：已经置顶就取消。手势和面板格子都是同一个入口反复触发的东西，
                // 「再来一次」自然该是撤销；分成置顶/取消两条动作，等于让用户为一件事配两条手势。
                //
                // 这一支不抢前台，也不需要——SetWindowPos 改的是窗口的 Z 序层级，与焦点无关。
                // 反倒是抢了前台更糟：你想钉住的往往是**副窗口**（参考文档、播放器），
                // 抢一次前台就把你正在打字的那个窗口顶掉了。
                case "topmost":
                {
                    bool on = (Win32.GetWindowLong(hs[0], Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0;
                    var layer = on ? Win32.HWND_NOTOPMOST : Win32.HWND_TOPMOST;
                    foreach (var h in hs)
                        Win32.SetWindowPos(h, layer, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                    // 复核读的是样式位本身：SetWindowPos 返回 true 只说明调用没出错，
                    // 提权窗口上这一下会「成功」却不生效（UIPI 拦在更下面）。
                    done = () => ((Win32.GetWindowLong(hs[0], Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0) != on;
                    break;
                }
                default:   // activate：SetForeground 自己已经复核过前台，不必再轮询
                    return SetForeground(process) ? WindowOutcome.Ok : WindowOutcome.Failed;
            }
        }
        finally { InjectionLock.Exit(got); }

        // 复核必须放在注入锁之外：它是纯只读的（IsIconic / IsZoomed / IsWindowVisible 都不注入任何东西），
        // 而 InjectionLock 的约定写在它自己的注释里——只包住单次注入动作（~120-200ms），不含等待。
        // 占着这把全进程信号量轮询一秒，会把别的动作组、提醒动作、热键文本步骤的注入全部堵在门外。
        return WaitAppWindow(1, VerifyStepMs, done, cancel: cancel).Present ? WindowOutcome.Ok : WindowOutcome.Failed;
    }
}
