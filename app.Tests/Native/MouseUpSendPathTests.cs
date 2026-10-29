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

    // 键态探针默认答「那个键此刻按着」——发送前那一道对账要过关，才会真的走到发送口去。
    // 想验「键已经松开了就别补发」的测试把它换成 `(_ => false)`。
    private static MouseHook NewHook(FakeSender sender, Func<MouseUpButton, bool>? keyDown = null)
        => new(0, () => { }, a => a(), gesture: null, canInject: () => true,
               sendUp: b => { sender.Sent.Add(b); return sender.Answer(b); },
               keyDown: keyDown ?? (_ => true));

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

    // **发之前先与真实键态对账**：那个键已经不按着了，这一笔就早被别人结清了。
    // 这时再发是一个没有配对按下的抬起 → 目标程序凭它合成一个莫名的右键菜单
    //（用户报的「凭空多出一次右键」）。就地销账，一个字节都不发。
    // 让它变红的改动：把 SendUpUnit 里那道键态判断删掉。
    [Fact]
    public void An_up_for_a_key_that_is_already_up_is_settled_without_injecting()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender, keyDown: _ => false);
        Owe(hook, MouseUpButton.Right, 1);

        hook.SettleOwedUps(1);

        Assert.Equal(0, sender.Count);                  // 一次都没发
        Assert.Equal(0, hook.UpLedger.OwedCount);       // 而账结清了
    }

    // **账按笔记，键态是布尔**：同键积压三笔时，第一个抬起就把那个键松开了，
    // 后面每一个都是多余的那一次。这条钉的是「积压一起就地销账、不逐个补发」。
    // 只验上面那条还不够：那只有 1 笔，删掉对账判断也照样绿（本条才是计数与布尔的那道缝）。
    // 让它变红的改动：删掉 SendUpUnit 的键态判断，或对账时只销「队首那一笔」。
    [Fact]
    public void A_same_button_backlog_discharges_without_extra_ups()
    {
        var sender = new FakeSender { Answer = _ => true };
        using var hook = NewHook(sender, keyDown: _ => false);
        Owe(hook, MouseUpButton.Right, 3);

        hook.SettleOwedUps(3);

        Assert.Equal(0, sender.Count);
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
        int before = MouseHook.RefusedTestInjections;
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, canInject: () => true,
                                      keyDown: _ => true);
        hook.UpLedger.Owe(MouseUpButton.Right, Environment.TickCount64, foregroundWindow: 0);

        hook.SettleOwedUps(1);

        Assert.Equal(before + 1, MouseHook.RefusedTestInjections);
        Assert.Equal(1, hook.UpLedger.OwedCount);       // 账没被当成已还
    }
}
