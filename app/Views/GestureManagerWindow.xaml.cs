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
    private bool _loading;
    private bool _loadingSettings;

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
        GesturesOnChk.IsChecked = _config.Settings.GesturesEnabled;
        PaintWatchState();
        PopulateSettings();
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
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushPaperMuted"),
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
        Bound.Items.Clear();
        foreach (var r in Steps.Select(x => new Row(x)))
            Bound.Items.Add(new ListBoxItem { Content = BuildRow(r), Tag = r, ToolTip = r.Detail });
        // 没有指定就选第一条：不选的话右边画布是禁用的，水印却写着「按住左键在此画」——
        // 一句你照做不了的指令。永远选中一条，那个死状态就不存在了。
        Bound.SelectedItem = select == null
            ? Bound.Items.Cast<ListBoxItem>().FirstOrDefault()
            : Bound.Items.Cast<ListBoxItem>().FirstOrDefault(it => it.Tag is Row r && ReferenceEquals(r.Step, select))
              ?? Bound.Items.Cast<ListBoxItem>().FirstOrDefault();
        EmptyNote.Visibility = Steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
        SyncButtons();
        PaintGesture();
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
        var menu = StepMenu.Build((kind, seed) =>
        {
            var made = StepEditorWindow.Edit(this, seed, kind, _config.ActionGroups);
            if (made == null) return;
            Steps.Add(made);
            Commit(made);
        });
        menu.PlacementTarget = AddBtn;
        menu.IsOpen = true;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } step) return;
        var edited = StepEditorWindow.Edit(this, step, step.Kind, _config.ActionGroups);
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
        GestureTrail.Points.Clear();
        GestureTrail.Points.Add(new Point(pt.X, pt.Y));
    }

    private void GestureCanvas_Move(object sender, MouseEventArgs e)
    {
        if (!_draw.Pending) return;
        var pt = e.GetPosition(GestureCanvas);
        _draw.OnMove((int)pt.X, (int)pt.Y);
        GestureTrail.Points.Add(new Point(pt.X, pt.Y));
    }

    private void GestureCanvas_Up(object sender, MouseButtonEventArgs e)
    {
        GestureCanvas.ReleaseMouseCapture();
        var verdict = _draw.OnRightUp();
        GestureTrail.Points.Clear();
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
        if (!GestureCanvas.IsMouseCaptured && _draw.Pending) { _draw.Reset(); GestureTrail.Points.Clear(); }
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

    private void PopulateSettings()
    {
        _loadingSettings = true;
        SensitivityCbo.Items.Clear();
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_High"), Tag = "high" });
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_Normal"), Tag = "normal" });
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_Low"), Tag = "low" });
        var currentSens = _config.Settings.GestureSensitivity;
        SensitivityCbo.SelectedItem = SensitivityCbo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == currentSens)
            ?? SensitivityCbo.Items[1];

        TrailWidthCbo.Items.Clear();
        TrailWidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Off"), Tag = "off" });
        TrailWidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thin"), Tag = "thin" });
        TrailWidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Normal"), Tag = "normal" });
        TrailWidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thick"), Tag = "thick" });
        var currentTrail = _config.Settings.GestureTrailWidth;
        TrailWidthCbo.SelectedItem = TrailWidthCbo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == currentTrail)
            ?? TrailWidthCbo.Items[2];
        _loadingSettings = false;
    }

    private void Sensitivity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (SensitivityCbo.SelectedItem is ComboBoxItem item && item.Tag is string sens)
        {
            _config.Settings.GestureSensitivity = sens;
            _save();
        }
    }

    private void TrailWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (TrailWidthCbo.SelectedItem is ComboBoxItem item && item.Tag is string trail)
        {
            _config.Settings.GestureTrailWidth = trail;
            _save();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
