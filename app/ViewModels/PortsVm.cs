using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Clockwork.Engine;

namespace Clockwork.ViewModels;

// 端口页一行。纯只读展示 —— 两个动作（打开链接 / 结束进程）都由窗口发起，
// 不像系统启动项那行还要往回写开关状态，故不需要 ObservableObject。
public sealed class PortRowVm
{
    public PortRowVm(PortEntry item) => Item = item;

    public PortEntry Item { get; }

    public string PortText => Item.Port.ToString(CultureInfo.InvariantCulture);
    public int Pid => Item.Pid;
    public string Address => Item.Address;
    // 名字取不到（受保护进程）时退回 PID：一行里总得留个能定位它的东西。
    public string ProcessName => Item.ProcessName.Length > 0 ? Item.ProcessName : "PID " + Item.Pid;
    public string Url => "http://localhost:" + PortText;
    // PID 0/4 是内核占位，杀必失败 → 菜单项灰掉，别让人点了没反应以为功能坏了
    //（与系统启动项页 CanEdit 门控「接管/删除」同一条思路）。
    public bool CanKill => Item.Pid > 4;

    // 读屏软件念的就是这一串：端口＋进程名＝一行里最能定位它的两个字段。
    public override string ToString() => PortText + " · " + ProcessName;
}

// 端口页 ViewModel：SetItems 收下扫描结果，搜索 + 「显示全部」在前端过滤。
// 与 SystemStartupVm 同形，区别是这里没有可写状态，构造函数不需要注入任何回调。
public sealed class PortsVm
{
    private List<PortEntry> _all = new();
    private string _search = "";
    private bool _devOnly;
    private bool _showAll;

    public ObservableCollection<PortRowVm> Rows { get; } = new();

    public string Search { get => _search; set { _search = value ?? ""; ApplyFilter(); } }
    // 三档视图的两个开关。ShowAll 压过 DevOnly（界面上同时把 DevOnly 置灰）：
    // 两个独立布尔能摆出四种组合，其中「只看 dev + 看全部」自相矛盾，
    // 不定优先级就得靠使用者自己猜。
    public bool DevOnly { get => _devOnly; set { _devOnly = value; ApplyFilter(); } }
    public bool ShowAll { get => _showAll; set { _showAll = value; ApplyFilter(); } }

    public void SetItems(List<PortEntry> items) { _all = items ?? new(); ApplyFilter(); }

    // 前端过滤的三档：全部 → 只看 dev 服务（白名单）→ 隐藏系统服务（兜底）。
    // 搜索是在当前那一档之上再筛而不是绕过它 —— 想看被挡掉的请改档位，那是个明确动作。
    public static List<PortEntry> Filter(IEnumerable<PortEntry> items, string search, bool devOnly, bool showAll)
    {
        IEnumerable<PortEntry> q = items;
        if (!showAll) q = q.Where(devOnly ? PortReader.IsDevService : PortReader.IsUserService);
        var s = (search ?? "").Trim();
        if (s != "")
            q = q.Where(e => e.Port.ToString(CultureInfo.InvariantCulture).Contains(s, StringComparison.Ordinal)
                          || e.ProcessName.Contains(s, StringComparison.OrdinalIgnoreCase));
        return q.ToList();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var e in Filter(_all, _search, _devOnly, _showAll)) Rows.Add(new PortRowVm(e));
    }
}
