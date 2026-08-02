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

    public static string PeriodLabel(Reminder r)
    {
        // 登录时触发不走周期判定（recurType/days 对它无效），照兜底分支返回「每天」是在陈述一件
        // 不成立的事。本列回答的是「多久一次」，对它的真实答案就是「每次登录」——具体限制
        // （仅 N 点前 / 开机 N 分钟内）已由 TimeLabel 那一列说清。
        if (r.Trigger == "startup") return Strings.Get("Period_EachLogin");
        return r.RecurType switch
        {
            "everyNDays" => Strings.Lf("Period_EveryNDays", r.IntervalDays),
            "monthly" => Strings.Lf("Period_Monthly", r.MonthlyDay),
            "once" => Strings.Lf("Period_Once", r.OnceDate ?? "").Trim(),   // 无日期=今天：只显示「仅一次」
            _ => StepDisplay.DaysLabel(r.Days),
        };
    }

    // 静默任务没有消息文本，照旧只显示 Message 会让整行空白——而「跑哪个组」正是这一行唯一
    // 值得说的事。groups 为 null 时维持旧行为（供不关心动作组的调用点使用）。
    public static string TextSummary(Reminder r, IReadOnlyList<ActionGroup>? groups = null)
    {
        if (!string.IsNullOrWhiteSpace(r.SilentGroupId))
        {
            var g = groups == null ? null : ActionGroupResolver.Resolve(groups, r.SilentGroupId);
            return Strings.Lf("Sum_RunGroup", g?.Name is { Length: > 0 } n ? n : Strings.Get("Sum_Group_None"));
        }
        return StepHelpers.Ellipsis(Regex.Replace(r.Message ?? "", @"\r?\n", " "));
    }
}
