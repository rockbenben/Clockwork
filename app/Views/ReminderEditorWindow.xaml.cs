using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
using static Clockwork.Views.EditorUi;

namespace Clockwork.Views;

// 提醒编辑器。
public partial class ReminderEditorWindow : Window
{
    private readonly Reminder _original;   // 保留 UI 未暴露的字段（启用态）
    public Reminder? Result { get; private set; }

    public ReminderEditorWindow(Reminder r, IReadOnlyList<ActionGroup> groups)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _original = r;

        // 触发下拉 = 时间 + 登录 + 七个事件。事件项的标签键与 ReminderDisplay.EventLabel 用同一套
        // Ed_Trig_<Id 首字母大写>，别在两处各维护一张表。
        var trigItems = new[] { (Strings.Get("Ed_Trig_Time"), "time"), (Strings.Get("Ed_Trig_Startup"), "startup") }
            .Concat(ReminderEvent.All.Select(id => (Strings.Get("Ed_Trig_" + char.ToUpperInvariant(id[0]) + id.Substring(1)), id)))
            .ToArray();
        FillCombo(TrigCombo, trigItems, r.Trigger);
        FillCombo(SModeCombo, new[] { (Strings.Get("Ed_SMode_Any"), "any"), (Strings.Get("Ed_SMode_Before"), "before"), (Strings.Get("Ed_SMode_After"), "after") }, r.StartupHourMode);
        FillCombo(RecurCombo, new[] { (Strings.Get("Ed_Rec_Daily"), "daily"), (Strings.Get("Ed_Rec_EveryN"), "everyNDays"), (Strings.Get("Ed_Rec_Monthly"), "monthly"), (Strings.Get("Ed_Rec_Once"), "once") }, r.RecurType);
        var groupItems = new[] { (Strings.Get("Ed_Group_None"), "") }.Concat(groups.Select(g => (g.Name, g.Id))).ToArray();
        FillCombo(SilentCombo, groupItems, r.SilentGroupId);
        FillCombo(OnYesTypeCombo, new[] { (Strings.Get("Ed_OnYes_None"), "none"), (Strings.Get("Ed_OnYes_Run"), "run"), (Strings.Get("Ed_OnYes_Url"), "url"), (Strings.Get("Ed_OnYes_Group"), "group") }, r.OnYes.Type == "sound" ? "run" : r.OnYes.Type);
        FillCombo(OnYesGroupCombo, groupItems, r.OnYes.Type == "group" ? r.OnYes.Target : "");

        TimeBox.Text = r.Time;
        SHourBox.Text = r.StartupHour.ToString();
        SWithinBox.Text = r.StartupWithinMinutes.ToString();
        IntervalBox.Text = r.IntervalDays.ToString();
        AnchorBox.Text = r.AnchorDate;
        MonthlyBox.Text = r.MonthlyDay.ToString();
        MsgBox.Text = r.Message;
        SpeakChk.IsChecked = r.Speak;
        OnYesTargetBox.Text = r.OnYes.Target;
        AutoBox.Text = r.PopupTimeoutSeconds.ToString();
        RepeatBox.Text = r.RepeatMinutes.ToString();
        RepeatUntilBox.Text = r.RepeatUntil;
        DelayBox.Text = r.DelaySeconds.ToString();
        RandomBox.Text = r.RandomDelaySeconds.ToString();
        GraceBox.Text = r.GraceMinutes.ToString();
        CatchUpChk.IsChecked = r.CatchUpIfMissed;
        LoadDays(r.Days, Day1, Day2, Day3, Day4, Day5, Day6, Day7);
        OnceDateBox.Text = r.OnceDate;
        IdleBox.Text = r.IdleMinutes.ToString();
        BatteryBox.Text = r.BatteryPercent.ToString();
        LoopMinBox.Text = r.IntervalMinutes.ToString();
        LoopUntilBox.Text = r.IntervalUntil;
        // 动作二选一是 SilentGroupId 的视图：非空=静默运行动作组。
        if (string.IsNullOrWhiteSpace(r.SilentGroupId)) ActPopup.IsChecked = true; else ActSilent.IsChecked = true;

