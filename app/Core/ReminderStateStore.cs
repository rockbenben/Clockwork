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

    // 后台补写状态：只留最新一份快照（状态以最后一份为准，中间态被覆盖是正确语义）。
    // 补写进行中时，新的 Save 一律入队交给单写者；所有实际磁盘写都持 WriteLock 串行化，
    // 且写前/写后都用引用比较确认自己仍是最新快照——否则 FlushPending 与 RetryLoop 并发时，
    // 旧快照可能在新快照之后落盘（stale 覆盖 fresh），恰好在退出前发生就是数据丢失。
    private static readonly object RetryLock = new();
    private static readonly object WriteLock = new();
    private static string? _retryPath;
    private static string? _retryJson;
    private static bool _retryRunning;

    // 从运行态字典抽耐久部分，原子写盘。best-effort，失败不抛。
    // Save 常在 UI 线程被调（提醒 tick），不能同步睡眠重试卡界面：快路径单次原子写（常态毫秒级），
    // 失败（OneDrive/杀软瞬时锁）转后台补写最新快照。丢失会导致「稍后/今天已弹」消失→重复弹窗。
    // durable=true（弹模态前的预存）例外地同步写到成：调用点的全部意义就是「先落盘再弹窗」防
    // 被杀/断电后次日重复弹；它每次触发至多一次且紧跟模态弹窗，容许最多 ~0.5s 的同步重试。
    public static void Save(string path, IReadOnlyDictionary<string, ReminderState> states, bool durable = false)
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
        var json = JsonSerializer.Serialize(map, JsonOpts);
        if (durable)
        {
            lock (RetryLock) { _retryPath = path; _retryJson = json; }   // 入队为最新快照 → 串行写队头
            FlushPending();
            return;
        }
        lock (RetryLock)
        {
            if (_retryRunning) { _retryPath = path; _retryJson = json; return; }   // 单写者进行中 → 只更新待写快照
        }
        if (ConfigStore.TryWriteTextAtomic(path, json))
        {
            // 写成功即代表盘上已是最新快照：作废此前写失败留下的旧待写份。
            // 不清的话，退出时 FlushPending 会拿那份旧的覆盖这份新的，丢掉其间的「稍后/今天已弹」→ 次日重复弹窗。
            // 走到这里说明 _retryRunning 为 false（上面刚查过，且 Save/Flush 都在 UI 线程），无后台写者会与此争用。
            lock (RetryLock) _retryJson = null;
            return;
        }
        lock (RetryLock)
        {
            _retryPath = path; _retryJson = json;
            if (_retryRunning) return;
            _retryRunning = true;
        }
        Task.Run(RetryLoop);
    }

    private static void RetryLoop()
    {
        while (true)
        {
            string path, json;
            lock (RetryLock)
            {
                if (_retryJson == null) { _retryRunning = false; return; }
                path = _retryPath!; json = _retryJson;   // 不先清：写完才放手，FlushPending 才能兜住「正在写」的快照
            }
            lock (WriteLock)
            {
                bool stale;
                lock (RetryLock) stale = !ReferenceEquals(_retryJson, json);
                // 等锁期间 FlushPending 可能已写入更新的快照——此时本份已过期，跳过以免 stale 覆盖 fresh
                if (!stale) ConfigStore.TryWriteTextAtomic(path, json, attempts: 5, delayMs: 100);
            }
            // 重试耗尽仍失败也放手（与旧行为同为 best-effort）；期间有更新则下一轮写新份
            lock (RetryLock)
            {
                if (ReferenceEquals(_retryJson, json)) _retryJson = null;
            }
        }
    }

    // 同步补写最新快照（进程退出兜底 + durable Save 共用）：持 WriteLock 与 RetryLoop 串行——
    // 若后台正写旧份，这里等它写完再写新份，最终盘上必是最新快照。失败不清队，留给后续机会再试。
    public static void FlushPending()
    {
        string? path, json;
        lock (RetryLock) { path = _retryPath; json = _retryJson; }
        if (path == null || json == null) return;
        bool ok;
        lock (WriteLock) ok = ConfigStore.TryWriteTextAtomic(path, json, attempts: 5, delayMs: 100);
        if (ok)
            lock (RetryLock) { if (ReferenceEquals(_retryJson, json)) _retryJson = null; }
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
