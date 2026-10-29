using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clockwork.Core;
using Xunit;

// 「欠着的抬起」账本的行为级守卫。
//
// 这一块的账目以前散在 MouseHook 里的几个字段里（欠几笔、退避到第几次、配对期到什么时候、
// 那一笔交还给程序的按下还在不在），它们之间的不变式只能靠注释维持——而注释不会跟着代码一起被改。
//
// 收成状态机之后，这一整块**不必往开发者的桌面上注入一个真的右键抬起**就能测：
// 账本不碰 Win32，时间由调用方喂进来，发不发的决定权也在调用方。
// 这正是把它抽出来的第二个好处——以前这些不变式只能钉形状（读源码比对字符串），
// 而钉形状的那几条测试连「改变了行为」都看不出来。
public class MouseUpLedgerTests
{
    // 认领一笔 → 它从「欠着」变成「在途」：
    // **欠账数少了，但在途数是 1**。账目读出来是「清零」的，但系统那边其实还欠着一个抬起。
    // 看门狗要是看着「账清零 + 钩子已拆」就把自己销毁，那笔在途的账一失败就没人还了。
    [Fact]
    public void A_claimed_unit_is_in_flight_and_not_owed()
    {
        var l = new MouseUpLedger();
        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 42);

        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_000, out var unit));
        Assert.Equal(0, l.OwedCount);
        Assert.Equal(1, l.InFlightCount);
        Assert.Equal(MouseUpButton.Right, unit.Button);
        Assert.Equal(42, unit.ForegroundWindow);
    }

    // 空账本没有可领的——这是最容易被「先减再看」那种写法漏掉的一档。
    [Fact]
    public void An_empty_ledger_has_nothing_to_claim()
    {
        var l = new MouseUpLedger();
        Assert.Equal(MouseUpClaim.None, l.TryClaimOldest(1_000, out var none));
        Assert.Equal(0, none.Token);
        Assert.False(l.TryClaimToken(1, out _));
        Assert.False(l.HasDebt);
    }

    // 发失败**不销账**：那笔账还在（销账 = 把那次抬起丢了 = 那个键卡在全系统按下态）。
    // 而它回到**队尾**——刚失败过的那一笔让更老的先试，
    // 一条一直失败（提权前台拦着注入）时另一条不该被饿死
    //（中键卡在全系统按下态 = 中键拖拽永远在拖）。
    [Fact]
    public void A_failed_unit_stays_owed_and_goes_to_the_back_of_the_queue()
    {
        var l = new MouseUpLedger();
        var first = l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        var second = l.Owe(MouseUpButton.Middle, 1_001, foregroundWindow: 1);

        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_010, out var claimed));
        Assert.Equal(first.Token, claimed.Token);
        l.ReOwe(claimed);

        Assert.Equal(2, l.OwedCount);
        Assert.Equal(0, l.InFlightCount);
        // 下一笔轮到的是**还没失败过的那一笔**（中键），不是刚失败的那一笔。
        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_011, out var next));
        Assert.Equal(second.Token, next.Token);
    }

    // **认领「指定」的那一笔，不许动别人那笔**——这是这个账本最要紧的一条新不变式。
    // 补发线程用它自己刚记下的那一笔：认领「最老的那一笔」会把别人的账领走，
    // 于是自己那一笔的抬起反而没人发（那个键卡在全系统按下态），
    // 而看门狗随后又会给它补一个没有配对按下的抬起（凭它合成一个莫名的右键菜单）。
    //
    // 让它变红的改动：把 TryClaimToken 里的 token 比对去掉（退化成「领最老的那一笔」）。
    [Fact]
    public void A_token_claim_takes_that_unit_and_leaves_the_others_alone()
    {
        var l = new MouseUpLedger();
        var older = l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        var wanted = l.Owe(MouseUpButton.Middle, 1_001, foregroundWindow: 1);

        Assert.True(l.TryClaimToken(wanted.Token, out var mine));
        Assert.Equal(wanted.Token, mine.Token);
        Assert.Equal(MouseUpButton.Middle, mine.Button);

        // 更老的那一笔**还在**（没被顺手领走）。
        Assert.Equal(1, l.OwedCount);
        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_002, out var rest));
        Assert.Equal(older.Token, rest.Token);
    }

    // 不在账上的 token 领不到（否则会凭空造出一笔债）。
    [Fact]
    public void A_token_that_is_not_owed_cannot_be_claimed()
    {
        var l = new MouseUpLedger();
        var unit = l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        Assert.True(l.TryClaimToken(unit.Token, out _));
        l.Settle(unit);

        // 同一个 token 结清之后不能再领（否则会发第二次抬起）。
        Assert.False(l.TryClaimToken(unit.Token, out _));
        Assert.False(l.TryClaimToken(unit.Token + 999, out _));
    }

    // 配对期（补发线程正在按下→抬起配对）里，看门狗不许动账：
    // 抢走那一笔会把点击变成 0~15ms 的零长度点击（同批两条事件时间戳几乎相同，
    // Chromium 那类程序直接当噪声丢掉），症状就是「这一下右键凭空消失」。
    // 而补发线程自己认领**它那一笔**不受这个闸限制——正在配对的就是它自己。
    [Fact]
    public void The_pairing_guard_blocks_the_oldest_claim_but_not_the_replay_threads_own_one()
    {
        var l = new MouseUpLedger();
        var unit = l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        l.ReservePairing(1_000, MouseUpLedger.PairingGuardMs);

        Assert.Equal(MouseUpClaim.Pairing, l.TryClaimOldest(1_050, out _));
        Assert.Equal(1, l.OwedCount);
        Assert.True(l.PairingActive(1_050));

        // 补发线程认领自己那一笔：认得到。
        Assert.True(l.TryClaimToken(unit.Token, out var mine));
        Assert.Equal(unit.Token, mine.Token);
        Assert.Equal(0, l.OwedCount);
        Assert.Equal(1, l.InFlightCount);
    }

    // 配对期会自己过期：补发线程若死在半路，这一刻过去，看门狗照常接手
    //（否则那一笔永远没人还）。
    [Fact]
    public void The_pairing_guard_expires_on_its_own()
    {
        var l = new MouseUpLedger();
        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        l.ReservePairing(1_000, MouseUpLedger.PairingGuardMs);

        Assert.Equal(MouseUpClaim.Pairing, l.TryClaimOldest(1_000 + MouseUpLedger.PairingGuardMs - 1, out _));
        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_000 + MouseUpLedger.PairingGuardMs, out _));
    }

    // 配对期只许往后延，不许往前缩——否则第二次预留会把第一次的保护撤掉。
    [Fact]
    public void A_pairing_reservation_never_shrinks_an_earlier_one()
    {
        var l = new MouseUpLedger();
        l.ReservePairing(1_000, 500);
        l.ReservePairing(1_100, 100);   // 更晚开始、更短的窗口

        Assert.True(l.PairingActive(1_400));
        Assert.False(l.PairingActive(1_600));
    }

    // 补发线程把抬起发完了就把配对期放掉——**立刻放掉**，不等它自己过期。
    // 留着的话，看门狗会白白晚半秒才敢动账。
    [Fact]
    public void Releasing_the_pairing_guard_lets_the_watchdog_claim_again()
    {
        var l = new MouseUpLedger();
        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        l.ReservePairing(1_000, MouseUpLedger.PairingGuardMs);
        Assert.Equal(MouseUpClaim.Pairing, l.TryClaimOldest(1_010, out _));

        l.ReleasePairing();

        Assert.False(l.PairingActive(1_011));
        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_011, out _));
    }

    // **两条补发线程共用这一个配对窗时，先结清的那一条不许把另一条的窗打开**。
    // 这不是纸面上的形状：拆过又装回来时旧线程可能还在排空老队列（`StartReplayWorker` 在
    // 「已拆完」那一档会放行第二条线程），而旧实现里 `ReleasePairing` 是无条件归零——
    // 谁先结清谁就把门打开，看门狗立刻领走另一条**正在配对**的那一笔：
    // 按下已经送进系统、抬起被从别的线程补出去，中间那 15ms 没了 = 0ms 的零长度点击
    // = 用户那一下右键凭空消失（Chromium 那类程序把同批时间戳相同的事件当噪声丢掉）。
    // 让它变红的改动：把 ReleasePairing 改回无条件归零（不数还有几条压着）。
    [Fact]
    public void Releasing_one_pairing_leaves_the_other_threads_window_standing()
    {
        var l = new MouseUpLedger();
        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        l.ReservePairing(1_000, 500);      // 线程 A：窗口到 1_500
        l.ReservePairing(1_100, 500);      // 线程 B：窗口到 1_600

        l.ReleasePairing();                 // A 结清了

        Assert.True(l.PairingActive(1_300));
        Assert.Equal(MouseUpClaim.Pairing, l.TryClaimOldest(1_300, out _));   // B 的那一笔还受保护

        l.ReleasePairing();                 // B 也结清 → 窗才真的收掉
        Assert.False(l.PairingActive(1_300));
        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_300, out _));
    }

    // 退避：500ms → 1s → 2s → 4s → 5s 封顶。够等到用户切走那个提权窗口。
    [Fact]
    public void The_retry_backs_off_and_then_caps()
    {
        var l = new MouseUpLedger();
        long now = 10_000;
        long[] expected = { 500, 1_000, 2_000, 4_000, 5_000, 5_000 };

        foreach (long delay in expected)
        {
            l.ScheduleRetry(now);
            Assert.Equal(now + delay, l.RetryAtMs);
            now = l.RetryAtMs;   // 下一次从上一次排期的那一刻起算
        }
    }

    // 账还清了就把重试状态清掉——下一次欠账从 500ms 重新起算，
    // 而不是接着上一次那个已经退到 5s 封顶的档位。
    [Fact]
    public void Clearing_the_retry_forgets_the_backoff()
    {
        var l = new MouseUpLedger();
        l.ScheduleRetry(1_000);
        l.ScheduleRetry(2_000);
        Assert.Equal(2, l.RetryAttempt);

        l.ClearRetry();

        Assert.Equal(0, l.RetryAttempt);
        Assert.Equal(0, l.RetryAtMs);
    }

    // **它不认输。** 重试到什么时候都不放弃：认输会让那一笔欠着的抬起永远送不出去，
    // 而系统那边那个键就一直按着——左键点击从此全被当成「按住右键时的左键」，
    // 这是这个类最不能出的一种事故，而它没有别的出路
    //（一次补发的点击是 DOWN+UP 净零，只有一次光秃秃的抬起能补平那个多出来的按下）。
    //
    // 让它变红的改动：给 ScheduleRetry 加一个「到次数就停」的分支。
    [Fact]
    public void The_retry_never_stops_on_its_own()
    {
        var l = new MouseUpLedger();
        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);

        // 每一轮都把时钟推过退避的封顶（5s）再排一次：任何「排到第 N 次就不排了」的写法，
        // 那个冻住的时刻在下一轮就落到过去里来了，这条断言才会红。
        //
        // 早先这一条每轮只把时钟推 1ms 而封顶是 5000ms —— 冻住的时刻**永远还在未来**，
        // 于是它只咬得住「第 1~6 次就停」那种没人会写的数，注释里声称的那个变异照样全绿。
        long now = 10_000;
        long previous = 0;
        for (int i = 0; i < 200; i++)
        {
            now += 6_000;
            l.ScheduleRetry(now);
            Assert.True(l.RetryAtMs > now, $"第 {i} 轮不再排下一次了（= 它认输了）");
            Assert.True(l.RetryAtMs > previous, "重试时刻要一路往后排，不是在原地不动");
            previous = l.RetryAtMs;
        }

        // 而账还在——认输的话它就不会在了。
        Assert.Equal(1, l.OwedCount);
    }

    // **补发线程自己结清最后一笔时也要把退避档位复位**。它走的是 token 认领那条路，
    // 不经过 `SettleOwedUps` 的收尾，而 ClearRetry 今天只有那一个调用点——复位永远轮不到它。
    // 不复位的后果不是崩溃而是**变慢**：档位随会话时长一路爬到 5s 封顶，下一次真卡住键的
    // 第一次重试要等 5s，而那句「第一次失败」的证据行（只在档位 1 打）从此再也不出现。
    // 让它变红的改动：把 Settle 里「账与在途都空了就复位」那一句删掉。
    [Fact]
    public void Settling_the_last_unit_forgets_the_backoff()
    {
        var l = new MouseUpLedger();
        var a = l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        var b = l.Owe(MouseUpButton.Right, 1_001, foregroundWindow: 1);
        l.ScheduleRetry(1_002);
        l.ScheduleRetry(1_003);
        Assert.Equal(2, l.RetryAttempt);

        Assert.True(l.TryClaimToken(a.Token, out var ua));
        l.Settle(ua);
        Assert.Equal(2, l.RetryAttempt);      // 还欠着 b 那一笔 → 不许复位

        Assert.True(l.TryClaimToken(b.Token, out var ub));
        l.Settle(ub);
        Assert.Equal(0, l.RetryAttempt);      // 账与在途都空了 → 下一次从 500ms 重新起算
        Assert.Equal(0, l.RetryAtMs);
    }

    // 看门狗这一拍该不该动账：
    //   · 没欠账 → 不动；
    //   · 欠着但没人排过重试（补发线程卡住了）→ **接手**；
    //   · 到点了 → 动；
    //   · 配对期里 → 不动。
    [Fact]
    public void The_watchdog_decision_covers_every_case()
    {
        var l = new MouseUpLedger();
        Assert.Equal(MouseUpDecision.Idle, l.Decide(1_000));

        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        Assert.Equal(MouseUpDecision.TakeOver, l.Decide(1_000));   // 没人排过重试

        l.ScheduleRetry(1_000);
        Assert.Equal(MouseUpDecision.Idle, l.Decide(1_400));       // 还没到点
        Assert.Equal(MouseUpDecision.Due, l.Decide(1_500));         // 到点了

        l.ReservePairing(2_000, MouseUpLedger.PairingGuardMs);
        Assert.Equal(MouseUpDecision.Pairing, l.Decide(2_100));
    }

    // 没欠账的时候，即便正在配对也什么都不做——**没账可动**。
    // 顺序反了（先看配对再看有没有账）也会给出 Idle，但那样「有账 + 配对中」这一档
    // 就分不出来了，所以这里钉的是「没账 → Idle」。
    [Fact]
    public void No_debt_means_idle_even_while_a_pairing_is_running()
    {
        var l = new MouseUpLedger();
        l.ReservePairing(1_000, MouseUpLedger.PairingGuardMs);
        Assert.Equal(MouseUpDecision.Idle, l.Decide(1_010));
    }

    // 看门狗退休的判据：钩子已拆 + 没有在途的那几笔 + 账已还清（账本不认输，没有「放弃」那一档）。
    // **在途的那几笔也算没还清**——那一笔一失败就把账还回去并排重试，
    // 看门狗要是已经没了，那个重试没人认领 → 那个键卡在全系统按下态。
    [Fact]
    public void The_watchdog_retires_only_when_nothing_is_outstanding()
    {
        var l = new MouseUpLedger();
        Assert.False(l.ShouldRetireWatchdog(tornDown: false));   // 钩子还在
        Assert.True(l.ShouldRetireWatchdog(tornDown: true));     // 没欠账

        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 1);
        Assert.False(l.ShouldRetireWatchdog(tornDown: true));     // 还欠着

        Assert.Equal(MouseUpClaim.Claimed, l.TryClaimOldest(1_000, out var claimed));
        Assert.False(l.ShouldRetireWatchdog(tornDown: true));     // 在途的那一笔也算没还清

        l.Settle(claimed);                                         // 结清
        Assert.True(l.ShouldRetireWatchdog(tornDown: true));
    }

    // 「交还给程序的那次按下」只能被认领一次：认领之后它就变成一笔欠着的抬起，
    // 而那一笔欠账要由下一次右键按下领走（不是由计时器——用户按着不放多久都可能）。
    [Fact]
    public void The_handed_down_is_claimed_once_and_becomes_an_owed_up()
    {
        var l = new MouseUpLedger();
        Assert.False(l.ClaimHandedDown(1_000, foregroundWindow: 1, out _));   // 压根没有交还过

        l.MarkHandedDown(900);
        Assert.True(l.HasHandedDown);
        Assert.Equal(100, l.HandedDownAgeMs(1_000));

        Assert.True(l.ClaimHandedDown(1_000, foregroundWindow: 7, out var unit));
        Assert.False(l.HasHandedDown);
        Assert.Equal(MouseUpButton.Right, unit.Button);
        Assert.Equal(7, unit.ForegroundWindow);
        Assert.Equal(1, l.OwedCount);

        // 认领过就没有第二笔了。
        Assert.False(l.ClaimHandedDown(1_001, foregroundWindow: 7, out _));
        Assert.Equal(1, l.OwedCount);
    }

    // 真抬起到了 → 那笔交还的按下不再欠抬起（清掉它，下一次右键就不会多补一个野抬起）。
    [Fact]
    public void A_real_up_clears_the_handed_down()
    {
        var l = new MouseUpLedger();
        l.MarkHandedDown(900);
        Assert.True(l.ClearHandedDown());
        Assert.False(l.HasHandedDown);
        Assert.False(l.ClearHandedDown());   // 幂等
        Assert.Equal(0, l.HandedDownAgeMs(1_000));
    }

    // 账目的诊断读数：还欠几笔、哪个键、最老那笔冲着哪个窗口、欠了多久。
    // 它们只在日志里出现，但「事后能自证是哪一笔」全靠它们。
    [Fact]
    public void The_diagnostics_describe_the_debt()
    {
        var l = new MouseUpLedger();
        Assert.Equal(0, l.OwedCountOf(MouseUpButton.Right));
        Assert.Equal(0, l.OwedCountOf(MouseUpButton.Middle));
        Assert.Equal(0, l.HeadForegroundWindow);
        Assert.Equal("right=0 middle=0 head=0ms", l.Describe(1_000));

        l.Owe(MouseUpButton.Right, 1_000, foregroundWindow: 11);
        l.Owe(MouseUpButton.Middle, 1_200, foregroundWindow: 22);

        Assert.Equal(1, l.OwedCountOf(MouseUpButton.Right));
        Assert.Equal(1, l.OwedCountOf(MouseUpButton.Middle));
        Assert.Equal(2, l.OwedCount);
        Assert.True(l.HasDebt);
        // 队首那一笔冲着哪个窗口：SendInput 送不到那里去，只能事后自证。
        Assert.Equal(11, l.HeadForegroundWindow);
        Assert.Equal("right=1 middle=1 head=500ms", l.Describe(1_500));
    }

    // **一笔账只能被领走一次**：并发认领时，各线程各领各的一笔，
    // 不会把同一笔账领两次（多发一次是一个没有配对的抬起，会合成一个莫名菜单）。
    //
    // **必须有栅栏**：没有它，第一个线程会在别的线程还没起来之前就把 64 笔全领走
    // （实测 198/200 轮是单线程干的），那这条测试就测不到竞态了。
    [Fact]
    public void Concurrent_claims_never_hand_the_same_unit_out_twice()
    {
        const int Units = 256;
        const int Threads = 8;
        var l = new MouseUpLedger();
        for (int i = 0; i < Units; i++) l.Owe(MouseUpButton.Right, 1_000 + i, foregroundWindow: 1);

        using var start = new Barrier(Threads);
        var seen = new List<long>();
        var gate = new object();
        var threads = new Thread[Threads];
        for (int t = 0; t < Threads; t++)
        {
            threads[t] = new Thread(() =>
            {
                start.SignalAndWait();
                while (l.TryClaimOldest(1_000, out var unit) == MouseUpClaim.Claimed)
                {
                    lock (gate) seen.Add(unit.Token);
                    l.Settle(unit);
                }
            });
            threads[t].Start();
        }
        foreach (var th in threads) th.Join();

        Assert.Equal(Units, seen.Count);
        Assert.Equal(Units, seen.Distinct().Count());   // 没有哪一笔被领走两次
        Assert.Equal(0, l.OwedCount);
        Assert.Equal(0, l.InFlightCount);
    }

    // 生产里真正并发的是这两条路：补发线程按 token 认领**它自己**那一笔，
    // 看门狗按「队首那一笔」认领。它们抢同一本账——
    // 一笔账只能被领走一次，而认领与发出去是分开的两步（发失败要把账还回去）。
    //
    // **每一轮都过栅栏，两条路每轮各领一笔。** 没有栅栏的话，看门狗那条线程
    // 会在补发线程记下第一笔之前就把队列排空（实测 200 轮里有 94 轮两条路根本没碰上），
    // 而面对一份「认领不是原子的」实现它照样 194/200 轮通过——那就不是守卫。
    // 每轮各领一笔（而不是让看门狗一轮排空整个队列）是为了让两条路**争同一笔**：
    // 谁领到算谁的，另一方这一轮领不到。
    [Fact]
    public void The_replay_threads_own_claim_and_the_watchdogs_oldest_claim_never_collide()
    {
        const int Rounds = 200;
        var l = new MouseUpLedger();
        int created = 0, settled = 0;
        var tokens = new List<long>();
        var gate = new object();
        using var round = new Barrier(2);

        var replay = new Thread(() =>
        {
            for (int i = 0; i < Rounds; i++)
            {
                var unit = l.Owe(MouseUpButton.Right, 1_000 + i, foregroundWindow: 1);
                lock (gate) { created++; tokens.Add(unit.Token); }
                round.SignalAndWait();          // 与看门狗那一路同时进账本
                if (l.TryClaimToken(unit.Token, out var mine))
                {
                    lock (gate) settled++;
                    l.Settle(mine);
                }
            }
        });
        var watchdog = new Thread(() =>
        {
            for (int i = 0; i < Rounds; i++)
            {
                round.SignalAndWait();
                if (l.TryClaimOldest(1_000, out var unit) == MouseUpClaim.Claimed)
                {
                    lock (gate) settled++;
                    l.Settle(unit);
                }
            }
        });
        replay.Start();
        watchdog.Start();
        replay.Join();
        watchdog.Join();

        // 每一笔都是独立的一笔（没有两笔共用一个 token）。
        Assert.Equal(Rounds, tokens.Distinct().Count());
        // 一笔都没丢、也没被领走两次：记下的 = 结清的 + 还欠着的 + 在途的。
        // （每一轮记下一笔、也只可能结清一笔，所以这三条同时成立。）
        Assert.Equal(created, settled + l.OwedCount + l.InFlightCount);
        Assert.Equal(Rounds, settled);
        Assert.Equal(0, l.OwedCount);
        Assert.Equal(0, l.InFlightCount);
    }
}
