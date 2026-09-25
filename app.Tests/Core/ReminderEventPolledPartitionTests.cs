using System.Linq;
using Clockwork.Core;
using Xunit;

// ReminderEvent.Polled 的穷尽性——它撑着 PollEnvironmentTriggers 的「没人用轮询触发就整段跳过」这条优化。
// 漏列一个硬件触发 = 那条提醒永远等不到轮询、静默失灵；多列 = 把订阅类当轮询白读。两种都不会报错，
// 只能靠这条断言钉住：每个事件型触发要么被订阅/消息送达，要么在 Polled 里，非此即彼，不许有第三种。
public class ReminderEventPolledPartitionTests
{
    // 非轮询的事件型触发 = 订阅类 + 消息类 + 网络类（各自靠回调送达）。这张名单改了，
    // 必须同时改 Polled 或 All，测试逼着两者对齐。
    private static readonly string[] Delivered =
        { "unlock", "lock", "resume", "display", "netUp", "netDown", "usb" };

    [Fact]
    public void Every_event_splits_into_polled_or_delivered_exactly_once()
    {
        foreach (var ev in ReminderEvent.All)
        {
            bool polled = ReminderEvent.NeedsPoll(ev);
            bool delivered = Delivered.Contains(ev);
            Assert.True(polled ^ delivered,
                $"{ev} 必须要么是轮询类、要么是送达类，且只能占一样（polled={polled} delivered={delivered}）");
        }
    }

    [Fact]
    public void Polled_is_exactly_the_five_hardware_conditions()
        => Assert.Equal(
            new[] { "idle", "busy", "acPlugged", "acUnplugged", "lowBattery" }.OrderBy(x => x),
            ReminderEvent.Polled.OrderBy(x => x));

    [Fact]
    public void Polled_and_delivered_are_disjoint_and_cover_all()
    {
        Assert.Empty(ReminderEvent.Polled.Intersect(Delivered));
        Assert.Empty(ReminderEvent.All.Except(ReminderEvent.Polled.Concat(Delivered)));   // 没有第三种
        Assert.Empty(ReminderEvent.Polled.Concat(Delivered).Except(ReminderEvent.All));   // 没有平白多出来的
    }

    // 时间型 / 登录时 不是事件，NeedsPoll 必须为 false（它们走 Decide 那条路，搭不搭轮询班车另说）。
    [Theory]
    [InlineData("time")]
    [InlineData("startup")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_event_triggers_are_not_polled(string? trigger)
        => Assert.False(ReminderEvent.NeedsPoll(trigger));
}
