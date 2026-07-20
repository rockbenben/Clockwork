using System.Collections.ObjectModel;
using System.Linq;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;

namespace Clockwork.ViewModels;

// 系统启动项页一行。开关经注入的 toggle（生产=SystemStartupReader.SetItemEnabled）；失败(NeedsAdmin/Error)回退显示并提示。
public sealed class SystemStartupRowVm : ObservableObject
{
    private readonly Func<SystemStartupItem, bool, string> _toggle;
    private readonly Action<string> _report;
    private readonly Action? _needsAdmin;   // NeedsAdmin 时回调（生产=询问「以管理员身份重开？」）；null 则退回文字提示

    public SystemStartupRowVm(SystemStartupItem item, Func<SystemStartupItem, bool, string> toggle, Action<string> report, Action? needsAdmin = null)
    {
        Item = item;
        _toggle = toggle;
        _report = report;
        _needsAdmin = needsAdmin;
    }

    public SystemStartupItem Item { get; }

    public bool Enabled
    {
        get => Item.Enabled;
        set
        {
            if (Item.Enabled == value) return;
            var res = _toggle(Item, value);
            if (res == "Ok") { Item.Enabled = value; }
            else if (res == "NeedsAdmin")
            {
                // 旧版同款：优先弹「以管理员身份重开？」一键提权（覆盖勾选与接管两条路径）；无回调才退回纯文字提示。
                if (_needsAdmin != null) _needsAdmin();
                else _report(Strings.Lf("SysMsg_NeedsAdmin", Item.Name));
            }
            else { _report(Strings.Lf("SysMsg_ToggleFail", Item.Name, res)); }
            OnPropertyChanged();   // 失败时强制复选框回读 Item.Enabled（未变）→ 视觉回退
        }
    }

    public bool CanEdit => Item.CanToggle;
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string SourceLabel => StartupLabels.TypeLabel(Item.Type) + (string.IsNullOrEmpty(Item.ReadOnlyNote) ? "" : "·" + Strings.Get(Item.ReadOnlyNote));
    public string ScopeLabel => StartupLabels.ScopeLabel(Item.Scope, Item.NeedsAdmin);
}

// 系统启动项页 ViewModel：异步扫描后 SetItems；搜索 + 隐藏只读项 前端过滤。
public sealed class SystemStartupVm
{
    private readonly Func<SystemStartupItem, bool, string> _toggle;
    private readonly Action<string> _report;
    private readonly Action? _needsAdmin;
    private List<SystemStartupItem> _all = new();
    private string _search = "";
    private bool _showReadOnly;

    public ObservableCollection<SystemStartupRowVm> Rows { get; } = new();

    public SystemStartupVm(Func<SystemStartupItem, bool, string> toggle, Action<string> report, Action? needsAdmin = null)
    {
        _toggle = toggle;
        _report = report;
        _needsAdmin = needsAdmin;
    }

    public string Search { get => _search; set { _search = value ?? ""; ApplyFilter(); } }
    public bool ShowReadOnly { get => _showReadOnly; set { _showReadOnly = value; ApplyFilter(); } }

    public void SetItems(List<SystemStartupItem> items) { _all = items ?? new(); ApplyFilter(); }

    // 系统删除成功后本地移除该项（重扫较慢，没必要为一次删除全量重扫）。
    public void Remove(SystemStartupItem item) { _all.Remove(item); ApplyFilter(); }

    // 前端过滤：默认隐藏只读项（策略/系统/一次性等，管不着）；搜索按名称或命令。纯函数、可测。
    public static List<SystemStartupItem> Filter(IEnumerable<SystemStartupItem> items, string search, bool showReadOnly)
    {
        IEnumerable<SystemStartupItem> q = items;
        if (!showReadOnly) q = q.Where(i => i.CanToggle);
        var s = (search ?? "").Trim();
        if (s != "")
            q = q.Where(i => i.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || i.Command.Contains(s, StringComparison.OrdinalIgnoreCase));
        return q.ToList();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var it in Filter(_all, _search, _showReadOnly))
            Rows.Add(new SystemStartupRowVm(it, _toggle, _report, _needsAdmin));
    }
}
