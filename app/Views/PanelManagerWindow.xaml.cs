using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有。
using System.Windows.Controls.Primitives;
using System.Windows.Input;
// 这些名字 WinForms 那边同名，显式取 WPF 的一套（同 DataGridReorder 里那段说明）。
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Clockwork.Views;

// 面板管理器：**它长得就是那个面板，只是可以编辑**。
// 顶栏选分类、左栏列这一类里的页，中间是当前那一页——两条栏的位置、样式（PanelTopTab /
// PanelPageTab）、分带、格子尺寸，全部取自面板那一份，一样不差。
//
// 顶上一度还有一条「场景」标签（按程序筛），已经拆掉：绑哪个程序是**每一页自己的一个字段**
// （页眉上那颗按钮），不是一层筛选——做成筛选之后，绑给别的程序的页在别的场景里整批看不见，
// 而你要改它的绑定恰恰得先看得见它。
//
// 与面板本身是同一份数据的两种视图——这里显示的页与格，就是 PanelLayout 算给面板的那一份
//（同一个 BuildPages），所以「管理器里看到什么样，呼出来就是什么样」是结构上保证的，不是靠对齐两套代码。
//
// 它曾经把该场景下所有页的格子阵一次全铺出来、纵向滚，理由是「决定哪个动作放哪一页时
// 得同时看见所有页」。那个理由成立，但代价是这扇窗不再像面板：四页就是四堵七行的格子墙。
// 页签把这件事拆开了——所有页的**名字**始终在眼前（归属一眼可见，那才是要判断的东西），
// 格子只画当前这一页。
//
// 分工：**版面归这里，参数归编辑器**。
//   · 一页只有四个字段（名字 / 分类 / 绑哪个程序 / 格子），四个全在页眉那一条上就地改，
//     没有页设置对话框——一扇只装两个输入框、而那两个框外面已经有了的窗，只是多一层门。
//   · 一格的参数（十几种类型、条件、重复、试跑）仍复用主界面那个步骤编辑器——
//     面板不该另造一个字段更少的表单，用户也不该为同一件事学两套。
//
// 一切靠拖，没有一个动作要开对话框：
//   · 拖页签换页的先后；**拖到顶栏某一格上** = 把这一页归进那一类。
//   · 拖格子在页内换位置；**拖到某条页签上** = 把这一步搬进那一页。
//     跨页搬东西不因为「一次只看一页」而变难，反而更短：页签一直都在眼前，不用先滚过去找。
public partial class PanelManagerWindow : Window
{
    /// <summary>一个格子指向谁：它属于哪一页，以及是哪一步（空 = 这是页尾的空位）。</summary>
    private sealed record CellRef(PanelPage Page, LaunchStep? Step);

    /// <summary>拖动中的载荷。拖格子传 <see cref="CellRef"/>，拖整页传 <see cref="PanelPage"/>。</summary>
    private sealed record PageDrag(PanelPage Page);

    /// <summary>拖一整个分类。分类没有自己的存储，所以载荷就是它的名字。</summary>
    private sealed record CatDrag(string Cat);

    private readonly RootConfig _config;
    private readonly Action _save;

    // 面板格子的点击统计（键是步骤 Id）。两个可选参数，**默认值不改变任何现有调用点**——
    // 没传就当作从未统计过：角标不画、悬停不追加、清空按钮不出现。
    // 传的是**引用**不是拷贝：App 记一次点击，这边看到的立即是新数，不必来回同步。
    private readonly IReadOnlyDictionary<string, PanelUsageStore.TileUsage>? _usage;
    private readonly Action? _resetUsage;
    // 正在编的是哪一页。存**引用**不存下标：拖页签会重排页列表，下标会指到隔壁那一页去。
    // 它被删掉了的话，重画时自动退回第一页。
    private PanelPage? _open;
    // 这一屏画出来的页。换页时不必再算一遍 BuildPages，也让 RenderBody 能独立于 RenderPages 跑。
    private List<PanelPageView> _shown = new();
    // 当前在看哪一类（空串 = 未分类那一格）。左栏列的就是这一类里的页。
    private string _cat = "";
    // 本次打开期间新建、**还没有页**的那一类。分类没有自己的存储（它从页的 Tab 上涌现），
    // 所以一类要等第一张页进来才真的存在——在那之前只能由这个字段让它在顶栏上占一格。
    // 第一张页一进来就清掉（那时它已经从页上涌现出来了）；一张页都没放就关窗，它自然不存在。
    private string? _pendingCat;
    // 正在就地改名的分类。与页的 _renaming 是两回事：改分类名要把这一类里
    // **每一页**的 Tab 一起改掉，那是一次批量重写，不是改一个字段。
    private string? _renamingCat;

    // 拖动起点。按下时记下，移动超过系统阈值才真的开拖——否则每一次点击都会被当成拖，格子就点不动了。
    // 起点。**必须可空**：`default` 是 (0,0)，而那是窗口里一个**真坐标**——
    // 于是「按在子按钮上，别开始拖」这道守卫（下面 BeginDragWatch 里）本意是把这一次按下作废，
    // 实际却把起点设成了左上角，接下来指针只要离左上角超过几像素就判定「拖开了」，
    // 拖的还是一个用户从没按过的格子；放开在别的页签上就真的把那一步搬过去并存盘。
    // null = 这一次按下不作数，PastThreshold 直接返回 false。
    private Point? _dragFrom;
    private bool _dragging;   // 拖动中：抬起时的那次 Click 要吞掉，不然拖完还会顺手执行/打开编辑器

