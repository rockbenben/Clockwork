using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
using static Clockwork.Views.EditorUi;

namespace Clockwork.Views;

// 统一步骤编辑器：公共字段 + 按类型切换的字段面板。
public partial class StepEditorWindow : Window
{
    private readonly IReadOnlyList<ActionGroup> _groups;
    private readonly LaunchStep _original;   // 保留 UI 未暴露的字段（启用态 + app 进阶项），编辑时不丢
    public LaunchStep? Result { get; private set; }

    public StepEditorWindow(LaunchStep step, IReadOnlyList<ActionGroup> groups)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.DarkTitleBar.Apply(this);
        _groups = groups;
        _original = step;

        FillCombo(KindCombo, StepDisplay.StepKinds.Select(k => (StepDisplay.StepKindLabel(k), k)).ToArray(), step.Kind);
        FillCombo(VolActionCombo, new[] { (Strings.Get("Vol_mute"), "mute"), (Strings.Get("Vol_unmute"), "unmute"), (Strings.Get("Ed_Vol_Set"), "set") }, string.IsNullOrEmpty(step.Action) ? "mute" : step.Action);
        FillCombo(WinActionCombo, new[] { (Strings.Get("Win_close"), "close"), (Strings.Get("Win_minimize"), "minimize"), (Strings.Get("Win_maximize"), "maximize"), (Strings.Get("Win_activate"), "activate"), (Strings.Get("Win_sendkey"), "sendkey") }, string.IsNullOrEmpty(step.Action) ? "close" : step.Action);
        FillCombo(SysCmdCombo, StepDisplay.SystemCommandMap().Select(kv => (kv.Value, kv.Key)).ToArray(), step.Command);
        FillCombo(OnYesTypeCombo, new[] { (Strings.Get("Ed_OnYes_None"), "none"), (Strings.Get("Ed_OnYes_Run"), "run"), (Strings.Get("Ed_OnYes_Url"), "url") }, step.OnYes.Type == "sound" ? "run" : step.OnYes.Type);
        FillCombo(GroupCombo, new[] { (Strings.Get("Ed_Group_None"), "") }.Concat(_groups.Select(g => (g.Name, g.Id))).ToArray(), step.GroupId);
        FillCombo(WinStyleCombo, new[]
        {
            (Strings.Get("WinStyle_Default"), ""), (Strings.Get("WinStyle_Minimized"), "minimized"),
            (Strings.Get("WinStyle_Maximized"), "maximized"), (Strings.Get("WinStyle_Hidden"), "hidden"),
        }, step.WindowStyle);

