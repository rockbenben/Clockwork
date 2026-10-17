using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
// UseWindowsForms 的全局 using 让 ButtonBase 撞名，显式钉到 WPF（同 GestureTrailWindow 的惯例）。
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.Views;

// 站点映射表的编辑器（域名 → 本地源码根 + 路径改写）。
//
// 存在的理由：「网址 → 本地源文件」那个模板落地之后，用户点了它、报错让他去改
// clockwork.settings.json 的 sites——一条要手写正则与绝对路径、中间隔着记事本的路。
// 这张表本身早该有界面；此前只是因为「看起来只有两列、控件化收益不大」就一直手改。
//
// 三条约定：
//   · **工作副本**：进来先深拷贝，点「确定」才写回（与 GroupEditorWindow 同一套，取消不动原配置）。
//   · **列表只读**：字段全在右边那一栏改，列表永远显示已存下来的值——
//     否则改到一半还没失焦就切走，改动会跟着选中项一起丢。
//   · **只补 null，不改写**用户写下的内容（域名不 Trim、不小写化），与 ConfigStore.Normalize 同一条底线。
public partial class SiteMapEditorWindow : Window
{
    private readonly List<SiteEntry> _sites;      // 工作副本
    private readonly ObservableCollection<Row> _rows = new();
    private ObservableCollection<SitePathRule> _rules = new();   // 当前装着的站点的改写规则
    private SiteEntry? _loaded;          // **界面上正装着哪一条**（与「选中了哪一条」不是一回事，见 OnSelectionChanged）
    private bool _loading;                          // 回填字段时为真，别把回填当成用户改动再写回去

    /// <summary>返回改好的站点表；null = 用户取消，调用方不要动原值。</summary>
    public static List<SiteEntry>? Edit(Window? owner, List<SiteEntry> current)
    {
        var dlg = new SiteMapEditorWindow(current) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg._sites : null;
    }

