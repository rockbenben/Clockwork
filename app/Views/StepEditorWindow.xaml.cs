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
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _groups = groups;
        _original = step;

        FillCombo(KindCombo, StepDisplay.StepKinds.Select(k => (StepDisplay.StepKindLabel(k), k)).ToArray(), step.Kind);
        FillCombo(VolActionCombo, new[]
        {
            (Strings.Get("Vol_mute"), "mute"), (Strings.Get("Vol_unmute"), "unmute"), (Strings.Get("Ed_Vol_Set"), "set"),
            (Strings.Get("Vol_micMute"), "micMute"), (Strings.Get("Vol_micUnmute"), "micUnmute"),
        }, string.IsNullOrEmpty(step.Action) ? "mute" : step.Action);
        FillCombo(WinActionCombo, new[] { (Strings.Get("Win_close"), "close"), (Strings.Get("Win_minimize"), "minimize"), (Strings.Get("Win_maximize"), "maximize"), (Strings.Get("Win_activate"), "activate"), (Strings.Get("Win_sendkey"), "sendkey") }, string.IsNullOrEmpty(step.Action) ? "close" : step.Action);
        FillCombo(SysCmdCombo, StepDisplay.SystemCommandMap().Select(kv => (kv.Value, kv.Key)).ToArray(), step.Command);
        FillCombo(OnYesTypeCombo, new[] { (Strings.Get("Ed_OnYes_None"), "none"), (Strings.Get("Ed_OnYes_Run"), "run"), (Strings.Get("Ed_OnYes_Url"), "url") }, step.OnYes.Type == "sound" ? "run" : step.OnYes.Type);
        FillCombo(GroupCombo, new[] { (Strings.Get("Ed_Group_None"), "") }.Concat(_groups.Select(g => (g.Name, g.Id))).ToArray(), step.GroupId);
        FillCombo(PresentCombo, new[] { (Strings.Get("Present_Dialog"), ""), (Strings.Get("Present_Card"), "card") }, step.Present == "card" ? "card" : "");
        FillCombo(WinStyleCombo, new[]
        {
            (Strings.Get("WinStyle_Default"), ""), (Strings.Get("WinStyle_Minimized"), "minimized"),
            (Strings.Get("WinStyle_Maximized"), "maximized"), (Strings.Get("WinStyle_Hidden"), "hidden"),
        }, step.WindowStyle);
        FillCombo(IfProcModeCombo, new[]
        {
            (Strings.Get("Ed_Cond_Any"), ""), (Strings.Get("Ed_IfProc_Running"), "running"), (Strings.Get("Ed_IfProc_Not"), "notRunning"),
        }, step.IfProcessMode);
        FillCombo(IfPowerCombo, new[]
        {
            (Strings.Get("Ed_Cond_Any"), ""), (Strings.Get("Ed_IfPower_Ac"), "ac"), (Strings.Get("Ed_IfPower_Battery"), "battery"),
        }, step.IfPower);

        LoadStep(step);
        ShowPanelForKind(step.Kind);
        UpdateVolRow();
        UpdateWinRows();
        UpdateOnYes();
        UpdateMessageRows();
        UpdateSysRows();
        UpdateIfProcRow();

        // 交叉口指路：一个组都没有时「动作组」下拉是死路，指条活路（Ed_NoGroupsHint）。
        Vis(NoGroupsHint, _groups.Count == 0);

        // 「条件与重复」折叠条：标题实时等于当前配置的摘要修饰段（与列表同一套文案）。
        // 没配置的默认收起——新用户加一条「打开微信」不必面对六种条件；配过的自动展开，别把已有配置藏没。
        foreach (var cb in new[] { Day1, Day2, Day3, Day4, Day5, Day6, Day7, OnlyBeforeChk, OnlyAfterChk })
            cb.Click += (_, _) => UpdateCondHeader();
        foreach (var tb in new[] { BeforeTimeBox, AfterTimeBox, IfProcBox, IfPathBox, RepeatBox })
            tb.TextChanged += (_, _) => UpdateCondHeader();
        IfPowerCombo.SelectionChanged += (_, _) => UpdateCondHeader();
        // IfProcModeCombo 已有 IfProcMode_Changed，在那里顺带刷新（别挂两个各自为政的处理器）。
        UpdateCondHeader();
        CondExp.IsExpanded = StepDisplay.DecorationSummary(ConditionProbe()).Length > 0;

        // 「组合键」是单个组合（keys 步骤经 SendKeyCombo 单发），与热键同性质，改「点击即录键」——去掉多余的捕捉按钮。
        // 值就在框里、确定时读取，故 set 空。（「发送键」是 SendKeys 序列，可含 {TAB}{ENTER}/字面文本，必须能打字，
        // 保留普通文本框 + 捕捉按钮，不套 KeyCaptureBox。）
        // allowTyping：Win+D / Win+E 这类被 Explorer 全局注册的组合，系统在应用之前就吃掉了按键、捕捉不到，
        // 但它们作为发送内容完全有效（SendKeyCombo 会真发 LWIN）——双击切手输才录得进来。
        // HasUnknownModifier：手输才需要的一道关。ToHotkeyParams 单独用不够——它对 "Ctrl+Shft+A" 也返回非空
        // （Shft 被当主键、又被 A 覆盖），结果框里显示 Ctrl+Shft+A、实际发 Ctrl+A。捕捉出来的串不会畸形。
        KeyCaptureBox.Attach(ComboBox2, Native.HotkeyCapture.KeyCaptureMode.SendKeys,
            c => Native.KeyInput.ToHotkeyParams(c) != null && !KeyCombo.HasUnknownModifier(c),
            () => ComboBox2.Text, _ => { }, allowTyping: true);
    }

    // （关窗恢复全局热键的兜底由 KeyCaptureBox 统一负责——挂宿主窗口 Closed，此处不再各写一份。）

    // 「发送键」的捕捉便利：SendKeys 序列须能打字，故仍用弹窗按需录一个键（校验目的地可编码），不改成只读捕捉框。
    private void CaptureSendKey_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.CaptureKey(this, KeyCombo.CanEncodeForSendKeys) is string s) SendKeyBox.Text = s;
    }

    // 「点是后」选「无」时，目标框和「浏览…」什么也控制不了——原来这行根本没接切换事件，
    // 两个假控件一直摆在那儿：看着能填、填了不生效。没有可填的东西就别显示。
    private void OnYesType_Changed(object sender, SelectionChangedEventArgs e) => UpdateOnYes();

    private void UpdateOnYes()
    {
        bool target = ComboVal(OnYesTypeCombo) != "none";
        Vis(OnYesTargetBox, target); Vis(OnYesBrowseBtn, target);
    }

    private void Present_Changed(object sender, SelectionChangedEventArgs e) => UpdateMessageRows();

    // 卡片只有「点击即关」一种交互，挂不了动作：选卡片时藏掉「是/否确认」与「点是后」，
    // 露出「自动关闭(秒)」。与本编辑器既有立场一致——没有可填的东西就别显示（见 UpdateOnYes）。
    private void UpdateMessageRows()
    {
        bool card = ComboVal(PresentCombo) == "card";
        Vis(MsgCardRow, card);
        Vis(ConfirmChk, !card);
        Vis(MsgOnYesRow, !card);
    }

    private void SysCmd_Changed(object sender, SelectionChangedEventArgs e) => UpdateSysRows();
    private void IfProcMode_Changed(object sender, SelectionChangedEventArgs e) { UpdateIfProcRow(); UpdateCondHeader(); }

    // 从表单当前值收「条件 + 重复」字段——这套映射的唯一出处：折叠条标题实时计算用它，
    // Ok_Click 保存也从它起步再补其余字段。只有一份，标题与落盘才不可能说两种话。
    // 「选了不限就清进程名」也在这儿：留着会在 json 里躺一个不生效的条件，
    // 下次改回「该进程在运行时」又悄悄复活（与 message 步骤切卡片时清掉 Confirm/OnYes 同一条理由）。
    private LaunchStep ConditionProbe()
    {
        ParseBeforeTime(BeforeTimeBox.Text, out int bh, out int bm);
        ParseBeforeTime(AfterTimeBox.Text, out int ah, out int am, fallbackHour: 18);
        return new LaunchStep
        {
            Repeat = StepHelpers.ClampRepeat(ParseOr(RepeatBox.Text, 0)),
            Days = CollectDays(Day1, Day2, Day3, Day4, Day5, Day6, Day7),
            OnlyBefore8 = OnlyBeforeChk.IsChecked == true, BeforeHour = bh, BeforeMinute = bm,
            OnlyAfter = OnlyAfterChk.IsChecked == true, AfterHour = ah, AfterMinute = am,
            IfProcessMode = ComboVal(IfProcModeCombo),
            IfProcess = ComboVal(IfProcModeCombo) == "" ? "" : StepHelpers.ToProcessName(IfProcBox.Text),
            IfPower = ComboVal(IfPowerCombo),
            IfPathExists = IfPathBox.Text.Trim(),
        };
    }

    private void UpdateCondHeader()
    {
        var deco = StepDisplay.DecorationSummary(ConditionProbe()).TrimStart();
        CondExp.Header = Strings.Lf("Ed_CondHeader", deco.Length == 0 ? Strings.Get("Ed_CondAlways") : deco);
    }

    // 只有带参数的系统命令才露出对应的输入行——没有可填的东西就别显示（与 UpdateOnYes 同一条立场）。
    private void UpdateSysRows()
    {
        var cmd = ComboVal(SysCmdCombo);
        Vis(SysTextRow, StepDisplay.SystemCommandTakesText(cmd));
        Vis(SysLevelRow, StepDisplay.SystemCommandTakesLevel(cmd));
    }

    // 选「不限」时进程名框与「选择…」什么也控制不了，一并藏掉。
    private void UpdateIfProcRow()
    {
        bool on = ComboVal(IfProcModeCombo) != "";
        Vis(IfProcBox, on); Vis(IfProcPickBtn, on);
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
        PopupSecondsBox.Text = s.PopupSeconds.ToString();
        DelayBox.Text = s.DelayMs.ToString();
        RepeatBox.Text = StepHelpers.StepRepeat(s).ToString();
        NoteBox.Text = s.Note;
        SysTextBox.Text = s.Text; SysLevelBox.Text = s.Level.ToString();
        LoadDays(s.Days, Day1, Day2, Day3, Day4, Day5, Day6, Day7);
        OnlyBeforeChk.IsChecked = s.OnlyBefore8;
        BeforeTimeBox.Text = StepHelpers.BeforeTimeLabel(s);   // HH:mm，支持任意时刻
        OnlyAfterChk.IsChecked = s.OnlyAfter;
        AfterTimeBox.Text = StepHelpers.AfterTimeLabel(s);
        IfProcBox.Text = s.IfProcess;
        IfPathBox.Text = s.IfPathExists;
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
        // 发送键编辑期校验：IsValidSendKeys 按 SendKeys 真实语法精确解析，只拦 SendWait 必抛的串
        //（未闭合/空花括号组、未知键名、孤立 } 等），合法转义（{{} {}}）不误伤——
        // 旧的「花括号是否成对」廉价校验因误伤被移除，没有编辑期兜底则畸形串要到每次开机运行时才暴露。
        if (kind == "window" && ComboVal(WinActionCombo) == "sendkey" && !KeyCombo.IsValidSendKeys(SendKeyBox.Text))
        {
            BrandDialog.Warn(this, "Clockwork", Strings.Get("Val_SendKeys"));
            return;
        }

        // 条件与重复从 ConditionProbe 起步——那是这套字段映射的唯一出处（折叠条标题实时用的同一份）。
        // 曾经这里逐字重抄一遍，评审指出两份手抄迟早漂移：下一个条件字段只加了保存这边，
        // 标题就会描述一个与落盘不同的步骤，而且不会有任何东西报错。
        var r = ConditionProbe();
        r.Kind = kind;
        r.Label = LabelBox.Text;
        r.DelayMs = ParseOr(DelayBox.Text, 0);
        r.Note = NoteBox.Text;
        r.Enabled = _original.Enabled;   // 保留启用/禁用态：编辑步骤不应把用户关掉的步骤又打开

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
            case "system":
                r.Command = ComboVal(SysCmdCombo); r.Label = StepDisplay.SystemCommandLabel(r.Command);
                // 只给用得上参数的命令存参数：否则「锁屏」步骤的 json 里会躺着一段与它无关的剪贴板文本。
                if (StepDisplay.SystemCommandTakesText(r.Command)) r.Text = SysTextBox.Text;
                if (StepDisplay.SystemCommandTakesLevel(r.Command)) r.Level = Math.Clamp(ParseOr(SysLevelBox.Text, 50), 0, 100);
                break;
            case "text": r.Text = TextBox2.Text; r.Process = StepHelpers.ToProcessName(TextProcessBox.Text); break;
            case "group": r.GroupId = ComboVal(GroupCombo); r.Label = _groups.FirstOrDefault(g => g.Id == r.GroupId)?.Name ?? r.Label; break;
            case "message":
                r.Message = MessageBox2.Text; r.Speak = SpeakChk.IsChecked == true;
                r.Present = ComboVal(PresentCombo);
                r.PopupSeconds = ParseOr(PopupSecondsBox.Text, 5, min: 0, max: 86400);
                // 卡片形态清掉确认/动作：留着会在 json 里躺一份点不到的配置，改回对话框时又悄悄复活。
                if (r.Present == "card") { r.Confirm = false; r.OnYes = new OnYes(); }
                else { r.Confirm = ConfirmChk.IsChecked == true; r.OnYes = new OnYes { Type = ComboVal(OnYesTypeCombo), Target = OnYesTargetBox.Text }; }
                break;
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
    private void PickIfProcess_Click(object sender, RoutedEventArgs e) { if (Pickers.PickProcess(this) is string p) IfProcBox.Text = p; }
    // 条件用的路径既可能是文件也可能是文件夹（U 盘盘符、导出目录），两个都能挑：先给文件框，
    // 用户取消了再给文件夹框——一个按钮覆盖两种，比并排放两个按钮省事。
    private void BrowseIfPath_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.BrowseFile(this) is string f) { IfPathBox.Text = f; return; }
        if (Pickers.BrowseFolder(this) is string d) IfPathBox.Text = d;
    }
    // 打开编辑器，返回编辑后的新步骤（取消→null）。step 为 null=新建指定 kind。
    public static LaunchStep? Edit(Window owner, LaunchStep? step, string kind, IReadOnlyList<ActionGroup> groups)
    {
        var s = step ?? new LaunchStep { Kind = kind, Action = kind == "volume" ? "set" : (kind == "window" ? "close" : "") };
        var dlg = new StepEditorWindow(s, groups) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
