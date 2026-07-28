using System.Globalization;
using Clockwork.Core;
using Clockwork.Views;
using Xunit;

// 回归：提醒的日期字段校验 + 「循环运行」跨编辑的连续性。
public class ReminderDateAndIntervalTests
{
    // ===== 日期校验 =====
    // 曾用 ^\d{4}-\d{2}-\d{2}$ 只看形状，放行日历上不存在的日期；而 IsRecurrenceDueToday 对解析不了的
    // 日期一律按「今天」兜底 → 「每 N 天」静默退化成每天、「仅一次」当场就弹。校验必须真解析。
    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-45")]
    [InlineData("2025-02-29")]   // 平年 2/29
    [InlineData("2026-4-5")]     // 位数不足
    [InlineData("2026/07/01")]
    [InlineData("abc")]
    public void Impossible_or_malformed_dates_are_rejected(string s)
        => Assert.False(ReminderEditorWindow.IsDate(s));

    [Theory]
    [InlineData("2026-07-01")]
    [InlineData("2024-02-29")]   // 闰年 2/29
    [InlineData("2026-12-31")]
    public void Real_dates_are_accepted(string s)
        => Assert.True(ReminderEditorWindow.IsDate(s));

    // 引擎侧的兜底口径（校验放行什么就决定了会发生什么）：解析不了 = 按今天/每天。
    // 这条钉住「为什么校验必须严」——两者中的任意一条被改松，这里就会红。
    [Fact]
    public void Engine_treats_unparsable_anchor_as_due_every_day()
    {
        var r = new Reminder { RecurType = "everyNDays", IntervalDays = 7, AnchorDate = "2026-02-30" };
        for (int i = 0; i < 7; i++)
            Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 1).AddDays(i)));
    }

    [Fact]
    public void Engine_honours_a_valid_anchor()
    {
        var r = new Reminder { RecurType = "everyNDays", IntervalDays = 7, AnchorDate = "2026-07-01" };
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 1)));
        Assert.False(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 2)));
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 8)));
    }

    // ===== 日期必须是公历，且与用户的区域设置无关 =====
    // 曾经日期选择器用不带 culture 的 ToString("yyyy-MM-dd")：泰历/回历/波斯历区域下写出的是
    // 2569 / 1448 / 1405 年，校验放行、引擎按公历解析 → 「每 N 天」永不到期、「仅一次」永不触发。
    // （CollectionBehavior 已关并行，这里改 CurrentCulture 不会污染其它用例。）
    [Theory]
    [InlineData("th-TH")]   // 佛历
    [InlineData("ar-SA")]   // 回历
    [InlineData("fa-IR")]   // 波斯历
    [InlineData("en-US")]
    public void Dates_round_trip_as_gregorian_under_any_regional_format(string code)
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(code);
            var day = new DateTime(2026, 7, 1);
            Assert.Equal("2026-07-01", DurationText.FormatDate(day));
            Assert.True(DurationText.TryParseDate(DurationText.FormatDate(day), out var back));
            Assert.Equal(day, back);
            Assert.True(ReminderEditorWindow.IsDate(DurationText.FormatDate(day)));
            // 选出来的日期当天必须判为到期（此前泰历区域下会写成 2569 → 永远 false）
            var r = new Reminder { RecurType = "once", OnceDate = DurationText.FormatDate(day) };
            Assert.True(ReminderEngine.IsRecurrenceDueToday(r, day));
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    // ===== 「循环运行」跨编辑的连续性 =====
    // 编辑提醒会换新 id + 全新运行态。若不把 NextIntervalAt 迁过去，当天首发窗口早已过期，
    // Decide 一路返回 none —— 跑了一上午的「每 30 分钟」会因为改一下文案当场停到明天。
    private static Reminder Looper() => new()
    {
        Time = "08:00", GraceMinutes = 5, IntervalMinutes = 30, IntervalUntil = "18:00", Message = "喝水",
    };

    [Fact]
    public void Carried_NextIntervalAt_keeps_the_loop_running_after_an_edit()
    {
        var now = new DateTime(2026, 7, 1, 14, 00, 00);
        var st = new ReminderState { NextIntervalAt = new DateTime(2026, 7, 1, 13, 50, 00) };
        Assert.Equal("fire", ReminderEngine.Decide(Looper(), now, now.Date, st).Action);
    }

    [Fact]
    public void Dropping_NextIntervalAt_silently_kills_the_loop_for_the_rest_of_the_day()
    {
        var now = new DateTime(2026, 7, 1, 14, 00, 00);
        Assert.Equal("none", ReminderEngine.Decide(Looper(), now, now.Date, new ReminderState()).Action);
    }
}
