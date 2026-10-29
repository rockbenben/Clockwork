using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「补发出去的按下必须配上抬起」这一组守卫。
//
// 背景（2026-10-28 17:54 现场）：右键在系统里卡在按下态（`GetAsyncKeyState(VK_RBUTTON)=0x8000`），
// 于是所有左键点击都被当成「按住右键时的左键」——任务栏不切换、点哪都没反应，键盘不受影响。
// 旧实现把抬起账记在**一个槽**里、而且先清零再注入：那一发被 UIPI 拒掉时账已经没了，
// 这个键就永远欠着（注入一次抬起即恢复）。这里钉住四件事：
//   1. 账按笔记（两条补发挨得近时，后一条不会覆盖前一条）；
//   2. 发失败不销账，交给看门狗退避重试；
//   3. **欠着的抬起在拆卸之后也有人还**：看门狗不跟着钩子一起死（否则退出时那笔账就没人还了）；
//   4. **「交还给程序的那次按下」不靠计时器兜底**——用户按着不放多久都可能，
//      计时器一响就会给一个还按着的键补一个抬起（凭空多出一次右键）。它由下一次右键按下认领。
//
// 账目的形状（谁欠谁、欠几笔、退避到第几次、配对期）在 **Core.MouseUpLedger** 里，
// 那一整块的行为级断言在 MouseUpLedgerTests —— 那里不必注入一个真的鼠标抬起就能验。
// 这个文件只留**接线**的断言：钩子这一侧有没有把账记上、有没有排重试、看门狗有没有同起同收。
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

    private static void SetHandedAt(MouseHook hook, long v)
        => hook.UpLedger.MarkHandedDown(v);

    private static long RetryAt(MouseHook hook) => hook.UpLedger.RetryAtMs;

    private static long OwedRightUps(MouseHook hook)
        => hook.UpLedger.OwedCountOf(MouseUpButton.Right);

    private static void SetOwedRightClick(MouseHook hook, bool v)
        => typeof(MouseHook).GetField("_owedRightClick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(hook, v);

    /// <summary>把账上的欠账还清（只清账，不发任何注入）。</summary>
    ///
    /// Dispose 会走 Teardown → SettleOwedKeys，而它会**真的注入一次抬起**——
    /// 欠着的抬起是系统里真按下去的那个键，拆卸时必须还上（这是设计，不是缺陷）。
    /// 但单测不许在开发者的桌面上注入真的鼠标事件（见 MouseHookThreadTests 的纪律），
    /// 所以这里先把账还清。账本公开了接口，做这件事不必再靠反射。
    private static void ClearOwedUps(MouseHook hook)
    {
        while (hook.UpLedger.TryClaimOldest(Environment.TickCount64, out var unit) == MouseUpClaim.Claimed)
            hook.UpLedger.Settle(unit);
    }

    // 抠出「条件里含某个片段的那个 if 块」（带大括号匹配）。
    // 只比两处字面量的先后是不够的：「销毁」被挪到 if 外面、或者那个判断被反过来，
    // 两处字面量都还在原位——而看门狗照样会在有欠账时被销毁。
    private static string IfBlockContaining(string src, string needle)
    {
        int at = src.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到含「" + needle + "」的 if");
        int open = src.LastIndexOf("if (", at, StringComparison.Ordinal);
        Assert.True(open >= 0, "找不到对应的 if");
        int brace = src.IndexOf('{', open);
        Assert.True(brace >= 0, "找不到 if 的左大括号");
        int depth = 0;
        for (int k = brace; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(open, k - open + 1);
            }
        }
        throw new InvalidOperationException("大括号不平衡");
    }

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

    // 只留代码行：被它处理的那些源码里保留着历史对照说明（`_clickUpDue` 之类），
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
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, modifierHeld: () => false);
        SetHandedAt(hook, Environment.TickCount64);
        Send(hook, WM_RBUTTONUP, 100, 100, Win32.InjectTag);
        Assert.True(hook.UpLedger.HasHandedDown);
        Send(hook, WM_RBUTTONUP, 100, 100, IntPtr.Zero);
        Assert.False(hook.UpLedger.HasHandedDown);
    }

    // 欠着的抬起在拆卸之后**也有人还**：看门狗不跟着钩子一起死（拆卸时只在账已清零才收工）。
    // 而账的形状（发失败不销账、退避重试、在途的那几笔）现在由账本自己保证，
    // 行为级的断言在 MouseUpLedgerTests；这里钉的是**接线**：
    // 看门狗与补发线程同起同收，拆卸**不**自己销毁看门狗。
    // 让它变红的改动：删掉看门狗的定时器；把拆卸那句 `_upWatchdog?.Dispose()` 移回无条件执行。
    [Fact]
    public void The_watchdog_outlives_the_hook_and_is_not_destroyed_by_the_teardown()
    {
        var src = Source("Native", "MouseHook.cs");
        // **看门狗与补发线程同起同收**。
        Assert.Contains("_upWatchdog ??= new System.Threading.Timer(_ => UpWatchdogTick(), null, UpWatchdogPeriodMs, UpWatchdogPeriodMs);",
            Body(src, "private void StartReplayWorker()"));
        // **拆卸不自己销毁看门狗**：在途的那一笔一失败就把账还回去并排重试，
        // 看门狗要是已经没了，那个重试没人认领 → 那个键卡在全系统按下态。
        Assert.DoesNotContain("_upWatchdog?.Dispose();", Body(src, "private void Teardown()"));
        Assert.DoesNotContain("Interlocked.Exchange(ref _upWatchdog, null)", Body(src, "private void Teardown()"));
        // 看门狗自己也要能把欠账还掉（不只是记账）。
        Assert.Contains("SettleOwedUps(", Body(src, "internal void UpWatchdogTick()"));
        // 旧的单槽台账不许回来：它撑不住两条补发挨在一起（后一条覆盖前一条）。
        // 钉的是**字段声明**而不是那个名字：名字如今只作为历史说明留在注释里，
        // 而名字出现在代码里才说明那个槽又回来了。
        Assert.DoesNotMatch(@"private\s+\w+\s+_clickUpDue", src);
    }

    // 钩子线程上的**发送预算**：钩子线程正是低级钩子回调被派发的地方，
    // 而一次 SendInput 实测能到 313ms——连发几个就越过 LowLevelHooksTimeout，
    // Windows 会静默摘钩（钩子一摘，手势就没了）。
    //
    // 这两条路在单测里**不可达**（它们跑在钩子线程上，而单测不装钩子），
    // 所以只能钉形状：让路时结账与拆卸时结清都只许发一个。
    //
    // 让它变红的改动：把这两处的 1 改成 5（钩子线程上一次发五个抬起）。
    [Fact]
    public void The_hook_thread_never_pays_more_than_one_up_at_a_time()
    {
        var src = Source("Native", "MouseHook.cs");
        Assert.Contains("SettleOwedUps(1)", Body(src, "private void ApplyYield(bool on)"));
        var settle = Body(src, "private void SettleOwedKeys()");
        Assert.Contains("SettleOwedUps(1)", settle);
        // 拆卸那一路一共两处（一次 + 重试循环里那一处），每处只发一个。
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(settle, @"SettleOwedUps\(1\)").Count);
    }

    // 看门狗退休判据里的另外两个合取项：补发线程还活着时不许退休（它正要记一笔新账），
    // 而**代号**必须对得上（换看门狗要换号：旧号的那一拍还在路上，它不能把新这只也退休掉）。
    //
    // 让它变红的改动：删掉这两个合取项。
    [Fact]
    public void The_watchdog_only_retires_when_the_replay_thread_is_done_and_its_generation_matches()
    {
        var src = Source("Native", "MouseHook.cs");
        var guard = IfBlockContaining(Body(src, "internal void UpWatchdogTick()"), "ShouldRetireWatchdog");
        Assert.Contains("(replay == null || !replay.IsAlive)", guard);
        Assert.Contains("_upWatchdogGen) == gen", guard);
    }

    // 欠账跨代际存在（自救重装不能把它清零）；而「交还给程序的那次按下」
    // **不能**由计时器兜底——用户按着不放多久都可能，计时器一响就会给一个还按着的键
    // 补一个抬起（目标程序先收到一次提前的松开，稍后用户真松手时又来一条野抬起，
    // 凭它合成一个莫名菜单）。它由下一次右键按下认领。
    // 让它变红的改动：在 ResetGenerationMetrics 里清掉账本；把看门狗改回「按住了 5 秒就补抬起」。
    [Fact]
    public void The_owed_ups_survive_a_generation_and_a_handed_down_is_claimed_by_the_next_press()
    {
        var src = Source("Native", "MouseHook.cs");
        // 账本跨代际不清零：那是「已经送进系统的按下」，跨代际仍然欠着。
        Assert.DoesNotContain("_upLedger", CodeOnly(Body(src, "private void ResetGenerationMetrics()")));
        Assert.Contains("_upLedger.MarkHandedDown(Environment.TickCount64)",
            Body(src, "private void ApplyYield(bool on)"));
        // **看门狗里不许有「按住了多久就补一个抬起」那条路**。
        Assert.DoesNotContain("SendRightUpTagged", Body(src, "internal void UpWatchdogTick()"));
        // 认领的那一档在回调里：下一次右键按下补一个抬起（走补发线程，不在回调里注入）。
        Assert.Contains("EnqueueReplay(ReplayKind.ReleaseOwed", CodeOnly(src));
        // ApplyYield 只在闸门还开着（= 真抬起还没到）时才把按下还回去。
        Assert.Contains("if (_gesture?.Pending == true && SendTimed(", CodeOnly(src));
    }

    // 钩子拆掉之后，那笔「交还给程序的按下」必须**清掉**：
    // 不清的话，下一次右键按下会认领一笔已经还过的账，补出一个没有配对按下的抬起
    // → DefWindowProc 凭它合成一个莫名的右键菜单（用户报的「凭空多出一次右键」）。
    //
    // 欠着的抬起是另一回事：它随拆卸被**还掉**（还不上才留给看门狗，见
    // The_watchdog_outlives_the_hook_and_is_not_destroyed_by_the_teardown）——
    // 而还账要真的注入一次抬起，所以这条只验「交还的按下被清掉」，不制造欠账。
    // 让它变红的改动：拆卸时把交还的按下留着。
    [Fact]
    public void Tearing_the_hook_down_clears_the_handed_down()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => false);
        hook.UpLedger.MarkHandedDown(Environment.TickCount64);
        Assert.True(hook.UpLedger.HasHandedDown);

        typeof(MouseHook).GetMethod("Teardown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(hook, null);

        Assert.False(hook.UpLedger.HasHandedDown);
    }

    // **这四条不变式在把账目抽成状态机的时候一度失去了守卫**——每一条都有变异测试证明：
    // 改坏它们而整套测试仍然全绿。现在把它们钉回去：
    //
    //   1. **发失败不销账**：那一笔账还挂在账上（销账 = 把那次抬起丢了
    //      = 那个键卡在全系统按下态 = 2026-10-28 那次整机左键失效）。
    //   2. **不许跨 SendInput 持锁**：钩子线程在锁上等着就越过了 LowLevelHooksTimeout，
    //      Windows 会静默摘掉这个钩子（钩子一摘，手势就没了）。
    //   3. **看门狗要退休，而且只在退休条件成立的时候退休**：账清零 + 没有在途的那几笔。
    //   4. **补发线程认领它自己那一笔**（右键与中键各一条——只盯右键那一档，
    //      中键那条路照样可以改回「领最老的一笔」，而中键卡住 = 中键拖拽永远在拖）。
    //
    // 钉形状而不钉行为：行为级的验证要真的发一次抬起，
    // 而那会在开发者桌面上注入一个真的右键抬起（见 MouseHookThreadTests 的纪律）。
    [Fact]
    public void The_send_path_keeps_the_debt_on_failure_and_never_holds_a_lock_across_the_injection()
    {
        var src = Source("Native", "MouseHook.cs");
        var up = Body(src, "private bool SendUpUnit(");
        // 1. 发失败不销账：成功才结清，失败要把账还回去。
        Assert.Contains("_upLedger.Settle(unit)", up);
        Assert.Contains("_upLedger.ReOwe(unit)", up);
        // 2. **不许跨 SendInput 持锁**。
        Assert.DoesNotContain("lock (", up);
        // 3. 看门狗的退休：销毁必须落在那个判断**里面**（只比两处字面量的先后是不够的）。
        //    而且要**先把引用摘走再释放**：反过来（先 Dispose 再置 null）的话，
        //    那一拍若正好被抢占在两句之间，而此刻装钩子那条路看到的是一个**非空但已释放**的
        //    定时器，它就不新建；而那一拍回来再把字段置空——看门狗就此彻底没有了，
        //    之后的欠账没人还（那个键卡在全系统按下态）。
        var guard = IfBlockContaining(Body(src, "internal void UpWatchdogTick()"), "ShouldRetireWatchdog");
        Assert.Contains("Interlocked.Exchange(ref _upWatchdog, null)", guard);
        Assert.Contains("Dispose();", guard);
        // 4. 补发线程认领它自己那一笔（两档点击各一次）。
        var loop = Body(src, "private void ReplayLoop()");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(loop, @"TryClaimToken\(unit\.Token").Count);
        // 5. **认领之后再核一次配对闸**：认领与核之间补发线程可能刚好开始配对，
        //    这时这一笔属于它，不该由我们发——发出去就成了 0ms 的零长度点击
        //    （Chromium 那类程序直接当噪声丢掉，症状是「这一下右键凭空消失」）。
        //    核出来是配对期就把账还回去，下一拍再说。
        var pump = Body(src, "internal void SettleOwedUps(int maxSends)");
        Assert.Contains("if (_upLedger.PairingActive(", pump);
        Assert.Contains("_upLedger.ReOwe(unit);", pump);
        // 6. **拆卸时的还键也要记下「交还给程序的那次按下」**：那一次按下是真的进了系统，
        //    它的抬起若再被安全桌面吃掉，就欠着一笔——不记的话那个键卡在全系统按下态。
        Assert.Contains("_upLedger.MarkHandedDown", Body(src, "private void SettleOwedKeys()"));
        // 7. **「还那一笔欠账」要带上那一笔的编号**：不带的话补发线程认领不到它。
        Assert.Contains("EnqueueReplay(ReplayKind.ReleaseOwed, unit.Token)", src);
    }

    // 钩子拆过又装回来（同一个对象）：**「已拆完」必须先清回去，再换补发线程**。
    // 换线程要花几毫秒（换队列、起线程），而看门狗每一拍都在跑：标志要是还停在「已拆完」，
    // 那一拍就会看着「账清零 + 补发线程还没活」判定可以退休——而补发线程马上就要记一笔新账
    // （按下已经送进系统、抬起还没发），那一笔就再没人还了（那个键卡在全系统按下态）。
    //
    // 让它变红的改动：把 `Volatile.Write(ref _tornDown, 0)` 挪到换线程之后。
    [Fact]
    public void Reinstalling_clears_the_torn_down_flag_before_the_replay_thread_is_swapped()
    {
        var src = Source("Native", "MouseHook.cs");
        var body = Body(src, "private void StartReplayWorker()");
        int cleared = body.IndexOf("Volatile.Write(ref _tornDown, 0)", StringComparison.Ordinal);
        int swapped = body.IndexOf("Volatile.Write(ref _replayThread", StringComparison.Ordinal);
        Assert.True(cleared >= 0 && swapped >= 0, "两处都要在");
        Assert.True(cleared < swapped,
            "「已拆完」必须先清回去：换线程的那几毫秒里看门狗每一拍都在跑，"
            + "标志还停在「已拆完」的话它会先走，而补发线程随后记下的那一笔就没人还了");
    }

    // **放行的那一笔按下不许认领**——它自己的抬起会原样进系统，
    // 那个抬起已经把卡住的键松开了；这时再补一个抬起就是没有配对按下的抬起，
    // → DefWindowProc 凭它合成一个莫名的右键菜单。
    //
    // 四条放行的路各钉一行（认领只许发生在「这一笔按下真的被我们吞了」那一档）：
    //   · 前台刚切过去 / 探针答不接管（`_mayEngage` 答 false）；
    //   · 这个程序在用户自己的「不接管手势」名单里；
    //   · 让路中（右键一个字节都不碰）；
    //   · 压根没配手势（`_gesture` 为 null）。
    //
    // 让它变红的改动：把认领从「按下真被吞」那一档挪到「任何一次右键按下」。
    [Fact]
    public void A_press_that_is_passed_through_does_not_claim_the_lost_handed_down()
    {
        // 前台不接管（刚切过去的窗口）：整笔放行。
        using (var hook = new MouseHook(0, () => { }, a => a(), gesture: new GestureGate(_ => true, 40),
                   mayEngage: _ => false, procNameForPid: _ => "notepad", canInject: () => true,
                   modifierHeld: () => false))
            AssertPassedThrough(hook);

        // 用户自己的「不接管手势」名单里：整笔放行。
        using (var hook = new MouseHook(0, () => { }, a => a(), gesture: new GestureGate(_ => true, 40),
                   mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
                   modifierHeld: () => false, ignoredApp: _ => true))
            AssertPassedThrough(hook);

        // 让路中：右键一个字节都不碰。
        using (var hook = new MouseHook(0, () => { }, a => a(), gesture: new GestureGate(_ => true, 40),
                   mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
                   modifierHeld: () => false))
        {
            hook.SetYielding(true);
            AssertPassedThrough(hook);
        }

        // 压根没配手势：钩子只当通道。
        using (var hook = new MouseHook(0, () => { }, a => a(), gesture: null, modifierHeld: () => false))
            AssertPassedThrough(hook);
    }

    // 一次「放行」的右键按下：标记留着、账上不多一笔。
    private static void AssertPassedThrough(MouseHook hook)
    {
        hook.UpLedger.MarkHandedDown(Environment.TickCount64);
        long owedBefore = hook.UpLedger.OwedCount;

        Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero);

        Assert.True(hook.UpLedger.HasHandedDown);            // 标记还在（没认领）
        Assert.Equal(owedBefore, hook.UpLedger.OwedCount);   // 也没有多记账
    }

    // 让路中：抬起若对应的按下被手势吞了（没进系统），原样放行就是一条**野抬起**
    //（没有配对按下）——DefWindowProc 凭它合成一个莫名的右键菜单。
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
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => false);

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

    // 「下一次右键按下认领那笔丢失的抬起」：认领之后那笔账必须**真的记上了**，
    // 而且**有人负责**（排进补发队列）。
    //
    // 认领发生在「这一笔按下被我们吞了」那一档（见 MouseHook 的 PressVerdict.Swallow），
    // 所以这里要先让手势把按下吞掉。
    //
    // 让它变红的改动：认领时不加账（清掉标记就把账丢了 = 那个键卡在按下态）；
    // 或者认领时不排补发（那笔账没人领）。
    [Fact]
    public void The_next_right_press_claims_a_lost_handed_down()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => false);
        SetHandedAt(hook, Environment.TickCount64);
        long owedBefore = OwedRightUps(hook);
        long queuedBefore = hook.ReplayQueued;

        // 先按一下：手势把按下吞了（return 1），认领的那一档才走得到。
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero));

        Assert.False(hook.UpLedger.HasHandedDown);              // 已被认领
        Assert.Equal(owedBefore + 1, OwedRightUps(hook));        // **账真的记上了**
        Assert.Equal(queuedBefore + 1, hook.ReplayQueued);        // 且有人负责（排进了补发队列）

        ClearOwedUps(hook);   // 别让 Dispose 去真的注入一次抬起
        gate.Reset();   // 别让 Dispose 走还键那一条注入路（那会真的注入一次右键按下）
    }

    // 队列已经收掉（装不进去了）时，那笔账必须**排下一次重试**（看门狗接手），
    // 否则它就永远挂着 = 那个键卡在全系统按下态。
    // 让它变红的改动：认领时把入队失败的返回值丢掉。
    [Fact]
    public void A_claim_that_cannot_be_queued_still_schedules_a_retry()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => false);
        gate.Reset();
        // 把补发队列收掉（Teardown 收它），于是下一次认领投不进去。
        // 拆卸会清掉认领标记，所以标记放在拆卸**之后**再设。
        typeof(MouseHook).GetMethod("Teardown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(hook, null);
        SetHandedAt(hook, Environment.TickCount64);

        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero));

        Assert.NotEqual(0, RetryAt(hook));   // 排了下一次重试 → 看门狗会接手

        ClearOwedUps(hook);   // 别让 Dispose 去真的注入一次抬起
        gate.Reset();   // 别让 Dispose 走还键那一条注入路（那会真的注入一次右键按下）
    }

    // 修饰键按着的右键是**别人的组合手势** → 那一笔整笔放行 → **不该认领**。
    // 认领 = 补一个抬起；而这一笔自己的抬起会原样进系统，那个抬起已经把键松开了，
    // 再补一个就是没有配对按下的抬起 → 凭它合成一个莫名的右键菜单。
    [Fact]
    public void A_modifier_held_press_does_not_claim_the_lost_handed_down()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => true);
        SetHandedAt(hook, Environment.TickCount64);
        long owedBefore = OwedRightUps(hook);

        Send(hook, WM_RBUTTONDOWN, 100, 100, IntPtr.Zero);

        Assert.True(hook.UpLedger.HasHandedDown);          // 标记还在（没认领）
        Assert.Equal(owedBefore, OwedRightUps(hook));      // 也没有多记账
        gate.Reset();
    }

    // 让路中：那一笔按下是被**超时兜底**欠下的（闸门已关、但欠着一笔按下 = _owedRightClick），
    // 抬起同样不能原样放行——否则又是一条没有配对按下的抬起。
    [Fact]
    public void Yielding_also_swallows_the_up_when_the_stale_path_owns_the_down()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", canInject: () => true,
            modifierHeld: () => false);
        SetOwedRightClick(hook, true);   // 超时兜底那一档：闸门已关、欠着一笔按下
        hook.SetYielding(true);
        long queuedBefore = hook.ReplayQueued;

        // 抬起被吞掉并补一次点击，而不是原样放行。
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONUP, 100, 100, IntPtr.Zero));
        Assert.Equal(queuedBefore + 1, hook.ReplayQueued);   // 补了一次点击

        gate.Reset();
    }

    // 取大（CAS）：较小的值不许盖掉较大的峰值。
    // 为什么要单独钉：让路判据的输入就是这两个峰值——盖掉一次 = 一次超预算回调没被看见，
    // 而让路正是为了在那种时候收手。
    //
    // **两个重载都要验**：_callbackMax / _callbackWindowMax 是 uint（让路判据真正读的那两个），
    // _replayMax / _replayWindowMax 是 int。
    [Fact]
    public void The_congestion_peak_never_gets_smaller()
    {
        // 更大的盖上去，更小的盖不上去（两个重载各来一遍）。
        int i = 412;
        MouseHook.MaxInto(ref i, 900);
        Assert.Equal(900, i);
        MouseHook.MaxInto(ref i, 100);
        Assert.Equal(900, i);

        uint u = 412u;
        MouseHook.MaxInto(ref u, 900u);
        Assert.Equal(900u, u);
        MouseHook.MaxInto(ref u, 100u);
        Assert.Equal(900u, u);
    }

    // **并发**下峰值不能被更小的值盖掉。
    // 顺序跑的话，「读—比—写」那种朴素写法也会全过——而那正是会盖掉峰值的写法，
    // 也正是这条守卫要拦的东西。所以这里必须真的并发。
    [Fact]
    public void Concurrent_peaks_keep_the_largest_one()
    {
        int peak = 0;
        const int N = 14;
        var threads = new Thread[N];
        for (int i = 0; i < N; i++)
        {
            int v = i + 1;
            threads[i] = new Thread(() =>
            {
                for (int k = 0; k < 500; k++) MouseHook.MaxInto(ref peak, v);
            });
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        Assert.Equal(N, peak);   // 最大的那个还在
    }

    // 诊断必须真的接进错误日志——否则「重试过几次、为什么重试」在事后只能靠猜。
    [Fact]
    public void The_watchdog_diagnostics_reach_the_error_log()
    {
        var coord = Source("Services", "GestureCoordinator.cs");
        Assert.Contains("diag: AppendErrorLog", coord);
    }
}
