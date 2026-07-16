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

// 窗口动作、等待、置前台、文本/登录注入。
// 活交互（真实窗口/前台/注入）不单测；仅 WaitAppWindow 注入探针/睡眠可测。
public static class WindowManager
{
    // 目标进程的可见顶层窗口句柄（先把进程标识归一为裸名，与编辑器保存口径一致）。
    public static IntPtr[] Handles(string process) => Win32.WindowsForProcess(StepHelpers.ToProcessName(process));

    public static int CloseWindow(string process)
    {
        int n = 0;
        foreach (var h in Handles(process)) { Win32.PostMessage(h, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); n++; }
        return n;
    }

    public static int MinimizeWindow(string process)
    {
        int n = 0;
        foreach (var h in Handles(process)) { Win32.ShowWindow(h, Win32.SW_MINIMIZE); n++; }
        return n;
    }

    public static int MaximizeWindow(string process)
    {
        int n = 0;
        foreach (var h in Handles(process)) { Win32.ShowWindow(h, Win32.SW_MAXIMIZE); n++; }
        return n;
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
        // 最小化窗口 SetForegroundWindow 后仍最小化 → 先还原再置前台。
        if (Win32.IsIconic(hs[0])) { Win32.ShowWindow(hs[0], Win32.SW_RESTORE); Thread.Sleep(120); }
        Win32.SetForegroundWindow(hs[0]);
        Thread.Sleep(120);
        return IsForeground(process);
    }

    // 轮询等待某窗口出现：probe 真即走；最多等 timeoutSeconds 秒（0=只探一次）。探针/睡眠可注入便于测试。
    public static WaitResult WaitAppWindow(int timeoutSeconds, int pollMs = 500, Func<bool>? probe = null, Action<int>? sleeper = null)
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
            if (StopSignal.IsRequested) break;        // 急停：不再干等
            sleeper(pollMs);
            waited += pollMs;
        }
        return new WaitResult { Present = present, WaitedMs = waited };
    }

    // 活：置前台+复核+发键，逐次重试至 timeoutSec。带不到前台就不发（绝不误发到别处）。
    public static bool WindowLogin(string process, string sendKey = "{ENTER}", int timeoutSec = 8, bool literal = false)
    {
        var deadline = DateTime.Now.AddSeconds(timeoutSec);
        while (DateTime.Now < deadline)
        {
            if (StopSignal.IsRequested) return false;   // 急停：等窗口/重试期间收到即弃发
            bool got = InjectionLock.Enter();
            try
            {
                if (SetForeground(process))
                {
                    Thread.Sleep(200);
                    if (IsForeground(process))   // 200ms 后焦点可能又被抢走 → 再复核
                    {
                        var seq = literal ? KeyCombo.ToSendKeysLiteral(sendKey) : KeyCombo.ToSendKeysSequence(sendKey);
                        WinSendKeys.SendWait(seq);
                        return true;
                    }
                }
            }
            finally { InjectionLock.Exit(got); }
            Thread.Sleep(500);
        }
        return false;
    }

    // 活：逐字输入字面文本。process 空=发给当前焦点窗口；填了则先带到前台、复核在前台再输入。
    public static ActionResult SendText(string text, string process = "")
    {
        if (string.IsNullOrEmpty(text)) return ActionResult.Empty;
        var seq = KeyCombo.ToSendKeysLiteral(text);
        if (string.IsNullOrEmpty(seq)) return ActionResult.Empty;
        if (!string.IsNullOrEmpty(process))
        {
            if (WindowLogin(process, text, 8, literal: true)) return ActionResult.Unver();
            // 急停返回 false 时不误报「未能带到最前」——那是用户停的。
            if (!StopSignal.IsRequested) return ActionResult.Warn(Strings.Lf("Warn_TextSendFail", process));
            return ActionResult.Empty;
        }
        bool got = InjectionLock.Enter();
        try { WinSendKeys.SendWait(seq); } finally { InjectionLock.Exit(got); }
        return ActionResult.Unver();
    }

    // 活：统一的窗口动作——先激活/定位目标窗口，再执行操作。返回「操作了几个窗口」（sendkey：1=已发送 0=没发），
    // 由引擎按 op 解读三态。
    public static int WindowAction(string process, string op, string sendKey = "{ENTER}", int waitForWindowSeconds = 0, int postWindowDelaySeconds = 0)
    {
        if (op == "sendkey")
        {
            int to = waitForWindowSeconds > 0 ? waitForWindowSeconds : 8;
            return WindowLogin(process, sendKey, to) ? 1 : 0;
        }
        if (op is "close" or "minimize" or "maximize" or "activate")
        {
            // 等窗口出现（N=0 只探一次=早退语义）。activate 也要等：慢启动窗口没出来就 activate=空跑。
            var w = WaitAppWindow(waitForWindowSeconds, 500, () => Handles(process).Length > 0);
            if (!w.Present) return 0;
            // 窗口已在 → 出现后延迟（登录/主窗切换就绪）再动手；急停打断延迟则不再动手。
            if (postWindowDelaySeconds > 0 && !StopSignal.InterruptibleSleep(postWindowDelaySeconds * 1000L)) return 0;
        }
        bool got = InjectionLock.Enter();
        try
        {
            switch (op)
            {
                case "close": SetForeground(process); Thread.Sleep(120); return CloseWindow(process);
                case "minimize": SetForeground(process); Thread.Sleep(120); return MinimizeWindow(process);
                case "maximize": SetForeground(process); Thread.Sleep(120); return MaximizeWindow(process);
                case "activate":
                    var hs = Handles(process);
                    if (hs.Length > 0) SetForeground(process);
                    return hs.Length;
                default: return 0;
            }
        }
        finally { InjectionLock.Exit(got); }
    }
}
