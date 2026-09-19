using System.IO;
using Clockwork.Core;
using Xunit;

// 面板格子点击统计的存取。统计不是关键路径：文件缺失/损坏给空表、写失败不抛，
// 但「记了又读回来」这条正路必须一分不差，否则面板管理器里的数字在重启后静默归零。
public class PanelUsageStoreTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), "cw_usage_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void RecordClick_increments_and_restamps()
    {
        var stats = new Dictionary<string, PanelUsageStore.TileUsage>();

        Assert.True(PanelUsageStore.RecordClick(stats, "a", 100));
        Assert.Equal(1, stats["a"].Clicks);
        Assert.Equal(100, stats["a"].LastUsedUnixMs);

        Assert.True(PanelUsageStore.RecordClick(stats, "a", 200));
        Assert.Equal(2, stats["a"].Clicks);
        Assert.Equal(200, stats["a"].LastUsedUnixMs);   // 时刻以最新一次为准
    }

    // 空白 id 不计数也不落键：这种格子（手写 json 抹掉了 id）在 App 那一路根本不该有统计，
    // 建一个空串键只会让以后所有没 id 的格子共用同一份计数。
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordClick_rejects_a_blank_id(string id)
    {
        var stats = new Dictionary<string, PanelUsageStore.TileUsage>();
        Assert.False(PanelUsageStore.RecordClick(stats, id, 1));
        Assert.Empty(stats);
    }

    [Fact]
    public void Prune_drops_stale_keys_and_reports_the_count()
    {
        var stats = new Dictionary<string, PanelUsageStore.TileUsage>
        {
            ["a"] = new() { Clicks = 1 },
            ["b"] = new() { Clicks = 2 },
            ["c"] = new() { Clicks = 3 },
        };

        Assert.Equal(2, PanelUsageStore.PruneUnreferenced(stats, new[] { "b" }));
        Assert.True(stats.ContainsKey("b"));
        Assert.False(stats.ContainsKey("a"));
        Assert.False(stats.ContainsKey("c"));

        // 幂等：再剪一遍没有东西可删，返回 0（App 据此决定要不要白写一次盘）。
        Assert.Equal(0, PanelUsageStore.PruneUnreferenced(stats, new[] { "b" }));
    }

    [Fact]
    public void Prune_with_no_live_ids_empties_the_map()
        => Assert.Equal(1, PanelUsageStore.PruneUnreferenced(
            new Dictionary<string, PanelUsageStore.TileUsage> { ["a"] = new() }, Array.Empty<string>()));

    [Fact]
    public void Load_missing_file_is_empty()
        => Assert.Empty(PanelUsageStore.Load(Path.Combine(Path.GetTempPath(), "cw_nope_" + Guid.NewGuid().ToString("N") + ".json")));

    [Fact]
    public void Load_corrupt_json_is_empty_not_throw()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ 这不是 json");
            Assert.Empty(PanelUsageStore.Load(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = TempPath();
        try
        {
            PanelUsageStore.Save(path, new Dictionary<string, PanelUsageStore.TileUsage>
            {
                ["tile-a"] = new() { Clicks = 7, LastUsedUnixMs = 123456789 },
            });
            var loaded = PanelUsageStore.Load(path);
            var u = Assert.Single(loaded);
            Assert.Equal("tile-a", u.Key);
            Assert.Equal(7, u.Value.Clicks);
            Assert.Equal(123456789, u.Value.LastUsedUnixMs);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // 落盘键名与 clockwork.settings.json 同一套 camelCase 口径，钉成字面量：
    // 命名策略一旦被人删掉，读自己写的文件会大小写不匹配，计数重启后静默归零。
    [Fact]
    public void Save_writes_camel_case_keys()
    {
        var path = TempPath();
        try
        {
            PanelUsageStore.Save(path, new Dictionary<string, PanelUsageStore.TileUsage>
            {
                ["tile-a"] = new() { Clicks = 1, LastUsedUnixMs = 9 },
            });
            var json = File.ReadAllText(path);
            Assert.Contains("\"clicks\": 1", json);
            Assert.Contains("\"lastUsedUnixMs\": 9", json);
            Assert.DoesNotContain("Clicks", json);
            Assert.DoesNotContain("LastUsedUnixMs", json);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // 手改 json 的容错：null 条目与空白键都跳过，其余照读，不因为一颗老鼠屎整份作废。
    [Fact]
    public void Load_skips_null_entries_and_blank_keys()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"dead": null, "   ": {"clicks": 1}, "ok": {"clicks": 3, "lastUsedUnixMs": 55}}""");
            var loaded = PanelUsageStore.Load(path);
            var only = Assert.Single(loaded);
            Assert.Equal("ok", only.Key);
            Assert.Equal(3, only.Value.Clicks);
            Assert.Equal(55, only.Value.LastUsedUnixMs);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
