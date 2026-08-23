using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Clockwork.Engine;
using Clockwork.I18n;

namespace Clockwork.ViewModels;

// 端口页一行 = 一个端口。纯只读展示 —— 两个动作（用浏览器打开 / 释放端口）都由窗口发起，
// 不像系统启动项那行还要往回写开关状态，故不需要 ObservableObject。
public sealed class PortRowVm
{
    public PortRowVm(PortEntry item, IReadOnlyList<int>? siblings = null)
    {
        Item = item;
        SiblingPorts = siblings ?? Array.Empty<int>();
    }

    // 同一个进程占着的其余端口。释放端口杀的是进程，所以这些会一并没——
    // 不说清楚就是把破坏半径藏起来：你以为在释放 3000，实际是停掉整个 dev server。
    public IReadOnlyList<int> SiblingPorts { get; }

    public PortEntry Item { get; }

    public string PortText => Item.Port.ToString(CultureInfo.InvariantCulture);
    // 列里显示的那个：同进程还占着别的端口时跟一个 +N。
    // 前两档已经把那些端口折起来了（见 PrimaryPorts），所以 +N 是在告诉你
    // 「这一行背后还有 N 个端口」，而不是「另外 N 行跟它是一回事」。
    public string PortLabel => SiblingPorts.Count > 0 ? $"{PortText} +{SiblingPorts.Count}" : PortText;
    // 确认框里要逐个列出的受影响端口
    public string SiblingPortsText => string.Join(", ", SiblingPorts.Select(p => p.ToString(CultureInfo.InvariantCulture)));
    public string Address => Item.Address;
    public string ProcessName => ProcessLabel(Item);
    public string PidText => string.Join(", ", Item.Owners.Select(o => o.Pid.ToString(CultureInfo.InvariantCulture)));
    public string Url => "http://localhost:" + PortText;
    // 来源：多个占用者取去重后并排（同一个脚本跑两遍时就是同一个来源，只显示一次）。
    public string SourceText => string.Join(", ", Item.Owners.Select(o => o.Source).Where(x => x.Length > 0).Distinct());
    // 悬停看完整命令行：来源列只留了最后两段路径，完整路径往往才是回答「哪个项目」的那一半。
    // 开头那一行是 +N 徽标的答案：列里只写得下「还有 N 个」，到底是哪几个只有这里说得出，
    // 否则这个数字提了一个界面自己回答不了的问题（此前只有释放端口的确认框里才列得出来，
    // 而那时候你已经在决定要不要杀进程了，太晚）。
    public string Tooltip
    {
        get
        {
            var owners = string.Join(Environment.NewLine + Environment.NewLine, Item.Owners.Select(o =>
                o.CommandLine.Length > 0
                    ? $"{o.Display}{Environment.NewLine}{o.CommandLine}{(o.WorkingDir.Length > 0 ? Environment.NewLine + o.WorkingDir : "")}"
                    : o.Display));
            return SiblingPorts.Count == 0
                ? owners
                : Strings.Lf("Ports_AlsoListening", SiblingPortsText)
                  + Environment.NewLine + Environment.NewLine + owners;
        }
    }
    // 内核占位（PID 0/4）杀不掉；只要还有一个正经进程占着，这一行就能释放。
    public bool CanKill => Item.Owners.Any(o => o.Pid > 4);
    // 确认框里逐个点名，不能只说「释放端口」——这一下可能带走不止一个进程。
    // 分隔符走 resx（同既有的 Days_Sep）：写死顿号会让 18 种语言全吃到它，
    // 英文弹框里冒出「python (8588)、python (9800)」是看得见的瑕疵。
    public string OwnersText => string.Join(Strings.Get("List_Sep"), Item.Owners.Select(o => o.Display));

    // 进程列的显示：同名多个折成「python ×2」，异名并排「python, node」。
    // 不直接把 PID 拼进来——那是 PID 列的活，一列只干一件事。
    public static string ProcessLabel(PortEntry e)
    {
        if (e.Owners.Count == 0) return "";
        var names = e.Owners.Select(o => o.ProcessName.Length > 0 ? o.ProcessName : "PID " + o.Pid)
                            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return string.Join(", ", names);
    }

    // 读屏软件念的就是这一串：端口＋占用者＝一行里最能定位它的两个字段。
    public override string ToString() => PortText + " · " + ProcessName;
}

// 端口页 ViewModel：SetItems 收下扫描结果，搜索 + 档位在前端过滤。
// 与 SystemStartupVm 同形，区别是这里没有可写状态，唯一的回调 Changed 只用来播报计数。
public sealed class PortsVm
{
    private List<PortEntry> _all = new();
    private string _search = "";
    private bool _devOnly;
    private bool _showAll;
    private string _signature = "";
    private bool _loaded;

    public ObservableCollection<PortRowVm> Rows { get; } = new();

    // 每次重新过滤后播报一次，界面拿它更新「N / M 个端口」。挂在 Rows.CollectionChanged 上也行，
    // 但那是每 Add 一行响一次；这里要的是「这一轮筛完了」这一个时刻。
    public Action? Changed;

    // 计数的分母：全部正在监听的端口，未经任何档位与搜索过滤。
    // 分子是 Rows.Count。两个数摆在一起，「我是不是被藏了东西」就不用靠一段说明去回答。
    public int TotalCount => _all.Count;

    public string Search { get => _search; set { _search = value ?? ""; ApplyFilter(); } }
    // 三档视图的两个开关。ShowAll 压过 DevOnly（界面上同时把 DevOnly 置灰）：
    // 两个独立布尔能摆出四种组合，其中「只看项目服务 + 看全部」自相矛盾，
    // 不定优先级就得靠使用者自己猜。
    public bool DevOnly { get => _devOnly; set { _devOnly = value; ApplyFilter(); } }
    public bool ShowAll { get => _showAll; set { _showAll = value; ApplyFilter(); } }

