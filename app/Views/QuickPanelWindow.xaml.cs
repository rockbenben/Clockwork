using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有。
// 本项目以 WPF 为主（见 GlobalUsings.cs 的同款做法），在本文件里就近指明。
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace Clockwork.Views;

/// <summary>面板上的一格：图标 + 标签 + 一个动作。禁用的格子仍然画出来（压暗），因为「这个组存在但被停用了」
/// 和「这个组不见了」是两件事，后者会让人以为面板漏了东西。
/// <paramref name="Icon"/> 说明这一格画什么：一个 MDL2 字形，或一个要取图标的文件路径（见 Core.PanelIcon）。</summary>
/// <param name="OnEdit">右键「编辑」。为 null 表示这一格不可编辑（夹板上那排自带操作就是）。</param>
/// <param name="OnDelete">右键「删除」。同上。</param>
/// <param name="Tip">悬停时说全的那句（「关闭窗口 Slack」）。为空则用 <paramref name="Label"/>——
/// 格子上的标题是给眼睛扫的，短；ToolTip 是给「这到底是哪个」兜底的，可以长。</param>
/// <param name="Search">搜索语料。为空就搜 <paramref name="Tip"/>。ToolTip 会把长路径截断，
/// 搜索却要搜得到路径尾巴，所以这一份必须是不截断的全文（见 StepDisplay.StepSearchText）。</param>
public sealed record PanelTile(string Label, PanelIconSpec Icon, Action Run, bool Enabled = true,
                               Action? OnEdit = null, Action? OnDelete = null, string? Tip = null,
                               string? Search = null, bool FillsEmptyPanel = false)
{
    /// <summary>便捷构造：直接给一个字形字符。托盘那几个自带操作用它。</summary>
    public PanelTile(string label, string glyph, Action run, bool enabled = true, bool fillsEmptyPanel = false)
        : this(label, new PanelIconSpec(PanelIconKind.Glyph, glyph, glyph), run, enabled,
               FillsEmptyPanel: fillsEmptyPanel) { }
}

/// <summary>面板的一页：页名 + 这页的格子。页名显示在翻页刻度旁边。</summary>
/// <param name="OnAdd">这一页末尾那个「新增」格。为 null 则不画。</param>
/// <param name="Tab">这一页归哪一类（空 = 未分类）。顶栏一格一类。</param>
/// <param name="Screen">同一页被容量切开之后的第几屏（从 0 起）。左侧页签只画 Screen==0 的那些——
/// 它回答「哪一页」，而「这一页的第几屏」是腰栏圆点的事。</param>
public sealed record PanelTilePage(string Title, IReadOnlyList<PanelTile> Tiles, Action? OnAdd = null,
                                   string Tab = "", int Screen = 0);

/// <summary>面板外观（设置页可改）。集中成一个记录而不是四个参数：
/// 这几项总是一起读、一起变，拆开传迟早会漏传一个。</summary>
public sealed record PanelLook(int Columns = 4, string TileSize = "normal", bool IconOnly = false, bool ShowOps = true,
                               int TopRows = 3, int BottomRows = 4, bool LeftTabs = true, bool TopTabs = true);

