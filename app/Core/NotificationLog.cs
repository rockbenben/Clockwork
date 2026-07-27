namespace Clockwork.Core;

// 一条通知的留痕。Key 非空=可合并的来源（提醒按 id），Warn=警示级，
// DurationMs=原卡片的显示时长（0=常驻）——重放须忠实还原：同样的时长、同样的常驻性、原来的时刻(At)，
// 不能把一张配了 120s 的长文卡重放成 5 秒一闪，也不能把常驻降级。
public sealed record NotificationEntry(DateTime At, string Title, string Message, bool Warn, string? Key = null, int DurationMs = 0);

// 最近通知环形缓冲：托盘「最近通知」用来回看被点掉 / 被挤掉 / 已自动消失的卡片。
// 会话级、不落盘（重启即清）——它是「刚才那条写的啥」的补救，不是审计日志。
// 同 Key 的重复触发只留最新一条并移到最前，避免一条每 5 分钟催一次的提醒把整个缓冲刷空。
public sealed class NotificationLog
{
    public const int Capacity = 8;   // 与托盘菜单可容纳的行数同量级：再多就把菜单撑成长条

    private readonly List<NotificationEntry> _items = new();   // 旧→新

    public void Add(NotificationEntry e)
    {
        if (!string.IsNullOrEmpty(e.Key)) _items.RemoveAll(x => x.Key == e.Key);
        _items.Add(e);
        while (_items.Count > Capacity) _items.RemoveAt(0);
    }

    // 最新在前——托盘菜单自上而下就是从新到旧。
    public IReadOnlyList<NotificationEntry> Recent
    {
        get
        {
            var list = new List<NotificationEntry>(_items);
            list.Reverse();
            return list;
        }
    }
}
