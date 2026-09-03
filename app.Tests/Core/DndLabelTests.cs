using System;
using Xunit;

// 「恢复提醒（剩 …）」的读法分界。
//
// 加了 8 小时和「到今天结束」两档之后，原来那句固定按分钟的写法会说出「剩 480 分钟」
// ——一个要在心里除一遍才知道是多久的数。分界取 60 分钟：那正好是原来最小的一档，
// 所以旧档位上的读法一字不变。
//
// 这里锁的是**分界与取整**这段纯算术。真正的字符串拼装在 App.DndResumeLabel 里
// （它要碰 resx 与 WPF，进不了这个测试工程），而那边的两条分支就是照这个判据写的：
//   left.TotalMinutes < 60 → 按分钟，向上取整
//   否则                   → 按小时，四舍五入
// 两档取整方式故意不同，各有理由：分钟向上取整，因为说「剩 0 分钟」会读成「已经结束了」，
// 而那一刻还没到点；小时四舍五入，因为这一档不可能读成 0（低于 60 分钟就走分钟那支了），
// 而向上取整会让「还剩 61 分钟」说成「剩 2 小时」，凭空多报 59 分钟。
public class DndLabelTests
{
    // 与生产一致：**先取整再分支**。判原始值的那一版会在刚点下「1 小时」时
    // 说出「剩 60 分钟」（left = 59.99 分 → 选分钟支 → Ceiling 抬成 60）。
    private static bool UsesMinutes(TimeSpan left) => Minutes(left) < 60;
    private static int Minutes(TimeSpan left) => (int)Math.Ceiling(left.TotalMinutes);
    private static int Hours(TimeSpan left) => (int)Math.Round(left.TotalHours, MidpointRounding.AwayFromZero);

    [Theory]
    [InlineData(1, true)]        // 1 分钟：按分钟
    [InlineData(59, true)]       // 分界之下
    [InlineData(60, false)]      // 正好一小时：按小时
    [InlineData(61, false)]
    [InlineData(480, false)]     // 8 小时那一档
    [InlineData(1439, false)]    // 「到今天结束」最长的情形（差一分钟满一天）
    public void The_cut_is_at_one_hour(int minutesLeft, bool expectMinutes)
        => Assert.Equal(expectMinutes, UsesMinutes(TimeSpan.FromMinutes(minutesLeft)));

    // **刚点下任一档位，读出来就得是那个档位。**
    //
    // 真实时铟永远给不出整数：点下那一瞬剩余时间已经是 59.99 分 / 479.99 分。
    // 上一版分支判的是原始值而显示向上取整，于是「1 小时」那一档回手说「剩 60 分钟」。
    // 旧用例里只有 InlineData(60, false) 盯着这一档，而恰好 60.00 是真实时铟唯一不会产生的值。
    [Theory]
    [InlineData(1)]      // 1 小时那一档
    [InlineData(4)]
    [InlineData(8)]      // 8 小时那一档
    public void A_freshly_clicked_tier_reads_as_that_tier(int hours)
    {
        // 模拟「点下去之后的第一次重建菜单」：已经跑了几毫秒到几百毫秒
        foreach (var elapsedMs in new[] { 1, 50, 400, 999 })
        {
            var left = TimeSpan.FromHours(hours) - TimeSpan.FromMilliseconds(elapsedMs);
            Assert.False(UsesMinutes(left));            // 不能掉回分钟那支
            Assert.Equal(hours, Hours(left));           // 读出来就是档位那个数
        }
    }

    // 取整向上：还剩 30 秒时说「剩 1 分钟」，不能说「剩 0 分钟」——那会读成已经结束。
    [Fact]
    public void Rounding_never_reads_as_finished()
    {
        Assert.Equal(1, Minutes(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, Minutes(TimeSpan.FromSeconds(1)));
        // 小时那一档四舍五入：61 分钟说「剩 1 小时」（向上取整会说 2，凭空多报 59 分钟）。
        Assert.Equal(1, Hours(TimeSpan.FromMinutes(61)));
        Assert.Equal(1, Hours(TimeSpan.FromMinutes(89)));
        Assert.Equal(2, Hours(TimeSpan.FromMinutes(90)));   // 正好半小时往上走
    }

    // 8 小时那一档刚点下去时读出来必须是「8 小时」——不多不少。整数小时上取整与四舍五入结果相同，
    // 这一条钉住的是「档位数字与读出来的数字一致」，那是用户点完立刻会去核对的东西。
    [Fact]
    public void A_freshly_set_eight_hour_pause_reads_as_eight()
    {
        Assert.Equal(8, Hours(TimeSpan.FromHours(8)));
        Assert.Equal(4, Hours(TimeSpan.FromHours(4)));
    }

    // 「到今天结束」= 次日 0 点。用日期加一天而不是「23:59:59」：后者会在最后一秒放行一次提醒。
    [Theory]
    [InlineData("2026-09-03 09:15:00")]
    [InlineData("2026-09-03 23:59:30")]
    [InlineData("2026-09-03 00:00:00")]
    public void End_of_day_is_the_next_midnight(string nowText)
    {
        var now = DateTime.Parse(nowText, System.Globalization.CultureInfo.InvariantCulture);
        var until = now.Date.AddDays(1);
        Assert.True(until > now);                        // 永远在未来，哪怕现在是 23:59:30
        Assert.Equal(0, until.Hour);
        Assert.Equal(0, until.Minute);
        Assert.Equal(now.Day + 1, until.Day);            // 这三个日期都不跨月，够用
        Assert.True((until - now).TotalHours <= 24);
    }
}
