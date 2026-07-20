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

    // 回归：一次写失败留下的「待写快照」，必须在后续快路径写成功后作废。
    // 否则退出时的 FlushPending 会拿这份旧快照覆盖掉更新的内容——丢掉「稍后/今天已弹」→ 次日重复弹窗。
    // 同类静态状态，与本类其它用例同集合串行执行。
    [Fact]
    public void Stale_pending_snapshot_must_not_overwrite_newer_state_on_flush()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw_state_" + Guid.NewGuid().ToString("N") + ".json");
        var tmp = path + ".tmp";
        var snoozed = new DateTime(2026, 7, 15, 10, 0, 0);
        try
        {
            // 1) 占住临时文件，让原子写必然失败（模拟 OneDrive/杀软瞬时锁），durable 同步重试耗尽 → 留下待写快照 S1
            var s1 = new Dictionary<string, ReminderState> { ["a"] = new ReminderState { LastFiredDate = "2026-07-15" } };
            using (new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                ReminderStateStore.Save(path, s1, durable: true);

            // 2) 锁已释放：保存更新的快照 S2（多了「稍后」），快路径应当成功落盘
            var s2 = new Dictionary<string, ReminderState>
            {
                ["a"] = new ReminderState { LastFiredDate = "2026-07-15", SnoozeUntil = snoozed },
            };
            ReminderStateStore.Save(path, s2);
            Assert.Equal(snoozed, ReminderStateStore.Load(path)["a"].SnoozeUntil);

            // 3) 模拟进程退出兜底：不得把过期的 S1 写回去
            ReminderStateStore.FlushPending();
            Assert.Equal(snoozed, ReminderStateStore.Load(path)["a"].SnoozeUntil);
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Load_missing_file_empty()
        => Assert.Empty(ReminderStateStore.Load(Path.Combine(Path.GetTempPath(), "cw_nope_" + Guid.NewGuid().ToString("N") + ".json")));
}
