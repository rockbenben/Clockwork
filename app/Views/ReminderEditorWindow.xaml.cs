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
        SourceInitialized += (_, _) => Native.DarkTitleBar.Apply(this);
        _original = r;

        FillCombo(TrigCombo, new[] { (Strings.Get("Ed_Trig_Time"), "time"), (Strings.Get("Ed_Trig_Startup"), "startup") }, r.Trigger);
        FillCombo(SModeCombo, new[] { (Strings.Get("Ed_SMode_Any"), "any"), (Strings.Get("Ed_SMode_Before"), "before"), (Strings.Get("Ed_SMode_After"), "after") }, r.StartupHourMode);
        FillCombo(RecurCombo, new[] { (Strings.Get("Ed_Rec_Daily"), "daily"), (Strings.Get("Ed_Rec_EveryN"), "everyNDays"), (Strings.Get("Ed_Rec_Monthly"), "monthly") }, r.RecurType);
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

        UpdateTrig(); UpdateSMode(); UpdateRecur(); UpdateOnYes();
    }

    private void Trig_Changed(object sender, SelectionChangedEventArgs e) => UpdateTrig();
    private void SMode_Changed(object sender, SelectionChangedEventArgs e) => UpdateSMode();
    private void Recur_Changed(object sender, SelectionChangedEventArgs e) => UpdateRecur();
    private void OnYesType_Changed(object sender, SelectionChangedEventArgs e) => UpdateOnYes();

    private void UpdateTrig()
    {
        bool time = ComboVal(TrigCombo) == "time";
        Vis(TimeRow, time); Vis(GraceRow, time); Vis(CatchUpRow, time); Vis(StartupRow, !time);
    }
    private void UpdateSMode()
    {
        bool show = ComboVal(SModeCombo) != "any";
        Vis(SHourBox, show); Vis(SHourLbl, show);
    }
    private void UpdateRecur()
    {
        var r = ComboVal(RecurCombo);
        Vis(DaysRow, r == "daily"); Vis(IntervalRow, r == "everyNDays"); Vis(MonthlyRow, r == "monthly");
    }
    private void UpdateOnYes()
    {
        bool group = ComboVal(OnYesTypeCombo) == "group";
        Vis(OnYesGroupCombo, group); Vis(OnYesTargetBox, !group); Vis(OnYesBrowseBtn, !group);
    }

    // HH:mm 校验用引擎共享 pattern；先经 FormatTimeHHmm 规整，"9:00" 这类单位数小时输入不再被拒（保存时同样走规整）。
    private static readonly Regex HhmmRe = new(ReminderEngine.HhmmPattern);
    private static readonly Regex DateRe = new("^\\d{4}-\\d{2}-\\d{2}$");

    // —— 选择器：取消则不动原值 ——
    private void PickAnchor_Click(object sender, RoutedEventArgs e) { if (Pickers.PickDate(this, AnchorBox.Text) is string d) AnchorBox.Text = d; }
    private void BrowseOnYes_Click(object sender, RoutedEventArgs e) { if (Pickers.BrowseFile(this) is string p) OnYesTargetBox.Text = p; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var trig = ComboVal(TrigCombo);
        if (trig == "time" && !HhmmRe.IsMatch(DurationText.FormatTimeHHmm(TimeBox.Text))) { Warn(Strings.Get("Val_TimeFormat")); return; }
        var repUntil = RepeatUntilBox.Text.Trim();
        if (repUntil != "" && !HhmmRe.IsMatch(DurationText.FormatTimeHHmm(repUntil))) { Warn(Strings.Get("Val_RepeatUntil")); return; }
        var anchor = AnchorBox.Text.Trim();
        if (anchor != "" && !DateRe.IsMatch(anchor)) { Warn(Strings.Get("Val_Anchor")); return; }

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

        var recur = ComboVal(RecurCombo);
        if (recur == "everyNDays" && anchor == "") anchor = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
            SilentGroupId = ComboVal(SilentCombo),
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
