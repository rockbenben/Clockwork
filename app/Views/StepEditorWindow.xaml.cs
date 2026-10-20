using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Clockwork.Core;
using Clockwork.I18n;
using static Clockwork.Views.EditorUi;

namespace Clockwork.Views;

// 统一步骤编辑器：公共字段 + 按类型切换的字段面板。
public partial class StepEditorWindow : Window
{
    private readonly IReadOnlyList<ActionGroup> _groups;
    // 面板语境传进来的一份启动清单：非 null 时类型下拉多一项「引用启动项」（见 StepDisplay.RefKind）。
    // 它的存在本身就是语境开关——清单/组/提醒那些入口不传，这一项连出现的机会都没有。
    private readonly IReadOnlyList<LaunchStep>? _listItems;
    private readonly LaunchStep _original;   // 保留 UI 未暴露的字段（启用态 + app 进阶项），编辑时不丢
    private string _icon = "";
    public LaunchStep? Result { get; private set; }

    public StepEditorWindow(LaunchStep step, IReadOnlyList<ActionGroup> groups, IReadOnlyList<LaunchStep>? listItems = null)
        : this(step, groups, listItems, insideGroup: false) { }

    /// <param name="insideGroup">这一条是加进**某个复杂动作的步骤序列**里的。
    /// 语境决定类型下拉给多少：动作组里给全套，单独一格（面板 / 开机清单 / 手势）只给原子步骤——
    /// 「每个」「跳出」「正则提取」那一族单独放一处没有意义（见 StepDisplay.BlockOnlyKinds）。</param>
    public StepEditorWindow(LaunchStep step, IReadOnlyList<ActionGroup> groups, IReadOnlyList<LaunchStep>? listItems,
                            bool insideGroup)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _groups = groups;
        _listItems = listItems;
        _original = step;

        // 手改过的配置里可能有块内类型，而它恰好是在单步语境里打开的——那种步骤**照样要能编辑**，
        // 否则用户删不掉它、也改不成别的。所以筛的是「新建时能选到的」，不是「打开时看得见的」。
        var offered = StepDisplay.KindsFor(insideGroup).ToList();
        if (!offered.Contains(step.Kind)) offered.Add(step.Kind);

