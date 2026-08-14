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
    Cancelled,      // 急停或本次运行被取消——不是故障，静默
}

// 窗口动作、等待、置前台、文本/登录注入。
// 活交互（真实窗口/前台/注入）不单测；仅 WaitAppWindow 注入探针/睡眠可测。
public static class WindowManager
{
    // 目标进程的可见顶层窗口句柄（先把进程标识归一为裸名，与编辑器保存口径一致）。
    public static IntPtr[] Handles(string process) => Win32.WindowsForProcess(StepHelpers.ToProcessName(process));

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
        // 最小化窗口 SetForegroundWindow 后仍最小化 → 先还原再置前台。
        if (Win32.IsIconic(hs[0])) { Win32.ShowWindow(hs[0], Win32.SW_RESTORE); Thread.Sleep(120); }
        Win32.SetForegroundWindow(hs[0]);
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
            if (!RunCancel.Stopped(cancel)) return ActionResult.Warn(Strings.Lf("Warn_TextSendFail", process));
            return ActionResult.Empty;
        }
        bool got = InjectionLock.Enter();
        try { Win32.SendUnicodeText(text); } finally { InjectionLock.Exit(got); }
        return ActionResult.Unver();
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
        if (op == "sendkey")
        {
            int to = waitForWindowSeconds > 0 ? waitForWindowSeconds : 8;
            if (WindowLogin(process, sendKey, to, cancel: cancel)) return WindowOutcome.Ok;
            return RunCancel.Stopped(cancel) ? WindowOutcome.Cancelled : WindowOutcome.Failed;
        }
        if (op is not ("close" or "minimize" or "maximize" or "activate")) return WindowOutcome.UnknownAction;

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
