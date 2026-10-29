using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「补发出去的按下必须配上抬起」这一组守卫。
//
// 背景（2026-10-28 17:54 现场）：右键在系统里卡在按下态（`GetAsyncKeyState(VK_RBUTTON)=0x8000`），
// 于是所有左键点击都被当成「按住右键时的左键」——任务栏不切换、点哪都没反应，键盘不受影响。
// 旧实现把抬起账记在**一个槽**里、而且先清零再注入：那一发被 UIPI 拒掉时账已经没了，
// 这个键就永远欠着（注入一次抬起即恢复）。这里钉住四件事：
//   1. 账按次数记（两条补发挨得近时，后一条不会覆盖前一条）；
//   2. 发失败不销账，交给看门狗退避重试；
//   3. **欠着的抬起在拆卸之后也有人还**：看门狗不跟着钩子一起死（否则退出时那笔账就没人还了）；
//   4. **「交还给程序的那次按下」不靠计时器兜底**——用户按着不放多久都可能，
//      计时器一响就会给一个还按着的键补一个抬起（凭空多出一次右键）。它由下一次右键按下认领。
public class ReplayUpSettlementTests
{
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    // holdMs=0 且 gesture:null：这条钩子只当通道，不对真实输入动手（与 MouseHookThreadTests 同一条纪律）。
    private static IntPtr Send(MouseHook hook, int msg, int x, int y, IntPtr extra)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y, DwExtraInfo = extra }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static long HandedAt(MouseHook hook)
        => (long)typeof(MouseHook).GetField("_handedRightDownAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook)!;

    private static void SetHandedAt(MouseHook hook, long v)
        => typeof(MouseHook).GetField("_handedRightDownAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(hook, v);

    private static long RetryAt(MouseHook hook)
        => (long)typeof(MouseHook).GetField("_upRetryAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook)!;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Native"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "app" }.Concat(parts).ToArray()));

    private static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i >= 0, signature + " 不见了");
        int j = src.IndexOf('{', i);
        int depth = 0;
        for (int k = j; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(j, k - j + 1);
            }
        }
        throw new InvalidOperationException("不平衡的括号");
    }

    // 只留代码行：本文件的注释里保留着旧实现的对照说明（`_clickUpDue` 之类），
    // 按整段文本断言会把解释误当成实现。
    private static string CodeOnly(string body)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in body.Split('\n'))
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            sb.AppendLine(i >= 0 ? line.Substring(0, i) : line);
        }
        return sb.ToString();
    }

    // 真的抬起才算配对；自己补发的抬起（带 InjectTag）不算——否则看门狗会被自己的回发放行骗过去。
    // 让它变红：把那句清除挪到 InjectTag 的早退之前，或者删掉。
    [Fact]
    public void A_real_right_up_pairs_the_handed_down_and_a_synthetic_one_does_not()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null);
        SetHandedAt(hook, Environment.TickCount64);
        Send(hook, WM_RBUTTONUP, 100, 100, Win32.InjectTag);
        Assert.NotEqual(0, HandedAt(hook));
        Send(hook, WM_RBUTTONUP, 100, 100, IntPtr.Zero);
        Assert.Equal(0, HandedAt(hook));
    }

    // 发失败不销账，**而且欠着的抬起在拆卸之后还有人还**：
    // 看门狗不跟着钩子一起死（拆卸时只在账已清零才收工）。
    // 让它变红的改动：把认领（TryClaim）换成「先减再看」；删掉 ScheduleUpRetry；
    // 把拆卸那句 `_upWatchdog?.Dispose()` 移回无条件执行。
    [Fact]
    public void A_failed_up_send_keeps_the_debt_and_the_watchdog_outlives_the_hook()
    {
        var src = Source("Native", "MouseHook.cs");
        var up = Body(src, "private void ReplayUp(int maxSends");
        // 发送失败 → 那笔账还回去（不销账），并排下一次退避重试。
        Assert.Contains("Interlocked.Increment(ref _owedRightUps);", up);
        Assert.Contains("ScheduleUpRetry();", up);
        // 旧的单槽台账不许回来：它撑不住两条补发挨在一起（后一条覆盖前一条）。
        Assert.DoesNotContain("_clickUpDue", CodeOnly(src));
        // **看门狗与补发线程同起同收**。
        Assert.Contains("_upWatchdog ??= new System.Threading.Timer(_ => UpWatchdogTick(), null, UpWatchdogPeriodMs, UpWatchdogPeriodMs);",
            Body(src, "private void StartReplayWorker()"));
        // **欠着的抬起在拆卸之后也有人还**：看门狗必须在「欠账已清零」的判断**之后**才销毁。
        // 顺序反了就等于「先杀还账的人」——那个键就卡在全系统按下态。
        // 这里只能钉顺序而不是钉行为：行为级的验证要真的发一次抬起，
        // 而那会在开发者桌面上注入一个真的右键抬起（见 MouseHookThreadTests 的纪律）。
        var teardown = Body(src, "private void Teardown()");
        int guard = teardown.IndexOf("Volatile.Read(ref _owedRightUps) <= 0", StringComparison.Ordinal);
        int dispose = teardown.IndexOf("_upWatchdog?.Dispose();", StringComparison.Ordinal);
        Assert.True(guard >= 0, "拆卸时看不到「欠账已清零」的判断：看门狗会被无条件销毁");
        Assert.True(dispose > guard, "看门狗必须在欠账清零的判断之后才销毁");
        // 看门狗自己也要能把欠账还掉（不只是记账）。
        Assert.Contains("ReplayUp();", Body(src, "private void UpWatchdogTick()"));
        // **不许跨 SendInput 持锁**：钩子线程也会走 ReplayUp（ApplyYield / SettleOwedKeys），
        // 它在锁上等着就越过了 LowLevelHooksTimeout，Windows 会静默摘掉这个钩子。
        Assert.DoesNotContain("lock (", up);
    }

    // 欠账跨代际存在（自救重装不能把它清零）；而「交还给程序的那次按下」
    // **不能**由计时器兜底——用户按着不放多久都可能，计时器一响就会给一个还按着的键
    // 补一个抬起（目标程序先收到一次提前的松开，稍后用户真松手时又来一条野抬起，
    // 凭它合成一个莫名菜单）。它由下一次右键按下认领。
    // 让它变红的改动：在 ResetGenerationMetrics 里清 _owed*；把看门狗改回「按住了 5 秒就补抬起」。
    [Fact]
    public void The_owed_ups_survive_a_generation_and_a_handed_down_is_claimed_by_the_next_press()
    {
        var src = Source("Native", "MouseHook.cs");
        var reset = CodeOnly(Body(src, "private void ResetGenerationMetrics()"));
        Assert.DoesNotContain("_owedRightUps", reset);
        Assert.DoesNotContain("_owedMiddleUps", reset);
        Assert.Contains("Interlocked.Exchange(ref _handedRightDownAt, Environment.TickCount64)",
            Body(src, "private void ApplyYield(bool on)"));
        // **看门狗里不许有「按住了多久就补一个抬起」那条路**。
        var tick = Body(src, "private void UpWatchdogTick()");
        Assert.DoesNotContain("SendRightUpTagged", tick);
        Assert.DoesNotContain("HandedDownLingerMs", CodeOnly(src));
        // 认领的那一档在回调里：下一次右键按下补一个抬起（走补发线程，不在回调里注入）。
        Assert.Contains("EnqueueReplay(3)", CodeOnly(src));
        // ApplyYield 只在闸门还开着（= 真抬起还没到）时才把按下还回去。
        Assert.Contains("if (_gesture?.Pending == true && SendTimed(", CodeOnly(src));
    }

    // 让路中：抬起若对应的按下被手势吞了（没进系统），原样放行就是一条**野抬起**
    //（没有配对按下）——DefWindowProc 会凭它合成一个莫名的右键菜单。
    // 所以让路路径必须吞掉它、补一次完整的点击，并且把闸门关上。
    //
    // 闸门关上还有第二个用处：ApplyYield 据此知道真抬起已经过去，
    // 那时**不能再补一个按下**（按下从没进过系统，补上去就是没有配对抬起的按下）。
    //
    // 让它变红的改动：把让路那一段改回无条件 `return Chain(...)`（不重置闸门、不补点击）。
    [Fact]
    public void Yielding_does_not_pass_through_a_right_up_whose_down_was_swallowed()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true);

        // 先按一下：手势把按下吞了（return 1），系统那边没有配对的按下。
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero));
        Assert.True(gate.Pending);

        // 进入让路（测试里没装钩子，SetYielding 只落标志不投消息）。
        hook.SetYielding(true);
        Assert.True(hook.Yielding);

        // 抬起必须被吞掉并补一次完整的点击，而不是原样放行。
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONUP, 100, 100, IntPtr.Zero));
        // 闸门关上 = 真抬起已经过去 = ApplyYield 不再补一个按下。
        Assert.False(gate.Pending);
    }

    // 「下一次右键按下认领那笔丢失的抬起」：认领之后必须真的排进补发队列。
    // **排不进去时还要排下一次重试**——否则那笔账没人还 = 那个键卡在全系统按下态
    //（2026-10-28 那次整机左键失效的同一类事故）。
    // 让它变红的改动：把 EnqueueReplay(3) 的返回值丢掉（账没主人，看门狗也不会接手）。
    [Fact]
    public void The_next_right_press_claims_a_lost_handed_down()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true);
        SetHandedAt(hook, Environment.TickCount64);
        long queuedBefore = hook.ReplayQueued;

        Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero);

        Assert.Equal(0, HandedAt(hook));                     // 已被认领
        // 认领之后那笔账必须**有人负责**：排进补发队列（生产里补发线程在跑），
        // 或者——队列没有活的消费者时——排下一次重试（看门狗接手）。
        // 单测里没装钩子（没有补发线程），所以走的是第二条路；两条都堵上才是「没人还」。
        bool queued = hook.ReplayQueued == queuedBefore + 1;
        bool scheduled = RetryAt(hook) != 0;
        Assert.True(queued || scheduled, "认领了却没有任何人负责那笔账——那个键会卡在按下态");

        gate.Reset();   // 别让 Dispose 走还键那一条注入路（那会真的注入一次右键按下）
    }

    // 取大（CAS）：较小的值不许盖掉较大的峰值。
    // 为什么要单独钉：让路判据的输入就是这两个峰值——盖掉一次 = 一次超预算回调没被看见，
    // 而让路正是为了在那种时候收手。
    [Fact]
    public void The_congestion_peak_never_gets_smaller()
    {
        var m = Array.Find(typeof(MouseHook).GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
            x => x.Name == "MaxInto" && x.GetParameters().Length == 2
                 && x.GetParameters()[1].ParameterType == typeof(int))!;
        // ref 参数的写回落在实参数组上，所以要用同一组来回读。
        object[] args = { 412, 900 };
        m.Invoke(null, args);
        Assert.Equal(900, (int)args[0]);   // 更大的值盖上去
        args[1] = 100;
        m.Invoke(null, args);
        Assert.Equal(900, (int)args[0]);   // 更小的值盖不上去
    }

    // 诊断必须真的接进错误日志——否则「重试过几次、为什么重试」在事后只能靠猜。
    [Fact]
    public void The_watchdog_diagnostics_reach_the_error_log()
    {
        var coord = Source("Services", "GestureCoordinator.cs");
        Assert.Contains("diag: AppendErrorLog", coord);
    }
}
