using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Clockwork.ViewModels;

namespace Clockwork.Views;

// 动作组编辑器：名称 + 步骤列表（增▾/改/删/上/下），步骤复用 StepEditorWindow。
public partial class GroupEditorWindow : Window
{
    private readonly ActionGroup _original;
    private readonly IReadOnlyList<ActionGroup> _groups;
    private readonly string _stopHotkey;   // 当前急停键（查重用：组热键不得与保命键相同）
    private readonly ObservableCollection<StepRowVm> _rows = new();

    public ActionGroup? Result { get; private set; }

    // 组内步骤可选类型：复用 StepDisplay.StepKinds 的规范顺序，去掉「group」（组不能再嵌套组）。
    // 标签一律经 StepKindLabel 本地化——不再在此内联硬编码中文（那份还是死数据、从不显示）。
    private static readonly string[] Kinds =
        StepDisplay.StepKinds.Where(k => k != "group").ToArray();

    public GroupEditorWindow(ActionGroup group, IReadOnlyList<ActionGroup> groups, string stopHotkey)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.DarkTitleBar.Apply(this);
        _original = group;
        _groups = groups;
        _stopHotkey = stopHotkey;
        NameBox.Text = group.Name;
        _hotkey = group.Hotkey ?? "";
        HotkeyBox.Text = _hotkey;
        foreach (var s in group.Steps) _rows.Add(new StepRowVm(Clone(s), () => { }));
        Steps.ItemsSource = _rows;
    }

    // —— 全局热键：按键捕捉 ——（与设置页急停键同一交互：点击后按下组合即录入，Esc 取消、Delete 清除）
    // 只改工作副本 _hotkey，点「确定」才随 Result 落库——取消编辑不影响已有热键。
    private string _hotkey = "";

    private void Hotkey_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = Strings.Get("Hotkey_PressPrompt");
        App.Instance?.SuspendHotkeys();   // 捕捉期间注销全部全局热键，避免按到已注册组合触发急停/跑组
    }

    private void Hotkey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (HotkeyBox.Text == Strings.Get("Hotkey_PressPrompt")) HotkeyBox.Text = _hotkey;
        App.Instance?.ResumeHotkeys();    // 恢复按当前配置注册（编辑未保存，故仍是旧键）
    }

    // 兜底：捕捉框仍持焦点时窗口被关（如裸 Enter 直接触发默认「确定」）不保证会走 LostFocus——
    // 关窗必恢复，否则急停/组热键保持挂起、保命键静默失效。ResumeHotkeys 幂等，双触发无害。
    protected override void OnClosed(EventArgs e)
    {
        App.Instance?.ResumeHotkeys();
        base.OnClosed(e);
    }

    private void Hotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;   // PassThrough 分支除外——决策统一在 HotkeyCapture.ProcessCaptureKey（与设置页急停键同一状态机）
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (HotkeyCapture.ProcessCaptureKey(key, Keyboard.Modifiers, out var combo))
        {
            case HotkeyCapture.CaptureAction.PassThrough:            // 裸 Tab/Enter：放行，键盘用户不被困在框里
                e.Handled = false; return;
            case HotkeyCapture.CaptureAction.Cancel:
                HotkeyBox.Text = _hotkey; Keyboard.ClearFocus(); return;
            case HotkeyCapture.CaptureAction.Clear:
                _hotkey = ""; HotkeyBox.Text = ""; Keyboard.ClearFocus(); return;
            case HotkeyCapture.CaptureAction.Captured:
                _hotkey = combo!; HotkeyBox.Text = combo; Keyboard.ClearFocus(); return;
            default: return;                                         // Ignore
        }
    }

    private int Sel => Steps.SelectedIndex;

    private void SAdd_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in Kinds)
        {
            var k = kind;
            var mi = new MenuItem { Header = StepDisplay.StepKindLabel(k) };
            mi.Click += (s, _) =>
            {
                var step = StepEditorWindow.Edit(this, null, k, _groups);
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
        var edited = StepEditorWindow.Edit(this, step, step.Kind, _groups);
        if (edited != null) { _rows[i] = new StepRowVm(edited, () => { }); Steps.SelectedIndex = i; }
    }

    private void SDel_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        _rows.RemoveAt(i);
        if (_rows.Count > 0) Steps.SelectedIndex = Math.Min(i, _rows.Count - 1);
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
        Result = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = _original.Enabled,
            Hotkey = _hotkey,
            Steps = _rows.Select(r => r.Step).ToList(),
        };
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
