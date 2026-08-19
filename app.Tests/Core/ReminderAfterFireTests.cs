using Clockwork.Core;
using Xunit;

public class ReminderAfterFireTests
{
    private static DateTime N(int h, int m) => new DateTime(2026, 7, 15, h, m, 0);

    [Fact]
    public void Confirmed_stops_repeat()
    {
        var st = new ReminderState { RepeatCount = 3, NextRepeatAt = N(10, 5) };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 5 }, N(10, 0), "yes", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
    }

    [Fact]
    public void No_repeat_config_clears()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 0 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void Unconfirmed_schedules_next()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10 }, N(10, 0), "", st);
        Assert.Equal(N(10, 10), st.NextRepeatAt);
        Assert.Equal(1, st.RepeatCount);
    }

    [Fact]
    public void RepeatUntil_stops_past_deadline()
    {
        // 同时配了循环：过截止是催促链的一个出口，出口上必须接着排下一轮循环——
        // 否则「催促 + 循环」的任务在催促窗结束后就静默丢掉当天余下的所有轮次。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 30, RepeatUntil = "10:20", IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);            // 10:30 > 10:20
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void RepeatUntil_single_digit_hour_still_enforced()
    {
        // 手改 json 的 repeatUntil="9:30"：规整后照常生效，不再因过不了严格校验而整个截止判定被跳过。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "09:00", RepeatMinutes = 30, RepeatUntil = "9:20" }, N(9, 0), "", st);
        Assert.Null(st.NextRepeatAt);   // 9:30 > 09:20 → 停
    }

    [Fact]
    public void RepeatUntil_not_extended_for_single_digit_hour_time()
    {
        // 手改 json 的 time="9:00"：序数比较 "10:30"<"9:00" 会把当天已过的截止误判成跨午夜、顺延一天，
        // 催促窗被错误拉长 ~7 小时。比较前须规整 Time。
        var st = new ReminderState();
        var r = new Reminder { Time = "9:00", RepeatMinutes = 20, RepeatUntil = "10:30" };
        ReminderEngine.UpdateAfterFire(r, new DateTime(2026, 7, 15, 11, 1, 0), "", st);   // 11:01 触发（错过必补场景）
        Assert.Null(st.NextRepeatAt);   // 截止 10:30 已过 → 停，不顺延到明天
    }

    [Fact]
    public void RepeatUntil_crossing_midnight_still_schedules()
    {
        // 23:50 触发、每 15 分钟、截止 00:30(早于提醒时刻→跨午夜)：下一次 00:05 仍应排上。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "23:50", RepeatMinutes = 15, RepeatUntil = "00:30" }, N(23, 50), "", st);
        Assert.Equal(new DateTime(2026, 7, 16, 0, 5, 0), st.NextRepeatAt);
    }

    [Fact]
    public void RepeatUntil_crossing_midnight_stops_after_window()
    {
        // 次日 00:20 触发、下一次 00:35 已越过当日 00:30 截止 → 停。
        var st = new ReminderState { RepeatCount = 2 };
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "23:50", RepeatMinutes = 15, RepeatUntil = "00:30" }, new DateTime(2026, 7, 16, 0, 20, 0), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void RepeatUntil_elapsed_same_day_stops()
    {
        // 提醒 10:15、截止 10:20(晚于提醒时刻，非跨午夜)，触发被延时推到 10:21：不得误判为次日，应停。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "10:15", RepeatMinutes = 15, RepeatUntil = "10:20" }, N(10, 21), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void MaxRepeats_caps()
    {
        // 同时配了循环：达催促上限也是催促链的一个出口，同样要排下一轮循环（理由见 RepeatUntil_stops_past_deadline）。
        var st = new ReminderState { RepeatCount = ReminderEngine.MaxRepeats - 1 };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 5, IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Snooze_sets_and_clears_repeat()
    {
        var st = new ReminderState { NextRepeatAt = N(10, 5) };
        ReminderEngine.Snooze(new Reminder(), st, N(10, 0), 15);
        Assert.Equal(N(10, 15), st.SnoozeUntil);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void Snooze_under_1_defaults_10()
    {
        var st = new ReminderState();
        ReminderEngine.Snooze(new Reminder(), st, N(10, 0), 0);
        Assert.Equal(N(10, 10), st.SnoozeUntil);
    }

    [Fact]
    public void Snooze_keeps_repeat_count_while_nag_chain_runs()
    {
        // 配了催促：RepeatCount 是这条链的已催次数。手点稍后不该让它重新拿满 MaxRepeats 次额度。
        var st = new ReminderState { RepeatCount = 3 };
        ReminderEngine.Snooze(new Reminder { RepeatMinutes = 15 }, st, N(10, 0), 10);
        Assert.Equal(3, st.RepeatCount);
    }

    [Fact]
    public void AutoSnooze_counts_then_degrades_at_cap()
    {
        // 无人应答的自动稍后：MaxAutoSnoozes-1 轮内照常稍后并计数；到顶那轮返回 false=降级，链清零、不再排稍后。
        var st = new ReminderState();
        for (int i = 1; i < ReminderEngine.MaxAutoSnoozes; i++)
        {
            Assert.True(ReminderEngine.AutoSnooze(st, N(10, 0), 10));
            Assert.Equal(N(10, 10), st.SnoozeUntil);
            Assert.Equal(i, st.RepeatCount);
        }
        Assert.False(ReminderEngine.AutoSnooze(st, N(11, 0), 10));
        Assert.Null(st.SnoozeUntil);
        Assert.Equal(0, st.RepeatCount);
    }

    [Fact]
    public void Manual_snooze_does_not_count_toward_auto_cap()
    {
        // 手点稍后是人的明确决定：不进自动稍后的计数，点多少次都不会被降级。
        var st = new ReminderState();
        for (int i = 0; i < ReminderEngine.MaxAutoSnoozes + 2; i++) ReminderEngine.Snooze(new Reminder(), st, N(10, 0), 10);
        Assert.Equal(0, st.RepeatCount);
        Assert.Equal(N(10, 10), st.SnoozeUntil);
    }

    [Fact]
    public void Manual_snooze_resets_the_unanswered_streak()
    {
        // 「自动 ×N → 人点了稍后 → 再离开」：计时从头来，而不是只剩最后一轮就降级成卡片。
        // 「连续一小时没人理才降级」这句话的成立条件就是这条。
        var st = new ReminderState();
        for (int i = 1; i < ReminderEngine.MaxAutoSnoozes; i++) ReminderEngine.AutoSnooze(st, N(10, 0), 10);
        ReminderEngine.Snooze(new Reminder(), st, N(10, 30), 10);
        Assert.Equal(0, st.RepeatCount);
        for (int i = 1; i < ReminderEngine.MaxAutoSnoozes; i++)
            Assert.True(ReminderEngine.AutoSnooze(st, N(11, 0), 10));   // 满额度重新起算
    }

    [Fact]
    public void SkipToday_clears_every_in_flight_chain()
    {
        // 「今天不再」必须连在途的链一起清。尤其 SnoozeUntil：留着它，开了「错过必补」的提醒
        // 明天会把这条昨天的稍后当成一次没送达的投递补弹——用户说的是今天别响，不是明天早上诈尸。
        var st = new ReminderState
        {
            SnoozeUntil = N(10, 5), NextRepeatAt = N(10, 5), NextRepeatUntil = N(11, 0),
            RepeatCount = 3, NextIntervalAt = N(10, 30), PendingFireAt = N(10, 1), PendingForDate = "2026-07-15",
        };
        ReminderEngine.SkipToday(st, N(10, 0));
        Assert.Equal("2026-07-15", st.SkippedDate);
        Assert.Null(st.SnoozeUntil);
        Assert.Null(st.NextRepeatAt);
        Assert.Null(st.NextRepeatUntil);
        Assert.Null(st.NextIntervalAt);
        Assert.Null(st.PendingFireAt);
        Assert.Equal(0, st.RepeatCount);
        Assert.Equal("", st.PendingForDate);
        Assert.True(st.StartupHandled);
    }

    [Fact]
    public void Skipped_today_blocks_every_branch_then_recovers_tomorrow()
    {
        // 跳过当天：连「错过必补」这种最强的补弹路径也拦住；次日同一条状态照常触发。
        var r = new Reminder { Time = "09:00", CatchUpIfMissed = true };
        var st = new ReminderState();
        ReminderEngine.SkipToday(st, N(10, 0));
        Assert.Equal("none", ReminderEngine.Decide(r, N(10, 0), N(8, 0), st).Action);
        Assert.Equal("arm", ReminderEngine.Decide(r, N(10, 0).AddDays(1), N(8, 0), st).Action);
    }

    [Fact]
    public void Skipped_today_also_silences_event_triggers()
    {
        // 事件型的 ShouldFire 不看 LastFiredDate，所以「今天不再」才需要独立的 SkippedDate——
        // 否则这句话在「解锁时」提醒上会变成一个安静的谎。
        var r = new Reminder { Trigger = "unlock" };
        var st = new ReminderState();
        Assert.True(ReminderEvent.ShouldFire(r, "unlock", N(10, 0), st));
        ReminderEngine.SkipToday(st, N(10, 0));
        Assert.False(ReminderEvent.ShouldFire(r, "unlock", N(10, 0), st));
        Assert.True(ReminderEvent.ShouldFire(r, "unlock", N(10, 0).AddDays(1), st));
    }

    [Fact]
    public void Nag_chain_without_a_deadline_tops_out_at_MaxRepeats_pops()
    {
        // 「直到」留空时到底会弹几次——文案照这个数字写，别靠脑补。
        // 首弹之后每次未确认排下一次，count 达到 MaxRepeats 那一轮结束链、不再排。
        var r = new Reminder { RepeatMinutes = 1 };   // 无 repeatUntil
        var st = new ReminderState();
        int pops = 1;   // 首弹
        var now = N(10, 0);
        while (true)
        {
            ReminderEngine.UpdateAfterFire(r, now, "", st);
            if (st.NextRepeatAt == null) break;
            pops++;
            now = st.NextRepeatAt.Value;
            st.NextRepeatAt = null;   // 模拟这一次已引爆
        }
        Assert.Equal(ReminderEngine.MaxRepeats, pops);   // 首弹 + 19 次催促 = 20 次
    }

    [Fact]
    public void Degrade_still_schedules_the_next_interval_round()
    {
        // 降级时链结束，但「循环运行」是另一条链——不排下一轮的话，配了循环的提醒一降级就把当天
        // 余下的轮次全部静默丢掉，而降级卡片没有按钮，用户回来也无从把它接回去。
        var r = new Reminder { IntervalMinutes = 30, IntervalUntil = "18:00" };
        var st = new ReminderState();
        for (int i = 1; i < ReminderEngine.MaxAutoSnoozes; i++) ReminderEngine.AutoSnooze(st, N(10, 0), 10);
        Assert.False(ReminderEngine.AutoSnooze(st, N(11, 0), 10));   // 到顶 → 降级
        ReminderEngine.UpdateAfterFire(r, N(11, 0), "ok", st);        // App 在降级出口做的事
        Assert.Equal(N(11, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Confirm_resets_auto_snooze_count()
    {
        // 人回来点了确定 → EndRepeatChain 清零，下一轮无人应答重新拿满额度。
        var st = new ReminderState { RepeatCount = ReminderEngine.MaxAutoSnoozes - 1 };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 0 }, N(10, 0), "ok", st);
        Assert.Equal(0, st.RepeatCount);
        Assert.True(ReminderEngine.AutoSnooze(st, N(22, 0), 10));
    }

    [Fact]
    public void Confirm_schedules_next_interval()
    {
        // 确认不终止循环——这正是它与催促的区别（静默组固定返回 "ok"，静默任务的轮询靠这条路径成立）。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30 }, N(10, 0), "ok", st);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Ongoing_nag_chain_defers_interval()
    {
        // 催促链在途（NextRepeatAt 排上了）→ 不排 interval，两条链不互相插队。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10, IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Equal(N(10, 10), st.NextRepeatAt);
        Assert.Null(st.NextIntervalAt);
    }

    [Fact]
    public void Nag_chain_end_schedules_interval()
    {
        // 催促确认收尾的那一刻排下一轮循环。
        var st = new ReminderState { RepeatCount = 2, NextRepeatAt = N(10, 5) };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10, IntervalMinutes = 30 }, N(10, 6), "yes", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(N(10, 36), st.NextIntervalAt);
    }

    [Fact]
    public void Interval_until_stops_for_the_day()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "10:20" }, N(10, 0), "ok", st);
        Assert.Null(st.NextIntervalAt);   // 10:30 > 10:20
    }

    [Fact]
    public void Interval_until_boundary_is_inclusive()
    {
        // 正好落在截止时刻的那一轮必须照排：判定是 next > until。10:30 vs 10:20 的用例分不出 > 与 >=，
        // 而改成 >= 会静默砍掉每个窗口最后一轮合法运行。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "10:30" }, N(10, 0), "ok", st);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Interval_defaults_to_end_of_day()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "" }, new DateTime(2026, 7, 15, 23, 40, 0), "ok", st);
        Assert.Null(st.NextIntervalAt);   // 00:10 越过当天 23:59 → 本日循环结束，不跨午夜
    }

    [Fact]
    public void Once_disables_only_after_chains_end()
    {
        var r = new Reminder { RecurType = "once" };
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState()));                                        // 还没弹过
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15", SnoozeUntil = N(10, 10) }));   // 稍后在途
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15", NextRepeatAt = N(10, 5) }));   // 催促在途
        Assert.True(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15" }));           // 链清 → 停用
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(new Reminder { RecurType = "daily" }, new ReminderState { LastFiredDate = "2026-07-15" }));
    }
}