        LoadStep(step);
        ShowPanelForKind(step.Kind);
        UpdateVolRow();
        UpdateWinRows();
    }

    private void LoadStep(LaunchStep s)
    {
        LabelBox.Text = s.Label;
        TargetBox.Text = s.Target; ArgsBox.Text = s.Args; WorkDirBox.Text = s.WorkDir; ElevatedChk.IsChecked = s.Elevated;
        ActivateChk.IsChecked = s.ActivateIfRunning; ActivateProcBox.Text = s.ActivateProcess; AltTargetsBox.Text = s.AltTargets;
        ComboBox2.Text = s.Combo;
        LevelBox.Text = s.Level.ToString();
        ProcessBox.Text = s.Process; SendKeyBox.Text = s.SendKey; WaitWinBox.Text = s.WaitForWindowSeconds.ToString(); PostDelayBox.Text = s.PostWindowDelaySeconds.ToString();
        TextBox2.Text = s.Text; TextProcessBox.Text = s.Process;
        MessageBox2.Text = s.Message; SpeakChk.IsChecked = s.Speak; ConfirmChk.IsChecked = s.Confirm; OnYesTargetBox.Text = s.OnYes.Target;
        DelayBox.Text = s.DelayMs.ToString();
        RepeatBox.Text = StepHelpers.StepRepeat(s).ToString();
        NoteBox.Text = s.Note;
        LoadDays(s.Days, Day1, Day2, Day3, Day4, Day5, Day6, Day7);
        OnlyBeforeChk.IsChecked = s.OnlyBefore8;
        BeforeHourBox.Text = StepHelpers.BeforeHour(s).ToString();
    }

    private void ShowPanelForKind(string kind)
    {
        Vis(PanApp, kind == "app"); Vis(PanKeys, kind == "keys"); Vis(PanVolume, kind == "volume");
        Vis(PanWindow, kind == "window"); Vis(PanSystem, kind == "system"); Vis(PanText, kind == "text");
        Vis(PanMessage, kind == "message"); Vis(PanGroup, kind == "group");
        Vis(RepeatRow, kind != "message");   // 消息步骤不循环
    }

    private void KindCombo_Changed(object sender, SelectionChangedEventArgs e) => ShowPanelForKind(ComboVal(KindCombo));
    private void VolAction_Changed(object sender, SelectionChangedEventArgs e) => UpdateVolRow();
    private void WinAction_Changed(object sender, SelectionChangedEventArgs e) => UpdateWinRows();

    private void UpdateVolRow() => Vis(VolLevelRow, ComboVal(VolActionCombo) == "set");
    private void UpdateWinRows()
    {
        var a = ComboVal(WinActionCombo);
        Vis(WinSendRow, a == "sendkey");
        Vis(WinPostRow, a is "close" or "minimize" or "maximize" or "activate");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var kind = ComboVal(KindCombo);
        int delay = ParseOr(DelayBox.Text, 0);
        var days = CollectDays(Day1, Day2, Day3, Day4, Day5, Day6, Day7);
        int beforeHour = ParseOr(BeforeHourBox.Text, 8, 1, 23);

        var r = new LaunchStep
        {
            Kind = kind,
            Label = LabelBox.Text,
            DelayMs = delay,
            Repeat = StepHelpers.ClampRepeat(ParseOr(RepeatBox.Text, 0)),
            Note = NoteBox.Text,
            Days = days,
            OnlyBefore8 = OnlyBeforeChk.IsChecked == true,
            BeforeHour = beforeHour,
            Enabled = _original.Enabled,   // 保留启用/禁用态：编辑步骤不应把用户关掉的步骤又打开
        };

        switch (kind)
        {
            case "app":
                r.Target = TargetBox.Text; r.Args = ArgsBox.Text; r.WorkDir = WorkDirBox.Text; r.Elevated = ElevatedChk.IsChecked == true;
                // 进阶项已有编辑控件（窗口风格/已运行则激活/备用路径），从 UI 收值。
                r.ActivateIfRunning = ActivateChk.IsChecked == true;
                r.ActivateProcess = StepHelpers.ToProcessName(ActivateProcBox.Text);
                r.WindowStyle = ComboVal(WinStyleCombo);
                r.AltTargets = AltTargetsBox.Text;
                break;
            case "keys": r.Combo = ComboBox2.Text; r.Label = string.IsNullOrEmpty(r.Label) ? ComboBox2.Text : r.Label; break;
            case "volume": r.Action = ComboVal(VolActionCombo); r.Level = Math.Clamp(ParseOr(LevelBox.Text, 0), 0, 100); break;
            case "window":
                r.Action = ComboVal(WinActionCombo); r.Process = StepHelpers.ToProcessName(ProcessBox.Text);
                r.SendKey = SendKeyBox.Text; r.WaitForWindowSeconds = ParseOr(WaitWinBox.Text, 0); r.PostWindowDelaySeconds = ParseOr(PostDelayBox.Text, 0);
                break;
            case "system": r.Command = ComboVal(SysCmdCombo); r.Label = StepDisplay.SystemCommandLabel(r.Command); break;
            case "text": r.Text = TextBox2.Text; r.Process = StepHelpers.ToProcessName(TextProcessBox.Text); break;
            case "group": r.GroupId = ComboVal(GroupCombo); r.Label = _groups.FirstOrDefault(g => g.Id == r.GroupId)?.Name ?? r.Label; break;
            case "message": r.Message = MessageBox2.Text; r.Speak = SpeakChk.IsChecked == true; r.Confirm = ConfirmChk.IsChecked == true; r.OnYes = new OnYes { Type = ComboVal(OnYesTypeCombo), Target = OnYesTargetBox.Text }; break;
        }

        Result = r;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // —— 选择器（浏览/选择进程/捕获按键）：取消则不动原值 ——
    private void BrowseTarget_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) TargetBox.Text = p; }
    private void BrowseWorkDir_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFolder(this) is string p) WorkDirBox.Text = p; }
    private void BrowseOnYes_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) OnYesTargetBox.Text = p; }
    private void PickProcess_Click(object sender, RoutedEventArgs e) { if (Pickers.PickProcess(this) is string p) ProcessBox.Text = p; }
    private void PickTextProcess_Click(object sender, RoutedEventArgs e) { if (Pickers.PickProcess(this) is string p) TextProcessBox.Text = p; }
    private void PickActivateProc_Click(object sender, RoutedEventArgs e) { if (Pickers.PickProcess(this) is string p) ActivateProcBox.Text = p; }
    // 两个捕获目的地走不同发送路径，键集不同：组合键 = SendInput（能解析出 VK 即可，Win 组合支持）；
    // 置前发送键 = SendKeys（Win 不支持、键名须能编码，否则运行时被当字面文本打进目标窗口）。
    private void CaptureCombo_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.CaptureKey(this, c => Native.KeyInput.ToHotkeyParams(c) != null) is string s) ComboBox2.Text = s;
    }
    private void CaptureSendKey_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.CaptureKey(this, KeyCombo.CanEncodeForSendKeys) is string s) SendKeyBox.Text = s;
    }

    // 打开编辑器，返回编辑后的新步骤（取消→null）。step 为 null=新建指定 kind。
    public static LaunchStep? Edit(Window owner, LaunchStep? step, string kind, IReadOnlyList<ActionGroup> groups)
    {
        var s = step ?? new LaunchStep { Kind = kind, Action = kind == "volume" ? "set" : (kind == "window" ? "close" : "") };
        var dlg = new StepEditorWindow(s, groups) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
