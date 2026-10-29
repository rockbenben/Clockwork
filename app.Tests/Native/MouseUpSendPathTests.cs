using System;
using System.Collections.Generic;
using System.Threading;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「欠着的抬起」这条接线（认领 → 发送 → 结清/记账）的行为级守卫。
//
// 这些不变式以前只能钉源码里的字符串——而钉字符串拦不住「条件反了」那种改法：
// 变异测试里把 `if (ok) Settle(unit); else ReOwe(unit);` 改成「无条件 Settle 再 ReOwe」
// （或者把两个反过来），整套测试照样全绿，而那个键就此卡在全系统按下态。
//
// 有了可注入的发送口（MouseHook 的 sendUp），它们就能真的跑一遍：
// 假的发送口说失败，账就必须还在；说成功，账就必须清掉。
// **而假发送口不会在开发者的桌面上注入一个真的右键抬起**——那是这条测试纪律的底线。
public class MouseUpSendPathTests
{
    // 记下每一次「抬起」的发送，并按脚本回答成功/失败。
    private sealed class FakeSender
    {
        public readonly List<MouseUpButton> Sent = new();
        public Func<MouseUpButton, bool> Answer = _ => true;
        public int Count => Sent.Count;
    }

    // 键态探针已经不在了：抬起不再与系统键态对账（那一问答的是键、不是笔，
    // 而且 SendInput 只入队不等处理——按下去还没落地时问出来是「不按」，
    // 那一笔于是被当众销掉、键卡在全系统按下态）。一笔欠账配一个抬起。
    private static MouseHook NewHook(FakeSender sender)
        => new(0, () => { }, a => a(), gesture: null, canInject: () => true,
               sendUp: b => { sender.Sent.Add(b); return sender.Answer(b); });

    private static void Owe(MouseHook hook, MouseUpButton button, int times)
    {
        for (int i = 0; i < times; i++)
            hook.UpLedger.Owe(button, Environment.TickCount64, foregroundWindow: 0);
    }

    // **发失败不销账**：那一笔账还挂在账上（销账 = 把那次抬起丢了
    // = 那个键卡在全系统按下态 = 2026-10-28 那次整机左键失效）。
    // 让它变红的改动：把 `if (ok) Settle else ReOwe` 改成无条件 Settle。
    [Fact]
    public void A_failed_up_send_leaves_the_debt_owed()
    {
        var sender = new FakeSender { Answer = _ => false };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);