// 快捷面板：热键或中键长按在光标处呼出的瓦片网格，点一下就跑，跑完自己关。
// 视觉定位与各处取舍写在 XAML 的注释里（机芯夹板 / 凹槽 / 刻度索引），这里只讲行为。
//
// 摆位为什么要自己算：本程序声明了 PerMonitorV2（见 app.manifest），不存在「整个桌面一个缩放」这回事。
// Left/Top 是 DIP，而光标位置是物理像素，两者之间的换算系数取决于光标落在哪块屏——
// 笔记本 200% 配外接 100% 时按主屏系数换算会把面板扔到另一块屏上去。
// 所以全程用物理像素算，最后一步 SetWindowPos 直接落物理坐标，中间不经过任何 DIP 往返。
public partial class QuickPanelWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint mon, int type, out uint x, out uint y);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004;
    // 工作区上下各留一点，面板不要顶满屏幕高——顶满时滚动条紧贴任务栏，看着像被裁了。
    private const double WorkAreaMargin = 110;

    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private readonly IReadOnlyList<PanelTilePage> _pages;
    private readonly PanelLook _look;
    private readonly PanelTileMetrics _metrics;
    private int _page;
    private bool _closing;
    private bool _menuOpen;   // 右键菜单挂着：此刻的失焦是菜单造成的，不是用户点了别处
    private bool _searching;  // 搜索态：凹坑里铺的是搜索结果，不是某一页
    private bool _hasSearch;  // 这个面板装了搜索吗（动作够多才装，见 BuildWaist）
    private List<PanelTile> _hits = new();   // 当前搜索结果，按名次排好（回车跑第一个）
    private DispatcherTimer? _focusWatch;

    /// <param name="pages">用户自己的那些页：动作组、以及展开成单格的动作。</param>
    /// <param name="ops">夹板上那排自带操作：重跑清单 / 停止 / 勿扰 / 打开窗口。</param>
    /// <param name="look">外观设置。</param>
    /// <param name="chromeOps">表圈右端那几颗：与这个程序自己有关的入口（管手势 / 管面板）。
    /// 名字里的 chrome 就是「表圈」——它们一度挂在腰栏上，理由与搬走的经过见 QuickPanelWindow.xaml。
    /// 与 <paramref name="ops"/> 分开传，是因为两者答的不是同一个问题：这几颗是「去管这个程序」，
    /// 那一排是「拿这台机器做点什么」（重跑 / 停止 / 勿扰 / 显示主窗）。</param>
    public QuickPanelWindow(IReadOnlyList<PanelTilePage> pages, IReadOnlyList<PanelTile>? ops = null,
                            PanelLook? look = null, IReadOnlyList<PanelTile>? chromeOps = null)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);   // 深色标题栏用不上（无边框），要的是它消白闪的那半边
        // 这里不设 FlowDirection：RTL 由 App.ApplyUiCulture 在建任何窗口之前覆盖 Window 的默认元数据
        // 统一处理，逐窗口再设一遍只会多出一处将来会和它分歧的地方。

        _look = look ?? new PanelLook();
        _metrics = PanelMetrics.For(_look.TileSize, _look.IconOnly);
        // 一页都没有（全新安装、或把组全从面板上摘了）：留一页说明，别让凹槽缩成一条缝看着像坏了。
        _pages = pages is { Count: > 0 } ? pages : new[] { new PanelTilePage("", Array.Empty<PanelTile>()) };

        HintText.Text = Strings.Get("Panel_Hint");
        Tiles.Columns = TilesBottom.Columns = PanelMetrics.ClampColumns(_look.Columns);
        // 面板宽度由格子阵说了算，不由底下那排文字说了算：给 WrapPanel 一个上限，
        // 它才会折行而不是把窗口横向撑开（详见 PanelMetrics.GridWidth）。
        // 宽度给外框：那条发丝线是夹板的下沿，得和凹坑一样宽（同一个 GridWidth），
        // 不铺满整幅——铺满就成了窗口的下沿，说的是另一件事。
        OpsPlate.MaxWidth = PanelMetrics.GridWidth(_look.TileSize, _look.IconOnly, _look.Columns);
        EmptyBlock.MaxWidth = OpsPlate.MaxWidth;   // 空面板与有内容的面板一样宽

        if (_look.ShowOps)
            foreach (var t in ops ?? Array.Empty<PanelTile>()) Ops.Children.Add(MakeRailButton(t));
        else OpsPlate.Visibility = Visibility.Collapsed;   // 连那条线一起收——没有开关就没有开关带

        BuildIndex();
        BuildRails();
        BuildChrome(chromeOps);
        ShowPage(0, animate: false);

        SourceInitialized += (_, _) => PlaceAtCursor();
        // 焦点离开就关：点了别处、Alt+Tab 走了，面板都该消失——一个浮在所有窗口最上面、
        // 又不在任务栏里的东西，留着就是块甩不掉的牛皮癣。
        Deactivated += (_, _) => { if (!_menuOpen) Dismiss(); };
    }

    // ── 翻页 ──

    private bool MultiPage => _pages.Count > 1;

    // 切换条：**一个点 = 一页，当前那个拉成胶囊**。摆在两坑之间那道缝里，设计理由写在 XAML 里。
    //
    // 一页一个，不是一页一段：一段三颗齿时，三页就是九颗，得先数清一段有几颗才知道有几页——
    // 而这条索引存在的意义正是「一眼看出有几页」。一页一个，数几个就是几个。
    //
    // 当前页除了变色还**变形**（6 → 16 宽的胶囊）。只靠颜色是不够的：
    // 深色下强调蓝与钢灰的明度差本来就不大，扫一眼未必分得出，色觉障碍更是分不出。
    // 形状是第二条线索，不占额外空间。这也是轮播指示器现在的通行做法。
    //
    // 点本身 6×6，可 6×6 的点击靶按不着。所以外面套一层透明的命中区：
    // 看得见的还是那个点，点得到的是 10×18。左右各留 2，间距仍是 4，节奏不因为「好点」而走样。
    // 这一页从第几条开始（同一页被容量切开的那几屏是连着的，Screen 从 0 数起）。
    private int PageStart(int i)
    {
        while (i > 0 && i < _pages.Count && _pages[i].Screen > 0) i--;
        return i;
    }

    // 这一页一共几屏。
    private int ScreenCount(int start)
    {
        int n = 1;
        while (start + n < _pages.Count && _pages[start + n].Screen == n) n++;
        return n;
    }

    // 圆点画什么，取决于左边那条页签栏在不在：
    //   页签栏在 → 点表示**这一页的第几屏**（「哪一页」已经由页签回答了）。
    //   页签栏不在（只有一页，或用户把它关了）→ 点退回表示「哪一页」，否则关掉页签栏就等于
    //     把翻页入口一起关掉了。
    // 两种模式下点的含义都是单一的；以前它同时兼着两件事，于是一个被切成三屏的页
    // 看着和「三个不同的页」一模一样。
    private void BuildIndex()
    {
        PageIndex.Children.Clear();
        bool byScreen = RailScroll.Visibility == Visibility.Visible;
        int first = byScreen ? PageStart(_page) : 0;
        int count = byScreen ? ScreenCount(first) : _pages.Count;
        // **腰栏跟着刻度走，不自己判。** 它俩曾各判各的：腰栏看「有几页」，刻度看「有几屏」——
        // 两页而每页都只有一屏时，腰栏可见、刻度收起，于是格子底下剩一条什么都没有的 1px 缝。
        // 腰栏唯一的孩子就是刻度，那就让它由刻度决定，两处真源合成一处。
        if (count <= 1) { Waist.Visibility = PageIndex.Visibility = Visibility.Collapsed; return; }
        Waist.Visibility = PageIndex.Visibility = Visibility.Visible;
        for (int k = 0; k < count; k++)
        {
            int i = first + k;
            int idx = i;
            var tooth = new Border { Width = i == _page ? 16 : 6, Height = 6, CornerRadius = new CornerRadius(3), Background = (Brush)FindResource("BrushSteel") };
            var hit = new Border
            {
                Child = tooth,
                Background = Brushes.Transparent,   // 不给底色，命中区就是空的，点不着
                Padding = new Thickness(2, 6, 2, 6),
                Cursor = Cursors.Hand,
                Tag = idx,
                ToolTip = _pages[i].Title,
            };
            // 齿可点：想去哪一页点哪一颗。只能滚轮/按键的话，鼠标用户得瞎试。
            hit.MouseLeftButtonDown += (_, e) => { e.Handled = true; ShowPage(idx); };
            // 没有文字的东西必须显式给可访问名，否则读屏软件只知道这儿有个方块。
            System.Windows.Automation.AutomationProperties.SetName(hit, _pages[i].Title);
            PageIndex.Children.Add(hit);
        }
    }

    // ── 两条导航栏：上边分类、左边页签 ──
    //
    // 它们和腰栏那排圆点分工明确：**栏回答「哪一页」，圆点回答「这一页的第几屏」**。
    // 以前这两件事挤在同一排圆点里，于是一个 24 格的页被容量切成三屏之后，
    // 那三个点看着和「三个不同的页」一模一样——而页是有名字的，人认路靠的正是名字。
    //
    // **两条栏是父子**：上边选分类，左边列这一类里的页。页只有一个落点（左栏），
    // 所以不存在「这一页到底在哪条栏上」这个问题——那正是把它们做成平行两条时的毛病。
    //
    // 两条栏都**只有一句话可说时就不画**：只有一类时没有分类栏，当前这一类只有一页时没有页签栏。
    // 所以不填分类的人永远看不到上面那条，版面与从前逐像素相同。
    // 开关（PanelLook.LeftTabs / TopTabs）是给「知道有好几类/好几页但就是不想看见」的人留的。

    // IReadOnlyList 没有 IndexOf，而页在这一份清单里是唯一的对象，按引用找就够。
    private int IndexOfPage(PanelTilePage p)
    {
        for (int i = 0; i < _pages.Count; i++) if (ReferenceEquals(_pages[i], p)) return i;
        return 0;
    }

    // 分类的口径（去空白 + 不区分大小写）与面板管理器、排版、重排共用一处定义：
    // 它们回答的是同一个问题「这一页归哪一类」，各写一份的话，同一批页在两处会分成不同的类。
    private static string CatOf(PanelTilePage p) => PanelLayout.Category(p.Tab);

    // 当前这一类。跟着当前页走（见 SyncCategory）：搜索命中、场景页优先、方向键翻页，
    // 都可能把你带到别的类里去，那时顶栏得跟着亮到对的那一格。
    private string _cat = "";

    // 顶栏要画哪几格：分类，按第一次出现的先后。未分类的那一堆也占一格。
    private List<string> CategoryList()
    {
        var seen = new List<string>();
        foreach (var p in _pages)
        {
            if (p.Screen != 0) continue;
            var c = CatOf(p);
            if (!seen.Any(x => PanelLayout.SameCategory(x, c))) seen.Add(c);
        }
        return seen;
    }

    // 左栏：当前这一类里的页。一页一条（同一页被容量切开的那几屏只算一条）。
    private IEnumerable<PanelTilePage> PagesInCurrentCategory()
        => _pages.Where(p => p.Screen == 0 && PanelLayout.SameCategory(CatOf(p), _cat));

    // 顶栏：一格一个分类，点了跳到这一类的第一页。
    private void BuildCategoryBar()
    {
        TopTabBar.Children.Clear();
        var cats = CategoryList();
        if (cats.Count < 2) return;   // 只有一类时这条栏只会把那一类的名字念一遍
        foreach (var c in cats)
        {
            string label = c.Length > 0 ? c : Strings.Get("Panel_Uncategorized");
            var b = new Button
            {
                Style = (Style)FindResource("PanelTopTab"),
                Content = label,
                Tag = c,
                ToolTip = label,
            };
            b.Click += (_, _) =>
            {
                // 跳到这一类的第一页。**先换类再找页**：页签栏画的是当前类里的页，
                // 顺序反过来的话会先拿旧的类去筛，找不到就停在原地，读起来像点不动。
                _cat = c;
                var first = _pages.FirstOrDefault(x => x.Screen == 0
                    && PanelLayout.SameCategory(CatOf(x), c));
                ShowPage(first == null ? _page : IndexOfPage(first));
            };
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            TopTabBar.Children.Add(b);
        }
    }

    // 左栏：一条一页，点了直接切到那一页。
    private void BuildPageRail()
    {
        PageRail.Children.Clear();
        foreach (var p in PagesInCurrentCategory())
        {
            int idx = IndexOfPage(p);
            var b = new Button
            {
                Style = (Style)FindResource("PanelPageTab"),
                Content = TabRow(p),
                Tag = idx,
                ToolTip = p.Title,
            };
            b.Click += (_, _) => ShowPage(idx);
            System.Windows.Automation.AutomationProperties.SetName(b, p.Title);
            PageRail.Children.Add(b);
        }
    }

    /// <summary>把「当前分类」对齐到当前页所属的那一类，并在变了的时候重画左栏。</summary>
    //
    // 当前页不是只有点页签才会变：搜索命中会跳、场景页会把面板开在别的一页上、
    // 翻页键会一路走到下一类里去。那时顶栏若还亮着旧的那一格，左栏列的也还是旧那一类的页——
    // 于是你眼前这一页在左栏里根本找不到自己。
    private void SyncCategory()
    {
        int i = PageStart(_page);
        if (i < 0 || i >= _pages.Count) return;
        var c = CatOf(_pages[i]);
        if (PanelLayout.SameCategory(c, _cat)) return;
        _cat = c;
        BuildPageRail();
        RailScroll.Visibility = Show(_look.LeftTabs && PageRail.Children.Count > 1);
    }

    // 左栏的一条：只有页名。
    //
    // 一度还画着页图标。可页图标绝大多数时候取的是同一个默认字形，于是左栏成了一列
    // 一模一样的记号 + 各不相同的名字——那个记号一个字节的信息都不带，却占着宽度，
    // 还让两条栏长得不一样（上栏本来就只有名字）。人认路靠的是名字，这一点这个文件里
    // 早就写着了。图标仍然有用，只是用在别处：这一页被别的格子指过来时显示的就是它。
    private object TabRow(PanelTilePage p) => new TextBlock
    {
        Text = p.Title,
        MaxWidth = 110,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // 两条栏都**只有一句话可说时就不画**：只有一类时没有分类栏（那格只会把类名念一遍），
    // 当前这一类只有一页时没有页签栏（同理）。开关是给「知道有好几个但就是不想看见」的人留的。
    private void BuildRails()
    {
        // 当前页在哪一类，就先站到哪一类去：面板可能一开就停在某个场景页上，
        // 而那一页未必在未分类那一格里。
        int start = PageStart(_page);
        _cat = start >= 0 && start < _pages.Count ? CatOf(_pages[start]) : "";
        BuildCategoryBar();
        BuildPageRail();
        TopTabRow.Visibility = Show(_look.TopTabs && TopTabBar.Children.Count > 1);
        RailScroll.Visibility = Show(_look.LeftTabs && PageRail.Children.Count > 1);
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // 两条栏各亮一格：上边亮当前这一类，左边亮当前这一页。第二屏也算在它那一页上（PageStart）。
    //
    // 两种亮法都各带一条**形状**线索（左栏那条竖杠、上栏那条下划线），不只靠颜色：
    // 深色下强调蓝与钢灰的明度差本来就不大，色觉障碍更分不出。
    private void PaintRails()
    {
        SyncCategory();
        int cur = PageStart(_page);
        foreach (var child in PageRail.Children)
            if (child is Button b && b.Tag is int i)
            {
                bool on = i == cur;
                b.BorderBrush = on ? (Brush)FindResource("BrushAccent") : Brushes.Transparent;
                b.Foreground = (Brush)FindResource(on ? "BrushPaper" : "BrushMuted");
                b.Background = on ? (Brush)FindResource("BrushAccentTint") : Brushes.Transparent;
            }
        foreach (var child in TopTabBar.Children)
            if (child is Button b && b.Tag is string c)
            {
                bool on = PanelLayout.SameCategory(c, _cat);
                b.BorderBrush = on ? (Brush)FindResource("BrushAccent") : Brushes.Transparent;
                b.Foreground = (Brush)FindResource(on ? "BrushAccentText" : "BrushMuted");
            }
    }

    // ── 腰栏右侧：找东西 / 管东西 ──

    // 空面板那颗按钮要做的事，与表圈上那颗齿轮一模一样。存下来而不是各建一份：
    // 「一个动作在整条流程里只有一个名字」——两处若各自取名，用户就得学两遍。
    private Action? _manage;
    private string _manageLabel = "";

    /// <summary>面板自己的那几个控件（找东西、管东西）摆表圈右端，并决定腰要不要出现。</summary>
    private void BuildChrome(IReadOnlyList<PanelTile>? chromeOps)
    {
        EmptyAction.Click += (_, _) => _manage?.Invoke();
        // 搜索只在「有得可搜」时才装：一页三个动作的面板摆一个放大镜，是给用户一个用不上的承诺。
        // 阈值取一屏（一页的容量）——装得下一屏的东西，用眼睛找比打字快。
        int total = _pages.Sum(p => p.Tiles.Count);
        _hasSearch = total > PanelMetrics.PageCapacity(_look.TopRows, _look.BottomRows, _look.Columns);
        if (_hasSearch)
            HeaderOps.Children.Add(MakeWaistButton(char.ConvertFromUtf32(0xE721), Strings.Get("Panel_Search"), () => StartSearch()));

        // 「管理面板」这类**关于这个面板本身**的入口摆表圈右端，不摆腰栏（理由见 XAML 那段）。
        foreach (var t in chromeOps ?? Array.Empty<PanelTile>())
        {
            var op = t;
            HeaderOps.Children.Add(MakeWaistButton(op.Icon.Fallback, op.Label, () => { Dismiss(); op.Run(); }));
            // 空面板那颗按钮用的是同一个动作、同一个名字：空态叫你去哪儿，按钮就是那儿。
            //
            // **认标记，不认顺序。** 这儿一度写的是「取第一个 chromeOps」，那在只有「管理面板」
            // 一个入口时是对的；后来手势管理器加了进来、且排在它前面，空面板的行动按钮就
            // 悄悄变成了「鼠标手势」——一块空面板叫你去配手势，那和把它填满毫无关系。
            // 位置依赖不会报错，只会在别人调整顺序时安静地指错地方，所以改成显式标记。
            if (op.FillsEmptyPanel && _manage == null)
            { _manage = () => { Dismiss(); op.Run(); }; _manageLabel = op.Label; }
        }

        // 腰的显隐由 BuildIndex 一处说了算（它算的是「这一页有几屏」，而腰上就只有那把刻度）。
        // 这儿曾按「有几页」另判一次，两处真源对不上就漏出一条空缝。
    }

    private Button MakeWaistButton(string glyph, string tip, Action run)
    {
        var g = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var b = new Button
        {
            Style = (Style)FindResource("RailButton"),
            Content = g,
            ToolTip = tip,
            Padding = new Thickness(6, 0, 6, 0),
            Height = 22,
        };
        // 没有文字的按钮必须显式给可访问名：内容只剩一个私用区码位，读屏软件念出来是乱码。
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        BindForeground(b, new[] { g });
        b.Click += (_, _) => run();
        return b;
    }

    // ── 搜索 ──
    //
    // 搜的是**所有页的所有格子**，不是当前这一页：动作多到要翻页时才有搜索，
    // 而那时「它在哪一页」恰恰是用户不知道、也不该被要求知道的事。
    // 结果直接铺进凹坑里，点一下就跑——与平时的格子完全一样，不另造一套结果列表。

    // internal 而非 private：截图 harness（DevChecks）要能拍到搜索态。
    // 只有渲染出来才知道输入框在最长译文下会不会挤掉那个计数、阿语下会不会镜像错——
    // 而这个状态没有任何静态入口能到达，除了让它自己进去一次。
    internal void StartSearch(string seed = "")
    {
        _searching = true;
        SearchField.Visibility = Visibility.Visible;
        Waist.Visibility = Visibility.Collapsed;   // 刻度收起：搜索结果不分页，留着只会让人以为还得翻页找
        HintText.Text = Strings.Get("Panel_HintBack");   // 此刻 Esc 是「回到分页」，不是「关闭」
        // 搜索时页名要收起来：此刻铺在坑里的是所有页的动作，不是某一页的。
        // 留着一个页名，等于告诉用户「你在这一页里搜」——而搜索恰恰是跨页的。
        PageTitle.Visibility = Visibility.Collapsed;
        // 两条导航栏同理，而且更甚：它们不只写着一个名字，还**高亮着**其中一页一类，
        // 那是在说「你正待在这一叠里」——可结果来自所有页。一并收掉。
        RailScroll.Visibility = Visibility.Collapsed;
        TopTabRow.Visibility = Visibility.Collapsed;
        SearchHint.Text = Strings.Get("Panel_Search");
        SearchBox.Text = seed;
        SearchBox.CaretIndex = seed.Length;
        SearchBox.Focus();
        ApplyFilter(seed);
    }

    private void EndSearch()
    {
        _searching = false;
        SearchField.Visibility = Visibility.Collapsed;
        // 腰的显隐不在这儿判：下面 ShowPage 会走一遍 BuildIndex，由它一处决定。
        // 这儿曾无条件放回去，单页面板退出搜索后就多出一条什么都没有的 1px 缝。
        HintText.Text = Strings.Get("Panel_Hint");
        BuildRails();                      // 两条栏由它按「有没有多页 / 多类」重新决定要不要出现
        ShowPage(_page, animate: false);   // 页名由它按多页与否重新决定
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    private void ApplyFilter(string query)
    {
        var q = (query ?? "").Trim();
        // 匹配与排序在 Core.ActionSearch（可测）：空格分词、拼音首字母、按命中得有多直接排序。
        // 这里只负责把结果铺出来。搜索走不截断的全文（Search），悬停那句截断过的 Tip 兜底。
        _hits = ActionSearch.Rank(_pages.SelectMany(p => p.Tiles), q, t => t.Label, t => t.Search ?? t.Tip);

        // 铺出来的结果有上限。**这不是为了好看，是量出来的**：搜索时每敲一个键都会把结果整批重造，
        // 实测 50 格 23ms、200 格 30ms，到 500 格就是 531ms——打两个字卡一秒，搜索框直接不能用了。
        // 上限按面板自己的容量算（两页），所以它跟着用户的列数/行数设置走，不是拍脑袋的一个数。
        // 而且这个数远超一屏：没有人会滚到第五十个搜索结果，那时该做的是多打一个字。
        int cap = PanelMetrics.PageCapacity(_look.TopRows, _look.BottomRows, _look.Columns) * 2;
        bool cut = _hits.Count > cap;
        var show = cut ? _hits.Take(cap).ToList() : _hits;

        // 读数：没截断时就一个命中数；截断了才显示「显示数 / 命中数」——
        // 分数只在它有话要说的时候出现，平时一个数就够。截断绝不能不吭声。
        SearchCount.Text = cut
            ? $"{show.Count}/{_hits.Count}"
            : _hits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SearchCount.ToolTip = cut ? Strings.Lf("Panel_SearchMore", show.Count, _hits.Count) : null;
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 结果**只用一个坑**，不分带。分带是「一页」的版面节奏（3+4 让眼睛有落点），
        // 而搜索结果是一条按名次排的序列——把它劈成两坑，第 12 名和第 13 名之间就横着
        // 一道缝加两条边框，名次的连续性被版面切断了。
        Tiles.Children.Clear();
        TilesBottom.Children.Clear();
        foreach (var t in show) Tiles.Children.Add(MakeTile(t));
        BandRule.Visibility = Visibility.Collapsed;
        EmptyBlock.Visibility = _hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MarkTopHit();

        if (_hits.Count == 0)
        {
            EmptyNote.Text = Strings.Get("Panel_SearchNone");
            EmptyAction.Visibility = Visibility.Collapsed;   // 搜不到不是「还没建东西」，别在这儿劝人去建
        }
    }

    // 给排第一的那一格贴一条强调色指针：**回车跑的就是它**。
    //
    // 这是这一版最要紧的一处。上一版打完字按回车，会跑掉一个屏幕上没有任何标记的东西——
    // 一个会执行动作的界面，不能让人不知道自己即将执行什么。
    // 用强调色、贴在左缘，与页刻度、场景标签同一种「强调色 = 当前」的语汇；
    // 而强调色在这一屏上只出现这一处，正是「只在关键处发亮」。
    private void MarkTopHit()
    {
        if (Tiles.Children.Count == 0 || Tiles.Children[0] is not Button first) return;
        if (first.Content is not UIElement inner) return;
        first.Content = null;   // 必须先脱离，同一个元素不能挂在两个父级下

        var mark = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)FindResource("BrushAccent"),
            Margin = new Thickness(0, 7, 0, 7),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(mark);
        Grid.SetColumn(inner, 1);
        grid.Children.Add(inner);
        first.Content = grid;
    }

    private void ShowPage(int index, bool animate = true)
    {
        if (_pages.Count == 0) return;
        // 环绕而不是夹住：滚到最后一页再滚一下回到第一页。面板的页数很少（页=组），
        // 环绕让「一路滚过去看看有什么」不会在两端卡住。
        int n = _pages.Count;
        _page = ((index % n) + n) % n;
        var page = _pages[_page];

        RenderBands(page.Tiles, page.OnAdd);

        // 页名在表圈正中间，只在多页时出现（单页时它只是把组名重复一遍）。
        PageTitle.Text = page.Title;
        // 页签栏在的时候，表圈中间那个页名是重复的——同一个名字同时出现在两处，
        // 眼睛会以为它们说的是两件事。谁离格子近谁留下：页签就贴着格子。
        PageTitle.Visibility = MultiPage && RailScroll.Visibility != Visibility.Visible
                               && !string.IsNullOrWhiteSpace(page.Title)
            ? Visibility.Visible : Visibility.Collapsed;

        // 换页只重涂选中态，不必重建两条栏——除非换到了别的类里去，那一步由 PaintRails
        // 里的 SyncCategory 负责（它会顺手重建左栏）。
        PaintRails();
        // 圆点按「当前页有几屏」画，换了页就得重画。放在 PaintRails 之后：
        // 它要读 RailScroll 的可见性来决定画屏还是画页。
        BuildIndex();
        for (int i = 0; i < PageIndex.Children.Count; i++)
            if (PageIndex.Children[i] is Border h2 && h2.Tag is int at && h2.Child is Border t2)
            {
                t2.Background = (Brush)FindResource(at == _page ? "BrushAccent" : "BrushSteel");
                t2.Width = at == _page ? 16 : 6;
            }

        if (animate) SlideIn();
    }

    // 把一批格子铺进上下两坑。翻页与搜索走同一条路——搜索结果就该长得和平时的格子一模一样，
    // 另造一套结果列表只会让人怀疑「搜出来的这个点下去还跑不跑」。
    private void RenderBands(IReadOnlyList<PanelTile> tiles, Action? onAdd)
    {
        // 上带填满 TopRows 行，其余归下带；下带空则连同下面那个坑一起收起来。
        var (top, bottom) = SplitBands(tiles);
        Tiles.Children.Clear();
        TilesBottom.Children.Clear();
        foreach (var t in top) Tiles.Children.Add(MakeTile(t));
        foreach (var t in bottom) TilesBottom.Children.Add(MakeTile(t));
        // 「新增」格挂在内容末尾那条带上——跟在最后一个动作后面，而不是固定在页脚：
        // 它就是「下一个格子」，位置本身在说「往这儿放」。
        if (onAdd is { } add)
            (bottom.Count > 0 ? TilesBottom : Tiles).Children.Add(MakeAddTile(add));
        BandRule.Visibility = bottom.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyBlock.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // 空屏幕是一次邀请，不是一条指路。按钮上的字与那句话里那个名字一字不差。
        EmptyNote.Text = tiles.Count == 0 ? Strings.Get("Panel_Empty") : "";
        EmptyAction.Visibility = tiles.Count == 0 && _manage != null ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyAction.Visibility == Visibility.Visible) EmptyAction.Content = _manageLabel;
    }

    // 切分公式在 Core.PanelLayout（可测），这里只把当前外观的行数/列数喂进去。
    private (List<PanelTile> Top, List<PanelTile> Bottom) SplitBands(IReadOnlyList<PanelTile> tiles)
        => PanelLayout.SplitBands(tiles, _look.TopRows, _look.Columns);

    // 换页时内容横向落位。机构不回弹，所以是线性收尾、没有 ease-out-back 那种弹性——
    // 幅度也压得很小（10px）：这一下是为了说明「内容换了一批」，不是为了表演。
    // 系统关掉动画效果时（辅助功能 / 远程桌面）直接不做，与 WPF 其它控件同一口径。
    private void SlideIn()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        ((TranslateTransform)FindResource("PageSlide")).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = 10,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(110),
        });
    }

    // 滚轮翻页。挂在窗口的 Preview 上而不是凹槽里：格子多到要纵向滚动时，
    // ScrollViewer 会先吃掉滚轮事件，翻页就再也滚不出来了。
    // 凹槽真的能纵向滚时把滚轮让给它——那时用户想滚的显然是内容。
    //
    // `!_searching` 不能省，与 PageUp / PageDown 那两条同一个理由，但后果更重：
    // ShowPage 只重铺凹槽，不动 `_searching` / `_hits`，也不移焦点（键盘翻页那两条
    // 紧跟着 FocusFirstTile，滚轮这条没有）。于是搜索时滚一下：屏上换成了页面格子，
    // 焦点还在搜索框，而回车那条快路的前提正好全部成立——回车跑的是
    // 已经看不见的 `_hits[0]`，而出厂页里就有「锁屏」和「清空回收站」。
    // 搜索态下落给 base：结果溢出时 ScrollViewer 滚结果，装得下时就什么也不做。
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (MultiPage && !_searching && Scroll.ScrollableHeight <= 0)
        {
            ShowPage(_page + (e.Delta < 0 ? 1 : -1));
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    // Esc 关闭；PageUp / PageDown 翻页。
    // 用 Preview 是因为按钮会先吃掉普通的 KeyDown（空格/回车走按钮自己的激活路径），
    // 而 Esc 必须在任何格子拿到它之前生效——它是这个窗口唯一的退出保证。
    // 方向键有意不接管：那是格子之间移动用的（DirectionalNavigation），
    // 两者抢同一组键的话，走到行尾到底是换行还是翻页就说不清了。
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            // 搜索态下 Esc 先退出搜索：一次按键撤一层，而不是把整个面板一起带走——
            // 打错一个字就得重新呼出面板，是最让人不想再用搜索的一种设计。
            case Key.Escape when _searching: EndSearch(); e.Handled = true; return;

            // 回车 = 跑排在第一位的那个。搜索的完整动作是「打几个字，回车」——
            // 中间插一步「把手挪到鼠标上去点」，前面省下的时间就全还回去了。
            //
            // **但焦点已经在某个格子上时不能抢。** 这里是 Preview（隧道）阶段，抢了就
            // `e.Handled = true`，那颗获得焦点的 Button 永远收不到回车，于是「方向键挑一个」
            // 挑中的是 A、回车跑的却是 _hits[0]——用户眼看着 A 高亮着，跑起来是另一个动作，
            // 而出厂默认页里就有「锁屏」和「清空回收站」。下一条 case 的注释写的正是那个流程：
            // 「打字缩小范围 → 方向键挑一个 → 回车」，两条得对得上。
            // 焦点还在搜索框（没按过方向键）时才走这条快路。
            case Key.Enter when _searching && _hits.Count > 0
                                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase:
                var top = _hits[0];
                e.Handled = true;
                if (!top.Enabled) return;   // 停用的项照样列出来（「它被关掉了」和「它没了」得分得清），但不该被回车跑掉
                Dismiss();
                top.Run();
                return;

            // ↓ 从输入框进结果区。进去之后方向键就是格子间移动（DirectionalNavigation），
            // 于是「打字缩小范围 → 方向键挑一个 → 回车」全程不用碰鼠标。
            case Key.Down when _searching:
                FocusFirstTile();
                e.Handled = true;
                return;
            case Key.Escape: Dismiss(); e.Handled = true; return;
            case Key.PageDown when MultiPage && !_searching: ShowPage(_page + 1); FocusFirstTile(); e.Handled = true; return;
            case Key.PageUp when MultiPage && !_searching: ShowPage(_page - 1); FocusFirstTile(); e.Handled = true; return;
        }
        base.OnPreviewKeyDown(e);
    }

    // 直接打字就开始搜索，不必先去点那颗放大镜。面板是呼出来就要用的东西，
    // 手已经在键盘上了，让它先去找一颗按钮再回来打字是白绕一圈。
    // 只在「装了搜索」时接管——没装的时候打字应当什么也不发生，而不是弹出一个搜不了几个东西的框。
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (!_searching && _hasSearch && e.Text.Length > 0 && !char.IsControl(e.Text[0]))
        {
            StartSearch(e.Text);
            e.Handled = true;
            return;
        }
        base.OnPreviewTextInput(e);
    }



    // ── 格子 ──

    private Button MakeTile(PanelTile t)
    {
        // 图标区：真实程序图标（彩色）与线描字形（单色）共用同一个**光学方框**。
        // 不给两者各自的尺寸，是因为 21px 的线描字放在 32px 的彩色图标旁边会显得轻飘飘，
        // 一行里高低不齐——统一方框之后，两种质感并排也是齐的。
        double box = Math.Round(_metrics.GlyphFont * 1.5);
        // 图标装在一个壳里：位图是后台取的（IconLoader 一秒都不能阻塞——这条线程同时是
        // 低级鼠标钩子的泵，占住超过 300ms 会让 Windows 静默卸掉钩子，手势和中键长按一起失灵）。
        // 所以先画字形，位图到货了再把壳里的内容换掉。
        var shell = new ContentControl { Focusable = false };
        FrameworkElement icon = shell;
        var texts = new List<TextBlock>();
        void PaintIcon()
        {
        var bmp = t.Icon.Kind == PanelIconKind.Image
            ? Native.IconLoader.Load(t.Icon.Value,
                () => { if (PresentationSource.FromVisual(shell) != null) PaintIcon(); })   // 面板早关了就别再画
            : null;

        if (bmp != null)
        {
            // 彩色图标**不**跟着按钮的前景色走：那是它自己的颜色，染上纸白就成了一片剪影。
            // 停用时的压暗由格子整体的 Opacity 负责，图标一并暗下去，不必单独处理。
            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Width = box,
                Height = box,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            shell.Content = img;
        }
        else
        {
            // 图片取不到（路径没了 / 读不了）就回退线描——回退画什么在 Core.PanelIcon 就定好了。
            var g = new TextBlock
            {
                Text = t.Icon.Fallback,
                FontFamily = IconFont,
                FontSize = _metrics.GlyphFont,
                // 显式限定枚举类型：Window 自己有个同名实例属性 HorizontalAlignment，会把类型名遮住。
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            texts.Add(g);
            shell.Content = g;
        }
        }
        PaintIcon();
        // 固定高度的容器把两种图标压进同一个方框，行与行之间才对得齐。
        var iconBox = new Grid { Height = box, Margin = new Thickness(0, 0, 0, 0) };
        iconBox.Children.Add(icon);

        var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        stack.Children.Add(iconBox);

        if (_metrics.LabelFont > 0)
        {
            iconBox.Margin = new Thickness(0, 0, 0, 5);
            // 格子是固定尺寸的，而标签是用户起的组名或动作描述，长度不受控。
            // 本仓库的规矩是「不静默截断」（四个列表的单元格都是超宽即省略号 + 悬停看全文），
            // 面板没有理由例外：换行到放不下就省略号，全文挂在 ToolTip 上。
            var text = new TextBlock
            {
                Text = t.Label,
                FontSize = _metrics.LabelFont,
                // 两行；超出的由 Trimming 收成省略号。
                // 2.8 而不是 2.6：WPF 的默认行高约是字号的 1.33 倍，两行要 2.67 倍才装得下。
                // 原来的 2.6 差了那么一点点，于是**第二行永远不出现**——德语「Bildschirm sperren」
                // 与「Bildschirm aus」双双截成「Bildschirm…」，两格看着一模一样。
                // 这种差一点的错误不会报任何异常，只有把长文案渲染出来才看得见。
                MaxHeight = _metrics.LabelFont * 2.8,
                // WrapWithOverflow 而不是 Wrap：Wrap 在**单个词比格子还宽**时会退回字符级断行，
                // 于是俄语「Заблокировать」被切成「Заблокиров / ать」——读起来像坏字，不像截断。
                // WrapWithOverflow 只在词边界断，装不下的词溢出后交给 TextTrimming 收成省略号，
                // 与四个列表「截断一律以 … 收尾、悬停看全文」是同一条口径。
                TextWrapping = TextWrapping.WrapWithOverflow,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
            };
            stack.Children.Add(text);
            texts.Add(text);
        }

        var b = new Button
        {
            Style = (Style)FindResource("Tile"),
            Margin = new Thickness(PanelMetrics.TileGap),   // 数字在尺寸表里，样式里不再写一份
            Content = stack,
            Width = _metrics.Width,
            Height = _metrics.Height,
            IsEnabled = t.Enabled,
            ToolTip = string.IsNullOrWhiteSpace(t.Tip) ? t.Label : t.Tip,   // 只显示图标时这是名字唯一的去处；有文字时它补上被截掉的部分
            // 同上：只显示图标那一档，格子里也没有文字可念。
            ContextMenu = BuildTileMenu(t),
        };
        // **禁用的格子也要能右键。** WPF 默认不在禁用元素上弹 ContextMenu，而「停用」正是用
        // IsEnabled=false 表达的（置灰而不是不显示，「它被关掉了」和「它没了」得分得清）。
        // 于是恰恰是最需要修的那一批格子，唯一的修法（右键 → 编辑 / 删除）也一起没了：
        // 点又点不动、菜单又出不来，面板上完全没有出路。
        // 隔壁面板管理器用 Opacity 表达停用，所以那边一直是可右键的——两处的表达方式不同，
        // 但「停用了还得能改」这条得一样。
        System.Windows.Controls.ContextMenuService.SetShowOnDisabled(b, true);
        System.Windows.Automation.AutomationProperties.SetName(b, t.Label);
        BindForeground(b, texts);
        // 先关窗再执行：动作里有「激活某个窗口」「发送按键」这类要抢前台的步骤，
        // 面板还置顶挂着的话，按键会打到面板上、激活的窗口又立刻被面板压住。
        // 关窗把前台交还给用户原本在用的那个程序，动作再跑，目标才是对的。
        b.Click += (_, _) => { Dismiss(); t.Run(); };
        return b;
    }

    // 「新增」格：一个虚位。压到半透明，因为它不是一个动作，是一个位置——
    // 与真动作同样亮的话，眼睛扫过去会把它当成「有个叫『新增』的东西可以跑」。
    private Button MakeAddTile(Action add)
    {
        var tile = MakeTile(new PanelTile(Strings.Get("Btn_Add"), char.ConvertFromUtf32(0xE710), add));
        tile.Opacity = 0.45;
        return tile;
    }

    // 右键菜单：编辑 / 删除。三条文案（新增/编辑/删除）主界面已经在用，18 语现成，不另造说法。
    //
    // 菜单弹出时面板会失去焦点，而面板的规矩是「失焦即关」——不挡住的话，
    // 菜单刚出来面板就没了，菜单项点下去指向的是一个已经关掉的窗口。
    // 故弹菜单期间挂起那条规矩，菜单关掉再恢复。
    private ContextMenu? BuildTileMenu(PanelTile t)
    {
        if (t.OnEdit == null && t.OnDelete == null) return null;
        var menu = new ContextMenu();
        if (t.OnEdit is { } edit)
            menu.Items.Add(NewMenuItem(Strings.Get("Btn_Edit"), () => { Dismiss(); edit(); }));
        if (t.OnDelete is { } del)
            menu.Items.Add(NewMenuItem(Strings.Get("Btn_Delete"), () => { Dismiss(); del(); }));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        return menu;
    }

    private static MenuItem NewMenuItem(string header, Action run)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => run();
        return mi;
    }

    private Button MakeRailButton(PanelTile t)
    {
        // 只画图标，名字进 ToolTip（理由见 XAML 里那段）。字号比格子里的图标小一档：
        // 这排是仪器自己的开关，不该和你的动作抢分量。
        var glyph = new TextBlock
        {
            Text = t.Icon.Fallback,
            FontFamily = IconFont,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        var b = new Button { Style = (Style)FindResource("RailButton"), Content = glyph, ToolTip = t.Label };
        // 没有文字的按钮必须显式给可访问名：内容只剩一个私用区码位，读屏软件念出来是一串乱码。
        // ToolTip 顶不了这个用——它是给鼠标的，不是给读屏的。
        System.Windows.Automation.AutomationProperties.SetName(b, t.Label);
        BindForeground(b, new[] { glyph });
        b.Click += (_, _) => { Dismiss(); t.Run(); };
        return b;
    }

    // 自己构造的 TextBlock 必须显式绑回按钮的 Foreground：不绑的话它们会命中全局 TextBlock 样式的纸白，
    // 于是「压暗」和「悬停提亮」只作用在背景上，字的亮度纹丝不动。
    private static void BindForeground(Button host, IEnumerable<TextBlock> texts)
    {
        foreach (var tb in texts)
            tb.SetBinding(TextBlock.ForegroundProperty,
                new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = host });
    }

    // ── 显示与摆位 ──

    /// <summary>关闭并保证只关一次。Deactivated 在关闭过程中还会再来一次，不挡住就会重入 Close()。</summary>
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        // 先停看门狗：关窗过程中它再跳一次没有意义，而 DispatcherTimer 不停会一直挂在
        // Dispatcher 的计时器表上，随窗口一起泄漏。
        try { _focusWatch?.Stop(); } catch { }
        _focusWatch = null;
        try { Close(); } catch { }
    }

    // 显示并把键盘焦点抢过来。
    //
    // Activate() 单独用不够：热键呼出时前台窗口属于别的进程，Windows 的前台锁会把这次激活降级成
    // 任务栏闪烁——面板出现了却收不到键盘，方向键和 Esc 全部失灵，只能用鼠标点掉。
    // SetForegroundWindow 走的是「本进程刚响应了一次全局热键」这条豁免路径，此刻调用是允许的。
    public void Popup()
    {
        Show();
        // ForceForeground 而不是裸的 SetForegroundWindow：中键长按那条路没有前台锁豁免，
        // 抢不到前台的话面板既收不到键盘、也永远不会触发「失焦即关」（见 Win32.ForceForeground）。
        try { Native.Win32.ForceForeground(new WindowInteropHelper(this).Handle); } catch { }
        Activate();
        FocusFirstTile();
        StartFocusWatch();
    }

    // 看门狗：定期确认自己还在前台，不在就关。
    //
    // Deactivated 事件是主路，这条是兜底——它专治「从来没激活过」那一类：那种情况下
    // Deactivated 一次都不会来，面板会一直挂在屏幕上，只能靠 Esc 或点格子才消失。
    // 只有**曾经拿到过前台**才允许它关窗：否则一个抢不到前台、但用鼠标仍然点得动的面板
    // 会在 250 毫秒后自己消失，那比不关更糟。
    private void StartFocusWatch()
    {
        bool hadFocus = false;
        _focusWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _focusWatch.Tick += (_, _) =>
        {
            if (_closing || _menuOpen) return;   // 右键菜单挂着时的失焦不算
            var mine = new WindowInteropHelper(this).Handle;
            if (mine == 0) return;
            bool now = Native.Win32.GetForegroundWindow() == mine;
            if (now) { hadFocus = true; return; }
            if (hadFocus) Dismiss();
        };
        _focusWatch.Start();
    }

    // 焦点落在第一个可用的格子：呼出后直接方向键 + 回车就能用，手不必先摸鼠标。
    // 跨凹槽与窄条一起找，且跳过占位文字和压暗的格子——第一格正好是个停用的组时，
    // 焦点落在一个按下去没反应的按钮上，看着就像面板坏了。
    // 顺序就是优先级：**先上带、再下带，最后才是开关带**。
    //
    // 漏掉 TilesBottom 曾让焦点直接跳过一整片格子落到开关带上：把上带行数设成 0
    //（PanelMetrics 允许 0）、或者上带那几个格子恰好全是停用的，第一下方向键 / 翻页之后
    // 焦点就在「重启 / 关机」那一排上，而回车紧跟其后——用户以为在选自己的动作，
    // 按下去跑的是仪器自己的开关。Ops 必须排在最后，那一排的分量本来就该最轻。
    private void FocusFirstTile()
    {
        foreach (var grid in new Panel[] { Tiles, TilesBottom, Ops })
            foreach (var child in grid.Children)
                if (child is Button { IsEnabled: true } b) { b.Focus(); return; }
    }

    // 按光标所在那块屏幕摆位，光标落在面板正中。全程物理像素，理由见类头注释。
    // 任何一步取不到信息就原样退出：WPF 的 WindowStartupLocation="Manual" 会把它留在 (0,0)，
    // 位置不理想但窗口还在、还能用——比为了摆位失败就不开面板好。
    private void PlaceAtCursor()
    {
        // ShowActivated=false 意味着这次显示不是弹给用户看的（冒烟自查会把每扇窗挪到屏幕外、
        // 不激活地开一遍，见 DevChecks.Park）。那种场合追着光标跑会把面板拽回屏幕中央闪一下，
        // 正好破坏 Park 要的「不抢焦点、不进视野」。真正的呼出路径走 Popup()，那里是要激活的。
        if (!ShowActivated) return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == 0 || !GetCursorPos(out var cur)) return;
            var mon = MonitorFromPoint(cur, MONITOR_DEFAULTTONEAREST);
            if (mon == 0) return;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return;

            // 缩放取目标屏的，不是本窗口当前的：窗口这会儿还没落到目标屏上，
            // VisualTreeHelper.GetDpi(this) 这时给的是它被创建时那块屏（通常是主屏）的系数。
            double scale = GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0 : 1.0;

            // 先按目标屏工作区收高度，再量尺寸——顺序反了的话量到的是 XAML 里那个兜底 MaxHeight
            // 撑出来的高度，在小屏上会算出一个比屏幕还高的面板，然后被夹到顶端、底下几排看不见。
            double workH = (mi.rcWork.Bottom - mi.rcWork.Top) / scale;
            // 页签栏与格子区**同一个上限**：它俩并排在一个 Grid 里，谁高谁说了算。
            // 不封住页签栏的话，十几条页签会把整个面板撑得比屏幕还高，而那时连关都不好关
            //（这个窗口没有标题栏）。封住之后多出来的页签在自己那一栏里滚。
            if (workH - WorkAreaMargin > 140) Scroll.MaxHeight = RailScroll.MaxHeight = workH - WorkAreaMargin;

            // Measure 走一遍拿 DIP 尺寸。SizeToContent 下 ActualWidth/Height 此刻还是 0
            //（布局尚未跑过），量一次是唯一能在显示之前知道自己多大的办法。
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            int w = (int)Math.Ceiling(DesiredSize.Width * scale);
            int h = (int)Math.Ceiling(DesiredSize.Height * scale);
            if (w <= 0 || h <= 0) return;

            var (x, y) = PanelPlacement.Place(cur.X, cur.Y, w, h,
                mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
            // NOSIZE：尺寸交给 SizeToContent，这里只搬位置。NOACTIVATE：激活由 Popup 统一负责，
            // 在窗口还没 Show 之前激活它没有意义。
            SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
        }
        catch { }
    }
}
