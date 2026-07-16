using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 提醒行的显示文案。文案取自 resx，随 UI 文化中/英切换。
public static class ReminderDisplay
{
    public static string TimeLabel(Reminder r)
    {
        if (r.Trigger == "startup")
        {
            return r.StartupHourMode switch
            {
                "before" => Strings.Lf("Time_Startup_Before", r.StartupHour),
                "after" => Strings.Lf("Time_Startup_After", r.StartupHour),
                _ => Strings.Get("Time_Startup"),
            };
        }
        return r.Time;
    }

    public static string PeriodLabel(Reminder r) => r.RecurType switch
    {
        "everyNDays" => Strings.Lf("Period_EveryNDays", r.IntervalDays),
        "monthly" => Strings.Lf("Period_Monthly", r.MonthlyDay),
        _ => StepDisplay.DaysLabel(r.Days),
    };

    public static string TextSummary(Reminder r) => StepHelpers.Ellipsis(Regex.Replace(r.Message ?? "", @"\r?\n", " "));
}
