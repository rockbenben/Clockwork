using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// 星期摘要什么时候收成区间、什么时候必须逐日列。
//
// 这一列在定时任务页只有 120px 出头，而「工作日」这种连续五天是最常见的设置：
// 逐日列出来是「周一、周二、周三、周四、周五」，读完一行的注意力已经没了。
// 收成「周一–周五」把这件事压回三个词。但收错一次的代价比啰嗦大——
// 「只勾了周日和周一」要是被写成「周一–周日」，那一行说的是「每天」，而它不是。
//
// 断言全部从 Strings 取值，不写字面「周一」：中性语料是中文，但这条判据讲的是结构，
// 换一门语言跑也应该照样成立。
public class DaysLabelRangeTests
{
    private static string Day(int n) => Strings.Get("Day_" + n);
    private static string Every => Strings.Get("Days_EveryDay");
    private static string Dash => Strings.Get("Days_RangeSep");
    private static string Join(params int[] ns) => string.Join(Strings.Get("Days_Sep"), ns.Select(Day));

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7 })]
    public void Nothing_or_everything_says_every_day(int[] days)
        => Assert.Equal(Every, StepDisplay.DaysLabel(days));

    [Fact]
    public void A_contiguous_run_of_three_or_more_collapses_to_its_ends()
    {
        Assert.Equal(Day(1) + Dash + Day(5), StepDisplay.DaysLabel(new[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(Day(2) + Dash + Day(4), StepDisplay.DaysLabel(new[] { 4, 2, 3 }));   // 乱序进、按序出
        Assert.Equal(Day(1) + Dash + Day(6), StepDisplay.DaysLabel(new[] { 1, 2, 3, 4, 5, 6 }));
    }

    [Theory]
    [InlineData("ja", "〜")]     // 日语的区间记号是波浪号
    [InlineData("ko", "~")]      // 韩语同
    [InlineData("en", "–")]
    [InlineData("zh-CN", "–")]
    public void Each_locale_uses_its_own_range_mark(string code, string expected)
    {
        var prev = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            Strings.ApplyCulture(code);
            Assert.Equal(expected, Strings.Get("Days_RangeSep"));
            Assert.Contains(expected, StepDisplay.DaysLabel(new System.Collections.Generic.List<int> { 1, 2, 3 }));
        }
        finally { Strings.ApplyCulture(prev.Name); }
    }

    [Fact]
    public void Two_days_are_listed_not_ranged()
    {
        // 「周六–周日」比「周六、周日」更不像人话，而且省不了几个字。
        var label = StepDisplay.DaysLabel(new[] { 6, 7 });
        Assert.DoesNotContain(Dash, label);
        Assert.Equal(Join(6, 7), label);
    }

    [Fact]
    public void A_gapped_set_is_listed()
    {
        var label = StepDisplay.DaysLabel(new[] { 1, 3, 5 });
        Assert.DoesNotContain(Dash, label);
        Assert.Equal(Join(1, 3, 5), label);
    }

    [Fact]
    public void Sunday_and_monday_never_read_as_every_day()
    {
        // 1..7 里它们是两端。收成区间会写出「周一–周日」——那是「每天」的意思。
        var label = StepDisplay.DaysLabel(new[] { 7, 1 });
        Assert.DoesNotContain(Dash, label);
        Assert.NotEqual(Every, label);
    }

    [Fact]
    public void Out_of_range_entries_are_dropped_not_rendered()
    {
        // 手改 json 塞进来的脏值：不能让它们把区间两端带歪（Day_0 / Day_9 取不到就是空串）。
        var label = StepDisplay.DaysLabel(new[] { 0, 1, 2, 3, 9 });
        Assert.Equal(Day(1) + Dash + Day(3), label);
    }

    [Fact]
    public void Every_day_survives_when_only_junk_was_set()
        => Assert.Equal(Every, StepDisplay.DaysLabel(new[] { 0, 8, -1 }));
}