    // 公开给 DevChecks 建变体用（--shots 要拍这一扇窗，见「有内容的站点」那一条）。
    public SiteMapEditorWindow(List<SiteEntry> current)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);

        // 深拷贝一份改：取消不留下半截改动，RuleDel 之类也不会碰到真配置里的集合。
        _sites = ConfigStore.DeepClone(current ?? new List<SiteEntry>());
        foreach (var s in _sites) { s.Hosts ??= new(); s.PathRules ??= new(); }

        _rows.CollectionChanged += (_, _) => SyncEmptyHint();
        SyncEmptyHint();

        SiteList.ItemsSource = _rows;
        SiteList.SelectionChanged += (_, _) => OnSelectionChanged();
        RefreshRows();
        if (_rows.Count > 0) SiteList.SelectedIndex = 0;
        SyncEditorEnabled();
    }

    private SiteEntry? Current => SiteList.SelectedIndex >= 0 && SiteList.SelectedIndex < _rows.Count
        ? _rows[SiteList.SelectedIndex].Site
        : null;

    /// <summary>把整个工作副本重新投进列表（增删、重排、字段改动之后）。</summary>
    private void RefreshRows()
    {
        _rows.Clear();
        foreach (var s in _sites) _rows.Add(new Row(s));
    }

    private void SyncEmptyHint()
        => EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnSelectionChanged()
    {
        // **先落旧的那一条，再装新的一条。** 落账的目标是 `_loaded`（界面上正装着哪一条），
        // 不是 `Current`（此刻选中了哪一条）——两者在换选中项的那一刻已经不是一个东西了。
        //
        // 写成 Current 会有一个安静而彻底的后果：`RefreshRows()` 往空列表里加第一行时，
        // 表格会自动选中它，于是 FlushEditor 拿着**还空着的输入框**去写那条站点——
        // 名字、域名、源码根全被抹掉，紧接着 LoadEditor 再把这条被抹空的站点装进来。
        // 打开这张表看到的就是一张空白表，而站点一条也没少。
        FlushEditor();
        LoadEditor();
        SyncEditorEnabled();
    }

    private void LoadEditor()
    {
        _loading = true;
        var s = Current;
        _loaded = s;
        NameBox.Text = s?.Name ?? "";
        HostsBox.Text = s == null ? "" : string.Join("\r\n", s.Hosts);
        RootBox.Text = s?.SourceRoot ?? "";
        IndexBox.Text = s?.IndexFile ?? "";
        _rules = new ObservableCollection<SitePathRule>(s?.PathRules ?? new List<SitePathRule>());
        Rules.ItemsSource = _rules;
        _loading = false;
        UpdateRootHint();
    }

    /// <summary>把右栏当前内容写回**界面上正装着的那一条**，并把名字/域名同步给列表那一行。</summary>
    private void FlushEditor()
    {
        var s = _loaded;
        if (s == null || _loading) return;

        s.Name = NameBox.Text.Trim();
        s.SourceRoot = RootBox.Text.Trim();
        s.IndexFile = IndexBox.Text.Trim();
        // 一行一个；空行丢掉。**不 Trim 每一行**：域名里首尾空格几乎肯定是打错了，
        // 而留着它、让匹配报「这个域名没配」比替他悄悄修好更容易查。
        s.Hosts = HostsBox.Text.Split('\n')
                   .Select(x => x.TrimEnd('\r').Trim())
                   .Where(x => x.Length > 0)
                   .ToList();
        s.PathRules = _rules.ToList();

        int row = _sites.IndexOf(s);
        if (row >= 0 && row < _rows.Count) _rows[row].Refresh();
        UpdateRootHint();
    }

    private void UpdateRootHint()
    {
        var root = RootBox.Text.Trim();
        // 只在写了东西的时候才提「不存在」：空目录框不是错误（站点可能还没配完）。
        RootMissing.Visibility = root.Length > 0 && !Directory.Exists(root) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        FlushEditor();
    }

    private void SyncEditorEnabled() => Editor.IsEnabled = Current != null;

    // ---- 左栏操作 ----

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        var s = new SiteEntry { Name = Strings.Get("Site_NewName"), IndexFile = "README.md" };
        _sites.Add(s);
        RefreshRows();
        SiteList.SelectedIndex = _rows.Count - 1;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Del_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        var s = Current;
        if (s == null) return;
        // 站点是「域名 → 路径」的一张表，删错了没有回收站也没有历史。
        // 走 BrandDialog.ConfirmDelete 那个统一口径（Warn 红轨 + 两颗动词按钮），且它会把
        // 要删的名字点出来——「删除」两个字在列表选中项上不指名道姓。
        if (!BrandDialog.ConfirmDelete(this, s.Name)) return;
        _sites.Remove(s);
        RefreshRows();
        // 删完把选中项挪到原位（而不是末尾），否则长列表里手一抖删到中间就跳到最底下去了。
        SiteList.SelectedIndex = Math.Min(_rows.Count - 1, Math.Max(0, SiteList.SelectedIndex - 1));
        SyncEditorEnabled();
    }

    private void Move(int delta)
    {
        FlushEditor();
        int i = SiteList.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _sites.Count) return;
        (_sites[i], _sites[j]) = (_sites[j], _sites[i]);
        RefreshRows();
        SiteList.SelectedIndex = j;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.BrowseFolder(this) is string dir)
        {
            RootBox.Text = dir;
            FlushEditor();
        }
    }

    // ---- 改写规则 ----

    private void RuleAdd_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        if (Current == null) return;
        _rules.Add(new SitePathRule());
        UpdateRootHint();
    }

    private void RuleDel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButtonBase { CommandParameter: SitePathRule r }) _rules.Remove(r);
        FlushEditor();
    }

    // ---- 确定 / 取消 ----

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        // 空的整站丢掉：点「添加」又没填就确定，不该在配置里留下一条什么都没有的记录。
        _sites.RemoveAll(s => string.IsNullOrWhiteSpace(s.Name) && s.Hosts.Count == 0
                              && string.IsNullOrWhiteSpace(s.SourceRoot));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>列表里的一行。只读展示，改动在右边那一栏发生。</summary>
    private sealed class Row : INotifyPropertyChanged
    {
        public Row(SiteEntry site) => Site = site;

        public SiteEntry Site { get; }

        public string Name
        {
            get => Site.Name;
            set { Site.Name = value; OnChanged(); }
        }

        // 域名可能有五六个，全列出来会把列表撑成一条横条。列前三个 + 省略号：
        // 匹配是**任一命中**（见 SiteMap.FindByHost），所以少列几个不影响用户判断对不对。
        public string HostsText
        {
            get
            {
                var h = Site.Hosts;
                if (h.Count == 0) return "";
                return h.Count <= 3 ? string.Join(" · ", h) : string.Join(" · ", h.Take(3)) + " …";
            }
        }

        public void Refresh()
        {
            OnChanged();
            OnChanged(nameof(HostsText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