        Assert.Equal(1, sender.Count);
        Assert.Equal(1, hook.UpLedger.OwedCount);        // 账还在
        Assert.Equal(0, hook.UpLedger.InFlightCount);   // 而且没有卡在途里
    }

    // **发成功才销账**：那一笔从账上消失（它已经送出去了）。
    // 让它变红的改动：把 `if (ok) Settle else ReOwe` 的两个分支反过来。
    [Fact]
    public void A_successful_up_send_clears_the_debt()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);

        Assert.Equal(1, sender.Count);
        Assert.Equal(0, hook.UpLedger.OwedCount);
        Assert.Equal(0, hook.UpLedger.InFlightCount);
    }

    // **不许跨 SendInput 持锁**：钩子线程在锁上等着就越过了 LowLevelHooksTimeout
    // （300ms，而一次 SendInput 实测能到 313ms），Windows 会静默摘掉这个钩子。
    //
    // 验法不是「源码里没有 lock (」——那是字符串，改个写法就绕过去了。
    // 而是让**另一条线程**在发送的那一瞬间去动那两把锁（读也要拿锁）：锁被占着它就动不了。
    // 两把都要试：账本那把（钩子线程在 `SettleOwedUps` 里等）与补发队列那把
    // （钩子线程在回调里 `EnqueueReplay` 时等）。
    [Fact]
    public void The_up_is_sent_without_holding_a_lock()
    {
        MouseHook? hook = null;
        bool probed = false;
        var sender = new FakeSender();
        sender.Answer = button =>
        {
            if (probed) return true;
            probed = true;
            var ledger = hook!.UpLedger;
            var replayLock = typeof(MouseHook).GetField("_replayLock",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(hook)!;
            var reader = new Thread(() =>
            {
                int seen = ledger.OwedCount;          // 账本那把锁
                lock (replayLock) { }                 // 补发队列那把锁
                GC.KeepAlive(seen);
            });
            reader.Start();
            Assert.True(reader.Join(2000),
                "发送期间有一把锁被占着：另一条线程动不了（钩子线程会在这里越过 LowLevelHooksTimeout）");
            return true;
        };
        hook = NewHook(sender);
        using (hook)
        {
            Owe(hook, MouseUpButton.Right, 1);
            hook.SettleOwedUps(1);
        }
        Assert.True(probed, "假的发送口没被叫到");
    }

    // **发失败要排下一次重试**：不然那笔账就没人管了（看门狗只在「已经排过重试」时才动）。
    [Fact]
    public void A_failed_send_schedules_a_retry()
    {
        var sender = new FakeSender { Answer = _ => false };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);

        Assert.NotEqual(0, hook.UpLedger.RetryAtMs);   // 排了下一次 → 看门狗会接手
    }

    // **账还清了就别排重试**：排了会让看门狗白醒一轮。
    [Fact]
    public void A_successful_send_leaves_no_retry_behind()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);

        Assert.Equal(0, hook.UpLedger.RetryAtMs);
        Assert.Equal(0, hook.UpLedger.RetryAttempt);
    }

    // **看门狗接手没人排过重试的欠账**（补发线程卡住了）：它一排就立刻还上。
    // 让它变红的改动：删掉 Decide 里的 TakeOver 那一档。
    [Fact]
    public void The_watchdog_takes_over_a_debt_nobody_scheduled()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);
        Assert.Equal(0, hook.UpLedger.RetryAtMs);   // 没人排过重试

        hook.UpWatchdogTick();

        Assert.Equal(1, sender.Count);              // 看门狗接手并还上了
        Assert.Equal(0, hook.UpLedger.OwedCount);
    }

    // **看门狗一拍最多发几个**：它在线程池上，但 SendInput 走的是同一条系统输入队列——
    // 发太多会把补发线程的用户可见点击堵在后面。
    // 让它变红的改动：把那个上限改成 0（看门狗就永远不还账了）或改成很大的数。
    [Fact]
    public void The_watchdog_pays_at_most_two_ups_per_tick()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 5);

        hook.UpWatchdogTick();

        Assert.Equal(2, sender.Count);
        Assert.Equal(3, hook.UpLedger.OwedCount);   // 剩下的留给下一拍
    }

    // **钩子线程上一拍只发一个**：钩子线程正是低级钩子回调被派发的地方，
    // 而一次 SendInput 实测能到 313ms——连发几个就越过 LowLevelHooksTimeout，
    // Windows 会静默摘钩（钩子一摘，手势就没了）。
    // 让它变红的改动：把 maxSends 传大。
    [Fact]
    public void The_hook_thread_pays_at_most_one_up_per_settle()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 3);

        hook.SettleOwedUps(1);

        Assert.Equal(1, sender.Count);
        Assert.Equal(2, hook.UpLedger.OwedCount);
    }

    // **欠着的抬起是哪个键的，账上就得记哪个键**：记错了键，
    // 补发出去的抬起就会打在另一个键上（右键卡住、中键被凭空松开）。
    [Fact]
    public void The_debt_records_the_button_that_is_owed()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Middle, 1);

        hook.SettleOwedUps(1);

        Assert.Equal(MouseUpButton.Middle, Assert.Single(sender.Sent));
    }

    // **到点了的那一拍也要还账**（`MouseUpDecision.Due` 那一档）。
    // 这条为什么单独要有：上面那两条看门狗的用例欠的都是**没人排过重试**的账，走的都是
    // TakeOver 那一档；把 `case Due: SettleOwedUps(...)` 整段删掉，其余各条照样全绿——
    // 而生产里第一次发失败之后 `_retryAtMs` 就再也不为 0，从此**只走 Due**。
    // 也就是说：没有这一条，退避重试那条主路是没人守的（那个键就此卡在全系统按下态）。
    // 让它变红的改动：删掉 case Due 那一支。
    [Fact]
    public void The_watchdog_pays_a_due_retry()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);
        hook.UpLedger.ScheduleRetry(Environment.TickCount64 - 1_000);   // 早就到点了

        hook.UpWatchdogTick();

        Assert.Equal(1, sender.Count);
        Assert.Equal(0, hook.UpLedger.OwedCount);
    }

    // **同键积压：一笔欠账配一个抬起，逐笔还清**。
    //
    // 曾经这里有一道「与真实键态对账」：那个键「不按」了就当众销账、不发（一次都不发）。
    // 那一问是错的：系统那边一个键只有一个按下态（布尔），而账是按笔记的——同键积压三笔时，
    // 第一个抬起就把那个键松开了，于是第二笔、第三笔被当成「已结清」销掉，
    // 而目标程序那边还欠着两个按下，那个键就此卡在全系统按下态
    // （2026-10-28 17:50 那次整机左键失效的同一类事故）。
    //
    // 现在的契约：**欠几笔就发几个抬起**，一个都不省。多发的那个抬起是一次没有配对
    // 按下的抬起（程序里一次莫名的右键菜单）；少发的那个是那个键永远按着（整机点不动）。
    // 两头代价不对等，这一头贵得多。
    //
    // 让它变红的改动：把 SendUpUnit 里的「不按就销账」那一支加回来（哪怕它答的是别的键）。
    [Fact]
    public void A_same_button_backlog_pays_one_up_per_owed_unit()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 3);

        hook.SettleOwedUps(3);

        Assert.Equal(3, sender.Count);                   // 三笔各配一个抬起
        Assert.Equal(0, hook.UpLedger.OwedCount);        // 而且都结清了
    }

    // **这一拍发了几个、还欠着，不该动退避档位**：档位记的是「真失败了几次」，
    // 不是「还欠着几笔」。以前是「还欠着就排」——配对闸挡下来的那一拍（什么都没发）
    // 也算一次失败，于是档位随让路次数一路爬到 5s 封顶：下一次真卡住键的第一次重试
    // 要等 5s，而那句「第一次失败」的证据行（只在档位 1 打）从此再也不出现。
    //
    // 让它变红的改动：把 MouseHook.SettleOwedUps 的 finally 改回「还欠着就排」。
    [Fact]
    public void A_tick_that_pays_some_units_leaves_the_backoff_alone()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 5);

        hook.SettleOwedUps(2);            // 这一拍还了 2 笔，3 笔还欠着

        Assert.Equal(2, sender.Count);
        Assert.Equal(3, hook.UpLedger.OwedCount);
        Assert.Equal(0, hook.UpLedger.RetryAttempt);      // 没有失败 → 档位不动
        Assert.Equal(0, hook.UpLedger.RetryAtMs);
    }

    // **配对闸挡下来那一拍也不算失败**：那一笔根本没发。
    //
    // 让它变红的改动：同上（finally 里不看 failed 而看 HasDebt）。
    [Fact]
    public void A_tick_blocked_by_the_pairing_gate_leaves_the_backoff_alone()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 1);
        // 配对窗开着：这一拍认领到的那一笔属于补发线程，不该由我们发。
        hook.UpLedger.ReservePairing(Environment.TickCount64, MouseUpLedger.PairingGuardMs);

        hook.SettleOwedUps(1);

        Assert.Equal(0, sender.Count);                     // 一次都没发
        Assert.Equal(1, hook.UpLedger.OwedCount);          // 账还在（没被销掉）
        Assert.Equal(0, hook.UpLedger.RetryAttempt);        // 没发过 → 不算失败 → 档位不动
        Assert.Equal(0, hook.UpLedger.RetryAtMs);
    }

    // **三个真的鼠标注入出口都要被拦**：钩子的两个出口只是其中一处。
    // 直接调 Win32.SendRightDownTagged() 的测试若不经这道闸，就会往开发者的桌面上
    // 注入一次**只有按下没有抬起**的右键——那个键就此卡在全系统按下态
    // （这正是账本要防的那种事故，而且收不回来）。
    //
    // 让它变红的改动：把 Win32 里那三处 `if (RefuseInjectionInTestHost()) return 0;` 删掉。
    [Fact]
    public void Every_real_mouse_injection_exit_is_refused_under_the_test_host()
    {
        int before = Win32.RefusedTestInjections;

        Assert.Equal(0u, Win32.SendRightDownTagged());                          // 带签名的补发（钩子那条）
        Assert.Equal(0u, Win32.SendMouseButton(Array.Empty<ushort>(), 2, 1));   // 鼠标伪键
        Assert.Equal(0u, Win32.SendWheel(Array.Empty<ushort>(), 1));            // 滚轮

        Assert.True(Win32.RefusedTestInjections >= before + 3,
            $"三个出口每一个都要记一笔（起 {before}，现在 {Win32.RefusedTestInjections}）");
    }

    // **判据只有一处**：环境变量 + 入口程序集名。
    // 生产那一侧认的是入口程序集名（csproj 里钉着的 "Clockwork"），**不是 exe 文件名**——
    // 这个程序是便携的，用户会把 exe 改名；认文件名的话，改了名 + 用户环境里恰好有那个
    // 变量，右键就彻底失灵了。
    //
    // 让它变红的改动：把判据改回看 exe 文件名，或把生产那一侧的排除条件去掉。
    [Fact]
    public void The_guard_spells_out_which_process_is_the_test_host()
    {
        Assert.False(Win32.ShouldRefuseInjection("1", "Clockwork"));        // 生产：永不拦
        // 用户把 exe 改名也没关系——入口程序集名不变（csproj 里钉着的 "Clockwork"）。
        Assert.True(Win32.ShouldRefuseInjection("1", "Clockwork.Tests"));   // 测试宿主：拦
        Assert.True(Win32.ShouldRefuseInjection("1", "testhost"));
        Assert.False(Win32.ShouldRefuseInjection(null, "Clockwork.Tests"));  // 没有那个变量：不是测试宿主
        Assert.False(Win32.ShouldRefuseInjection("0", "Clockwork.Tests"));  // 值不是 1：不是测试宿主
    }

    // **「第一次失败」那行唯一的自证日志要真的打出来**：它只在档位 1 打，
    // 而档位与「是不是第一次」现在同一次加锁问出来（MouseUpLedger.ScheduleRetry 返回档位）。
    // 以前是自增在账本里、判据在调用方另读一次，两次之间别的线程爬了一档，
    // 那行证据就永远不出现了——而它正是事后能自证是哪一笔的唯一线索。
    //
    // 让它变红的改动：把 MouseHook.ScheduleUpRetry 改回「自增在这里、判据在别处再读一次」。
    [Fact]
    public void The_first_failed_send_leaves_its_evidence_line()
    {
        var sender = new FakeSender { Answer = _ => false };
        var lines = new List<string>();
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, canInject: () => true,
            diag: lines.Add,
            sendUp: b => { sender.Sent.Add(b); return sender.Answer(b); });
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);          // 这一发失败 → 档位 1 → 那行证据要打出来

        Assert.Contains(lines, l => l.Contains("replay up retry"));
        Assert.Equal(1, hook.UpLedger.RetryAttempt);
    }

    // **人工逃生口**：把账上欠着的抬起全部补出去。
    // 托盘那条「松开卡住的鼠标键」走的就是它——看门狗只认账上欠着的，
    // 真抬起被安全桌面吃掉的那一次要等下一次右键才被认领，在那之前用户的右键一直是死的。
    //
    // 让它变红的改动：把「先一次把账全领走再发」改回边领边发（发失败的会被还回队列，
    // 下一轮又领出来——死循环）。
    [Fact]
    public void The_escape_hatch_pays_every_owed_up()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender);
        Owe(hook, MouseUpButton.Right, 3);

        int paid = hook.PayAllOwedUpsNow();

        Assert.Equal(3, paid);
        Assert.Equal(3, sender.Count);
        Assert.Equal(0, hook.UpLedger.OwedCount);
    }

    // **装配级守卫**：测试宿主里不许把真的鼠标事件送进系统。
    // 这条纪律以前只写在每条测试的收尾里（记得关闸门、记得先清账），而纪律不是守卫——
    // 任何一条新测试忘了，就往开发者的桌面上注入一次真的按下/抬起。
    // 这里故意**不**注入发送接缝：走真发送口的那条路必须被拒，并且账要留着
    //（拒发不等于丢账：看门狗还在退避重试，少发一次是有主人的）。
    // 让它变红的改动：把 SendTimed/SendSync 开头那句 RefusedInTestHost 删掉。
    [Fact]
    public void Real_injections_are_refused_under_the_test_host()
    {
        int before = Win32.RefusedTestInjections;
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, canInject: () => true);
        hook.UpLedger.Owe(MouseUpButton.Right, Environment.TickCount64, foregroundWindow: 0);

        hook.SettleOwedUps(1);

        Assert.True(Win32.RefusedTestInjections >= before + 1,
            $"被挡下的真注入要记一笔（起 {before}，现在 {Win32.RefusedTestInjections}）");
        Assert.Equal(1, hook.UpLedger.OwedCount);       // 账没被当成已还
    }
}
