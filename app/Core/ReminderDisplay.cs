using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 提醒行的显示文案。文案取自 resx，随 UI 文化中/英切换。
public static class ReminderDisplay
{
    public static string TimeLabel(Reminder r)
    {
        string baseLabel;
        if (r.Trigger == "startup")
        {
            baseLabel = r.StartupHourMode switch
            {
                "before" => Strings.Lf("Time_Startup_Before", r.StartupHour),
                "after" => Strings.Lf("Time_Startup_After", r.StartupHour),
                _ => Strings.Get("Time_Startup"),
            };
        }
        else baseLabel = r.Time;
        // 循环运行后缀：一眼看出这条不是一天一响。两种基础文案（登录时短语 / 原始 HH:mm）都要追加。
        if (r.IntervalMinutes > 0)
        {
            baseLabel += " " + Strings.Lf("Time_LoopEvery", r.IntervalMinutes);
            if (!string.IsNullOrWhiteSpace(r.IntervalUntil)) baseLabel += " " + Strings.Lf("Time_LoopUntil", r.IntervalUntil);
        }
        return baseLabel;
    }

    public static string PeriodLabel(Reminder r) => r.RecurType switch
    {
        "everyNDays" => Strings.Lf("Period_EveryNDays", r.IntervalDays),
        "monthly" => Strings.Lf("Period_Monthly", r.MonthlyDay),
        "once" => Strings.Lf("Period_Once", r.OnceDate ?? "").Trim(),   // 无日期=今天：只显示「仅一次」
        _ => StepDisplay.DaysLabel(r.Days),
    };

    public static string TextSummary(Reminder r) => StepHelpers.Ellipsis(Regex.Replace(r.Message ?? "", @"\r?\n", " "));
}
