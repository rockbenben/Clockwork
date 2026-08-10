using Clockwork.Core;
using Xunit;

// 事件型提醒与计时器的分工：首发不归 Decide 管，但「稍后 / 催促 / 循环」这三条续接归它管。
public class ReminderEventDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 13, 9, 0, 0);   // 周一 09:00，正是 r.Time 的默认值

    private static ReminderDecision Decide(Reminder r, ReminderState st, DateTime? now = null)
        => ReminderEngine.Decide(r, now ?? Now, (now ?? Now).AddMinutes(-1), st);

    // 回归：事件型提醒的 Time 字段留着默认 "09:00"，若放行到时间判定，每天早九点会平白多弹一次。
    [Theory]
    [InlineData("idle")]
    [InlineData("unlock")]
    [InlineData("lock")]
    [InlineData("resume")]
    [InlineData("acPlugged")]
    [InlineData("acUnplugged")]
    [InlineData("lowBattery")]
    public void Timer_never_fires_an_event_reminder_on_its_own(string trigger)
    {
        var st = new ReminderState();
        Assert.Equal("none", Decide(new Reminder { Trigger = trigger }, st).Action);
        Assert.True(string.IsNullOrEmpty(st.LastFiredDate));   // 也不该被记成「今天已弹过」
    }

    // 事件型任务上点「稍后 10 分钟」必须真的会回来。挡在方法开头就等于把它扔了：
    // SnoozeUntil 落了盘却永远没人来看。
    [Fact]
    public void Snooze_still_comes_back_for_an_event_reminder()
    {
        var r = new Reminder { Trigger = "unlock" };
        var st = new ReminderState { SnoozeUntil = Now.AddMinutes(10) };

        Assert.Equal("none", Decide(r, st).Action);                       // 还没到点
        Assert.Equal("fire", Decide(r, st, Now.AddMinutes(10)).Action);   // 到点，补发
        Assert.Null(st.SnoozeUntil);                                      // 一次性，发完即清
    }

    // 「没确认就每 N 分钟再喊」在事件型任务上同样成立。
    [Fact]
    public void Nagging_still_repeats_for_an_event_reminder()
    {
        var r = new Reminder { Trigger = "lock", RepeatMinutes = 5 };
        var st = new ReminderState { NextRepeatAt = Now.AddMinutes(5) };

        Assert.Equal("none", Decide(r, st).Action);
        Assert.Equal("fire", Decide(r, st, Now.AddMinutes(5)).Action);
        Assert.Null(st.NextRepeatAt);
    }

    // 「循环运行」排下的下一轮也照跑（解锁后每 30 分钟提醒一次，直到某时刻）。
    [Fact]
    public void Interval_loop_still_runs_for_an_event_reminder()
    {
        var r = new Reminder { Trigger = "unlock", IntervalMinutes = 30 };
        var st = new ReminderState { NextIntervalAt = Now.AddMinutes(30) };

        Assert.Equal("none", Decide(r, st).Action);
        Assert.Equal("fire", Decide(r, st, Now.AddMinutes(30)).Action);
        Assert.Null(st.NextIntervalAt);
    }

    // 禁用优先于一切，事件型也不例外。
    [Fact]
    public void Disabled_event_reminder_stays_silent_even_with_a_pending_snooze()
    {
        var st = new ReminderState { SnoozeUntil = Now.AddMinutes(-1) };
        Assert.Equal("none", Decide(new Reminder { Trigger = "unlock", Enabled = false }, st).Action);
    }

    // 回归（评审 #5）：事件的「触发延迟」走 PendingFireAt——App 在事件发生时武装它，计时器到点引爆。
    // 事件门必须开在 pending 分支之后，否则武装好的延迟永远不会到点，编辑器收下的延迟被静默吞掉。
    [Fact]
    public void Armed_delay_for_an_event_reminder_fires_when_due()
    {
        var r = new Reminder { Trigger = "unlock", DelaySeconds = 300 };
        var st = new ReminderState { PendingFireAt = Now.AddSeconds(300) };

        Assert.Equal("none", Decide(r, st).Action);                        // 还没到点
        Assert.Equal("fire", Decide(r, st, Now.AddSeconds(300)).Action);   // 到点引爆
        Assert.Null(st.PendingFireAt);
    }

    // 回归（评审 #6）：陈旧稍后的「错过必补」对事件不生效——事件的语义是「没发生就是没发生」，
    // 不能凭一个残留勾选把昨晚的稍后在今早诈尸成一次从未发生的事件。稍后照清，但不补发。
    [Fact]
    public void Stale_snooze_catchup_does_not_resurrect_event_reminders()
    {
        var r = new Reminder { Trigger = "lowBattery", CatchUpIfMissed = true };
        var st = new ReminderState { SnoozeUntil = Now.AddDays(-1) };

        Assert.Equal("none", Decide(r, st).Action);
        Assert.Null(st.SnoozeUntil);   // 陈旧记录照常清掉，别烂在盘里
    }

    // 回归（评审 #4）：编辑器往返保真后，事件提醒身上可能残留 recurType="once"——
    // 它不得触发「仅一次响完自动取消勾选」，否则一条还想要的解锁提醒响一次就被悄悄关掉。
    [Fact]
    public void Leftover_once_recur_type_does_not_disable_event_reminders()
    {
        var r = new Reminder { Trigger = "unlock", RecurType = "once" };
        var st = new ReminderState { LastFiredDate = "2026-07-13" };
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, st));
        // 对照：同样状态的时间型 once 应当取消勾选
        Assert.True(ReminderEngine.ShouldDisableAfterOnce(new Reminder { Trigger = "time", RecurType = "once" }, st));
    }
}
