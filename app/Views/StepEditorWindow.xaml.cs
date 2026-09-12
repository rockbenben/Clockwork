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
    private string _icon = "";
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
        // 下拉项直接由 StepDisplay.MouseActions 生成：动作 id / 文案 / 伪键只此一张表，
        // 加一个动作只改那张表，编辑器、摘要、执行三处自动跟上，不会出现「选得到但跑不了」。
        FillCombo(MouseActCombo, StepDisplay.MouseActions.Select(m => (Strings.Get(m.LabelKey), m.Action)).ToArray(),
                  StepDisplay.MouseActions.Any(m => m.Action == step.Action) ? step.Action : "down");
        FillCombo(WinActionCombo, StepDisplay.WindowActionMap().Select(kv => (kv.Value, kv.Key)).ToArray(), string.IsNullOrEmpty(step.Action) ? "close" : step.Action);
        FillCombo(SysCmdCombo, StepDisplay.SystemCommandMap().Select(kv => (kv.Value, kv.Key)).ToArray(), step.Command);
        // 引擎下拉的「值」就是地址本身，所以选中哪一项 = 盘上存的是哪个地址，不必另存一个引擎 id。
        // 手改 json 填了个表外的地址（或从旧版本升上来）→ 反查不到 → 落到「自定义…」并把地址原样露出来。
        // 空地址按默认（Bing）算，与执行那边的兜底同一口径。
        var engines = StepDisplay.SearchEngines.Select(e => (e.Name, e.Url)).ToList();
        engines.Add((Strings.Get("Ed_SearchCustom"), CustomEngine));
        string url = string.IsNullOrWhiteSpace(step.Text) ? StepDisplay.DefaultSearchUrl : step.Text.Trim();
        FillCombo(SysEngineCombo, engines.ToArray(), StepDisplay.SearchEngineOf(url) == null ? CustomEngine : url);
        FillCombo(OnYesTypeCombo, new[] { (Strings.Get("Ed_OnYes_None"), "none"), (Strings.Get("Ed_OnYes_Run"), "run"), (Strings.Get("Ed_OnYes_Url"), "url") }, step.OnYes.Type == "sound" ? "run" : step.OnYes.Type);
        // withNone 必须是 true。上一版这个下拉的第一项就是「（无）」（值为空），所以盘上
        // 完全可能躺着一条 {"kind":"group","groupId":""} 的步骤——它什么都不做。
        // 去掉那一项之后，空值在下拉里找不到对应项，选中就落到**第一个真实动作组**上，
        // 于是用户只是打开看一眼再点确定，这一步就从「什么都不做」变成了「运行第一个组」，
        // 而第一个组完全可能是关机组。打开再关上绝不能改变一个步骤。
        FillGroupCombo(GroupCombo, _groups, step.GroupId, withNone: true);
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
        // 目标一变，自动取到的图标也变。跟着输入走，而不是只在打开时算一次——
        // 选完程序却看见上一个程序的图标，会让人以为图标是自己填死的。
        TargetBox.TextChanged += (_, _) => RefreshIcon();
        // 说明行里带着当前变量名（「后面的步骤用 {关键词} 引用它」）。跟着输入实时变——
        // 只在打开时算一次的话，改完名字看到的还是旧名字，而那句话正是拿来照着抄的。
        OutputVarBox.TextChanged += (_, _) => ShowPanelForKind(ComboVal(KindCombo));
        // 按**下拉里实际选中的那一项**铺表单，不按 step.Kind 原值。
        // FillCombo 认不出 selected 时会落回第一项（EditorUi.FillCombo），于是一条 kind 为空
        // 或写错的步骤（手改过 json、或从别处导入）打开来是：下拉写着「运行程序」，
        // 而那一类的字段一个都不出现——表单自己和自己的下拉说的不是一回事。
        ShowPanelForKind(ComboVal(KindCombo));
        UpdateVolRow();
        UpdateWinRows();
        UpdateOnYes();
        UpdateMessageRows();
        UpdateSysRows();
        UpdateIfProcRow();

        // 交叉口指路：一个组都没有时「动作组」下拉是死路，指条活路（Ed_NoGroupsHint）。
        Vis(NoGroupsHint, _groups.Count == 0);
        GroupCombo.SelectionChanged += (_, _) => PaintGroupPeek();
        PaintGroupPeek();

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
            Native.KeyInput.CanSendCombo,
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

    // 「自定义」在下拉里的值。用一个不可能是真地址的记号，免得和某个引擎的地址撞上。
    private const string CustomEngine = "(custom)";

    private void SysCmd_Changed(object sender, SelectionChangedEventArgs e) => UpdateSysRows();

    private void SysEngine_Changed(object sender, SelectionChangedEventArgs e) => UpdateSysRows();
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
            // 消息步骤的重复次数不在这里清：StepHelpers.StepRepeat 是所有读取方的共用漏斗，已经在那儿恒定为 1，
            // 那份修法连盘上既有配置和手改的 json 一起管，比只管新保存的这一次深。
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

    // 「搜索选中文字」会把 SysTextBox 写成引擎地址，而**这个框是三个命令共用的**
    // （搜索地址 / 声音文件 / 剪贴板文本）。只写不还的话，在命令下拉里**路过**一下搜索档，
    // 就把一个 playSound 步骤的 .wav 路径永久换成了一串 https://…——按确定即落盘，
    // 之后每次运行都报 Err_SoundNotWav；setClipboard 那一档更安静：它开始往剪贴板写搜索地址。
    // 触发条件只要 Text 是空的（那正是这两个命令的默认值：空 = 系统提示音 / 空剪贴板）。
    // 所以离开搜索档时必须把原文还回去，这两个字段就是那份寄存。
    private bool _wasSearchCmd;
    private string _nonSearchText = "";

    // 只有带参数的系统命令才露出对应的输入行——没有可填的东西就别显示（与 UpdateOnYes 同一条立场）。
    private void UpdateSysRows()
    {
        var cmd = ComboVal(SysCmdCombo);
        bool search = cmd == "searchSelection";
        bool sound = cmd == "playSound";
        // 选好引擎的人不该再看见一行 https://…——那串东西对他毫无意义，还容易被误当成要填的东西。
        // 地址框只在「自定义」那一档出现，其余时候它只是下拉的存储介质，藏起来。
        var engine = ComboVal(SysEngineCombo);
        bool custom = search && engine == CustomEngine;
        // 选中具体引擎时，地址框**始终**跟着它——存盘取的一直是这个框，两边不同步就会出现
        // 「下拉写着 Google，跑起来搜的是必应」。切到「自定义」时不清空：刚才那个引擎的地址
        // 正好是最省事的起点，照着改一个参数名就成。
        // 进出搜索档时寄存 / 还原（见上面那两个字段）。顺序是承重的：先还原，再写引擎地址，
        // 否则「自定义」那一档会把刚还回来的原文又冲掉。
        if (search && !_wasSearchCmd) _nonSearchText = SysTextBox.Text;   // 刚从别的命令切进来
        if (!search && _wasSearchCmd) SysTextBox.Text = _nonSearchText;   // 切回去了，原文还给它
        _wasSearchCmd = search;
        if (search && !custom && !string.IsNullOrEmpty(engine)) SysTextBox.Text = engine;
        Vis(SysEngineRow, search);
        Vis(SysTextRow, custom || (StepDisplay.SystemCommandTakesText(cmd) && !search));
        Vis(SysLevelRow, StepDisplay.SystemCommandTakesLevel(cmd));
        // 同一个输入框三个命令在用，标签得跟着换：顶着「剪贴板文本」去填搜索地址，
        // 是那种你照做了、却不知道自己在填什么的框。
        SysTextLabel.Text = Strings.Get(search ? "Ed_SearchUrl" : sound ? "Ed_SoundFile" : "Ed_ClipText");
        System.Windows.Automation.AutomationProperties.SetName(SysTextBox, SysTextLabel.Text);
        // 说明行：自定义搜索地址要一句，声音文件更要——「留空 = 系统提示音」是这一档
        // 唯一不显然的事，不说的话，一个空框读起来像是必填。
        SysTextHint.Text = Strings.Get(sound ? "Ed_SoundHint" : "Ed_SearchUrlHint");
        Vis(SysTextHint, custom || sound);
        // 同一个框，三种内容，高度也得跟着换：剪贴板文本可能是一整段，地址和文件路径只有一行。
        // 一行的东西放在三行高的框里，读作「这儿要粘一大段」——框的高矮本身就是一句提示。
        bool oneLine = search || sound;
        SysTextBox.Height = oneLine ? 28 : 72;
        SysTextBox.AcceptsReturn = !oneLine;
        // 地址永远是从左到右的一串东西。在阿拉伯语这类 RTL 界面里让它跟着继承方向，
        // 双向算法会把结尾那个 } 甩到最前，显示成「{https://…?q={0」——而那正是你要编辑的一行。
        // 剪贴板文本相反，它是用户自己的话，该跟着界面走，所以那一档把本地值清掉、恢复继承。
        // 文件路径同地址：一串从左到右的东西，跟着 RTL 界面走会被双向算法拆得认不出来。
        if (oneLine) SysTextBox.FlowDirection = System.Windows.FlowDirection.LeftToRight;   // 裸写 FlowDirection 会撞上本窗口自己那个同名属性
        else SysTextBox.ClearValue(FlowDirectionProperty);
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
        _icon = s.Icon ?? "";
        TargetBox.Text = s.Target; ArgsBox.Text = s.Args; WorkDirBox.Text = s.WorkDir; ElevatedChk.IsChecked = s.Elevated;
        ActivateChk.IsChecked = s.ActivateIfRunning; ActivateProcBox.Text = s.ActivateProcess; AltTargetsBox.Text = s.AltTargets;
        ComboBox2.Text = s.Combo;
        LevelBox.Text = s.Level.ToString();
        ProcessBox.Text = s.Process; SendKeyBox.Text = s.SendKey; WaitWinBox.Text = s.WaitForWindowSeconds.ToString(); PostDelayBox.Text = s.PostWindowDelaySeconds.ToString();
        TextBox2.Text = s.Text; TextProcessBox.Text = s.Process;
UrlBox.Text = s.Kind == "url" ? s.Target : "";
        WaitSecondsBox.Text = StepHelpers.ClampWaitSeconds(s.Level).ToString();
        MessageBox2.Text = s.Message; SpeakChk.IsChecked = s.Speak; ConfirmChk.IsChecked = s.Confirm; OnYesTargetBox.Text = s.OnYes.Target;
        PopupSecondsBox.Text = s.PopupSeconds.ToString();
        DelayBox.Text = s.DelayMs.ToString();
        RepeatBox.Text = StepHelpers.StepRepeat(s).ToString();
        NoteBox.Text = s.Note;
        SysTextBox.Text = s.Text; SysLevelBox.Text = s.Level.ToString();
        // 初值必须在第一次 UpdateSysRows 之前定好：不定的话，打开一个搜索步骤时
        // 它的引擎地址会被当成「非搜索原文」寄存起来，之后切到播放声音就把那串 https:// 还了过去。
        _wasSearchCmd = s.Command == "searchSelection";
        _nonSearchText = _wasSearchCmd ? "" : s.Text;
        // 两个问句类型都借 Message 当提示语、Text 当默认值/选项（见 Ok_Click 那两条）。
        // 两个提示语框各读一次同一个字段：切换类型时不必来回搬值，谁显示谁读自己的。
        AskTextBox.Text = ChoiceTextBox.Text = s.Message;
        AskDefaultBox.Text = OptionsBox.Text = s.Text;
        OutputVarBox.Text = s.OutputVar;
        LoadDays(s.Days, Day1, Day2, Day3, Day4, Day5, Day6, Day7);
        OnlyBeforeChk.IsChecked = s.OnlyBefore8;
        BeforeTimeBox.Text = StepHelpers.BeforeTimeLabel(s);   // HH:mm，支持任意时刻
        OnlyAfterChk.IsChecked = s.OnlyAfter;
        AfterTimeBox.Text = StepHelpers.AfterTimeLabel(s);
        IfProcBox.Text = s.IfProcess;
        IfPathBox.Text = s.IfPathExists;
        RefreshIcon();
    }

    private void ShowPanelForKind(string kind)
    {
        Vis(PanApp, kind == "app"); Vis(PanKeys, kind == "keys"); Vis(PanVolume, kind == "volume");
        Vis(PanMouse, kind == "mouse");
        Vis(PanWindow, kind == "window"); Vis(PanSystem, kind == "system"); Vis(PanText, kind == "text");
        Vis(PanMessage, kind == "message"); Vis(PanGroup, kind == "group");
        Vis(PanUrl, kind == "url"); Vis(PanCopySelection, kind == "copySelection");
        Vis(PanWaitClipboard, kind == "waitClipboard");
        Vis(PanPrompt, kind == "prompt"); Vis(PanChoice, kind == "choice");
        // 「输出到变量」跟着**会产出值的类型**走，不跟着单个面板走——三种类型共用这一行。
        Vis(PanOutputVar, kind is "prompt" or "choice" or "copySelection");
        // 说明里带上当前的变量名，好让「下一步怎么引用它」有个照着抄的样子：
        // 只写「用 {名字} 引用」的话，用户还得自己把名字代进去一次，而那一步正是最容易写错的。
        OutputVarHint.Text = Strings.Lf("Ed_OutputVarHint",
            "{" + (OutputVarBox.Text.Trim().Length > 0 ? OutputVarBox.Text.Trim() : Strings.Get("Ed_OutputVarSample")) + "}");
        Vis(RepeatRow, kind is not ("message" or "prompt" or "choice"));   // 这三类不循环，与 StepHelpers.StepRepeat 同口径
    }

    private void KindCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // **「运行程序」与「打开网址」共用 Target 这一个字段**，只是各有一个输入框。
        // 换类型时要把值搬过去，否则：一个 Kind="app"、Target="C:\Tools\backup.exe" 的步骤，
        // UrlBox 载入时是空的（只有 url 类型才填），把类型改成「打开网址」再按确定，
        // 保存那一支 `r.Target = UrlBox.Text.Trim()` 直接把路径写成空串——没有校验、没有提示、
        // 没有备份，而本文件第 50 行的规矩是「打开再关上绝不能改变一个步骤」。
        // 搬而不是各自留一份：用户改类型的语义是「同一个东西换个说法」，不是「另填一个」。
        // 只在对方为空时搬，免得把用户在新框里已经改过的内容冲掉。
        var kind = ComboVal(KindCombo);
        if (kind == "url" && UrlBox.Text.Trim().Length == 0) UrlBox.Text = TargetBox.Text;
        else if (kind == "app" && TargetBox.Text.Trim().Length == 0) TargetBox.Text = UrlBox.Text;
        ShowPanelForKind(kind);
        RefreshIcon();   // 换了类型，自动取到的图标也就变了
    }
    private void VolAction_Changed(object sender, SelectionChangedEventArgs e) => UpdateVolRow();
    private void WinAction_Changed(object sender, SelectionChangedEventArgs e) => UpdateWinRows();

    private void UpdateVolRow() => Vis(VolLevelRow, ComboVal(VolActionCombo) == "set");
    private void UpdateWinRows()
    {
        var a = ComboVal(WinActionCombo);
        // 「恢复活动窗口」不选目标（目标是触发那一刻的前台窗口，配置期没有可填的名字），
        // 于是进程名、两句说明、等窗口出现——四行一起收起来。留一个填了也不生效的框比没有更糟。
        bool target = StepDisplay.WindowActionNeedsTarget(a);
        Vis(WinProcRow, target); Vis(WinProcHint, target); Vis(WinHint, target); Vis(WinWaitRow, target);
        Vis(WinRestoreHint, !target);
        Vis(WinSendRow, a == "sendkey");
        Vis(WinPostRow, a is "close" or "minimize" or "maximize" or "activate" or "topmost");
        // 置顶是这一组里唯一的**开关**，而下拉里那一行字看不出这一点：
        // 配好之后跑两次，窗口自己弹回去了，用户只会以为是没生效。只在这一档说一句。
        Vis(WinTopmostHint, a == "topmost");
    }

    // 选中的组里有什么。选组时最该回答的问题是「我是不是选对了」，而只有名字回答不了它——
    // 同名难分的组、以及自己几个月前建的组，光看名字都想不起来内容。
    // 摘要口径与动作组列表那一列一致（前三步 · 串联），认一次就够。
    private void PaintGroupPeek()
    {
        var g = ActionGroupResolver.Resolve(_groups, ComboVal(GroupCombo));
        GroupPeek.Text = StepDisplay.GroupSummary(g);
        Vis(GroupPeek, GroupPeek.Text.Length > 0);
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

        // 自定义搜索地址少了 {0}：不拦的话，这一步每次都打开同一个页面、跟你选中什么毫无关系，
        // 而界面上一切正常。与上面那条发送键校验同一立场——畸形的配置别留到运行时才暴露。
        if (kind == "system" && ComboVal(SysCmdCombo) == "searchSelection"
            && ComboVal(SysEngineCombo) == CustomEngine && !SysTextBox.Text.Contains("{0}"))
        {
            BrandDialog.Warn(this, "Clockwork", Strings.Get("Val_SearchUrl"));
            return;
        }

        // 条件与重复从 ConditionProbe 起步——那是这套字段映射的唯一出处（折叠条标题实时用的同一份）。
        // 曾经这里逐字重抄一遍，评审指出两份手抄迟早漂移：下一个条件字段只加了保存这边，
        // 标题就会描述一个与落盘不同的步骤，而且不会有任何东西报错。
        var r = ConditionProbe();
        r.Kind = kind;
        r.Label = LabelBox.Text;
        r.Icon = _icon.Trim();
        r.DelayMs = ParseOr(DelayBox.Text, 0);
        r.Note = NoteBox.Text;
        r.Enabled = _original.Enabled;   // 保留启用/禁用态：编辑步骤不应把用户关掉的步骤又打开
        // 同理保留手势：本方法是**整份重建** LaunchStep，漏一个字段就等于保存时把它抹掉。
        // 手势在「鼠标手势」管理器里绑，这里只负责别把它弄丢——改一下动作参数就丢掉轨迹是静默数据丢失。
        r.Gesture = _original.Gesture;
        r.ForProcess = _original.ForProcess;

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
            // 网址存进 Target（与 app 同一个字段）：两者都是「要打开的东西」，
            // 而共用字段意味着把一条 app 步骤的类型改成「打开网址」时，填好的地址不会凭空消失。
            case "url": r.Target = UrlBox.Text.Trim(); break;
            case "copySelection": break;   // 除了「输出到变量」（下面统一收）没有别的参数
            case "waitClipboard": r.Level = StepHelpers.ClampWaitSeconds(ParseOr(WaitSecondsBox.Text, 5)); break;
            // 动作存进 Action（与音量/窗口步骤同一个字段的用法），不为它新开配置字段。
            // 点几次/滚几格用共用的「重复次数」，这里不存第二个计数。
            case "mouse": r.Action = ComboVal(MouseActCombo); break;
            case "volume": r.Action = ComboVal(VolActionCombo); r.Level = Math.Clamp(ParseOr(LevelBox.Text, 0), 0, 100); break;
            case "window":
                r.Action = ComboVal(WinActionCombo); r.Process = StepHelpers.ToProcessName(ProcessBox.Text);
                r.SendKey = SendKeyBox.Text; r.WaitForWindowSeconds = ParseOr(WaitWinBox.Text, 0); r.PostWindowDelaySeconds = ParseOr(PostDelayBox.Text, 0);
                break;
            case "system":
            {
                // 名字只在「用户没自己起过」时才代填。
                //
                // 无条件覆盖会把用户起的名字冲掉（命名为「睡前锁屏」按确定后当场变回「锁屏」，
                // 列表、每个面板格子、托盘菜单里那个名字一起没）。而只判空又走到了另一头：
                // 编辑器建出来的步骤**必然**已经有名字，于是把一个已有步骤的命令从「搜索选中文字」
                // 改成「清空剪贴板」之后，名字还是「搜索选中文字」——步骤名着一件事、干的是另一件。
                //
                // 判据是「现在这个名字，正好就是**旧命令**的代填名吗」：是就说明它是代填来的，
                // 该跟着刷新；不是就说明人动过手，留着。
                //
                // **旧命令要从 `_original` 取，不是从 `r`。** 这里曾经写的是 `r.Command`，
                // 配上一句「必须在覆盖 r.Command 之前问，那一刻它还是旧值」——而那句话是错的：
                // `r` 是 ConditionProbe() 新造的，那个初始化器压根不设 Command，所以它一直是 ""。
                // 于是后半个条件永远为假，labelWasAuto 坨缩成「名字是不是空」——而编辑器建出来的
                // 步骤必然有名字，于是这段注释声称修好的那个 bug 原样存在：把命令从「搜索选中文字」
                // 改成「清空剪贴板」之后，名字还是「搜索选中文字」。
                //
                // 已知边界：代填时是中文、之后把界面语言切成英文再来编辑，旧代填名对不上，
                // 会被当成用户起的而保留。比现在这个 bug 好，但要彻底解决只能加一个持久标记字段。
                bool labelWasAuto = string.IsNullOrWhiteSpace(r.Label)
                                    || r.Label == StepDisplay.SystemCommandShortLabel(_original.Command);
                r.Command = ComboVal(SysCmdCombo);
                if (labelWasAuto) r.Label = StepDisplay.SystemCommandShortLabel(r.Command);
                // 只给用得上参数的命令存参数：否则「锁屏」步骤的 json 里会躺着一段与它无关的剪贴板文本。
                if (StepDisplay.SystemCommandTakesText(r.Command)) r.Text = SysTextBox.Text;
                if (StepDisplay.SystemCommandTakesLevel(r.Command)) r.Level = Math.Clamp(ParseOr(SysLevelBox.Text, 50), 0, 100);
                break;
            }
            case "text": r.Text = TextBox2.Text; r.Process = StepHelpers.ToProcessName(TextProcessBox.Text); break;
            // 提示语存进 Message、默认值/选项存进 Text——都借既有字段，不为这两个类型新开。
            // 借的是**语义对得上**的字段：Message 一直是「要显示给人看的那句话」，
            // Text 一直是「这一步自带的那段文字」，把类型从「用户输入」改成「消息」时那句提示语还在。
            case "prompt": r.Message = AskTextBox.Text.Trim(); r.Text = AskDefaultBox.Text; break;
            case "choice": r.Message = ChoiceTextBox.Text.Trim(); r.Text = OptionsBox.Text; break;
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

        // 「输出到变量」三种产出类型共用，所以收在 switch 外面——收进各自的 case 就得写三遍。
        // 只给用得上的类型存：否则一条「锁屏」步骤的 json 里会躺着一个它永远不会写的变量名
        //（同上面「只给用得上参数的命令存参数」那条）。
        if (kind is "prompt" or "choice" or "copySelection") r.OutputVar = OutputVarBox.Text.Trim();

        Result = r;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // —— 选择器（浏览/选择进程/捕获按键）：取消则不动原值 ——
    private void BrowseTarget_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) TargetBox.Text = p; }
    private void BrowseWorkDir_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFolder(this) is string p) WorkDirBox.Text = p; }
    private void BrowseOnYes_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) OnYesTargetBox.Text = p; }
    // 图标可以是一张图片，也可以是任意 exe（借它的图标）——所以用通用的文件选择器，
    // 不去限定「只能选图片」：「借 Chrome 的图标给我这个打开网址的步骤」是很自然的用法。
    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    { if (IconPickerWindow.Pick(this, _icon) is string s) { _icon = s; RefreshIcon(); } }

    // 预览走生产代码那条解析（Core.PanelIcon），不在这儿另写一份近似逻辑——
    // 否则「编辑器里预览的」和「面板上画的」迟早分家，而那种分歧只有用户先被坑一次才发现。
    private void RefreshIcon()
    {
        if (IconBtn == null) return;   // 构造途中控件还没建好，Load 会再叫一次
        var spec = PanelIcon.Resolve(_icon, ComboVal(KindCombo), TargetBox.Text, AltTargetsBox.Text);
        IconVisual.Fill(IconBtn, spec, 24, (System.Windows.Media.Brush)FindResource("BrushPaper"));
        IconBtn.ToolTip = _icon.Length > 0 ? _icon : Strings.Get("Icon_None");
    }

    // 只有**窗口动作**这一处提供「当前窗口」：它是这个字段唯一支持那个记号的地方。
    // 「发送文本到」和「已在运行则激活」要的是一个具体程序，给它们那一条只会让人以为也能填。
    private void PickProcess_Click(object sender, RoutedEventArgs e)
    { if (Pickers.PickProcess(this, withCurrentWindow: true) is string p) ProcessBox.Text = p; }
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
    public static LaunchStep? Edit(Window? owner, LaunchStep? step, string kind, IReadOnlyList<ActionGroup> groups)
    {
        // Level 是个**共用的数值位**：音量用 0-100（模型默认 50 是给它挑的），
        // 「等剪贴板变化」借了同一个位置但量纲是秒（1-60，意图默认 5，见 ClampWaitSeconds）。
        // 于是新建一个「等剪贴板」步骤时，模型默认的 50 会原样通过夹取（它只改 <1 的），
        // 预填 50、保存 50——一个默认就等 50 秒的步骤，而这个文件里「5」这个意图值出现了两次。
        // 在新建这一处按类型给初值：改夹取会同时改掉音量那一档的语义。
        var s = step ?? new LaunchStep
        {
            Kind = kind,
            Action = kind == "volume" ? "set" : (kind == "window" ? "close" : (kind == "mouse" ? "down" : "")),
            Level = kind == "waitClipboard" ? StepHelpers.ClampWaitSeconds(0) : new LaunchStep().Level,
        };
        var dlg = new StepEditorWindow(s, groups) { Owner = owner };
        // owner 为 null 时必须自己找位置、自己保证看得见：这两个入口都能从**面板**进来
        // （面板一格右键 → 编辑；主窗口收在托盘时 App 传的就是 null），而面板此刻已经自己关掉了。
        // 不兜底的话，WindowStartupLocation=CenterOwner 没有 owner 可居中、Topmost 也没置，
        // 于是模态开在任意位置、压在所有窗口后面 —— 用户看到面板消失、什么都没出现，
        // 而一个模态框正拦着后续操作。BrandDialog 与面板管理器都已经这么兜了（同款说明见那两处）。
        if (owner == null) { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; dlg.Topmost = true; }
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
