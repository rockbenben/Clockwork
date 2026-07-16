using Clockwork.Core;
using Xunit;

public class SystemInfoTests
{
    [Fact] public void Zero_ticks_is_zero() => Assert.Equal(0, SystemInfo.UptimeMinutesFromTicks(0));
    [Fact] public void One_minute() => Assert.Equal(1, SystemInfo.UptimeMinutesFromTicks(60_000));
    [Fact] public void Ten_minutes() => Assert.Equal(10, SystemInfo.UptimeMinutesFromTicks(600_000));

    [Fact]
    public void Does_not_wrap_past_49_days() // TickCount64 不回绕：超过 32 位的开机时长应如实换算，不掩成小值
    {
        long ticks = 5_000_000_000L; // > 2^32（约 57.8 天）；旧的掩低 32 位会错算成 11750
        Assert.Equal(83333, SystemInfo.UptimeMinutesFromTicks(ticks));
    }

    [Fact] public void Negative_ticks_is_zero() => Assert.Equal(0, SystemInfo.UptimeMinutesFromTicks(-1));
}