        FillCombo(KindCombo, (listItems != null ? offered.Append(StepDisplay.RefKind) : offered)
                             .Select(k => (StepDisplay.StepKindLabel(k), k)).ToArray(), step.Kind);
        FillCombo(VolActionCombo, new[]
        {
            (Strings.Get("Vol_mute"), "mute"), (Strings.Get("Vol_unmute"), "unmute"), (Strings.Get("Ed_Vol_Set"), "set"),
            (Strings.Get("Vol_micMute"), "micMute"), (Strings.Get("Vol_micUnmute"), "micUnmute"),
        }, step.VolumeAction, StepHelpers.DefaultActionFor("volume"));
        // 下拉项直接由 StepDisplay.MouseActions 生成：动作 id / 文案 / 伪键只此一张表，
        // 加一个动作只改那张表，编辑器、摘要、执行三处自动跟上，不会出现「选得到但跑不了」。
        FillCombo(MouseActCombo, StepDisplay.MouseActions.Select(m => (Strings.Get(m.LabelKey), m.Action)).ToArray(),
                  step.MouseAction, StepHelpers.DefaultActionFor("mouse"));
        FillCombo(WinActionCombo, StepDisplay.WindowActionMap().Select(kv => (kv.Value, kv.Key)).ToArray(),
                  step.WindowAction, StepHelpers.DefaultActionFor("window"));
        // siteMap 的打开方式。三档的 id 与 StepRunner 那支的 mode、StepDisplay.SiteMapActionOf 的
        // 归一结果同一批字面量——三处各写一份的话，改动只落一处，清单上就会写着「定位」而实际打开了文件。
        FillCombo(SiteMapActionCombo, new[]
        {
            (Strings.Get("Val_SiteMapOpen"), "open"),
            (Strings.Get("Val_SiteMapReveal"), "reveal"),
            (Strings.Get("Val_SiteMapNone"), "none"),
        }, StepDisplay.SiteMapActionOf(step));
        // 跳过的两档：判据**不分大小写**（"RUN" 也认），与其它几个下拉的精确匹配不同——
        // 这里是手改 json 最容易写成大写的一处，而「跳过一整段」与「跳过一次运行」是两件事。
        // 它的默认档恰好就是第一项，所以不需要 DefaultActionFor 那一格来兜。
        FillCombo(SkipModeCombo, new[]
        {
            (Strings.Get("Val_SkipBlock"), "block"),
            (Strings.Get("Val_SkipRun"), "run"),
        }, string.Equals(step.SkipMode, "run", StringComparison.OrdinalIgnoreCase) ? "run" : "block");
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
        // 引用目标下拉只在面板语境（拿到清单）时填。失联目标不静默改指（FillRefCombo 的说明）。
        if (_listItems != null) FillRefCombo(RefCombo, _listItems, step.RefId);
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
        TargetBox.TextChanged += (_, _) => { RefreshIcon(); UpdatePathPreview(TargetBox, TargetPreview); };
        // 与 TargetBox 同一条：路径框一变，自动取到的文件/文件夹图标也得跟着变。
        PathBox.TextChanged += (_, _) => { RefreshIcon(); UpdatePathPreview(PathBox, PathPreview); };
        WorkDirBox.TextChanged += (_, _) => UpdatePathPreview(WorkDirBox, WorkDirPreview);
        IfPathBox.TextChanged += (_, _) => UpdatePathPreview(IfPathBox, IfPathPreview);
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
        // 指路同理：清单是空的，「引用启动项」这一类就没有可指的东西——先说清出口。
        Vis(NoRefsHint, _listItems is { Count: 0 });
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
        LevelBox.Text = s.VolumeLevel.ToString();
        ProcessBox.Text = s.Process; SendKeyBox.Text = s.SendKey; WaitWinBox.Text = s.WaitForWindowSeconds.ToString(); PostDelayBox.Text = s.PostWindowDelaySeconds.ToString();
        TextBox2.Text = s.Text; TextProcessBox.Text = s.Process;
        UrlBox.Text = s.Kind == "url" ? s.Target : "";
        PathBox.Text = s.Kind == "path" ? s.Target : "";
        WaitSecondsBox.Text = StepHelpers.ClampWaitSeconds(s.WaitSeconds).ToString();
        // 五个新类型的字段。与上面同一批字段一起载入，缺一个就是「编辑再保存抹掉用户填的值」。
        SiteMapUrlBox.Text = s.Text;
        // siteMap 的打开方式下拉在构造时由 FillCombo 填好并按 s.SiteMapMode 选中，这里不再重设：
        // LoadStep 跑在构造之后，重设等于把 FillCombo 那套「表外值退回第一项」的兜底绕过去。
        DecodeTextBox.Text = s.Text;
        RegexInputBox.Text = s.Text; RegexPatternBox.Text = s.Pattern;
        RegexGroupBox.Text = Math.Max(0, s.RegexGroup).ToString();
        ReplaceInputBox.Text = s.Text; ReplacePatternBox.Text = s.Pattern; ReplaceWithBox.Text = s.Replace;
        WaitKeysBox.Text = s.Text;
        WaitKeySecondsBox.Text = StepHelpers.ClampWaitSeconds(s.WaitKeySeconds).ToString();
        SplitTextBox.Text = s.Text;
        MathExprBox.Text = s.Text;
        EachListCombo.Text = s.Text;
        EachItemBox.Text = s.OutputVar;      // 留空 = 回落成 item
        EachIndexBox.Text = s.Pattern;       // 留空 = 回落成 index
        // 「列表」与「用户输入」共用 Message/Text 两个字段（问句 / 预填），照两行填进去。
        // 两个面板各有各的输入框，但存盘口径与 prompt 一致——换类型时字段不会互相吞掉。
        ListAskBox.Text = s.Message;
        ListDefaultBox.Text = s.Text;
        JoinListBox.Text = s.Text;
        JoinSepBox.Text = s.Separator;
        MessageBox2.Text = s.Message; SpeakChk.IsChecked = s.Speak; ConfirmChk.IsChecked = s.Confirm; OnYesTargetBox.Text = s.OnYes.Target;
        PopupSecondsBox.Text = s.PopupSeconds.ToString();
        DelayBox.Text = s.DelayMs.ToString();
        RepeatBox.Text = StepHelpers.StepRepeat(s).ToString();
        NoteBox.Text = s.Note;
        SysTextBox.Text = s.Text; SysLevelBox.Text = s.Brightness.ToString();
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
        Vis(PanRef, kind == StepDisplay.RefKind && _listItems != null);   // 非面板语境打开一条 step 步骤（手改/导入）：不铺没有目标可选的空白面板
        Vis(PanUrl, kind == "url"); Vis(PanPath, kind == "path"); Vis(PanCopySelection, kind == "copySelection");
        Vis(PanWaitClipboard, kind == "waitClipboard");
        Vis(PanPrompt, kind == "prompt"); Vis(PanChoice, kind == "choice"); Vis(PanList, kind == "list"); Vis(PanJoin, kind == "join");
        Vis(PanBrowserUrl, kind == "browserUrl");
        Vis(PanSiteMap, kind == "siteMap");
        Vis(PanUrlDecode, kind == "urlDecode");
        Vis(PanRegex, kind == "regex");
        Vis(PanReplace, kind == "replace");
        Vis(PanWaitKey, kind == "waitKey");
        Vis(PanSplitLines, kind == "splitLines");
        Vis(PanMath, kind == "math");
        Vis(PanSkip, kind == "skip");
        Vis(PanEach, kind == "each");
        // 「输出到变量」跟着**会产出值的类型**走，不跟着单个面板走——多类共用这一行。
        // 漏一个，那一步就产不出值，而界面上没有任何地方提示过它本该产出：
        // 用户填完了、看着像配好了，跑起来却什么都没发生。
        //
        // **「列表」与「合」必须在这里。** 漏掉它们的症状最难自查——面板画得好好地，
        // 用户把多行填进去、把分隔符也填了，没有任何地方提示「你没地方存这个值」，
        // 而下一步的「每个」只会报「这个名字不是列表」。截图才看得见那一行不见了。
        Vis(PanOutputVar, kind is "prompt" or "choice" or "copySelection"
                           or "browserUrl" or "siteMap" or "urlDecode" or "regex" or "replace"
                           or "waitKey" or "splitLines" or "math" or "list" or "join");
        // 说明里带上当前的变量名，好让「下一步怎么引用它」有个照着抄的样子：
        // 只写「用 {名字} 引用」的话，用户还得自己把名字代进去一次，而那一步正是最容易写错的。
        OutputVarHint.Text = Strings.Lf("Ed_OutputVarHint",
            "{" + (OutputVarBox.Text.Trim().Length > 0 ? OutputVarBox.Text.Trim() : Strings.Get("Ed_OutputVarSample")) + "}");
        Vis(RepeatRow, kind is not ("message" or "prompt" or "choice"));   // 这三类不循环，与 StepHelpers.StepRepeat 同口径
    }

    private void KindCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // **「运行程序」「打开网址」「打开文件或文件夹」三者共用 Target 这一个字段**，只是各有一个输入框。
        // 换类型时要把值搬过去，否则：一个 Kind="app"、Target="C:\Tools\backup.exe" 的步骤，
        // UrlBox/PathBox 载入时是空的（只有对应类型才填），把类型改一下再按确定，
        // 保存那一支直接把目标写成空串——没有校验、没有提示、没有备份，
        // 而本文件开头的规矩是「打开再关上绝不能改变一个步骤」。
        // 搬而不是各自留一份：用户改类型的语义是「同一个东西换个说法」，不是「另填一个」。
        // 只在目标框为空时从另两个框搬，免得把用户在新框里已经改过的内容冲掉。
        var kind = ComboVal(KindCombo);
        var dest = kind == "app" ? TargetBox : kind == "url" ? UrlBox : kind == "path" ? PathBox : null;
        if (dest != null && dest.Text.Trim().Length == 0)
        {
            var source = new[] { TargetBox, UrlBox, PathBox }.FirstOrDefault(b => b != dest && b.Text.Trim().Length > 0);
            if (source != null) dest.Text = source.Text;
        }
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
        // 身份也不能丢：这是面板点击统计（Core.PanelUsageStore）的键。丢一次就是重新发一个，
        // 那个格子改个参数就变成「从未点过」，之前的计数直接归零、且不报错。
        r.Id = _original.Id;

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
            // 与 url 对称：路径也存进 Target 这个共用字段，三个「打开」类型互相改来改去都不丢值。
            case "path": r.Target = PathBox.Text.Trim(); break;
            case "copySelection": break;   // 除了「输出到变量」（下面统一收）没有别的参数
            case "browserUrl": break;      // 同上：只有「输出到变量」
            case "siteMap":
                r.Text = SiteMapUrlBox.Text;
                r.SiteMapMode = StepDisplay.SiteMapActionOf(new LaunchStep { SiteMapMode = ComboVal(SiteMapActionCombo) });
                break;
            case "urlDecode": r.Text = DecodeTextBox.Text; break;
            case "list":
                r.Message = ListAskBox.Text;
                r.Text = ListDefaultBox.Text;
                break;
            case "join":
                r.Text = JoinListBox.Text;
                r.Separator = JoinSepBox.Text;
                break;
            case "regex":
                r.Text = RegexInputBox.Text;
                r.Pattern = RegexPatternBox.Text;
                r.RegexGroup = Math.Max(0, ParseOr(RegexGroupBox.Text, 0));
                break;
            case "replace":
                r.Text = ReplaceInputBox.Text;
                r.Pattern = ReplacePatternBox.Text;
                r.Replace = ReplaceWithBox.Text;
                break;
            case "waitKey":
                r.Text = WaitKeysBox.Text;
                // 0 = 不限时。**照实存 0**，不夹成 1：ClampWaitSeconds 把 0 变成 5 秒，
                // 而「一直等」与「等 5 秒」是两件事，存进去就再也分不出来了。
                r.WaitKeySeconds = ParseOr(WaitKeySecondsBox.Text, 0) <= 0 ? 0 : StepHelpers.ClampWaitSeconds(ParseOr(WaitKeySecondsBox.Text, 0));
                break;
            case "splitLines": r.Text = SplitTextBox.Text; break;
            case "math": r.Text = MathExprBox.Text; break;
            case "skip": r.SkipMode = ComboVal(SkipModeCombo); break;
            case "each":
                r.Text = EachListCombo.Text.Trim();
                r.OutputVar = EachItemBox.Text.Trim();
                r.Pattern = EachIndexBox.Text.Trim();
                break;
            case "waitClipboard": r.WaitSeconds = StepHelpers.ClampWaitSeconds(ParseOr(WaitSecondsBox.Text, 5)); break;
            // 动作存进 Action（与音量/窗口步骤同一个字段的用法），不为它新开配置字段。
            // 点几次/滚几格用共用的「重复次数」，这里不存第二个计数。
            case "mouse": r.MouseAction = ComboVal(MouseActCombo); break;
            case "volume": r.VolumeAction = ComboVal(VolActionCombo); r.VolumeLevel = Math.Clamp(ParseOr(LevelBox.Text, 0), 0, 100); break;
            case "window":
                r.WindowAction = ComboVal(WinActionCombo); r.Process = StepHelpers.ToProcessName(ProcessBox.Text);
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
                if (StepDisplay.SystemCommandTakesLevel(r.Command)) r.Brightness = Math.Clamp(ParseOr(SysLevelBox.Text, 50), 0, 100);
                break;
            }
            case "text": r.Text = TextBox2.Text; r.Process = StepHelpers.ToProcessName(TextProcessBox.Text); break;
            // 提示语存进 Message、默认值/选项存进 Text——都借既有字段，不为这两个类型新开。
            // 借的是**语义对得上**的字段：Message 一直是「要显示给人看的那句话」，
            // Text 一直是「这一步自带的那段文字」，把类型从「用户输入」改成「消息」时那句提示语还在。
            case "prompt": r.Message = AskTextBox.Text.Trim(); r.Text = AskDefaultBox.Text; break;
            case "choice": r.Message = ChoiceTextBox.Text.Trim(); r.Text = OptionsBox.Text; break;
            case "group": r.GroupId = ComboVal(GroupCombo); r.Label = _groups.FirstOrDefault(g => g.Id == r.GroupId)?.Name ?? r.Label; break;
            // 引用清单项：只存 id + 同步一次名字（与 group 同一手）。名字不追求实时——清单里改名后
            // 格子仍显示旧名，点开编辑/悬停摘要会露出真相；换来的是格子的排序、搜索都不依赖解析。
            case "step": r.RefId = ComboVal(RefCombo); r.Label = _listItems?.FirstOrDefault(x => x.Id == r.RefId)?.Label ?? r.Label; break;
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
    private void BrowseTarget_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) TargetBox.Text = PathVariables.ToPortable(p); }
    // 「打开文件或文件夹」一个按钮覆盖两种：先弹文件框，取消了再弹文件夹框——
    // 与条件里「路径存在」的浏览按钮同一个挑法（见 BrowseIfPath_Click）。
    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.BrowseFile(this) is string f) { PathBox.Text = PathVariables.ToPortable(f); return; }
        if (Pickers.BrowseFolder(this) is string d) PathBox.Text = PathVariables.ToPortable(d);
    }
    private void BrowseWorkDir_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFolder(this) is string p) WorkDirBox.Text = PathVariables.ToPortable(p); }
    private void BrowseOnYes_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) OnYesTargetBox.Text = PathVariables.ToPortable(p); }
    // 图标可以是一张图片，也可以是任意 exe（借它的图标）——所以用通用的文件选择器，
    // 不去限定「只能选图片」：「借 Chrome 的图标给我这个打开网址的步骤」是很自然的用法。
    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    { if (IconPickerWindow.Pick(this, _icon) is string s) { _icon = s; RefreshIcon(); } }

    // 预览走生产代码那条解析（Core.PanelIcon），不在这儿另写一份近似逻辑——
    // 否则「编辑器里预览的」和「面板上画的」迟早分家，而那种分歧只有用户先被坑一次才发现。
    private void RefreshIcon()
    {
        if (IconBtn == null) return;   // 构造途中控件还没建好，Load 会再叫一次
        // 三个「打开」类型各有一个目标框：自动图标得从当前类型正在编辑的那个框取，
        // 否则切到「打开文件或文件夹」后预览的还是 TargetBox 里上一个程序的图标。
        var kind = ComboVal(KindCombo);
        var autoTarget = kind switch { "url" => UrlBox.Text, "path" => PathBox.Text, _ => TargetBox.Text };
        var spec = PanelIcon.Resolve(_icon, kind, autoTarget, kind == "app" ? AltTargetsBox.Text : "");
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
        if (Pickers.BrowseFile(this) is string f) { IfPathBox.Text = PathVariables.ToPortable(f); return; }
        if (Pickers.BrowseFolder(this) is string d) IfPathBox.Text = PathVariables.ToPortable(d);
    }

    private void PickTargetVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(TargetBox, TargetVarBtn);
    private void PickPathVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(PathBox, PathVarBtn);
    private void PickWorkDirVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(WorkDirBox, WorkDirVarBtn);
    private void PickIfPathVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(IfPathBox, IfPathVarBtn);
    // 四个新面板的「插入变量」按钮。四条同款写法，与上面那四条是同一个函数的不同入参——
    // 各写一份出来的话，将来给菜单加一项（比如剪贴板历史）就只改得动其中一半。
    private void PickSiteMapUrlVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(SiteMapUrlBox, SiteMapUrlVarBtn);
    private void PickDecodeVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(DecodeTextBox, DecodeVarBtn);
    private void PickRegexInputVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(RegexInputBox, RegexInputVarBtn);
    private void PickReplaceInputVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(ReplaceInputBox, ReplaceInputVarBtn);
    private void PickSplitVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(SplitTextBox, SplitVarBtn);
    private void PickMathVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(MathExprBox, MathVarBtn);
    private void PickListDefaultVar_Click(object sender, RoutedEventArgs e) => PickVar_Click(ListDefaultBox, ListDefaultVarBtn);

    private void PickVar_Click(TextBox targetBox, Button anchorBtn)
    {
        var menu = new ContextMenu();
        bool isZh = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        var folders = PathVariables.GetCommonFolders(isZh);
        var secFolders = new MenuItem { Header = Strings.Get("Var_SecFolders"), Style = (Style)FindResource("MenuHeader") };
        menu.Items.Add(secFolders);

        foreach (var item in folders)
        {
            var mi = new MenuItem
            {
                Header = item.Name,
                InputGestureText = item.Expression,
                ToolTip = item.Resolved
            };
            mi.Click += (_, _) => InsertVar(targetBox, item.Expression);
            menu.Items.Add(mi);
        }

        menu.Items.Add(new Separator());

        var secVars = new MenuItem { Header = Strings.Get("Var_SecEnv"), Style = (Style)FindResource("MenuHeader") };
        menu.Items.Add(secVars);

        var vars = PathVariables.GetSystemVariables(isZh);
        foreach (var item in vars)
        {
            var mi = new MenuItem
            {
                Header = item.Name,
                InputGestureText = item.Expression,
                ToolTip = item.Resolved
            };
            mi.Click += (_, _) => InsertVar(targetBox, item.Expression);
            menu.Items.Add(mi);
        }

        menu.PlacementTarget = anchorBtn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void InsertVar(TextBox box, string text)
    {
        if (box.SelectionLength > 0)
        {
            box.SelectedText = text;
        }
        else if (box.Text.Trim().Length == 0)
        {
            box.Text = text;
        }
        else if (!text.Contains('\\'))
        {
            int idx = box.CaretIndex;
            box.Text = box.Text.Insert(idx, text);
            box.CaretIndex = idx + text.Length;
        }
        else
        {
            box.Text = text;
        }
        box.Focus();
    }

    private static void UpdatePathPreview(TextBox box, TextBlock preview)
    {
        var text = box.Text.Trim();
        if (text.Contains('%'))
        {
            var resolved = PathVariables.Resolve(text, out bool hasUnresolved);
            if (hasUnresolved)
            {
                preview.Text = "⚠️ " + resolved;
                preview.Visibility = Visibility.Visible;
                return;
            }
            if (resolved != text)
            {
                preview.Text = "🔍 " + resolved;
                preview.Visibility = Visibility.Visible;
                return;
            }
        }
        preview.Visibility = Visibility.Collapsed;
    }
    /// <summary>新建一步：<paramref name="kind"/> 决定它是什么、初始值是什么。
    /// 返回编辑后的新步骤（取消 → null）。</summary>
    /// <param name="seed">「常用」里预填好的那一份（非空时编辑器直接拿它当初值），没有就 null。</param>
    /// <param name="listItems">非 null = 面板语境：类型下拉追加「引用启动项」（见 _listItems 的说明）。</param>
    /// <param name="insideGroup">加进某个复杂动作的步骤序列：类型下拉给全套（默认 false = 单独一格，只给原子步骤）。</param>
    public static LaunchStep? Create(Window? owner, string kind, LaunchStep? seed,
                                     IReadOnlyList<ActionGroup> groups,
                                     IReadOnlyList<LaunchStep>? listItems = null, bool insideGroup = false)
    {
        // Level 是个**共用的数值位**：音量用 0-100（模型默认 50 是给它挑的），
        // 「等剪贴板变化」借了同一个位置但量纲是秒（1-60，意图默认 5，见 ClampWaitSeconds）。
        // 于是新建一个「等剪贴板」步骤时，模型默认的 50 会原样通过夹取（它只改 <1 的），
        // 预填 50、保存 50——一个默认就等 50 秒的步骤，而这个文件里「5」这个意图值出现了两次。
        // 在新建这一处按类型给初值：改夹取会同时改掉音量那一档的语义。
        //
        // Action 那一格的初值也走同一处（StepHelpers.DefaultActionFor），
        // 与「换类型」那条路共用——见那处注释：同一个类型在两条路上起手不一样，
        // 差的是「音量」那一条（新建是「设到几成」，换类型曾落到「静音」，因为它是下拉的第一项）。
        var s = seed ?? new LaunchStep { Kind = kind };
        switch (kind)
        {
            case "volume": s.VolumeAction = StepHelpers.DefaultActionFor("volume"); break;
            case "window": s.WindowAction = StepHelpers.DefaultActionFor("window"); break;
            case "mouse": s.MouseAction = StepHelpers.DefaultActionFor("mouse"); break;
            // 等剪贴板的那一格要**意图值 5 秒**，不是模型默认的 50（那是音量那一档的量纲）。
            case "waitClipboard": s.WaitSeconds = StepHelpers.ClampWaitSeconds(0); break;
        }
        return Show(owner, s, groups, listItems, insideGroup);
    }

    /// <summary>编辑已有的一步。类型就是**它自己的类型**，调用方不必（也不该）再说一遍。
    /// 返回编辑后的新步骤（取消 → null）。</summary>
    ///
    /// 从前这一格叫 `Edit(owner, step, kind, groups, …)`，而 kind 那一格只在 step 为 null 时才有意义
    /// （新建）——编辑已有的一步时它**从头到尾没被读过**。于是六个调用点各自写着 `step.Kind`，
    /// 看着像「调用方在指定类型」，实际是「我怕忘了才抄一遍」；而它把「类型由谁说了算」
    /// 这件事说成了两处。两件事一起办：拆成 Create（新建，类型是参数）与 Edit（编辑，类型是步骤自己的）。
    public static LaunchStep? Edit(Window? owner, LaunchStep step,
                                   IReadOnlyList<ActionGroup> groups,
                                   IReadOnlyList<LaunchStep>? listItems = null, bool insideGroup = false)
        => Show(owner, step, groups, listItems, insideGroup);

    private static LaunchStep? Show(Window? owner, LaunchStep step, IReadOnlyList<ActionGroup> groups,
                                    IReadOnlyList<LaunchStep>? listItems, bool insideGroup)
    {
        var dlg = new StepEditorWindow(step, groups, listItems, insideGroup) { Owner = owner };
        // owner 为 null 时自己找位置、自己保证看得见（理由见 EditorUi.ShowModal）。
        return EditorUi.ShowModal(dlg, owner) == true ? dlg.Result : null;
    }
}