        UpdateTrig(); UpdateSMode(); UpdateRecur(); UpdateOnYes(); UpdateAction();

        // 「进阶」折叠条标题实时等于催促/循环的行为句（见 ReminderDisplay.AdvancedSummary 的理由）。
        // 配过任意一项进阶的自动展开——别把已有配置藏没；全默认的收起，新用户只看到一句「到点触发一次」。
        foreach (var tb in new[] { RepeatBox, RepeatUntilBox, LoopMinBox, LoopUntilBox })
            tb.TextChanged += (_, _) => UpdateAdvHeader();
        UpdateAdvHeader();
        AdvExp.IsExpanded = r.RepeatMinutes > 0 || r.IntervalMinutes > 0 || r.DelaySeconds > 0
            || r.RandomDelaySeconds > 0 || r.CatchUpIfMissed || r.PopupTimeoutSeconds > 0
            || r.GraceMinutes != 5;   // 5 是模型默认；改过宽限也算「配过进阶」
        UpdateCrossingHint();
    }

    // 「仅一次」是否真的生效：事件与「登录时」都不看周期（编辑器里那块也是藏着的），残留在下拉里的
    // "once" 只是往返保真的历史值。凡按 once 分支的地方（清循环、日期校验、标题句）都必须用这个有效值——
    // 用原始下拉值曾造出「标题说触发一次、保存后每 30 分钟跑一轮」的口是心非（评审 #3）。
    // 判据走 UsesRecurrence 而非 !IsEvent：后者把「登录时」错划进受周期约束的一侧，于是登录时下
    // 那个明明可见的循环行填了值会在保存时被静默清零。
    private bool IsEffectiveOnce()
        => ComboVal(RecurCombo) == "once" && ReminderEvent.UsesRecurrence(ComboVal(TrigCombo));

    // 行为句里的分钟数取「保存后真正生效」的值：静默任务的催促不生效（FireReminder 固定返回 ok）、
    // 「仅一次」保存时强制清循环——照抄这两条口径，标题才不会许一个保存后不存在的行为。
    private void UpdateAdvHeader()
    {
        int rm = ActSilent.IsChecked == true ? 0 : ParseOr(RepeatBox.Text, 0, min: 0);
        int im = IsEffectiveOnce() ? 0 : ParseOr(LoopMinBox.Text, 0, min: 0);
        AdvExp.Header = Strings.Lf("Ed_AdvHeader",
            ReminderDisplay.AdvancedSummary(rm, RepeatUntilBox.Text.Trim(), im, LoopUntilBox.Text.Trim()));
    }

    // 交叉口指路：「登录时 + 静默动作组」与启动清单是同一件事的两条路，指路牌只在真走到路口时出现。
    private void UpdateCrossingHint()
        => Vis(LoginSilentHint, ComboVal(TrigCombo) == "startup" && ActSilent.IsChecked == true);

    // 换触发方式要同时重算两组显隐：UpdateTrig 管触发本身那几行，UpdateRecur 管周期那四行 + 循环行。
    // 只调其一会留下上一种触发的残行（换到「解锁时」还挂着「每月第几天」）。
    // 标题句也要跟着换：触发决定「仅一次」是否生效（IsEffectiveOnce），不刷会沿用上一种触发的口径。
    private void Trig_Changed(object sender, SelectionChangedEventArgs e) { UpdateTrig(); UpdateRecur(); UpdateCrossingHint(); UpdateAdvHeader(); }
    private void SMode_Changed(object sender, SelectionChangedEventArgs e) => UpdateSMode();
    private void Recur_Changed(object sender, SelectionChangedEventArgs e) { UpdateRecur(); UpdateAdvHeader(); }   // 「仅一次」清循环 → 标题跟着变
    private void OnYesType_Changed(object sender, SelectionChangedEventArgs e) => UpdateOnYes();
    private void Action_Changed(object sender, RoutedEventArgs e) { UpdateAction(); UpdateAdvHeader(); UpdateCrossingHint(); }   // 静默下催促不生效 → 标题跟着变

    // 动作二选一：选哪边就只显示哪边的控件——填了不生效的假控件不出现（与「点是后」死控件同一条思路）。
    // MsgBox 是唯一的例外，两边都留：静默任务虽然不弹窗，这段文本仍是它**唯一的名字**——
    // 引用的动作组被删/被禁用时，Warn_SilentGroupMissing/Disabled 拿它当标识（App.xaml.cs 的静默分支），
    // 藏掉它就会让新建的静默任务留空，那条告警于是变成「提醒「」引用的动作组不存在」，
    // 而它恰恰在后台例程悄悄停摆时才出现——正是最需要认出是哪一条的时刻。
    private void UpdateAction()
    {
        bool silent = ActSilent.IsChecked == true;
        Vis(SilentRow, silent);
        Vis(SpeakChk, !silent);
        Vis(OnYesRow, !silent);
        Vis(AutoRow, !silent);
        Vis(NagRow, !silent);
    }

    private void UpdateTrig()
    {
        var t = ComboVal(TrigCombo);
        bool time = t == "time";
        bool ev = ReminderEvent.IsEvent(t);
        Vis(TimeRow, time);
        // 宽限 / 错过必补都是「到点那一刻机器不在」的补救，只有按时间触发才谈得上：
        // 事件是发生即触发，机器不在就根本没发生过，没有需要补的东西。
        Vis(GraceRow, time); Vis(CatchUpRow, time);
        Vis(StartupRow, t == "startup");
        // 周期整块只对「按时间」有意义：登录时与事件都不看 recurType，留着等于让人配一个不起作用的东西。
        // 判据走共享谓词而不是就地写 == "time"：这次的整个 bug 类就是「编辑器藏了、运行期照旧过滤」的口径分家。
        Vis(PeriodRow, ReminderEvent.UsesRecurrence(t));
        Vis(IdleRow, t == "idle");
        Vis(BatteryRow, t == "lowBattery");
        Vis(EventDaysRow, ev);
    }
    private void UpdateSMode()
    {
        bool show = ComboVal(SModeCombo) != "any";
        Vis(SHourBox, show); Vis(SHourLbl, show);
    }
    private void UpdateRecur()
    {
        // 登录时/事件触发下周期整块已被 UpdateTrig 隐藏；这里按 recurType 分的显隐对它们没有意义，
        // 四个 Row 直接收掉后返回，避免两处显隐逻辑打架（谁后跑谁说了算）。两个例外：
        //   · DaysRow —— 星期过滤对事件触发仍然有效（「工作日解锁时打卡」），故事件下保留、登录时收掉；
        //   · LoopRow —— 循环运行（每 N 分钟）不属于「周期」，两者下都依旧有效（见 TimeLabel 的循环后缀）。
        var t = ComboVal(TrigCombo);
        if (!ReminderEvent.UsesRecurrence(t))   // 同 UpdateTrig：周期判据只此一处，别就地写 == "time"
        {
            Vis(DaysRow, ReminderEvent.IsEvent(t));
            Vis(IntervalRow, false); Vis(MonthlyRow, false); Vis(OnceRow, false);
            Vis(LoopRow, true);
            return;
        }
        var r = ComboVal(RecurCombo);
        Vis(DaysRow, r == "daily"); Vis(IntervalRow, r == "everyNDays"); Vis(MonthlyRow, r == "monthly");
        Vis(OnceRow, r == "once");
        Vis(LoopRow, r != "once");   // 仅一次与循环运行互斥：once 隐藏循环行，保存时强制 IntervalMinutes=0
    }
    private void UpdateOnYes()
    {
        var type = ComboVal(OnYesTypeCombo);
        bool group = type == "group";
        // 选「无」时目标框和「浏览…」什么也控制不了——留着等于在表单里摆两个假控件，
        // 看着能填、填了不生效。没有可填的东西就别显示。
        bool target = type != "group" && type != "none";
        Vis(OnYesGroupCombo, group); Vis(OnYesTargetBox, target); Vis(OnYesBrowseBtn, target);
    }

    // HH:mm 校验用引擎共享 pattern；先经 FormatTimeHHmm 规整，"9:00" 这类单位数小时输入不再被拒（保存时同样走规整）。
    private static readonly Regex HhmmRe = new(ReminderEngine.HhmmPattern);

    // 日期校验必须用真实解析而非「形状」正则：^\d{4}-\d{2}-\d{2}$ 会放行 2026-02-30 / 2026-13-45 这种
    // 日历上不存在的日期，而 IsRecurrenceDueToday 对解析不了的日期一律按「今天」兜底——
    // 于是「每 7 天」静默退化成每天、「仅一次」当场就弹。拦在输入处，别让打错一位变成天天弹。
    public static bool IsDate(string s) => DurationText.TryParseDate(s, out _);

    // —— 选择器：取消则不动原值 ——
    private void PickAnchor_Click(object sender, RoutedEventArgs e) { if (Pickers.PickDate(this, AnchorBox.Text) is string d) AnchorBox.Text = d; }
    private void PickOnce_Click(object sender, RoutedEventArgs e) { if (Pickers.PickDate(this, OnceDateBox.Text) is string d) OnceDateBox.Text = d; }
    private void BrowseOnYes_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) OnYesTargetBox.Text = p; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var trig = ComboVal(TrigCombo);
        if (trig == "time" && !HhmmRe.IsMatch(DurationText.FormatTimeHHmm(TimeBox.Text))) { Warn(Strings.Get("Val_TimeFormat")); return; }
        var repUntil = RepeatUntilBox.Text.Trim();
        if (repUntil != "" && !HhmmRe.IsMatch(DurationText.FormatTimeHHmm(repUntil))) { Warn(Strings.Get("Val_RepeatUntil")); return; }
        var anchor = AnchorBox.Text.Trim();
        if (anchor != "" && !IsDate(anchor)) { Warn(Strings.Get("Val_Anchor")); return; }

        // recur 提前到这里声明：仅一次日期校验、循环行互斥都要用到，避免和 brief 草稿里的 recurSel 重复一个同义变量。
        // 事件触发下原样往返（曾经强改成 daily，评审 #4 指出那会静默改写用户数据：把月度提醒切去事件试了一下
        // 再切回来，周期就丢了）。「残留 once 会被误分支」的风险改由运行时守卫兜住：
        // Decide/ShouldDisableAfterOnce/编辑器口径全部按 IsEvent 忽略不适用的周期——保存永远保真，运行时各自把关。
        var recur = ComboVal(RecurCombo);
        // 「仅一次」的日期校验只在它真生效时做：事件触发下 once 是隐藏的历史值，为它弹「日期已过」是无中生有。
        bool effOnce = IsEffectiveOnce();   // 与折叠条标题同一个判据，别在这儿再手写一遍
        var onceDate = OnceDateBox.Text.Trim();
        if (effOnce && onceDate != "" && !IsDate(onceDate)) { Warn(Strings.Get("Val_OnceDate")); return; }
        // 日期已过：提示但放行——不替用户做主（与项目校验风格一致，只拦真正会崩的）。
        if (effOnce && onceDate != ""
            && DateTime.TryParseExact(onceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var od)
            && od.Date < DateTime.Today)
            Warn(Strings.Get("Val_OnceDatePast"));
        var loopUntil = LoopUntilBox.Text.Trim();
        // 专用文案：弹窗模式下催促行与循环行同时可见、各有一个 HH:mm 截止框，复用「重复直到」的提示
        // 会把用户指向另一个框去改（他改了也不会好）。
        if (loopUntil != "" && !HhmmRe.IsMatch(DurationText.FormatTimeHHmm(loopUntil))) { Warn(Strings.Get("Val_LoopUntil")); return; }
        // 选了静默却没挑组=没配动作，必须拦：静默任务到点悄悄什么都不做是最难察觉的配置错误。
        if (ActSilent.IsChecked == true && string.IsNullOrWhiteSpace(ComboVal(SilentCombo))) { Warn(Strings.Get("Val_SilentNoGroup")); return; }

        // 解析失败/越界回退默认。
        int iv = ParseOr(IntervalBox.Text, 1, min: 1);
        int md = ParseOr(MonthlyBox.Text, 1, min: 1);
        int sh = ParseOr(SHourBox.Text, 9, min: 0, max: 23);
        int sw = ParseOr(SWithinBox.Text, 10, min: 0);
        int au = ParseOr(AutoBox.Text, 0, min: 0);
        int rm = ParseOr(RepeatBox.Text, 0, min: 0);
        int ds = ParseOr(DelayBox.Text, 0, min: 0);
        int rd = ParseOr(RandomBox.Text, 0, min: 0);
        int gm = ParseOr(GraceBox.Text, 5, min: 0);

        // 只在周期真生效时补起算日：给「登录时」补一个起算日，等于把它钉成每 N 天才认一次登录，
        // 而那块 UI 对它是隐藏的，用户既看不见也改不掉。
        if (recur == "everyNDays" && anchor == "" && ReminderEvent.UsesRecurrence(trig))
            anchor = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var days = CollectDays(Day1, Day2, Day3, Day4, Day5, Day6, Day7);

        var yType = ComboVal(OnYesTypeCombo);
        var yTarget = yType == "group" ? ComboVal(OnYesGroupCombo) : OnYesTargetBox.Text;
        if (yType == "group" && string.IsNullOrWhiteSpace(yTarget)) yType = "none";   // 选了「组」却没选具体组=没配动作，存成 none（免得点「是」啥也不干）

        Result = new Reminder
        {
            Trigger = trig,
            Time = DurationText.FormatTimeHHmm(TimeBox.Text),
            Days = days,
            RecurType = recur,
            IntervalDays = iv,
            MonthlyDay = md,
            StartupHourMode = ComboVal(SModeCombo),
            StartupHour = sh,
            StartupWithinMinutes = sw,
            Message = MsgBox.Text,
            Speak = SpeakChk.IsChecked == true,
            OnYes = new OnYes { Type = yType, Target = yTarget },
            GraceMinutes = gm,
            CatchUpIfMissed = CatchUpChk.IsChecked == true,
            DelaySeconds = ds,
            RandomDelaySeconds = rd,
            RepeatMinutes = rm,
            RepeatUntil = DurationText.FormatTimeHHmm(RepeatUntilBox.Text),
            AnchorDate = anchor,
            PopupTimeoutSeconds = au,
            SilentGroupId = ActSilent.IsChecked == true ? ComboVal(SilentCombo) : "",
            // effOnce 而非 recur=="once"：事件触发下 LoopRow 可见、once 只是隐藏的历史值，按原始值清会吞掉刚填的循环。
            IntervalMinutes = effOnce ? 0 : ParseOr(LoopMinBox.Text, 0, min: 0),
            IntervalUntil = DurationText.FormatTimeHHmm(LoopUntilBox.Text),
            OnceDate = onceDate,
            IdleMinutes = ParseOr(IdleBox.Text, 10, min: 1),
            BatteryPercent = ParseOr(BatteryBox.Text, 20, min: 1, max: 100),
            Enabled = _original.Enabled,   // 保留启用/禁用态：编辑提醒不应把用户关掉的提醒又打开
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static void Warn(string m) => BrandDialog.Warn(null, "Clockwork", m);

    public static Reminder? Edit(Window owner, Reminder? reminder, IReadOnlyList<ActionGroup> groups)
    {
        var dlg = new ReminderEditorWindow(reminder ?? new Reminder(), groups) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
