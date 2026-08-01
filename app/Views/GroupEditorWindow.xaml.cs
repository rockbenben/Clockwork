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
    private readonly string _stopHotkey;   // 当前急停键（查重用：组热键不得与保命键相同）
    private readonly ObservableCollection<StepRowVm> _rows = new();

    public ActionGroup? Result { get; private set; }

    // 组内步骤可选类型：现含「group」——组可嵌套引用组（部分步骤循环 = 子组引用 ×N）。
    // 环引用由保存时 FindCycle DFS 拦（主防线），运行期重入集兜手改 json。
    private static readonly string[] Kinds = StepDisplay.StepKinds;

    public GroupEditorWindow(ActionGroup group, IReadOnlyList<ActionGroup> groups, string stopHotkey)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.DarkTitleBar.Apply(this);
        _original = group;
        _groups = groups;
        _stopHotkey = stopHotkey;
        NameBox.Text = group.Name;
        GroupRepeatBox.Text = StepHelpers.ClampRepeat(group.Repeat).ToString();
        GroupRepeatDelayBox.Text = group.RepeatDelayMs.ToString();
        // 新建组 / 模板预填的组 ShowInTray 还是 null → 默认不勾（不进托盘）；已有组读盘时已被 Normalize 补过值。
        ShowInTrayChk.IsChecked = group.ShowInTray ?? false;
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

    // 选组下拉排除本组：直环在挑选时就选不出来；间接环（A→B→A）由 Ok_Click 的 FindCycle 拦。
    private IReadOnlyList<ActionGroup> StepGroups => _groups.Where(g => g.Id != _original.Id).ToList();

    private void SAdd_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in Kinds)
        {
            var k = kind;
            var mi = new MenuItem { Header = StepDisplay.StepKindLabel(k) };
            mi.Click += (s, _) =>
            {
                var step = StepEditorWindow.Edit(this, null, k, StepGroups);
                if (step == null) return;
                int pos = StepHelpers.InsertPosition(Sel, _rows.Count);
                _rows.Insert(pos, new StepRowVm(step, () => { }));
                Steps.SelectedIndex = pos;
            };
            menu.Items.Add(mi);
        }
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
    private RunCancel? _stepGroupRun;

    private void SRunStep_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var s = _rows[i].Step;
        if (s.Kind == "group")
        {
            // 引用步骤跑的是「已保存」的那份目标组——本编辑器里的未保存改动不属于它。
            var g = ActionGroupResolver.Resolve(_groups, s.GroupId);
            if (g != null) _stepGroupRun = App.Instance?.RunGroupAsync(g, this, () => _stepGroupRun = null);
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
    // 两个闸都要收——「运行整组」的 _tryRun 与「运行这一步」命中嵌套组时的 _stepGroupRun 是各自独立
    // 的运行，用户关窗时没有办法区分是哪一个还在跑，也不该被要求分清楚。
    protected override void OnClosed(EventArgs e)
    {
        _tryRun?.Request();
        _stepGroupRun?.Request();
        base.OnClosed(e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { BrandDialog.Warn(this, "Clockwork", Strings.Get("Val_GroupName")); return; }
        // 热键查重：与其它「启用」组或急停键相同就地拦下（等注册失败才报，用户可能早已关掉编辑器）。
        // 只算启用组——运行时禁用组不注册、主动让出组合（用户禁用 A 正是为了把键腾给 B），此处不能反着拦。
        if (!string.IsNullOrWhiteSpace(_hotkey))
        {
            var other = _groups.FirstOrDefault(g => g.Id != _original.Id && g.Enabled
                && string.Equals(g.Hotkey, _hotkey, StringComparison.OrdinalIgnoreCase));
            string? owner = other != null ? other.Name
                : string.Equals(_stopHotkey, _hotkey, StringComparison.OrdinalIgnoreCase) ? Strings.Get("Settings_StopHotkey") : null;
            if (owner != null) { BrandDialog.Warn(this, "Clockwork", Strings.Lf("Val_HotkeyDup", _hotkey, owner)); return; }
        }
        var candidate = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = _original.Enabled,
            Hotkey = _hotkey,
            Repeat = StepHelpers.ClampRepeat(ParseOr(GroupRepeatBox.Text, 1)),
            RepeatDelayMs = ParseOr(GroupRepeatDelayBox.Text, 0, min: 0),
            ShowInTray = ShowInTrayChk.IsChecked == true,
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

    public static ActionGroup? Edit(Window owner, ActionGroup? group, IReadOnlyList<ActionGroup> groups, string stopHotkey)
    {
        var dlg = new GroupEditorWindow(group ?? new ActionGroup { Name = "" }, groups, stopHotkey) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
