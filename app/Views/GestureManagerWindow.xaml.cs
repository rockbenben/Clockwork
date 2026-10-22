using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有。
using Point = System.Windows.Point;
using Orientation = System.Windows.Controls.Orientation;
using Grid = System.Windows.Controls.Grid;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Clockwork.Views;

// 手势管理器：手势自己的一块地方，与面板管理器（PanelManagerWindow）平级——
// 都是「同一批动作的另一种视图」，从动作组页的侧栏进来。
//
// **一条手势 = 一个步骤 + 一条轨迹**，而不是「一个动作组 + 一条轨迹」。
// 这一条是被用户点破的：「粘贴」「最小化」「返回」这类动作只作为手势存在，根本没有对应的组，
// 逼人先建一个只装一步的组纯属仪式。而「跑一整个组」用 group 类型的步骤指过去就行——
// 那本来就是那个步骤类型的用途，与面板同一条规矩（见 ActionGroup.PanelExpand 上那段说明）。
//
// 于是这里不必自造任何编辑器：动作走主界面同一个「新增 ▾」菜单和同一个 StepEditorWindow，
// 十种类型、条件、重复、试跑全都现成——用户也不该为同一件事学两套。
//
// 改动即时写进模型并存盘（同面板管理器）：这个窗口没有「确定 / 取消」，
// 因为它编的是绑定关系而不是一份表单——画完那一刻语义就已经完整了。
public partial class GestureManagerWindow : Window
{
    private readonly RootConfig _config;
    private readonly System.Action _save;
    // 录入用的量化器：与运行时同一个类、同一套默认参数，画出什么存什么。
    // 画布上的录制。**这里的最小笔画长度必须写死，不能跟主屏宽度走**（那是钩子那边的事）：
    // 画布只有 196×150 DIP，一条腿撑死一百多，而 4K 屏上按 2.5% 算出来是 96——
    // 那样在这块板子上根本画不出第二个方向。这里的坐标也是 DIP 不是物理像素，与缩放无关。
    private readonly GestureGate _draw = new(_ => true, 40);
    // 画布上那一笔的渲染器：**与屏幕上那条线是同一个**（GestureTrailRenderer）。
    // 早先画布画的是一根普通的强调色折线，于是「笔迹外观」里挑的样式在这一步完全看不见——
    // 所见即所得断在了「画手势」这一格，而这一格恰恰是用户唯一会去挑样式的地方。
    //
    // **它不点亮**：画布上这一笔是**正在被定义**的那一条，它此刻还没绑任何动作，
    // 点亮等于说「这一划已经认出来了」——那是没有的。而屏幕上那条线点亮，
    // 是因为它已经命中了某条已绑的手势（见 GestureTrailWindow 的 SetArmed）。
    private readonly GestureTrailRenderer _trail;
    // 全部手势（未按搜索词过滤）。列表是按它过滤出来的，搜索词一变就重排一次。
    private List<Row> _rows = new();
    private bool _loading;