    // 数据与上一轮完全相同就直接返回，一个 UI 元素都不碰。
    // 自动刷新靠这一条才能存在：ApplyFilter 是清空重建整个列表，
    // 每 5 秒重建一次会丢选中行、丢滚动位置、还闪。端口列表绝大多数时候是稳定的，
    // 所以绝大多数 tick 是空转；真变了才重建，那时候重建是应该的。
    public void SetItems(List<PortEntry> items)
    {
        items ??= new();
        var sig = Signature(items);
        // 用显式的「加载过了」而不是 Rows.Count>0：当前档位真的筛不出行时（比如一个项目服务都没起），
        // Rows.Count 恒为 0，会让每 5 秒的 tick 白白重建一遍空列表。
        if (_loaded && sig == _signature) return;
        _loaded = true;
        _signature = sig;
        _all = items;
        ApplyFilter();
    }

    // 端口 + 占用者 + 绑定地址。来源不用入签：它由命令行推出，而命令行对一个 PID 是不变的。
    public static string Signature(IEnumerable<PortEntry> items)
        => string.Join(";", items.Select(e => $"{e.Port}:{string.Join(",", e.Owners.Select(o => o.Pid))}:{e.Address}"));

    // 前端过滤的三档（从窄到宽）：只看项目服务 → 隐藏系统服务 → 显示全部端口。
    // 搜索是在当前那一档之上再筛而不是绕过它 —— 想看被挡掉的请改档位，那是个明确动作。
    // 进程名与命令行都匹配任一占用者：按端口分行之后一行可能挂着好几个进程，
    // 而搜「serve」或项目名比记端口号更顺手。
    public static List<PortEntry> Filter(IEnumerable<PortEntry> items, string search, bool devOnly, bool showAll)
    {
        var s = (search ?? "").Trim();
        IEnumerable<PortEntry> q = items;
        if (!showAll)
        {
            // 有搜索词时不折叠。折叠是为了让默认浏览视图安静；
            // 一旦你敲了查询，就是明确表达了要找什么，这时候藏匹配项是错的——
            // 否则敲一个被折进去的端口号（它真实存在）会得到零行。
            if (s == "")
            {
                // 先折叠再筛档位：主端口的认定需要该进程的**全部**端口，
                // 先筛掉一部分再比，会把被挡掉的那些错当成不存在。
                var primary = PrimaryPorts(items);
                q = q.Where(e => e.Owners.Any(o => primary.Contains((o.Pid, e.Port))));
            }
            q = q.Where(devOnly ? PortReader.IsDevService : PortReader.IsUserService);
        }
        if (s != "")
            q = q.Where(e => e.Port.ToString(CultureInfo.InvariantCulture).Contains(s, StringComparison.Ordinal)
                          || e.Owners.Any(o => o.ProcessName.Contains(s, StringComparison.OrdinalIgnoreCase)
                                            || o.CommandLine.Contains(s, StringComparison.OrdinalIgnoreCase)));
        return q.ToList();
    }

    // 每个进程的「主端口」。一个进程占十几个端口是常态（Next 的 worker IPC、
    // QQ / 微信的内部通信），逐个占一行会把列表淹掉——实测一个 next 服务就占 14 行。
    //
    // 判据：**绑全网卡的是对外服务，只绑回环的是内部通信**。实测 PID 6600 的 14 个端口里恰好
    // 一个绑 0.0.0.0/::（3000）、其余 13 个只绑 127.0.0.1。
    // 这条只用于进程**内部**比较，不能跨进程用——Vite 默认就只绑 127.0.0.1。
    // 一个都不绑全网卡时退回最小端口号；全都绑时全部保留（宁可多显一行，不藏真服务）。
    public static HashSet<(int Pid, int Port)> PrimaryPorts(IEnumerable<PortEntry> items)
    {
        var set = new HashSet<(int, int)>();
        foreach (var g in items.SelectMany(e => e.Owners.Select(o => (o.Pid, Entry: e))).GroupBy(t => t.Pid))
        {
            var ports = g.Select(t => t.Entry).Distinct().ToList();
            var wide = ports.Where(e => BindsAllInterfaces(e.Address)).ToList();
            var keep = wide.Count > 0 ? wide : new List<PortEntry> { ports.OrderBy(e => e.Port).First() };
            foreach (var e in keep) set.Add((g.Key, e.Port));
        }
        return set;
    }

    // 地址列是多个绑定地址以 ", " 连接；只要有一个是全网卡就算对外。
    private static bool BindsAllInterfaces(string address)
        => address.Split(',').Select(a => a.Trim()).Any(a => a == "0.0.0.0" || a == "::");

    private void ApplyFilter()
    {
        // 兄弟端口必须从**未过滤**的全集算：被当前档位挡掉的那些端口看不见，
        // 但释放时照样会没——按可见行算会把破坏半径报小。
        var byPid = new Dictionary<int, List<int>>();
        foreach (var e in _all)
            foreach (var o in e.Owners)
            {
                if (!byPid.TryGetValue(o.Pid, out var l)) byPid[o.Pid] = l = new List<int>();
                l.Add(e.Port);
            }

        Rows.Clear();
        foreach (var e in Filter(_all, _search, _devOnly, _showAll))
        {
            var siblings = e.Owners.SelectMany(o => byPid.TryGetValue(o.Pid, out var l) ? l : Enumerable.Empty<int>())
                                   .Where(p => p != e.Port).Distinct().OrderBy(p => p).ToList();
            Rows.Add(new PortRowVm(e, siblings));
        }
        Changed?.Invoke();
    }
}
