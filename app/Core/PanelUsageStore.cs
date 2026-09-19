using System.IO;
using System.Text.Json;

namespace Clockwork.Core;

// 面板格子的使用统计（点击数 + 上次点击时刻），键是 LaunchStep.Id。
//
// **为什么与 clockwork.settings.json 分开存。** 一次点击要计数一次，而
// App.SaveConfig() 没有脏检查：它每次都重新序列化整份配置，然后重绑动作组热键、
// 重挂鼠标钩子、重绑功能键热键。为了一个计数器去付那笔代价，代价和收益完全不匹配；
// 而且钩子重装发生在点击的同一次事件里，最容易出问题的时候恰恰是用户在连点。
// 与提醒运行态（ReminderStateStore）是同一个取舍：配置放「我要怎么配置」，状态文件放「用了多少」。
//
// **为什么没有重试队列。** ReminderStateStore 那套重试是为了防止「今天已弹」丢失导致的次日重复弹窗——
// 一次丢失就是一次重复打扰。这里丢一次点击不产生任何用户可见的错误行为，所以只保留单次原子写，
// 写失败就当作没记下。统计是可有可无的辅助信息，不该为它复制一遍九十多行的补偿机制。
//
// **字段只增不改**：新统计项按同一条路加进 TileUsage 即可，旧文件读进来缺键就是 0，
// 不需要迁移、也不会因为多写字段而损坏。
public static class PanelUsageStore
{
    public sealed class TileUsage
    {
        public long Clicks { get; set; }
        // 上次点击的 Unix 毫秒。0 = 从未点过——「从未」和「1970 年点过」不需要区分。
        public long LastUsedUnixMs { get; set; }
    }

    // camelCase 与 clockwork.settings.json 同一套口径（ConfigStore.JsonOptions）：
    // 盘上写 clicks/lastUsedUnixMs，不写 PascalCase。缺了它 STJ 默认按原属性名落盘。
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // 记一次点击。返回是否真的改了内容——App 用它决定要不要排队落盘。
    // 不在此处落盘：点击在 UI 线程上，落盘交给单写者。
    // 参数是 IDictionary 而不是 IReadOnlyDictionary：后者的索引器是只读的，赋值编译不过。
    public static bool RecordClick(IDictionary<string, TileUsage> stats, string id, long nowUnixMs)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        stats.TryGetValue(id, out var usage);
        usage ??= new TileUsage();
        usage.Clicks += 1;
        usage.LastUsedUnixMs = nowUnixMs;
        stats[id] = usage;
        return true;
    }

    // 清掉已经不存在于配置里的格子。面板编辑会删格子、改页；不清的话这个文件就会
    // 变成只增不减的墓碑，重启后统计里的行也比面板上的格子多。
    // 返回删掉的条数，调用方据此决定要不要落盘。
    // 同上取 IDictionary：IReadOnlyDictionary 没有 Remove，会退化成全局扩展方法那个带 out 的重载。
    public static int PruneUnreferenced(IDictionary<string, TileUsage> stats, IEnumerable<string> liveIds)
    {
        var live = new HashSet<string>(liveIds, StringComparer.Ordinal);
        var stale = stats.Keys.Where(k => !live.Contains(k)).ToList();
        foreach (var k in stale) stats.Remove(k);
        return stale.Count;
    }

    // 文件缺失/损坏 → 空字典。统计不是功能的关键路径，读不动就当作从未统计过，
    // 绝不因为一份统计文件挡掉面板。
    public static Dictionary<string, TileUsage> Load(string path)
    {
        var result = new Dictionary<string, TileUsage>();
        if (!File.Exists(path)) return result;
        try
        {
            // 必须用同一份 JsonOpts：里面的 camelCase 策略在**反序列化**这头也生效。
            // STJ 默认大小写敏感，用默认选项读自己写的 clicks/lastUsedUnixMs 会绑不上属性，
            // 于是每次重启计数全部静默读回 0——存盘那一步就白做了。
            var map = JsonSerializer.Deserialize<Dictionary<string, TileUsage>>(File.ReadAllText(path), JsonOpts);
            if (map == null) return result;
            foreach (var (id, u) in map)
                if (u != null && !string.IsNullOrWhiteSpace(id))
                    result[id] = new TileUsage { Clicks = u.Clicks, LastUsedUnixMs = u.LastUsedUnixMs };
        }
        catch { }
        return result;
    }

    // 单次原子写，best-effort，失败不抛。调用方（App）负责串行化与排队。
    public static bool Save(string path, IReadOnlyDictionary<string, TileUsage> stats)
    {
        try
        {
            return ConfigStore.TryWriteTextAtomic(path, JsonSerializer.Serialize(stats, JsonOpts));
        }
        catch { return false; }
    }
}
