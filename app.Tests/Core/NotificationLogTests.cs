using Clockwork.Core;
using Xunit;

public class NotificationLogTests
{
    private static readonly DateTime T0 = new(2026, 7, 27, 9, 0, 0);

    private static NotificationEntry E(int min, string msg, string? key = null)
        => new(T0.AddMinutes(min), "Clockwork", msg, false, key);

    [Fact]
    public void Recent_is_newest_first()
    {
        var log = new NotificationLog();
        log.Add(E(0, "a")); log.Add(E(1, "b")); log.Add(E(2, "c"));
        Assert.Equal(new[] { "c", "b", "a" }, log.Recent.Select(x => x.Message));
    }

    [Fact]
    public void Empty_log_has_no_entries()
        => Assert.Empty(new NotificationLog().Recent);

    // 超出容量丢最旧的，不是丢最新的。
    [Fact]
    public void Drops_oldest_beyond_capacity()
    {
        var log = new NotificationLog();
        for (int i = 0; i < NotificationLog.Capacity + 3; i++) log.Add(E(i, "m" + i));
        Assert.Equal(NotificationLog.Capacity, log.Recent.Count);
        Assert.Equal("m" + (NotificationLog.Capacity + 2), log.Recent[0].Message);
        Assert.Equal("m3", log.Recent[^1].Message);
    }

    // 同 key 的重复触发不该把缓冲刷空：只留最新一条。
    [Fact]
    public void Same_key_collapses_to_latest()
    {
        var log = new NotificationLog();
        log.Add(E(0, "other"));
        log.Add(E(1, "喝水 1", "reminder:x"));
        log.Add(E(2, "喝水 2", "reminder:x"));
        Assert.Equal(2, log.Recent.Count);
        Assert.Equal("喝水 2", log.Recent[0].Message);
        Assert.Equal("other", log.Recent[1].Message);
    }

    // 不同 key 互不影响。
    [Fact]
    public void Different_keys_coexist()
    {
        var log = new NotificationLog();
        log.Add(E(0, "a", "reminder:1"));
        log.Add(E(1, "b", "reminder:2"));
        Assert.Equal(2, log.Recent.Count);
    }

    // 无 key 的状态通知不参与合并（两条一样的运行回执各留一条）。
    [Fact]
    public void Keyless_entries_never_collapse()
    {
        var log = new NotificationLog();
        log.Add(E(0, "same")); log.Add(E(1, "same"));
        Assert.Equal(2, log.Recent.Count);
    }
}
