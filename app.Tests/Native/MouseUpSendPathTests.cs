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
}