    public PanelManagerWindow(RootConfig config, Action save,
        IReadOnlyDictionary<string, PanelUsageStore.TileUsage>? usage = null, Action? resetUsage = null)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _config = config;
        _save = save;
        _usage = usage;
        _resetUsage = resetUsage;
        // 没数据就不给这颗按钮留一个只能点出「确定要清空吗」的位置。
        ResetUsageBtn.Visibility = (_usage is { Count: > 0 } && _resetUsage != null)
            ? Visibility.Visible : Visibility.Collapsed;
        LoadPanelLook();
        RenderPages();
    }

    // 这个格子被点过几次。没有 = 从未统计过（角标不画）。
    private long ClicksOf(LaunchStep step)
        => _usage is not null && step.Id.Length > 0 && _usage.TryGetValue(step.Id, out var u) ? u.Clicks : 0;

    // 清空：统计是辅助信息，但它是这份文件里唯一的不可再生的东西（点击历史没有第二个来源），
    // 所以照 destructive 那一族的口径先问一句。确认完只动统计，配置一根汗毛不碰——
    // 不碰 _config 也就不会触发动作组热键重绑那一串副作用。
    private void ResetUsage_Click(object sender, RoutedEventArgs e)
    {
        if (_resetUsage == null) return;
        if (!BrandDialog.Confirm(this, Strings.Get("Confirm_Title"), Strings.Get("Confirm_ResetUsage"), ToastLevel.Warn)) return;
        _resetUsage();
        ResetUsageBtn.Visibility = Visibility.Collapsed;
        RenderPages();
    }

    // ── 页 ──

    private void RenderPages()
    {
        if (Pages == null) return;
        Pages.Children.Clear();
        TopTabBar.Children.Clear();
        PageRail.Children.Clear();
        // 全部面板页，一个不筛。allContexts：绑了程序的页也要在这儿露面——
        // 「只在哪个程序上出现」是每一页自己的字段，不是决定你能不能看见它的开关。
        // keepEmpty：刚建的页一步都没有，不留着就等于「点了添加什么也没发生」——
        // 而它其实已经存进配置了，于是每点一次多一张看不见的空页。
        // actions 只是拿去给「运行动作」格子取被引用那个动作的图标，页本身与动作清单无关。
        var pages = PanelLayout.BuildPages(_config.PanelPages, keepEmpty: true, allContexts: true,
                                           actions: _config.ActionGroups);
        // 正在改名的那一页要是这一屏根本不画（它被删了），就把这个状态收掉：
        // 那个输入框已经随着重画消失，再留着标记，下次会莫名其妙又冒出一个光标。
        if (_renaming != null && !pages.Any(p => ReferenceEquals(p.Source, _renaming))) _renaming = null;
        // 顶栏一格一类、左栏一条一页——**两条栏本身就是「哪一页归哪一类」的答案**，
        // 不需要另画小标题来说一遍。而格子只画当前这一页：面板上你一次也只看见一页。
        _shown = pages;
        // 顶栏要画的分类 = 从页上涌现的那些 + 这一次新建、还没有页的那一个。
        var cats = PanelLayout.Categories(pages);
        // 已经有页进来了 → 它自己涌现得出来，这个临时标记就该退场。
        if (_pendingCat != null && cats.Any(c => PanelLayout.SameCategory(c, _pendingCat))) _pendingCat = null;
        if (_pendingCat != null) cats.Add(_pendingCat);
        // 正在改名的分类要是这一屏根本不画（它被解散了 / 它的页都没了），把状态收掉——同上面那一页。
        // **必须排在 cats 算完之后**：刚新建、还没有页的那一类不在 Categories(pages) 里，
        // 提前清会把它的改名态当场抹掉，表现是「按下 +，那一格出来了，却没进改名框」。
        if (_renamingCat != null && !cats.Any(c => PanelLayout.SameCategory(c, _renamingCat)))
            _renamingCat = null;

        // 停在一个还没有页的分类上时不去跟当前页——那一类里本来就没有页可跟，
        // 跟了就会被拽回别的类，刚建的那一格当场亮不起来。
        bool onEmptyCat = PanelLayout.PagesIn(pages, _cat).Count == 0
                          && cats.Any(c => PanelLayout.SameCategory(c, _cat));
        if (onEmptyCat) _open = null;
        else
        {
            var cur = pages.FirstOrDefault(p => ReferenceEquals(p.Source, _open)) ?? pages.FirstOrDefault();
            _open = cur?.Source;
            // 当前类跟着当前页走：换了页（或那一页被拖进别的类）之后，顶栏得亮在对的那一格上，
            // 否则左栏列的是另一类的页，而你眼前这一页在里面根本找不到自己。
            _cat = cur != null ? PanelLayout.CategoryOf(cur) : "";
        }
        foreach (var c in cats) TopTabBar.Children.Add(MakeCategoryTab(c));
        // 「+」常驻：**新建分类的唯一入口**。没它的话，一个还没分过类的面板顶栏上只有
        // 「未分类」一格，而「怎么加一类」在界面上没有任何痕迹——上一版正是栽在这里。
        TopTabBar.Children.Add(MakeAddCategory());
        foreach (var p in PanelLayout.PagesIn(pages, _cat)) PageRail.Children.Add(MakeTab(p));
        // 「+」常驻，理由与顶栏那颗一字不差：**新建页的就地入口**。
        // 没它的话，你正看着左栏，而「再来一页」的按钮在窗口另一头的底部工具条上。
        PageRail.Children.Add(MakeAddPage());
        PaintTabs();

        // **两条栏在管理器里都永远画**（哪怕只有「未分类」一格、只有一页）：它们各带着一颗「+」，
        // 而那是新建分类 / 新建页的就地入口。面板上不是这样——那儿只有一类 / 一页时整条栏不画，
        // 因为那时页签只是把名字重复一遍，而面板上也没有什么可新建的。
        //
        // 也**不看那两个「显示页签栏」的勾**：在这扇窗里页签是唯一的换页手段，
        // 关掉就换不了页了。那两个勾管的是呼出来的面板，就在下面那条外观栏里，隔着两厘米。
        TopTabRow.Visibility = Visibility.Visible;
        RailScroll.Visibility = Visibility.Visible;

        RenderBody();
    }

    // 这一类里一张页都没有时，中间画一句「往这儿放第一页」而不是留一片空白。
    // 两种情形共用同一句：刚建的空分类，和一张页都没有的全新配置——对用户是同一件事
    //（这儿可以放东西，入口在左栏那颗 +），没必要为它们各写一句。
    private void RenderEmptyCat()
        => Pages.Children.Add(new TextBlock
        {
            Text = Strings.Get("Panel_NoPages"),
            Foreground = (Brush)FindResource("BrushMuted"),
            Margin = new Thickness(6, 10, 6, 10),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
        });

    /// <summary>一页的右键菜单：改名 / 删除。挂在页签和页眉上。</summary>
    //
    // 右键那个东西本身找它的操作，是几乎所有带页签的界面的共同约定，也是人第一下会试的。
    //
    // **它现在是真的删除，所以走删除确认。** 从前这里叫「从面板移除」、不弹确认，
    // 因为页就是一个动作组，摘掉之后那个组还在主界面列表里，什么都没丢。
    // 页独立成实体之后不再有那个退路：一页就是它自己，删掉就是连着上面的格子一起没了。
    private ContextMenu PageMenu(PanelPage owner)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = Strings.Get("Btn_Rename") };
        rename.Click += (_, _) => { _open = _renaming = owner; RenderPages(); };
        menu.Items.Add(rename);
        var remove = new MenuItem { Header = Strings.Get("Btn_Delete") };
        remove.Click += (_, _) => DeletePage(owner);
        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>一个分类的右键菜单：重命名 / 解散。挂在顶栏那一格上。</summary>
    //
    // 与页签的右键菜单成对——那条操作说明里写的就是「右键页签可重命名或删除」，
    // 而分类页签此前右键什么都不会发生。两种页签长得一样、都能拖、都能双击改名，
    // 只有右键一个有一个没有，这种不对称只会读作「坏了」。
    //
    // 删这一类**不会删掉任何一张页**——它们回到「未分类」。这件事此前是写在菜单标题里的
    //（「把这一类的页移到未分类」），那是把**机制**当成了标题：右键一个分类，看到的却是一句
    // 关于页的话；而它对一个空分类还是空转的，读起来就成了「这条不适用」，那一类看着就删不掉了。
    //
    // 现在标题一律是「删除」（和页签的右键菜单一个词），**后果放进确认框**——
    // 那是本程序既有的做法：动作被引用时也是照常叫「删除」，由确认框说明会联动清理什么
    //（Confirm_DeleteGroupRefs）。
    //
    // 空分类不弹确认：它什么都不会丢。与「从面板移除」当年那条同一个判据——**确认只在真会丢东西时才拦**。
    // 此前解散只有一条路：双击进改名框、把名字清空、回车——界面上没有任何痕迹说这样可以。
    private ContextMenu CategoryMenu(string cat)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = Strings.Get("Btn_Rename") };
        rename.Click += (_, _) => { _renamingCat = cat; RenderPages(); };
        menu.Items.Add(rename);
        // 与页签的右键菜单同一个词：两种页签长得一样、都能拖、都能双击改名，
        // 右键给出的动作没道理一个叫「删除」、一个叫一句话。后果由确认框说（见下）。
        var dissolve = new MenuItem { Header = Strings.Get("Btn_Delete") };
        dissolve.Click += (_, _) =>
        {
            // 有页才确认，且要说清「页不会没」——不说的话，一个叫「删除」的菜单项落在一叠页上，
            // 谁都会以为那些页跟着没了。空分类什么都不丢，拦一道只是碍事。
            int pages = _config.PanelPages.Count(p => PanelLayout.SameCategory(p.Tab, cat));
            if (pages > 0 && !BrandDialog.Confirm(this, Strings.Get("Confirm_Title"),
                    Strings.Lf("Confirm_DeleteCategory", cat, pages, Strings.Get("Panel_Uncategorized")),
                    ToastLevel.Warn)) return;
            // 还没落地的那一类：没有页可搬，去掉这个标记就是了。
            if (PanelLayout.SameCategory(_pendingCat, cat))
            {
                _pendingCat = null;
                _cat = "";
                RenderPages();
                return;
            }
            // 解散 = 把这一类的页移回「未分类」。走与改名同一条路（改成空名），
            // 于是「合并前先整块搬」那条也照样生效：它们会并到未分类那一段旁边，不和别的类交错。
            if (_config.PanelPages.Any(p => PanelLayout.SameCategory(p.Tab, "")))
                PanelReorder.MoveCategory(_config.PanelPages, cat, "");
            foreach (var p in _config.PanelPages)
                if (PanelLayout.SameCategory(p.Tab, cat))
                    p.Tab = "";
            _cat = "";
            Commit();
        };
        menu.Items.Add(dissolve);
        return menu;
    }

    /// <summary>顶栏的一格：一个分类。点了切过去，双击就地改名，把页签拖上来就归进这一类。</summary>
    //
    // 「未分类」那一格不能改名也不能删：它不是用户建的一类，是「没归类的那些页」的去处。
    private FrameworkElement MakeCategoryTab(string cat)
    {
        bool none = cat.Length == 0;
        string label = none ? Strings.Get("Panel_Uncategorized") : cat;

        if (!none && string.Equals(cat, _renamingCat, StringComparison.Ordinal))
            return CategoryRenameBox(cat);

        var b = new Button
        {
            Style = (Style)FindResource("PanelTopTab"),
            Content = label,
            Tag = cat,
            ToolTip = label,
            AllowDrop = true,
            Margin = new Thickness(0, 0, 2, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) => { if (!_dragging) ShowCategory(cat); };
        // 「未分类」不能改名也不能解散：它不是用户建的一类，是「没归类的那些页」的去处。
        if (!none)
        {
            b.MouseDoubleClick += (_, e) => { e.Handled = true; _renamingCat = cat; RenderPages(); };
            b.ContextMenu = CategoryMenu(cat);
        }

        // 拖分类本身换先后。「未分类」那一格也能拖——它在顶栏上占一格，就该和别的格子一样能挪。
        b.PreviewMouseLeftButtonDown += BeginDragWatch;
        b.PreviewMouseMove += (_, e) =>
        {
            if (_dragging || !PastThreshold(e)) return;
            _dragging = true;
            try { DragDrop.DoDragDrop(b, new DataObject(typeof(CatDrag), new CatDrag(cat)), DragDropEffects.Move); }
            finally { _dragging = false; _dragFrom = null; ClearTabHints(); }
            e.Handled = true;
        };

        // 一格分类收两种东西：另一个分类（换先后），和一条页签（把那一页归进这一类）。
        b.DragOver += (_, e) =>
        {
            bool ok = e.Data.GetDataPresent(typeof(PageDrag)) || e.Data.GetDataPresent(typeof(CatDrag));
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            if (ok) b.Opacity = 0.55;
            e.Handled = true;
        };
        b.DragLeave += (_, _) => b.Opacity = 1;
        b.Drop += (_, e) =>
        {
            e.Handled = true;
            b.Opacity = 1;
            // 分类落在分类上 = 把那一整类挪到这一类之前（整块搬，见 PanelReorder.MoveCategory）。
            if (e.Data.GetData(typeof(CatDrag)) is CatDrag moved)
            {
                if (PanelReorder.MoveCategory(_config.PanelPages, moved.Cat, cat)) { _cat = moved.Cat; Commit(); }
                return;
            }
            if (e.Data.GetData(typeof(PageDrag)) is not PageDrag src) return;
            if (PanelLayout.SameCategory(src.Page.Tab, cat)) return;
            src.Page.Tab = cat;
            PanelReorder.PlaceInCategory(_config.PanelPages, src.Page);   // 并进这一类的段尾，别让两类的页交错
            _open = src.Page;      // 跟着它走：不然那一页从眼前消失，读起来像被删了
            _cat = cat;
            Commit();
        };
        return b;
    }

    // 分类就地改名：改的是这一类里**每一页**的 Tab。
    // 分类不是一个独立实体（没有自己的存储），它是从页上涌现出来的——
    // 所以「改名」就是把用同一个名字的那些页一起改掉，而不是改某一条记录。
    private FrameworkElement CategoryRenameBox(string cat)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = cat,
            FontSize = 12,
            MinWidth = 84,
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, Strings.Get("Panel_Category"));
        // 改名当中右键它，要的还是这个页签的菜单——不挂的话弹出来的是文本框自带的剪切/复制/粘贴。
        // 刚按下「+」建出一类时它正处在这一态，而那一刻最想做的两件事恰恰是改名和删掉。
        box.ContextMenu = CategoryMenu(cat);
        void Done()
        {
            if (_renamingCat == null) return;   // 回车之后失焦会再来一次，只认第一次
            _renamingCat = null;
            var name = box.Text.Trim();
            // 还没落地的那一类没有页可改，改的就是这个标记本身；名字清空 = 这一类不要了。
            if (PanelLayout.SameCategory(_pendingCat, cat))
            {
                _pendingCat = name.Length > 0 ? name : null;
                _cat = _pendingCat ?? "";
                RenderPages();
                return;
            }
            // 改成一个**已存在**的名字 = 把两类合并。两类的页在列表里本是分开的两段，
            // 直接改名会让它们交错，此后拖这一类里的页可能把别的分类整个越过去
            //（用户拖的是页，动的却是分类，见 PanelReorder 类头）。
            // 所以**先整块搬到目标那一类前面，再改名**——搬完两段相邻，改完就是连续的一段。
            // 顺序不能反：先改名的话全都成了同一类，再逐个并段尾只会把列表原地轮转一圈。
            if (!PanelLayout.SameCategory(name, cat)
                && _config.PanelPages.Any(p => PanelLayout.SameCategory(p.Tab, name)))
                PanelReorder.MoveCategory(_config.PanelPages, cat, name);
            // 认「同一类」用的是与顶栏折叠时同一条规则（不区分大小写）。这里曾经写成 Ordinal——
            // 于是分别填了 Dev 和 dev 的两页在顶栏上是一格，改名却只带走其中一半。
            foreach (var p in _config.PanelPages)
                if (PanelLayout.SameCategory(p.Tab, cat))
                    p.Tab = name;
            // 名字清空 = 把这一类解散，页回到「未分类」。当前类跟着落到它们的新去处。
            _cat = name;
            Commit();
        }
        box.LostFocus += (_, _) => Done();
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == System.Windows.Input.Key.Enter) { ke.Handled = true; Done(); }
            else if (ke.Key == System.Windows.Input.Key.Escape) { ke.Handled = true; _renamingCat = null; RenderPages(); }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return box;
    }

    // 顶栏末尾那颗「+」：新建一个分类，并把**当前这一页**放进去——
    // 建一个空分类没有意义（顶栏上会多一格永远打不开的东西），而你按下它时
    // 手边正好有一页。建完直接进改名，同新建文件夹的手感。
    private Button MakeAddCategory()
    {
        var b = new Button
        {
            Style = (Style)FindResource("PanelTopTab"),
            Content = "+",
            ToolTip = Strings.Get("Panel_NewCategory"),
            Margin = new Thickness(4, 0, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, Strings.Get("Panel_NewCategory"));
        b.Click += (_, _) => NewCategory();
        return b;
    }

    // 左栏末尾那颗「+」：在当前这一类里再来一页。样式借页签的（PanelPageTab），
    // 于是它读起来就是「这一列的下一条」，而不是一个飘在旁边的工具按钮。
    private Button MakeAddPage()
    {
        var b = new Button
        {
            Style = (Style)FindResource("PanelPageTab"),
            Content = "+",
            ToolTip = Strings.Get("Panel_AddPage"),
            Margin = new Thickness(0, 0, 0, 2),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, Strings.Get("Panel_AddPage"));
        b.Click += (_, _) => AddPage();
        return b;
    }

    /// <summary>新建一类。**只建这一类，不连带建页。**</summary>
    //
    // 曾经它顺手建一张页，理由是「空分类什么都不是——顶栏上多一格，点进去左栏是空的」。
    // 那条理由随左栏那颗「+」一起失效了：一个空分类现在是「往这儿放第一页」，有入口、有去处。
    // 而按下「＋」的人要的就是多一类，不是多一页——多出来的那张页他还得去删。
    //
    // 分类没有自己的存储（从页的 Tab 上涌现），所以这一类先记在 _pendingCat 上，
    // 让它在顶栏占一格；第一张页进来它才真的落地。**这里不写盘**：确实什么都还没存下来。
    private void NewCategory()
    {
        var use = UnusedCategoryName(Strings.Get("Panel_NewCategory"));
        _pendingCat = use;
        _cat = use;
        _renamingCat = use;   // 先给分类起名——按下「＋」要的就是这一类
        RenderPages();
    }

    /// <summary>把某一页挪进一个新建的分类（页眉那颗分类按钮里的「新建分类…」走这条）。</summary>
    private void NewCategoryFor(PanelPage page)
    {
        var use = UnusedCategoryName(Strings.Get("Panel_NewCategory"));
        page.Tab = use;
        _open = page;
        _cat = use;
        _renamingCat = use;
        Commit();
    }

    // 「新建分类」的名字：重名了就在后面缀个数字。两个入口（顶栏那颗「+」、页眉的分类菜单）
    // 各抄一份的话，改一次重名规则就得改两处——而它们本来就该给出同一个名字。
    private string UnusedCategoryName(string name)
    {
        var use = name;
        // 也要避开还没落地的那一类：两格同名会指向同一堆页，点哪一格都一样，读起来像坏了。
        for (int i = 2; _config.PanelPages.Any(p => PanelLayout.SameCategory(p.Tab, use))
                        || PanelLayout.SameCategory(_pendingCat, use); i++)
            use = name + " " + i;
        return use;
    }

    private void ShowCategory(string cat)
    {
        if (PanelLayout.SameCategory(cat, _cat)) return;
        _cat = cat;
        // 切到这一类的第一页。**先换类再找页**：左栏画的是当前类里的页。
        _open = PanelLayout.PagesIn(_shown, cat).FirstOrDefault()?.Source ?? _open;
        RenderPages();
    }

    // 换页**只重画格子那一半**，两条页签栏原地涂色。
    // 整窗重画会把页签按钮本身销毁重建，而「双击页签改名」的第二下就落在那个新建的按钮上——
    // 对它来说那是第一次点击，ClickCount 永远回不到 2，那条路走不通。面板那边同样是分开的（PaintRails）。
    private void ShowPage(PanelPage p)
    {
        if (ReferenceEquals(_open, p)) return;
        _open = p;
        PaintTabs();
        RenderBody();
    }

    private void RenderBody()
    {
        Pages.Children.Clear();
        if (_shown.FirstOrDefault(p => ReferenceEquals(p.Source, _open)) is { } cur)
            Pages.Children.Add(MakePageCard(cur));
        else
            RenderEmptyCat();
    }

    // 选中态与面板同一套涂法（PaintRails）：左栏是竖杠 + 淡底，上栏是下划线 + 强调色字。
    // 两种都各带一条**形状**线索，不只靠颜色——深色下强调蓝与钢灰的明度差本来就不大，
    // 色觉障碍更分不出。
    private void PaintTabs()
    {
        foreach (var child in PageRail.Children)
            if (child is Button b)
            {
                // `_open != null` 不能省：「+」那颗没有 Tag，而 `_open` 在空分类上、
                // 以及刚删掉当前页之后就是 null——ReferenceEquals(null, null) 为真，
                // 于是那颗「+」会被涂成当前页（强调边 + 淡底 + 亮字）。
                // 顶栅那颗没事只是因为下面那个循环写的是 `b.Tag is string c`，模式匹配把它挡了。
                bool on = _open != null && ReferenceEquals(b.Tag, _open);
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

    // 一条页签。样式取的是**面板上那两个同名样式**（PanelTopTab / PanelPageTab），
    // 所以这儿的页签栏与呼出来的那条是同一个东西，不是照着做的一个像的。
    //
    // 它一个人顶四件事，全是直接操作，没有一个要开对话框：
    //   点   → 切到这一页          双击 → 就地改名
    //   拖它 → 换页序；拖到顶栏某一格上 = 归进那一类
    //   往它上面拖一个格子 → 把那个动作收进这一页
    private Button MakeTab(PanelPageView page)
    {
        var b = new Button
        {
            Style = (Style)FindResource("PanelPageTab"),
            Content = TabRow(page),
            Tag = page.Source,
            ToolTip = page.Title,
            AllowDrop = true,
            Margin = new Thickness(0, 0, 0, 2),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, page.Title);
        // 拖完那一下抬起仍会触发 Click，_dragging 把它吞掉——否则拖一次就顺手换了页。
        b.Click += (_, _) => { if (!_dragging && page.Source != null) ShowPage(page.Source); };
        b.MouseDoubleClick += (_, e) =>
        {
            if (page.Source == null) return;
            e.Handled = true;
            _renaming = page.Source;
            RenderPages();
        };
        if (page.Source != null) b.ContextMenu = PageMenu(page.Source);
        b.PreviewMouseLeftButtonDown += BeginDragWatch;
        b.PreviewMouseMove += PageHead_MouseMove;
        b.DragOver += PageHead_DragOver;
        b.DragLeave += (_, _) => b.Opacity = 1;
        b.Drop += PageHead_Drop;
        return b;
    }

    // 左栏的一条：只有页名（与面板同一个取舍，说明见那边）。
    private object TabRow(PanelPageView page) => new TextBlock
    {
        Text = page.Title,
        MaxWidth = 110,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // 一页 = 一张卡：页眉 + 上带 + 发丝线 + 下带。**列数与分带都取用户当前的设置**，
    // 不写死 4 列——类注释说「管理器里看到什么样，呼出来就是什么样」，那就得连版面节奏一起对上。
    // 少了那条发丝线，这里看到的是一堵七行的方格墙，而面板上是 3+4 两段，两处根本不是一个东西。
    //
    // 空位只补到「当前行填满 + 再留一行」，不铺满整页容量：一页能放 28 个，
    // 而多数页只有五六个动作，把余下二十几个空框全画出来，卡片会变成一片空盒子。
    // 「还能放多少」交给页眉那个读数说，它一个字符顶二十个空框。
    private Border MakePageCard(PanelPageView page)
    {
        // 页本身就在 Source 上。**不能**从 Items[0].Page 反推——空页没有第一个格子，
        // 反推出来是 null，那张卡就此拖不动、也改不了名：新建的页天生是空的，
        // 于是「添加面板页」之后得到的正好是一张动不了的卡。
        var owner = page.Source;
        int cols = PanelMetrics.ClampColumns(_config.Settings.PanelColumns);
        int topRows = PanelMetrics.ClampRows(_config.Settings.PanelTopRows);

        // 与面板同一次分带（PanelLayout.SplitBands），不在这儿另算一遍。
        var (top, bottom) = PanelLayout.SplitBands(page.Items, topRows, cols);

        var grid = MakeBand(top, cols, owner, bottom.Count == 0);
        UniformGrid? lower = bottom.Count > 0 ? MakeBand(bottom, cols, owner, true) : null;

        // 页眉 = 这一页的属性条：名字、装了几个、归哪一类、只在哪个程序上出现。
        // 一页只有这几个字段，所以它们全在这条上——没有「其余设置」，也就不需要一扇设置窗。
        // 它不再兼作拖动抓手——换页序和归类现在都是拖页签，那才是「页」看得见的那个东西。
        var head = new Grid { Margin = new Thickness(10, 8, 6, 2), Background = Brushes.Transparent };
        if (owner != null) head.ContextMenu = PageMenu(owner);
        // 双击页眉改名：就地编辑那套本来就为「新建一页」写好了，改名是同一件事——
        // 再为它开一个对话框，等于同一个动作有两副面孔，而其中一副还更麻烦。
        head.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || owner == null) return;
            e.Handled = true;
            _renaming = owner;
            RenderPages();
        };
        // 名字(撑开) / 读数 / (空) / 归哪一类 / 只在哪个程序上出现
        //
        // 页曾经也有一个图标，摆在这条最左边。它只在「这一页被别的格子指过来时」才起作用，
        // 而页独立成实体之后没有任何东西能指向一页（能被指的是**动作**），那个图标于是无处可用。
        for (int i = 0; i < 5; i++)
            head.ColumnDefinitions.Add(new ColumnDefinition
            { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        FrameworkElement title;
        if (owner != null && ReferenceEquals(owner, _renaming))
        {
            // 就地改名：输入框长得和标题一样（同字体字号字重），只是可以打字——
            // 换一副面孔的话，那一下会读作「跳到了另一个地方」，而它其实还是同一张卡。
            var box = new System.Windows.Controls.TextBox
            {
                Text = page.Title,
                FontFamily = (FontFamily)FindResource("FontDisplay"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 90,
            };
            System.Windows.Automation.AutomationProperties.SetName(box, Strings.Get("Col_Name"));
            void Done()
            {
                if (_renaming == null) return;   // 回车之后失焦会再来一次，只认第一次
                _renaming = null;
                var name = box.Text.Trim();
                // 名字空着就退回默认名，不留一张无名的卡：页签、页眉、翻页提示上全靠这个名字认路。
                owner.Name = name.Length > 0 ? name : Strings.Get("Panel_NewPage");
                Commit();
            }
            box.LostFocus += (_, _) => Done();
            box.KeyDown += (_, ke) =>
            {
                if (ke.Key == System.Windows.Input.Key.Enter) { ke.Handled = true; Done(); }
                // Esc = 不改这一次。名字已经是默认值了，撤销就是「保持默认」。
                else if (ke.Key == System.Windows.Input.Key.Escape) { ke.Handled = true; _renaming = null; RenderPages(); }
            };
            // 建好就把光标放进去、全选：想改直接敲，不想改点别处即可（同新建文件夹的手感）。
            box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
            title = box;
        }
        else
        {
            title = new TextBlock
            {
                Text = page.Title,
                FontFamily = (FontFamily)FindResource("FontDisplay"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }
        Grid.SetColumn(title, 0);
        head.Children.Add(title);
        // 容量读数：这一页放了几个、总共放得下几个。用等宽面——主题里 Cascadia Mono
        // 就是留给「机器读数」的（Theme.xaml），而这正是一个读数，不是一句话。
        // 顺带它不需要翻译：一个斜杠两个数字，十八种语言下长得一样宽。
        var meter = new TextBlock
        {
            Text = $"{page.Items.Count}/{PanelMetrics.PageCapacity(_config.Settings.PanelTopRows, _config.Settings.PanelBottomRows, _config.Settings.PanelColumns)}",
            FontFamily = (FontFamily)FindResource("FontMono"),
            FontSize = 10.5,
            Foreground = (Brush)FindResource("BrushFaint"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        };
        Grid.SetColumn(meter, 1);
        head.Children.Add(meter);

        // 「只在哪个程序上出现」——**这一页自己的一个字段**，默认任何程序，与分类是两回事。
        // 它此前是窗口顶上一整条筛选栏，选中「任何程序」时绑给别的程序的页整批消失；
        // 而绑定这件事一页一个值，天生就属于这一页，不属于一条导航栏。
        //
        // 按钮上写的就是当前值（进程名，或淡着的「任何程序」），点开只有两项——
        // 用选择器解绑得先想到「挑一个空的」，而那个选择器里根本没有「空」这一项。
        if (owner != null)
        {
            var proc = StepHelpers.ToProcessName(owner.ForProcess ?? "");
            var app = new Button
            {
                Content = new TextBlock
                {
                    Text = proc.Length > 0 ? proc : Strings.Get("Panel_Global"),
                    FontSize = 11,
                    MaxWidth = 128,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource(proc.Length > 0 ? "BrushPaper" : "BrushFaint"),
                },
                ContentTemplate = null,
                Padding = new Thickness(7, 2, 7, 2),
                MinWidth = 0,
                Margin = new Thickness(6, 0, 2, 0),
                ToolTip = Strings.Get("Panel_SceneHint"),
            };
            System.Windows.Automation.AutomationProperties.SetName(app, Strings.Get("Ed_GroupPanelForProcess"));
            app.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = app };
                var any = new MenuItem { Header = Strings.Get("Panel_Global"), IsEnabled = proc.Length > 0 };
                any.Click += (_, _) => { owner.ForProcess = ""; Commit(); };
                menu.Items.Add(any);
                var pick = new MenuItem { Header = Strings.Get("Ed_GroupPanelForProcess") + "…" };
                pick.Click += (_, _) =>
                {
                    if (Pickers.PickProcess(this) is not string picked) return;
                    owner.ForProcess = picked.Trim();
                    Commit();
                };
                menu.Items.Add(pick);
                menu.IsOpen = true;
            };
            Grid.SetColumn(app, 4);
            head.Children.Add(app);
        }
        // 归哪一类。与旁边那颗「只在哪个程序上出现」同一副样子：按钮上写的就是当前值，
        // 点开是现有的那几类 + 未分类 + 新建。拖页签到顶栏那一格上是同一件事的另一条路。
        if (owner != null)
        {
            var cat = PanelLayout.CategoryOf(owner);
            var catBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = cat.Length > 0 ? cat : Strings.Get("Panel_Uncategorized"),
                    FontSize = 11,
                    MaxWidth = 128,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource(cat.Length > 0 ? "BrushPaper" : "BrushFaint"),
                },
                ContentTemplate = null,
                Padding = new Thickness(7, 2, 7, 2),
                MinWidth = 0,
                Margin = new Thickness(6, 0, 2, 0),
                ToolTip = Strings.Get("Panel_CategoryHint"),
            };
            System.Windows.Automation.AutomationProperties.SetName(catBtn, Strings.Get("Panel_Category"));
            catBtn.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = catBtn };
                foreach (var c in PanelLayout.Categories(_shown))
                {
                    var item = new MenuItem
                    {
                        Header = c.Length > 0 ? c : Strings.Get("Panel_Uncategorized"),
                        // 同顶栏折叠的口径（不区分大小写）：写成 Ordinal 的话，
                        // 页填的是 dev、菜单里那一项写着 Dev，于是「当前这一类」也点得动。
                        IsEnabled = !PanelLayout.SameCategory(c, cat),
                    };
                    var target = c;
                    item.Click += (_, _) =>
                    {
                        owner.Tab = target;
                        PanelReorder.PlaceInCategory(_config.PanelPages, owner);
                        _cat = target;
                        Commit();
                    };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var mk = new MenuItem { Header = Strings.Get("Panel_NewCategory") + "…" };
                mk.Click += (_, _) => NewCategoryFor(owner);
                menu.Items.Add(mk);
                menu.IsOpen = true;
            };
            Grid.SetColumn(catBtn, 3);
            head.Children.Add(catBtn);
        }

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        body.Children.Add(head);
        body.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BrushLine"),
            Margin = new Thickness(10, 4, 10, 2),
        });
        body.Children.Add(Well(grid, top: 0));
        if (lower != null)
        {
            body.Children.Add(Well(lower, top: 6));
        }

        return new Border
        {
            Background = (Brush)FindResource("BrushSlate"),
            BorderBrush = (Brush)FindResource("BrushLine"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,   // 宽度由列数定，不拉满
            VerticalAlignment = VerticalAlignment.Top,
            Child = body,
        };
    }

    // 把一条带嵌进一个凹坑。两截之间露出卡片自己的底色，那条底就是分界——
    // 与面板同一个做法（说明见 QuickPanelWindow.xaml）。管理器要是还用一条线，预览就和实物两个样。
    private Border Well(UIElement band, double top) => new()
    {
        Background = (Brush)FindResource("BrushInk"),
        BorderBrush = (Brush)FindResource("BrushLine"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Margin = new Thickness(4, top, 4, 0),
        Child = band,
    };

    // 一条带。空位只补在最后一条带上——补在上带末尾会把下带整个顶下去，
    // 而那条带在面板上是紧挨着发丝线的，位置一错，预览就不是预览了。
    private UniformGrid MakeBand(IReadOnlyList<PanelItem> items, int cols, PanelPage? owner, bool padHere)
    {
        var g = new UniformGrid { Columns = cols, Margin = new Thickness(3) };
        foreach (var it in items) g.Children.Add(MakeTile(it));
        if (!padHere) return g;
        int slots = Math.Max(cols * 2, ((items.Count / cols) + 1) * cols) - items.Count;
        for (int i = 0; i < slots; i++) g.Children.Add(MakeSlot(owner));
        return g;
    }

    // ── 格 ──

    private Button MakeTile(PanelItem it)
    {
        var b = NewCell(it.Label, it.Enabled ? 1.0 : 0.4);
        b.Tag = new CellRef(it.Page, it.Step);
        // 悬停这句是全的；有点过就追加点击数，没有就只留原来的句子（不留一个空尾巴）。
        var clicks = ClicksOf(it.Step);
        var tip = StepDisplay.StepSummary(it.Step);
        if (clicks > 0) tip += $"  · {Strings.Lf("Panel_UsageCount", clicks)}";
        b.ToolTip = tip;
        // 方框边长 = 字形字号 × 1.5，与真实面板同一条算法（见 QuickPanelWindow 那段说明）。
        SetCellIcon(b, it.Icon, System.Math.Round(Metrics.GlyphFont * 1.5));
        SetClickBadge(b, clicks);
        // 拖完那一下抬起仍会触发 Click，_dragging 把它吞掉——否则拖一次就顺手打开了编辑器。
        b.Click += (_, _) => { if (!_dragging) EditStep(it.Page, it.Step); };

        var menu = new ContextMenu();
        var edit = new MenuItem { Header = Strings.Get("Btn_Edit") };
        edit.Click += (_, _) => EditStep(it.Page, it.Step);
        menu.Items.Add(edit);
        var del = new MenuItem { Header = Strings.Get("Btn_Delete") };
        del.Click += (_, _) => DeleteStep(it.Page, it.Step);
        menu.Items.Add(del);
        b.ContextMenu = menu;
        return b;
    }

    // 空位：点了就往这一页加东西。压到很淡——它不是一个动作，是一个位置。
    private Button MakeSlot(PanelPage? owner)
    {
        var b = NewCell("", 0.28);
        if (owner != null) b.Tag = new CellRef(owner, null);
        b.ToolTip = Strings.Get("Panel_AddHere");
        SetCellIcon(b, new PanelIconSpec(PanelIconKind.Glyph, char.ConvertFromUtf32(0xE710), ""), System.Math.Round(Metrics.GlyphFont * 1.25));
        b.Click += (_, _) => { if (!_dragging && owner != null) AddStep(owner); };
        return b;
    }

    /// <summary>预览格子的尺寸 —— **与真实面板同一个来源**（PanelMetrics）。
    /// 这里曾经写死 84×88，于是「格子尺寸」和「只显示图标」两项设置在管理器里毫无反应，
    /// 而标准档真实是 88×76 —— 连默认值都对不上。这个窗口的全部价值是「看到什么样，呼出来就是什么样」，
    /// 尺寸对不上就等于那句话只兑现了一半（页与格的组成对，格子本身不对）。</summary>
    private PanelTileMetrics Metrics
        => PanelMetrics.For(_config.Settings.PanelTileSize, _config.Settings.PanelIconOnly);

    private Button NewCell(string label, double opacity)
    {
        var m = Metrics;
        var stack = new StackPanel { Margin = new Thickness(2) };
        stack.Children.Add(new TextBlock());   // 图标占位，由 SetCellIcon 填

        // **只显示图标时整块文字不建**，而不是建好再折叠：
        // 那一档的 LabelFont 是 0，而 WPF 的 FontSize 不接受 0 —— 赋值当场抛
        //「"0" 不是属性 "FontSize" 的有效值」，与可见性无关。真实面板一直是这么写的
        //（if (_metrics.LabelFont > 0) 整块跳过），这里当初没照抄，就成了一个只在这一档触发的崩溃。
        if (m.LabelFont > 0)
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = m.LabelFont,
                MaxHeight = m.LabelFont * 2.8,   // 两行；2.8 的由来见 QuickPanelWindow 那段说明
                // WrapWithOverflow 而不是 Wrap：Wrap 在**单个词比格子还宽**时会退回字符级断行，
                // 于是俄语「Заблокировать」被切成「Заблокиров / ать」——读起来像坏字，不像截断。
                // WrapWithOverflow 只在词边界断，装不下的词溢出后交给 TextTrimming 收成省略号，
                // 与四个列表「截断一律以 … 收尾、悬停看全文」是同一条口径。
                TextWrapping = TextWrapping.WrapWithOverflow,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)FindResource("BrushPaper"),
            });

        // 内容包一层 Grid：StackPanel 居中（与原来一模一样），右下角留一格给点击次数角标。
        // **这层壳是有代价的**——SetCellIcon 原来假设 `Content` 就是那个 StackPanel，
        // 内容一旦换壳它就在 guard 处静默返回，每格的图标全都不画，而且不报错。
        // 所以壳与 guard 必须同一次改（见 SetCellIcon / CellStack）。
        var cell = new Button
        {
            // 与真实面板同一个样式（Theme.xaml 的 Tile）：它自带常态透明 / 悬停提亮 / 焦点环 / 停用压暗，
            // 而且 **Padding 为 0** —— 默认按钮样式的 7,7 会把两行标签的第二行挤掉半截。
            Style = (Style)FindResource("Tile"),
            Content = new Grid { Children = { stack } },
            Width = m.Width,
            Height = m.Height,
            Margin = new Thickness(PanelMetrics.TileGap),
            Opacity = opacity,
            // 管理器多一件事：每一格都是拖放的落点，所以要有可见的边界提示空位在哪。
            BorderBrush = (Brush)FindResource("BrushLine"),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
        };
        // 每一格既是拖动源（空位除外，见 Cell_MouseMove）也是落点。
        cell.PreviewMouseLeftButtonDown += BeginDragWatch;
        cell.PreviewMouseMove += Cell_MouseMove;
        cell.DragOver += Cell_DragOver;
        cell.DragLeave += Cell_DragLeave;
        cell.Drop += Cell_Drop;
        return cell;
    }

    // 格子里那个 StackPanel。内容外面套了一层 Grid（用来放右下角的点击次数角标），
    // 所以不能直接 `cell.Content is StackPanel` 断言——那种写法在换壳后会于 guard 处静默返回，
    // 于是**每一格的图标都不画**，且不抛异常、不进日志，只有人眼看见满屏空格。
    private StackPanel? CellStack(Button cell)
        => cell.Content is Grid g ? g.Children.OfType<StackPanel>().FirstOrDefault() : cell.Content as StackPanel;

    // 与面板同一条取图标的路（画法见 IconVisual，与三个编辑器里的预览块共用一份）。
    private void SetCellIcon(Button cell, PanelIconSpec spec, double size)
    {
        if (CellStack(cell) is not { } sp || sp.Children.Count == 0) return;
        var box = new Grid { Height = size, Margin = new Thickness(0, 2, 0, 4) };
        box.Children.Add(IconVisual.Make(spec, size, (Brush)FindResource("BrushPaper")));
        // 先摘再插，不能直接给下标赋值：WPF 的 UIElementCollection 不允许覆盖一个已占用的位置
        //（「指定的索引已经在使用，请先断开 Visual 子级」），必须先让旧的那个脱离视觉树。
        sp.Children.RemoveAt(0);
        sp.Children.Insert(0, box);
    }

    // 右下角的点击次数角标：只在**面板管理器**里画（面板本身保持干净，统计是回顾用的，不是提示用的）。
    // 没有计数就不建元素，而不是建好再折叠——空位（AddSlot）走同一个画法，不该凭空多出子节点。
    private void SetClickBadge(Button cell, long clicks)
    {
        if (clicks <= 0) return;
        var grid = cell.Content as Grid;
        if (grid == null) return;
        if (grid.Children.OfType<TextBlock>().Any(t => ReferenceEquals(t.Tag, ClickBadgeTag))) return;
        grid.Children.Add(new TextBlock
        {
            Text = clicks > 999 ? "999+" : clicks.ToString(),
            FontFamily = (FontFamily)FindResource("FontMono"),
            // 字号跟格子档位走，但要有下限：紧凑档的 GlyphFont 才 18，等比 0.42 会小到读不出。
            FontSize = System.Math.Max(9, System.Math.Round(Metrics.GlyphFont * 0.42)),
            Foreground = (Brush)FindResource("BrushFaint"),
            // 不能简写成 HorizontalAlignment.Right：本类继承自 Control，那个名字在成员里先解析成
            // **这个窗口的实例属性** HorizontalAlignment，于是报「不能用实例引用访问成员」——
            // 编译器不帮你想到枚举。
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 2),
            // 标记身份，重画这一格时才能识别旧的角标（NewCell 每次都新建，所以实际不会撞上）。
            Tag = ClickBadgeTag,
        });
    }

    private static readonly object ClickBadgeTag = new();

    // ── 拖动排序 ──
    //
    // 两种拖动，同一套骨架：按下记起点 → 超过系统阈值才开拖 → 落点决定插到哪儿。
    //   · 拖格子：页内换位置，也可以直接拖到另一页上去（跨页就是把那一步从一个组挪到另一个组）。
    //   · 拖页眉：换页的先后。页序即页列表的顺序，所以拖页就是在那个列表里挪位置。
    //
    // 阈值这一步不能省：不等阈值就开拖的话，每一次点击都会变成一次零距离拖动，格子从此点不动。
    // 而拖完那一下抬起仍会触发 Click，所以还要一个 _dragging 把它吞掉——否则拖一次就顺手
    // 把编辑器也打开了。

    private void BeginDragWatch(object sender, MouseButtonEventArgs e)
    {
        // 按在**别的**按钮上不算起拖。这是隧道事件，装在页眉上，按页眉里那两颗按钮
        //（归哪一类 / 只在哪个程序上出现）同样会触发它——于是手一抖（超过系统的拖动阈值）
        // 就从「点开那个菜单」变成了「拖动这一页」，而那一下用户以为自己只是点了一下。
        //
        // 判据是「不是 sender 自己」，不能只判「是不是按钮」：**格子本身就是一个 Button**，
        // 它既是拖动源又是按钮。只判类型的话，连拖格子一起拦掉了。
        if (e.OriginalSource is DependencyObject d
            && FindParent<System.Windows.Controls.Primitives.ButtonBase>(d) is { } b
            && !ReferenceEquals(b, sender))
        { _dragFrom = null; return; }
        _dragFrom = e.GetPosition(this);
        _dragging = false;
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    // 每一次按下先把起点抹掉。
    //
    // 这是下面那句「起点是上一次留下的旧值」的根因：`_dragFrom` 只在真的拖起来之后
    // 的 finally 里清，而「按下→直接松开」这条路从来没人清——于是它能活得比那一次按下更久。
    // 后果不是白拖一下：拿着旧起点过阀之后，Cell_MouseMove 会拖走鼠标当下那个格子——
    // 用户压根没碰过它——而 drop 后 Commit() 直接落盘。
    //
    // 放在窗口级的 Preview 上而不是补一个 MouseUp：隐藏事件从根往叶走，所以它先于
    // BeginDragWatch（那三处都是 PreviewMouseLeftButtonDown）跑。于是口径变成「默认没有起点，
    // 只有主动挂了 BeginDragWatch 的元素才给自己设一个」，而不是「总有一个旧值等着被误用」。
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragFrom = null;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private bool PastThreshold(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return false;
        // 没有起点就不是一次拖动。按在压根没挂 BeginDragWatch 的地方（页眉的透明 Grid、
        // 卡片、窗口底）就是这种情形——上面那个 override 保证了那时它真的是 null，
        // 而不是上一次按下留下的旧坐标。
        if (_dragFrom is not Point from) return false;
        var p = e.GetPosition(this);
        return Math.Abs(p.X - from.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(p.Y - from.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void Cell_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || !PastThreshold(e)) return;
        if (sender is not FrameworkElement { Tag: CellRef { Step: { } step } cell }) return;   // 空位不能被拖走
        _dragging = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CellRef), cell), DragDropEffects.Move); }
        finally { _dragging = false; _dragFrom = null; ClearTabHints(); }
        e.Handled = true;
        _ = step;   // 载荷已带着它，这里只是让「空位不可拖」这条判断读起来完整
    }

    private void PageHead_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || !PastThreshold(e)) return;
        if (sender is not FrameworkElement { Tag: PanelPage page }) return;
        _dragging = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(PageDrag), new PageDrag(page)), DragDropEffects.Move); }
        finally { _dragging = false; _dragFrom = null; ClearTabHints(); }
        e.Handled = true;
    }

    // 拖动结束后把页签的落点提示（淡下去那一档）收干净。
    // 靠 DragLeave 单独收不住：**中途按 Esc 取消**时那一下不一定来得及，
    // 而没有落点也就没有 Commit、没有重画，那条页签会一直淡着，读起来像被停用了。
    private void ClearTabHints()
    {
        foreach (var host in new System.Windows.Controls.Panel[] { TopTabBar, PageRail })
            foreach (var child in host.Children)
                if (child is Button b) b.Opacity = 1;
    }

    // 悬停高亮：拖到哪儿就把哪儿点亮成强调色。没有落点提示的拖动等于蒙着眼睛放手。
    private void Cell_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(typeof(CellRef)) && sender is FrameworkElement { Tag: CellRef };
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        if (ok && sender is Button b) b.BorderBrush = (Brush)FindResource("BrushAccent");
        e.Handled = true;
    }

    private void Cell_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button b) b.BorderBrush = (Brush)FindResource("BrushLine");
    }

    private void Cell_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is Button b) b.BorderBrush = (Brush)FindResource("BrushLine");
        if (e.Data.GetData(typeof(CellRef)) is not CellRef src || src.Step is not { } step) return;
        if (sender is not FrameworkElement { Tag: CellRef dst }) return;
        // 落点计算在 Core.PanelReorder（纯函数、有测试）：那里的下标顺序是承重的，
        // 算晚一步就会出现「从前往后拖没反应」。跨页拖不需要单独一条路径——
        // 同一次摘与插，源和目标是不同列表时就是把那一步从一页搬到另一页。
        if (PanelReorder.Move(src.Page.Steps, step, dst.Page.Steps, dst.Step)) Commit();
    }

    // 页签收两种东西：另一条页签（换页的先后），和一个格子（把那个动作挪进这一页）。
    // 后者是并排铺开那套布局唯一真正独有的能力，页签栏把它接了回来——而且更短：
    // 原来得先滚到看见那一页，现在所有页的页签一直都在眼前。
    private void PageHead_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(typeof(PageDrag)) || e.Data.GetDataPresent(typeof(CellRef));
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        // 落点提示：没有提示的拖动等于蒙着眼睛放手。页签是个模板化的按钮，
        // 描边被选中态占着（那条杠说的是「你在这一页」，不能拿去说落点），所以用整条淡下去。
        if (ok && sender is Button b) b.Opacity = 0.55;
        e.Handled = true;
    }

    private void PageHead_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is Button ub) ub.Opacity = 1;
        if (sender is not FrameworkElement { Tag: PanelPage dst }) return;

        // 一个格子落在页签上 = 把这一步搬进那一页，插在末尾。
        // 落回它自己那一页不算动作（那是「拖出去又拖回来」，不是一次移动）。
        if (e.Data.GetData(typeof(CellRef)) is CellRef cell)
        {
            if (cell.Step is not { } step || ReferenceEquals(cell.Page, dst)) return;
            if (PanelReorder.Move(cell.Page.Steps, step, dst.Steps, null)) Commit();
            return;
        }

        if (e.Data.GetData(typeof(PageDrag)) is not PageDrag src) return;
        if (ReferenceEquals(src.Page, dst)) return;

        // 页序就是页列表的顺序，所以拖页 = 在那个列表里挪位置。
        // 同样走 PanelReorder：拖页与拖格子会犯一模一样的下标错位，没理由在这儿再手写一遍。
        // 拖到另一条**页签**上只是换先后（左栏里列的都是同一类的页）。
        // 换类是把它拖到顶栏那一格上，或者用页眉上那颗分类按钮——那是另一件事，另一个落点。
        if (PanelReorder.Move(_config.PanelPages, src.Page, _config.PanelPages, dst)) Commit();
    }

    // ── 编辑：一律转交主界面那两个编辑器 ──

    // 传的是**动作**清单（_config.ActionGroups），不是页——编辑器里那个「运行动作」下拉列的是
    // 可以被这一格指过去的动作。页在那儿没有位置：一格指向一页是没有意义的。
    private void EditStep(PanelPage p, LaunchStep s)
    {
        var edited = StepEditorWindow.Edit(this, s, s.Kind, _config.ActionGroups);
        if (edited == null) return;
        int i = p.Steps.IndexOf(s);
        if (i < 0) return;
        p.Steps[i] = edited;
        Commit();
    }

    private void AddStep(PanelPage p)
    {
        // 以「运行程序」开场：它是最常放上面板的一类，而编辑器顶上就是类型下拉，想换当场换。
        var s = StepEditorWindow.Edit(this, null, "app", _config.ActionGroups);
        if (s == null) return;
        p.Steps.Add(s);
        Commit();
    }

    private void DeleteStep(PanelPage p, LaunchStep s)
    {
        // 与全应用同一条删除契约：必先确认，且点名删的是什么。
        if (!BrandDialog.ConfirmDelete(this, StepDisplay.StepListSummary(s))) return;
        if (!p.Steps.Remove(s)) return;
        Commit();
    }

    // 删掉一整页。**页上的格子跟着一起没了**，所以确认框里点名的是页名加它装了几格——
    // 只报页名的话，一张被折叠起来看不见格子的页删掉时，用户不知道自己刚丢了二十个动作。
    // 指向某个动作的引用格删掉的只是那个引用，动作本身在「动作」页里还在。
    private void DeletePage(PanelPage p)
    {
        // 专用文案，不走通用的 ConfirmDelete：那句话只有一个「名字」的位置，
        // 而这里要说的是两件事——删的是哪一页、上面几个格子会跟着走。
        // 塞成「手边（25）」当名字用过一版，读起来像是这张页就叫「手边（25）」。
        if (!BrandDialog.Confirm(this, Strings.Get("Confirm_Title"),
                Strings.Lf("Confirm_DeletePage", p.Name, p.Steps.Count), ToastLevel.Warn)) return;
        if (!_config.PanelPages.Remove(p)) return;
        // 删的正是当前这一页时把「当前」放掉：不放的话重画会拿一个已经不在册的引用去找页，
        // 找不到会退回第一页——那是对的，但顺手清掉更不容易读错。
        if (ReferenceEquals(_open, p)) _open = null;
        Commit();
    }

    // 「添加面板页」新建的那一页，名字正等着就地改（新建那一下）。
    private PanelPage? _renaming;

    /// <summary>截图 harness 专用：把某一页摆成「正在就地改名」的样子。</summary>
    // 这一态只能由「添加面板页」那一下到达，而 harness 不能真去点按钮（那会改配置）。
    // 与 QuickPanelWindow.StartSearch 同一条理由：没有静态入口的状态，得让它自己进去一次。
    internal void BeginRenameForShots(PanelPage page) { _open = _renaming = page; RenderPages(); }

    // 入口只有一个：左栏末尾那颗「+」。底部那颗「添加面板页」已经删了——
    // 两条栏各带一颗「+」之后它就是第二个入口，而且在窗口另一头。
    private void AddPage()
    {
        // **当场落一张卡，不弹对话框。** 这一步的常态是「先建几个空页，再往里拖动作」，
        // 每建一页都过一遍模态窗太重——何况那扇窗里只有名字此刻有意义。
        // 名字给个默认值再就地选中：想改就直接敲（同新建文件夹的手感），不想改就点别处。
        //
        // **归进你正看着的那一类。** 这里曾经不设 Tab，于是新页落进「未分类」，
        // 又因为它随即被设成当前页，重画时 _cat 跟着变成「未分类」——顶栏在你手底下跳走，
        // 而你刚建的那一页并不在你刚才看的那一类里。旁边的注释一直写着「在这个场景里再来一页」，
        // 只是代码没做。
        //
        // 不预绑程序：新建一页的常态就是「任何程序都能用」，绑定是那一页建好之后的事，
        // 而它现在就在页眉上，一眼看得见也一点就改。
        var page = new PanelPage { Name = Strings.Get("Panel_NewPage"), Tab = _cat };
        _config.PanelPages.Add(page);
        // 并进这一类的段尾，别让两类的页在列表里交错（同拖页归类那条，理由见 PanelReorder.PlaceInCategory）。
        PanelReorder.PlaceInCategory(_config.PanelPages, page);
        // 新建的那一页当场翻到眼前：否则你按下「添加面板页」，看见的还是刚才那一页，
        // 只有左栏底下多出一条页签——那读起来像「没反应」，而这正是上一轮踩过的坑。
        _open = _renaming = page;
        Commit();
    }

    // 每次改动即存盘并就地重画。主窗口那份列表由调用方在本窗关闭后刷新（见 MainWindow.PanelManager_Click）——
    // 这里不去反向调它，管理器不该知道自己是被谁打开的。
    private void Commit()
    {
        _save();
        RenderPages();
    }

    // ── 外观 ──
    // 这四项从设置页搬过来的：它们是纯视觉选择，只有在能看见面板的地方调才有意义（说明见 XAML）。
    // 改一下就存盘 + 重画预览 —— 这个窗口本身就是那个预览，所以「改完是什么样」当场就在眼前。
    private bool _loadingLook;

    private void LoadPanelLook()
    {
        _loadingLook = true;
        try
        {
            for (int n = PanelMetrics.MinColumns; n <= PanelMetrics.MaxColumns; n++)
                PanelColumnsCombo.Items.Add(new ComboBoxItem { Content = n.ToString(), Tag = n });
            PanelColumnsCombo.SelectedIndex = PanelMetrics.ClampColumns(_config.Settings.PanelColumns) - PanelMetrics.MinColumns;

            // 上下两条带的行数。**下带可以为 0，上带不行**：
            //   下带 0 = 退回单条带，是个合理的偏好，不该逼用户在「至少一行」和「关掉」之间二选一；
            //   上带 0 则只在下带也为 0 时才有意义，而 0+0 是一个坏配置 ——
            //   PageCapacity 会退回兜底容量（= 列数），上条带永远空着，面板却按 4 格一页翻，
            //   界面上没有任何东西解释为什么。PanelMetrics 的注释一直断言「下拉给不出 0+0」，
            //   而在这条修好之前，下拉给得出。
            for (int n = 1; n <= PanelMetrics.MaxRows; n++)
                PanelTopRowsCombo.Items.Add(new ComboBoxItem { Content = n.ToString(), Tag = n });
            for (int n = PanelMetrics.MinRows; n <= PanelMetrics.MaxRows; n++)
                PanelBottomRowsCombo.Items.Add(new ComboBoxItem { Content = n.ToString(), Tag = n });
            // 上带的选项从 1 开始，所以索引要减 1；手改 json 写了 0 的按 1 显示（存的值由 ClampRows 兜住）。
            PanelTopRowsCombo.SelectedIndex = Math.Max(1, PanelMetrics.ClampRows(_config.Settings.PanelTopRows)) - 1;
            PanelBottomRowsCombo.SelectedIndex = PanelMetrics.ClampRows(_config.Settings.PanelBottomRows) - PanelMetrics.MinRows;

            // 档位标签走 resx（Size_compact / Size_normal / Size_roomy），值存英文 id——
            // 存本地化文案的话，换一次界面语言配置就读不回来了。
            foreach (var s in PanelMetrics.Sizes)
                PanelTileSizeCombo.Items.Add(new ComboBoxItem { Content = Strings.Get("Size_" + s), Tag = s });
            PanelTileSizeCombo.SelectedIndex = Array.IndexOf(PanelMetrics.Sizes, PanelMetrics.NormalizeSize(_config.Settings.PanelTileSize));

            PanelShowOpsChk.IsChecked = _config.Settings.PanelShowOps;
            PanelLeftTabsChk.IsChecked = _config.Settings.PanelLeftTabs;
            PanelTopTabsChk.IsChecked = _config.Settings.PanelTopTabs;
            PanelIconOnlyChk.IsChecked = _config.Settings.PanelIconOnly;
        }
        finally { _loadingLook = false; }
    }


    private void Look_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingLook) return;
        if ((PanelColumnsCombo.SelectedItem as ComboBoxItem)?.Tag is int cols)
            _config.Settings.PanelColumns = PanelMetrics.ClampColumns(cols);
        if ((PanelTopRowsCombo.SelectedItem as ComboBoxItem)?.Tag is int top)
            _config.Settings.PanelTopRows = PanelMetrics.ClampRows(top);
        if ((PanelBottomRowsCombo.SelectedItem as ComboBoxItem)?.Tag is int bottom)
            _config.Settings.PanelBottomRows = PanelMetrics.ClampRows(bottom);
        if ((PanelTileSizeCombo.SelectedItem as ComboBoxItem)?.Tag is string size)
            _config.Settings.PanelTileSize = PanelMetrics.NormalizeSize(size);
        _config.Settings.PanelShowOps = PanelShowOpsChk.IsChecked == true;
        _config.Settings.PanelLeftTabs = PanelLeftTabsChk.IsChecked == true;
        _config.Settings.PanelTopTabs = PanelTopTabsChk.IsChecked == true;
        _config.Settings.PanelIconOnly = PanelIconOnlyChk.IsChecked == true;
        _save();
        // 预览必须当场重画：格子尺寸/只显示图标改的是 PanelMetrics，而每一格的宽高都由它算。
        RenderPages();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
