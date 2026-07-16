using System.IO;
using Clockwork.Core;
using Xunit;

public class ReminderStateStoreTests
{
    [Fact]
    public void RoundTrip_persists_durable_only()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw_state_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var states = new Dictionary<string, ReminderState>
            {
                ["a"] = new ReminderState
                {
                    LastFiredDate = "2026-07-15",
                    SnoozeUntil = new DateTime(2026, 7, 15, 10, 0, 0),
                    PendingFireAt = new DateTime(2026, 7, 15, 9, 0, 0),   // 会话字段——不应持久化
                    NextRepeatAt = new DateTime(2026, 7, 15, 9, 30, 0),
                    RepeatCount = 3,
                    StartupHandled = true,
                },
                ["b"] = new ReminderState(),   // 无耐久内容 → 不写
            };
            ReminderStateStore.Save(path, states);
            var loaded = ReminderStateStore.Load(path);

            Assert.True(loaded.ContainsKey("a"));
            Assert.False(loaded.ContainsKey("b"));
            Assert.Equal("2026-07-15", loaded["a"].LastFiredDate);
            Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), loaded["a"].SnoozeUntil);
            // 会话字段一律回到默认（不持久化）
            Assert.Null(loaded["a"].PendingFireAt);
            Assert.Null(loaded["a"].NextRepeatAt);
            Assert.Equal(0, loaded["a"].RepeatCount);
            Assert.False(loaded["a"].StartupHandled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_missing_file_empty()
        => Assert.Empty(ReminderStateStore.Load(Path.Combine(Path.GetTempPath(), "cw_nope_" + Guid.NewGuid().ToString("N") + ".json")));
}
