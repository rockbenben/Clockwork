using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Clockwork.Core;

// 提醒运行态的持久化。只存"耐久"部分：上次触发日期(防同日重复弹 + 供「错过必补」判当天是否已弹) +
// 「稍后」到点时刻。会话性字段(pending/repeat/startupHandled)有意不落盘——每次启动重新判定。
public static class ReminderStateStore
{
    public sealed class Persisted
    {
        public string LastFiredDate { get; set; } = "";
        public string SnoozeUntil { get; set; } = "";   // ISO8601("o") 或空
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // 从运行态字典抽耐久部分，原子写盘。best-effort，失败不抛。
    public static void Save(string path, IReadOnlyDictionary<string, ReminderState> states)
    {
        var map = new Dictionary<string, Persisted>();
        foreach (var (id, st) in states)
        {
            if (string.IsNullOrEmpty(st.LastFiredDate) && st.SnoozeUntil == null) continue;   // 无耐久内容不写
            map[id] = new Persisted
            {
                LastFiredDate = st.LastFiredDate,
                SnoozeUntil = st.SnoozeUntil?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            };
        }
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, JsonOpts));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        catch { }
    }

    // 读盘 → id→ReminderState(只填耐久字段)。文件缺失/损坏 → 空字典。
    public static Dictionary<string, ReminderState> Load(string path)
    {
        var result = new Dictionary<string, ReminderState>();
        if (!File.Exists(path)) return result;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, Persisted>>(File.ReadAllText(path));
            if (map == null) return result;
            foreach (var (id, p) in map)
            {
                if (p == null || string.IsNullOrWhiteSpace(id)) continue;
                var st = new ReminderState { LastFiredDate = p.LastFiredDate ?? "" };
                if (!string.IsNullOrEmpty(p.SnoozeUntil) &&
                    DateTime.TryParse(p.SnoozeUntil, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var su))
                    st.SnoozeUntil = su;
                result[id] = st;
            }
        }
        catch { }
        return result;
    }
}