    public GestureManagerWindow(RootConfig config, System.Action save)
    {
        _config = config;
        _save = save;
        InitializeComponent();
        // 标题栏也要跟着变深。本窗是全仓库唯一漏了这一句的带边框窗口——于是它开出来是
        // 浅色标题栏顶着一身深色内容，而它和面板管理器是平级的两扇窗，并排看格外明显。
        // 顺序照兄弟窗口（先 Apply 再 FitToWorkArea）。
        Native.DarkWindow.Apply(this);
        // 本窗是 SizeToContent="Height"，而那张列表的行数由**用户配了几条手势**决定，没有上限。
        // 不封顶时窗口会一路长出工作区，底下那一行（关闭按钮）落到任务栏底下，点都点不着——
        // 与主窗口、动作组编辑器同一条处理。封顶之后 ListBox 自带的 ScrollViewer 接手滚动。
        //
        // 这一条截图测不出来：--shots harness 会无条件给每扇窗设 MaxHeight，
        // 于是「窗口自己忘了封顶」在图里永远是正常的（DevChecks 顶部那段注释记着这个盲区）。
        WindowSizing.FitToWorkArea(this);
        _trail = new GestureTrailRenderer(
            GestureTrailStyles.Resolve(_config.Settings.GestureTrailStyle),
            GestureTrailWidths.Resolve(_config.Settings.GestureTrailWidth),
            (System.Windows.Media.Brush)FindResource("BrushAccentText"),
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0xEF, 0xFF)),
            _config.Settings.GestureTrailFade);
        GestureTrailHost.Children.Add(_trail.Surface);
        GesturesOnChk.IsChecked = _config.Settings.GesturesEnabled;
        // 按钮上的字在这儿设（不写在 XAML 里）：那个「…」是「会打开另一扇窗」的记号，
        // 而它得排在本地化词**之后**——RTL 下省略号该在前，可那句本程序还不支持。
        SettingsBtn.Content = Strings.Get("Gesture_Settings") + "…";
        PaintWatchState();
        Reload(null);
    }

    // 总开关。存盘那一步会按新配置装卸右键监听（App.SaveConfig 末尾调 ApplyMouseHook），
    // 所以勾掉的当下右键就恢复原样了，不用重启。
    private void Rehook_Click(object sender, RoutedEventArgs e)
    {
        App.Instance?.RehookMouse();
        PaintWatchState();
    }

    private void GesturesOn_Changed(object sender, RoutedEventArgs e)
    {
        _config.Settings.GesturesEnabled = GesturesOnChk.IsChecked == true;
        _save();          // 末尾会按新设置装 / 卸钩子
        PaintWatchState();
    }

    // 右键此刻有没有真的被监听。勾着却没装上，是这个功能唯一真正难查的状态——
    // 而在此之前界面上一个字都没有，用户只能看到「按了没反应」。
    private void PaintWatchState()
    {
        var st = App.Instance?.GestureWatch ?? GestureWatchState.Off;
        WatchState.Text = Strings.Get(st switch
        {
            GestureWatchState.Live => "Gesture_WatchLive",
            GestureWatchState.Failed => "Gesture_WatchFailed",
            GestureWatchState.Stale => "Gesture_WatchStale",
            GestureWatchState.Deaf => "Gesture_WatchDeaf",
            GestureWatchState.Blocked => "Gesture_WatchBlocked",
            _ => "Gesture_WatchOff",
        });
        bool trouble = st is GestureWatchState.Failed or GestureWatchState.Stale
                          or GestureWatchState.Deaf or GestureWatchState.Blocked;
        WatchState.Foreground = (System.Windows.Media.Brush)FindResource(
            trouble ? "BrushDanger" : st == GestureWatchState.Live ? "BrushMuted" : "BrushFaint");
        // 「重新挂钩」只在出事时露面：它是**补救**，不是日常操作。
        // 常驻的话，它会在标题行里和总开关抢注意力，而 99% 的时候没人需要它；
        // 跟着问题一起出现，它就自带了「这是拿来治那句红字的」这层意思，不用再解释。
        RehookBtn.Visibility = trouble ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<LaunchStep> Steps => _config.Gestures;

    private LaunchStep? Current => ((Bound.SelectedItem as ListBoxItem)?.Tag as Row)?.Step;

    // 列表行的显示壳。轨迹用大号箭头（这一列是拿来扫形状的），摘要复用 StepDisplay——
    // 主界面列表、面板格子、气泡回执用的都是它，措辞天然一致。
    private sealed record Row(LaunchStep Step)
    {
        /// <summary>行上显示的那句：**用户起的名字优先**，没起名才用动作本身的描述。</summary>
        //
        // 用 StepTitle 而不是 StepSummary：后者只为 app / group 两种类型照顾 Label
        //（StepDisplay 里那段注释自己写着这件事），于是一条起名叫「后退」的手势，
        // 在这张表上显示成「发送 Alt+Left」——机制盖住了意图，而画手势的人心里想的是「后退」。
        // 共享的摘要逻辑不动（气泡回执、日志、主界面列表都用它，那些地方要的正是机制）；
        // 只有这张表换口径，因为它是「我一共设了哪些手势」的总览，那里要的是意图。
        public string Title => StepDisplay.StepTitle(Step);

        /// <summary>完整摘要（带 ×N、星期、条件后缀）。挂悬停提示，不占行宽。</summary>
        public string Detail => StepDisplay.StepListSummary(Step);

        // 读屏软件念的是箭头 + 完整摘要：笔迹缩略图是给眼睛的，
        // 念出来仍要是一句完整的话——一条画出来的线没法朗读。
        public override string ToString() => GestureGate.Arrows(Step.Gesture) + " " + Detail;
    }

    // 笔迹用强调色，与画布上那条正在画的线（GestureTrail）同色：手一松线就变灰的话，
    // 会读成「刚才画的没存上」。这也是强调色少数几处名副其实的用法——一行里要扫的就是这个形状，
    // 旁边的摘要是解释，形状才是这条手势的身份。
    // 一行 = 笔迹缩略图 + 摘要。定宽的图列给摘要一条共同的左缘。
    private System.Windows.FrameworkElement BuildRow(Row r)
    {
        var box = new Grid { Width = 52, Height = 30 };
        var glyph = GestureGlyph.Make(r.Step.Gesture, 52, 30, (System.Windows.Media.Brush)FindResource("BrushAccentText"), 1.6);
        if (glyph != null) box.Children.Add(glyph);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        line.Children.Add(new TextBlock
        {
            Text = r.Title,
            Margin = new System.Windows.Thickness(12, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
        });
        var proc = StepHelpers.ToProcessName(r.Step.ForProcess ?? "");
        if (proc.Length > 0)
        {
            var badge = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("BrushInk"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BrushLine"),
                BorderThickness = new System.Windows.Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(3),
                Padding = new System.Windows.Thickness(5, 1, 5, 1),
                Margin = new System.Windows.Thickness(8, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = proc,
                    FontSize = 10.5,
                    // BrushPaperMuted **这个键从来不存在**（全仓库只有这一处提到它），
                    // 而 FindResource 找不到键是**抛异常**，不是返回 null——
                    // 于是「绑了分应用手势」的那一行一建就炸。
                    // 截图矩阵看不到它：默认的几条手势没有分应用绑定，这条分支压根没走到过。
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushMuted"),
                }
            };
            line.Children.Add(badge);
        }
        return line;
    }

    private void Reload(LaunchStep? select)
    {
        _loading = true;
        // 直接塞 ListBoxItem 而不是走 ItemTemplate：笔迹是画出来的（Path 几何），
        // 每行的形状都不一样，模板里没法用绑定表达——这本来就是代码画的东西。
        Bound.ItemsSource = null;
        _rows = Steps.Select(x => new Row(x)).ToList();
        ApplyFilter(select);
        _loading = false;
        SyncButtons();
        PaintGesture();
    }

    // 搜索：按动作名、箭头串与摘要过滤，命中的行才进列表。
    // **「一条都没剩」时那句提示要分两种**：「这里还没有手势」与
    // 「这里有手势、只是都不叫这个词」是两件事，补救也完全不同。
    private void ApplyFilter(LaunchStep? select = null)
    {
        var rows = _rows.Where(Matches).ToList();
        Bound.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListBoxItem { Content = BuildRow(r), Tag = r, ToolTip = r.Detail };
            // 无障碍名用 Row.ToString()（箭头 + 摘要）——它本来就是为朗读写的
            // （见 Row 那条注释：「笔迹缩略图是给眼睛的……一条画出来的线没法朗读」）。
            // 不给的话 WPF 只能从 Content 取名字，而那是一块**画出来的面板**（笔迹 + 文字），
            // 取不到字符串——这一列每一行都只报「System.Windows.Controls.ListBoxItem」。
            System.Windows.Automation.AutomationProperties.SetName(item, r.ToString());
            Bound.Items.Add(item);
        }
        // 没有指定就选第一条：不选的话右边画布是禁用的，水印却写着「按住左键在此画」——
        // 一句你照做不了的指令。永远选中一条，那个死状态就不存在了。
        Bound.SelectedItem = select == null
            ? Bound.Items.Cast<ListBoxItem>().FirstOrDefault()
            : Bound.Items.Cast<ListBoxItem>().FirstOrDefault(it => it.Tag is Row r && ReferenceEquals(r.Step, select))
              ?? Bound.Items.Cast<ListBoxItem>().FirstOrDefault();
        EmptyNote.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyNote.Text = _rows.Count == 0 ? Strings.Get("Gesture_None") : Strings.Get("Gesture_SearchNone");
    }

    // 名字、形状与摘要都算：用户记得的可能是「哪个动作」，也可能是「哪条形状」。
    // 形状那一项用箭头串（↑↓）——界面上画的与列表里显示的都是它。
    private bool Matches(Row r)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        return r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || GestureGate.Arrows(r.Step.Gesture).Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || r.Detail.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 水印：框里有字就藏起来（同画布与工具箱的做法）。
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_loading) return;
        // 先把当前选中项记下来：ApplyFilter 会清空列表，之后就取不到了。
        var cur = Current;
        ApplyFilter(cur);
    }

    private void SyncButtons()
    {
        bool has = Current != null;
        EditBtn.IsEnabled = has;
        DelBtn.IsEnabled = has;
        AppBtn.IsEnabled = has;
        GestureCanvas.IsEnabled = has;
        GestureCanvas.Opacity = has ? 1.0 : 0.45;

        if (Current is { } step)
        {
            var proc = StepHelpers.ToProcessName(step.ForProcess ?? "");
            AppBtn.Content = proc.Length > 0 ? proc : Strings.Get("Panel_Global");
            AppBtn.ToolTip = Strings.Get("Gesture_AppHint");
        }
        else
        {
            AppBtn.Content = Strings.Get("Gesture_App");
            AppBtn.ToolTip = null;
        }
    }

    private void Bound_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SyncButtons();
        PaintGesture();
    }

    private void Bound_DoubleClick(object sender, MouseButtonEventArgs e) => Edit_Click(sender, e);

    // 新增：主界面同一个「新增 ▾」菜单挑类型，再走同一个步骤编辑器。
    // 新条目先落进列表但还没有轨迹——右边画完才真正生效（没轨迹的条目不会被钩子看见）。
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // 手势是最常用来跑一整串动作的地方，所以它也得能**当场编**那个动作——
        // 与面板管理器、浮动面板同一组入口（见 StepMenu.Build 的 headerItems 说明）。
        // 没有它时想跑一个复杂动作只能先去动作标签页建好、再回来挑：为一个手势要多开两个窗。
        var compound = new MenuItem { Header = Strings.Get("Panel_NewCompound") };
        compound.Click += (_, _) =>
        {
            var g = GroupEditorWindow.Edit(this, new ActionGroup { Name = Strings.Get("Panel_NewCompound") },
                                           _config.ActionGroups, _config.Settings.FunctionHotkeys());
            if (g == null) return;
            _config.ActionGroups.Add(g);
            Steps.Add(new LaunchStep { Kind = "group", GroupId = g.Id });
            Commit(Steps[^1]);
        };

        var menu = StepMenu.Build((kind, seed) =>
        {
            var made = StepEditorWindow.Create(this, kind, seed, _config.ActionGroups);
            if (made == null) return;
            Steps.Add(made);
            Commit(made);
        }, headerItems: new[] { compound });
        StepMenu.OpenAt(AddBtn, menu);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } step) return;
        var edited = StepEditorWindow.Edit(this, step, _config.ActionGroups);
        if (edited == null) return;
        // 编辑器返回的是**重建**出来的新对象，就地换掉列表里那一个（轨迹已由它自己带回来）。
        int i = Steps.IndexOf(step);
        if (i < 0) return;
        Steps[i] = edited;
        Commit(edited);
    }

    private void Del_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } step) return;
        if (!BrandDialog.ConfirmDelete(this, StepDisplay.StepSummary(step))) return;
        Steps.Remove(step);
        Commit(null);
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } step) return;
        var proc = StepHelpers.ToProcessName(step.ForProcess ?? "");
        var menu = new ContextMenu { PlacementTarget = AppBtn };
        var any = new MenuItem { Header = Strings.Get("Panel_Global"), IsEnabled = proc.Length > 0 };
        any.Click += (_, _) =>
        {
            if (CheckDuplicate(step, step.Gesture, "")) return;
            step.ForProcess = "";
            Commit(step);
        };
        menu.Items.Add(any);
        var pick = new MenuItem { Header = Strings.Get("Gesture_App") + "…" };
        pick.Click += (_, _) =>
        {
            if (Pickers.PickProcess(this) is not string picked) return;
            var newProc = picked.Trim();
            if (CheckDuplicate(step, step.Gesture, newProc)) return;
            step.ForProcess = newProc;
            Commit(step);
        };
        menu.Items.Add(pick);
        menu.IsOpen = true;
    }

    private bool CheckDuplicate(LaunchStep step, string path, string forProcess)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var norm = GestureGate.Normalize(path);
        if (string.IsNullOrEmpty(norm)) return false;
        var stepProc = StepHelpers.ToProcessName(forProcess);
        var owner = Steps.FirstOrDefault(x => !ReferenceEquals(x, step)
            && string.Equals(GestureGate.Normalize(x.Gesture), norm, System.StringComparison.Ordinal)
            && string.Equals(StepHelpers.ToProcessName(x.ForProcess ?? ""), stepProc, System.StringComparison.OrdinalIgnoreCase));
        if (owner != null)
        {
            BrandDialog.Warn(this, "Clockwork",
                Strings.Lf("Val_GestureDup", GestureGate.Arrows(norm), StepDisplay.StepSummary(owner)));
            return true;
        }
        return false;
    }

    // —— 画布 ——（左键按住画，松开即定串）
    private void GestureCanvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (Current == null) return;
        GestureCanvas.CaptureMouse();
        var pt = e.GetPosition(GestureCanvas);
        _draw.OnRightDown((int)pt.X, (int)pt.Y);
        _trail.Clear();
        _trail.Add(new Point(pt.X, pt.Y));
    }

    private void GestureCanvas_Move(object sender, MouseEventArgs e)
    {
        if (!_draw.Pending) return;
        var pt = e.GetPosition(GestureCanvas);
        _draw.OnMove((int)pt.X, (int)pt.Y);
        _trail.Add(new Point(pt.X, pt.Y));
    }

    private void GestureCanvas_Up(object sender, MouseButtonEventArgs e)
    {
        GestureCanvas.ReleaseMouseCapture();
        var verdict = _draw.OnRightUp();
        _trail.Clear();
        if (verdict != PressVerdict.Fire || Current is not { } step) { PaintGesture(); return; }

        if (CheckDuplicate(step, _draw.Path, step.ForProcess))
        {
            PaintGesture();
            return;
        }
        step.Gesture = _draw.Path;
        Commit(step);
    }

    // 画一半拖出画布：CaptureMouse 让事件继续来，这里只处理没捕获时的离开（如按下前掠过）。
    private void GestureCanvas_Leave(object sender, MouseEventArgs e)
    {
        if (!GestureCanvas.IsMouseCaptured && _draw.Pending) { _draw.Reset(); _trail.Clear(); }
    }

    // 落盘 + 重画。存盘那一步会顺带按新配置装卸右键监听（App.SaveConfig 末尾调 ApplyMouseHook），
    // 所以「配了第一条手势」与「删掉最后一条」当场生效，不用重启。
    private void Commit(LaunchStep? select)
    {
        _save();
        Reload(select);
        // 存完要重画一次「右键此刻有没有真的被监听」。
        //
        // _save() 末尾就是按新设置装 / 卸钩子的地方，而增删一条手势、或把最后一条停用，
        // 恰好就是让 GestureGate.ShouldWatch 翻面的那种改动。不刷的话，这扇窗唯一存在理由的
        // 那个指示器恰恰在它变化的那一刻停在旧值上。
        PaintWatchState();
    }

    private void PaintGesture()
    {
        var path = Current?.Gesture ?? "";
        // 画布里的笔迹与列表缩略图出自同一个 GestureGlyph，只是尺寸不同——
        // 「列表里看到什么，手上就画什么」靠的是同一份几何，不是两处对齐的代码。
        GestureShape.Content = GestureGlyph.Make(path, 150, 118, (System.Windows.Media.Brush)FindResource("BrushAccentText"), 2.4);
        GestureWatermark.Visibility = GestureShape.Content == null ? Visibility.Visible : Visibility.Collapsed;
        // 「置顶」是开关，而标签「置顶当前窗口」读起来像单向动作——看列表时正是会冒出
        // 「那取消置顶呢」的时候，所以那句说明得出现在这儿，不能只留在步骤编辑器里。
        TopmostHint.Visibility = Current is { Kind: "window", Action: "topmost" }
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // 全局设置（灵敏度、笔迹的样子、尾迹淡出、动作名、名单）在「手势设置…」那一扇窗里。
    //
    // 为什么搬出去：这一扇管理器**没有余量**（内容 390、兜底上限 392），
    // 而那五项本就不属于「有哪些手势」这件事。顺带把职责分清：
    // 这一扇回答「有哪些手势」，那一扇回答「它们整体怎么表现」。
    // 模态打开：改完当场生效，窗里没有「确定 / 取消」——挑完那一刻意思就已经完整了。
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new GestureSettingsWindow(_config, _save);
        w.Owner = this;
        w.ShowDialog();
        // 设置窗改的是笔迹的样子，而画布上那一笔用的是同一个渲染器——
        // 不跟着换，画布上就还是旧样式，「所见即所得」又断一次。
        RefreshTrailLook();
    }

    // 画布上那一笔的样子，重新按配置刷一遍。
    private void RefreshTrailLook()
    {
        _trail.SetStyle(GestureTrailStyles.Resolve(_config.Settings.GestureTrailStyle),
                        GestureTrailWidths.Resolve(_config.Settings.GestureTrailWidth),
                        _config.Settings.GestureTrailFade);
    }

    // 笔迹旁那句「这将要做的事」。改了当场生效（_save 末尾会重装钩子，
    // 笔迹旁那句「这将要做的事」与「不接管手势的程序」两份名单都搬进了「手势设置…」那一扇窗
    // （见 Settings_Click 那里为什么搬）。这一扇现在只回答「有哪些手势」。

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
