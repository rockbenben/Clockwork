using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Clockwork.ViewModels;
using static Clockwork.Views.EditorUi;

namespace Clockwork.Views;

// 动作组编辑器：名称 + 步骤列表（增▾/改/删/上/下），步骤复用 StepEditorWindow。
public partial class GroupEditorWindow : Window
{
    private readonly ActionGroup _original;
    private readonly IReadOnlyList<ActionGroup> _groups;
    // 本程序自己占着的功能键（键 → 它属于谁，查重时点名用）。**是一份清单而不是一个键**：
    // 此前它只装得下急停键，于是快捷面板键加进来的那一轮谁也没想起要更新这里。
    private readonly IReadOnlyList<(string Key, string Owner)> _functionHotkeys;
    private readonly ObservableCollection<StepRowVm> _rows = new();
    private string _groupIcon = "";

    public ActionGroup? Result { get; private set; }

    // 组内步骤可选类型：现含「group」——组可嵌套引用组（部分步骤循环 = 子组引用 ×N）。
    // 环引用由保存时 FindCycle DFS 拦（主防线），运行期重入集兜手改 json。
    private static readonly string[] Kinds = StepDisplay.StepKinds;

    public GroupEditorWindow(ActionGroup group, IReadOnlyList<ActionGroup> groups,
                             IReadOnlyList<(string Key, string Owner)> functionHotkeys)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _original = group;
        _groups = groups;
        _functionHotkeys = functionHotkeys;
        NameBox.Text = group.Name;
        GroupRepeatBox.Text = StepHelpers.ClampRepeat(group.Repeat).ToString();
        GroupRepeatDelayBox.Text = group.RepeatDelayMs.ToString();
        // 新建组 / 模板预填的组 ShowInTray 还是 null → 默认不勾（不进托盘）；已有组读盘时已被 Normalize 补过值。
        ShowInTrayChk.IsChecked = group.ShowInTray ?? false;
        _groupIcon = group.Icon ?? "";
        RefreshGroupIcon();
        _hotkey = group.Hotkey ?? "";
        // 全局热键「点击即录键」，与急停键/发送键统一走 KeyCaptureBox。只改工作副本 _hotkey，
        // 点「确定」才随 Result 落库——取消编辑不影响已有热键。
        KeyCaptureBox.Attach(HotkeyBox, HotkeyCapture.KeyCaptureMode.Hotkey, null,
            () => _hotkey, combo => _hotkey = combo);
        foreach (var s in group.Steps) _rows.Add(new StepRowVm(Clone(s), () => { }));
        Steps.ItemsSource = _rows;
        DataGridReorder.Attach(Steps, (from, to) =>
        {
            if (from < 0 || from >= _rows.Count || to < 0 || to >= _rows.Count || from == to) return;
            var r = _rows[from];
            _rows.RemoveAt(from);
            _rows.Insert(to, r);
            Steps.SelectedIndex = to;
        });
    }

    private string _hotkey = "";
    // 关窗恢复全局热键的兜底已由 KeyCaptureBox 统一负责（挂宿主窗口 Closed），此处不再各写一份。

    private int Sel => Steps.SelectedIndex;

    private void BrowseGroupIcon_Click(object sender, RoutedEventArgs e)
    { if (IconPickerWindow.Pick(this, _groupIcon) is string s) { _groupIcon = s; RefreshGroupIcon(); } }

    private void RefreshGroupIcon()
    {
        IconVisual.Fill(GroupIconBtn, PanelIcon.Resolve(_groupIcon, null), 24,
                        (System.Windows.Media.Brush)FindResource("BrushPaper"));
        GroupIconBtn.ToolTip = _groupIcon.Length > 0 ? _groupIcon : Strings.Get("Icon_None");
    }

    // 选组下拉排除本组：直环在挑选时就选不出来；间接环（A→B→A）由 Ok_Click 的 FindCycle 拦。
    private IReadOnlyList<ActionGroup> StepGroups => _groups.Where(g => g.Id != _original.Id).ToList();

    private void SAdd_Click(object sender, RoutedEventArgs e)
    {
        // 与启动清单同一份意图分节菜单（StepMenu），别在两处各排一版。
        var menu = StepMenu.Build((k, seed) =>
        {
            var step = StepEditorWindow.Edit(this, seed, k, StepGroups);
            if (step == null) return;
            int pos = StepHelpers.InsertPosition(Sel, _rows.Count);
            _rows.Insert(pos, new StepRowVm(step, () => { }));
            Steps.SelectedIndex = pos;
        });
        menu.PlacementTarget = SAdd;
        menu.IsOpen = true;
    }

    private void SEdit_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var step = _rows[i].Step;
        var edited = StepEditorWindow.Edit(this, step, step.Kind, StepGroups);
        if (edited != null) { _rows[i] = new StepRowVm(edited, () => { }); Steps.SelectedIndex = i; }
    }

    private void SDel_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        // 与三个列表页同一条删除契约：必先确认。虽然取消编辑器可整体回退，但用户不该为救一个误删丢掉本次全部编辑。
        if (!BrandDialog.ConfirmDelete(this, StepDisplay.StepListSummary(_rows[i].Step))) return;
        _rows.RemoveAt(i);
        if (_rows.Count > 0) Steps.SelectedIndex = Math.Min(i, _rows.Count - 1);
    }

    // 复制选中步骤：深拷贝插到选中之后（与主窗口三列表的「复制」同一条插入契约）。
    private void SCopy_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        int pos = StepHelpers.InsertPosition(i, _rows.Count);
        _rows.Insert(pos, new StepRowVm(Clone(_rows[i].Step), () => { }));
        Steps.SelectedIndex = pos;
    }

    private void SUp_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i > 0) { _rows.Move(i, i - 1); Steps.SelectedIndex = i - 1; }
    }

    private void SDown_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i >= 0 && i < _rows.Count - 1) { _rows.Move(i, i + 1); Steps.SelectedIndex = i + 1; }
    }

    // —— 试跑：跑的是编辑中（未保存）的内容，所见即所得 ——
    // 时间条件照常生效：周末试跑「仅工作日」的步骤会被跳过，这是真实语义，不为试跑放宽。
    private RunCancel? _tryRun;

    // 「运行这一步」命中 group 类型步骤时，跑的是已保存的目标组（见下）——这本质上也是一次
    // RunGroupAsync，同样要在关窗时收掉，不能让它变成孤儿。故意不复用 _tryRun：那个字段驱动的是
    // 「运行整组」按钮的文字/开关状态（Run ⇄ Stop），这里只是单步操作的副作用，不该让那颗按钮
    // 也跟着变成「停止」——两个闸各管各的运行，OnClosed 里一起收。
    //
    // 用列表而不是单个字段：两个引用步骤指向不同的组时，先后点「运行这一步」是两次都该跑的合法操作
    //（同一个组的重复触发另有 ActionGroupRunner._running 挡着，不必在这里再拦一道）。单字段会被后一次
    // 覆盖，且先跑完的那次回调会无条件清空字段，把仍在跑的后一次的句柄一起抹掉——关窗就收不掉它，
    // 正是本字段要防的孤儿运行。每次运行只摘掉自己那一个闸，互不干扰。
    // 三处读写（点击、onDone、OnClosed）都在 UI 线程上——onDone 由 RunGroupAsync 经 Dispatcher 派发
    //——故不加锁。
    private readonly List<RunCancel> _stepGroupRuns = new();

    private void SRunStep_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var s = _rows[i].Step;
        if (s.Kind == "group")
        {
            // 引用步骤跑的是「已保存」的那份目标组——本编辑器里的未保存改动不属于它。
            var g = ActionGroupResolver.Resolve(_groups, s.GroupId);
            var app = App.Instance;
            if (g == null || app == null) return;
            // 闭包捕获的是局部变量本身：onDone 经 Dispatcher 派发，UI 线程要等本方法返回才轮得到它，
            // 所以回调真正执行时 cancel 早已赋值、也已入列（与 _tryRun 的赋值竞态同一条理由）。
            RunCancel? cancel = null;
            cancel = app.RunGroupAsync(g, this, () => { if (cancel != null) _stepGroupRuns.Remove(cancel); });
            _stepGroupRuns.Add(cancel);
            return;
        }
        App.Instance?.RunStepAsync(s, this);
    }

    private void SRunGroup_Click(object sender, RoutedEventArgs e)
    {
        // 已在试跑 → 本次点击是「停止」。
        if (_tryRun != null) { _tryRun.Request(); return; }
        var app = App.Instance;
        if (app == null) return;
        // 保留真实 Id：运行集（ActionGroupRunner._running）据此挡住「已被热键触发中又来试跑」与自引用，
        // 行为正确且零新代码。名称留空也无妨——试跑不落盘。
        var temp = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = true,
            Repeat = StepHelpers.ClampRepeat(ParseOr(GroupRepeatBox.Text, 1)),
            RepeatDelayMs = ParseOr(GroupRepeatDelayBox.Text, 0, min: 0),
            Steps = _rows.Select(r => r.Step).ToList(),
        };
        SRunGroup.Content = Strings.Get("Btn_StopRun");
        _tryRun = app.RunGroupAsync(temp, this, () =>
        {
            _tryRun = null;
            SRunGroup.Content = Strings.Get("Btn_RunGroup");
        });
    }

    // 关窗即停：这次试跑归本编辑器所有，不能在窗口没了之后还在偷偷跑（用户以为「取消」了一切）。
    // 所有闸都要收——「运行整组」的 _tryRun 与「运行这一步」命中嵌套组时的每一次运行是各自独立的，
    // 用户关窗时没有办法区分是哪一个还在跑，也不该被要求分清楚。
    // 遍历副本：Request 只置位、不会同步回调（onDone 经 Dispatcher 派发，本方法返回后才可能跑），
    // 复制是廉价的保险——免得日后有人把回调改成同步就地摘元素，把这里变成 InvalidOperationException。
    protected override void OnClosed(EventArgs e)
    {
        _tryRun?.Request();
        foreach (var c in _stepGroupRuns.ToList()) c.Request();
        base.OnClosed(e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { BrandDialog.Warn(this, "Clockwork", Strings.Get("Val_GroupName")); return; }
        // 热键查重：与其它「启用」组、或本程序自己的功能键（急停 / 快捷面板）相同就地拦下
        //（等注册失败才报，用户可能早已关掉编辑器；而那句失败提示还会把人指去查别的程序）。
        // 只算启用组——运行时禁用组不注册、主动让出组合（用户禁用 A 正是为了把键腾给 B），此处不能反着拦。
        // 功能键则不分启停：它们一直注册着。
        if (!string.IsNullOrWhiteSpace(_hotkey))
        {
            var owner = HotkeyConflict.OwnerOf(_hotkey, _groups, _original.Id, _functionHotkeys);
            if (owner != null) { BrandDialog.Warn(this, "Clockwork", Strings.Lf("Val_HotkeyDup", _hotkey, owner)); return; }
        }
        var candidate = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = _original.Enabled,
            Hotkey = _hotkey,
            // 手势不在本窗口编辑（归「鼠标手势」管理器），但必须原样带过去——
            // 这里是整份重建 ActionGroup，漏一个字段就等于在保存时把它抹掉。
            Gesture = _original.Gesture,
            // Panel* 一串是历史字段（只有 ConfigStore 那次迁移读它们），仍原样带过去：
            // 这里是整份重建 ActionGroup，漏一个字段就等于在保存时把它抹掉，
            // 而一份还没迁移过就被编辑过的配置，迁移时会读到被抹平的值。
            ShowInPanel = _original.ShowInPanel,
            PanelTab = _original.PanelTab,
            PanelExpand = _original.PanelExpand,
            PanelForProcess = _original.PanelForProcess,
            Repeat = StepHelpers.ClampRepeat(ParseOr(GroupRepeatBox.Text, 1)),
            RepeatDelayMs = ParseOr(GroupRepeatDelayBox.Text, 0, min: 0),
            ShowInTray = ShowInTrayChk.IsChecked == true,
            Icon = _groupIcon.Trim(),
            Steps = _rows.Select(r => r.Step).ToList(),
        };
        // 环引用校验：候选列表 = 其余组 + 本组编辑结果（新建组即追加），从本组出发 DFS。
        // 编辑期是主防线——运行期重入集只会静默空转，用户会以为组坏了。
        var cycle = ActionGroupResolver.FindCycle(
            _groups.Where(g => g.Id != _original.Id).Append(candidate).ToList(), _original.Id);
        if (cycle != null)
        {
            // 环路径上可能有手改配置留下的空名组：逐段替空为占位符，别让消息渲成 "A →  → A" 指不出是谁。
            var path = string.Join(" → ", cycle.Select(n => string.IsNullOrWhiteSpace(n) ? Strings.Get("Ed_Group_None") : n));
            BrandDialog.Warn(this, "Clockwork", Strings.Lf("Val_GroupCycle", path));
            return;
        }
        Result = candidate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // 步骤深拷贝（工作副本，取消即丢弃不动原组）。经 JSON 往返：LaunchStep 以后加字段自动带上，
    // 不再手抄 30 个字段——手抄漏一个，编辑组就悄悄丢那个字段的值。
    private static LaunchStep Clone(LaunchStep s)
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<LaunchStep>(
            System.Text.Json.JsonSerializer.Serialize(s, ConfigStore.JsonOptions), ConfigStore.JsonOptions)!;
        c.Days ??= new(); c.OnYes ??= new();   // 源对象字段为 null（手改配置）时补默认，与 ConfigStore.Read 同口径
        return c;
    }

    public static ActionGroup? Edit(Window? owner, ActionGroup? group, IReadOnlyList<ActionGroup> groups,
                                   IReadOnlyList<(string Key, string Owner)> functionHotkeys)
    {
        var dlg = new GroupEditorWindow(group ?? new ActionGroup { Name = "" }, groups, functionHotkeys) { Owner = owner };
        // owner 为 null 时必须自己找位置、自己保证看得见：这两个入口都能从**面板**进来
        // （面板一格右键 → 编辑；主窗口收在托盘时 App 传的就是 null），而面板此刻已经自己关掉了。
        // 不兜底的话，WindowStartupLocation=CenterOwner 没有 owner 可居中、Topmost 也没置，
        // 于是模态开在任意位置、压在所有窗口后面 —— 用户看到面板消失、什么都没出现，
        // 而一个模态框正拦着后续操作。BrandDialog 与面板管理器都已经这么兜了（同款说明见那两处）。
        if (owner == null) { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; dlg.Topmost = true; }
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
