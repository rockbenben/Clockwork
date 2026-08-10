using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 提醒行的显示文案。文案取自 resx，随 UI 文化中/英切换。
public static class ReminderDisplay
{
    // 事件触发在「时间」列显示的是事件本身（解锁时 / 唤醒时…）——那一列问的是「什么时候」，
    // 对事件型任务，答案就是那个事件。空闲 / 低电量还带一个阈值，一并写出来，否则两条「空闲时」分不清谁是谁。
    public static string EventLabel(Reminder r) => r.Trigger switch
    {
        "idle" => Strings.Lf("Time_Idle", r.IdleMinutes),
        "lowBattery" => Strings.Lf("Time_LowBattery", r.BatteryPercent),
        _ => Strings.Get("Ed_Trig_" + char.ToUpperInvariant(r.Trigger[0]) + r.Trigger.Substring(1)),
    };

    public static string TimeLabel(Reminder r)
    {
        string baseLabel;
        if (ReminderEvent.IsEvent(r.Trigger)) baseLabel = EventLabel(r);
        else if (r.Trigger == "startup")
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
        // 事件触发同理不看 recurType：它的「多久一次」就是「这件事每发生一次」。
        // 但星期限制仍然有效，所以限了星期就照实显示星期，没限才显示「每次发生」。
        if (ReminderEvent.IsEvent(r.Trigger))
        {
            var d = r.Days ?? new();
            return d.Count is > 0 and < 7 ? StepDisplay.DaysLabel(d) : Strings.Get("Period_OnEvent");
        }
        return r.RecurType switch
        {
            "everyNDays" => Strings.Lf("Period_EveryNDays", r.IntervalDays),
            "monthly" => Strings.Lf("Period_Monthly", r.MonthlyDay),
            "once" => Strings.Lf("Period_Once", r.OnceDate ?? "").Trim(),   // 无日期=今天：只显示「仅一次」
            _ => StepDisplay.DaysLabel(r.Days),
        };
    }

    // 提醒编辑器「进阶」折叠条的标题句：把催促 / 循环这两个最容易混的字段翻译成一句行为描述。
    // 「循环 vs 催促」是全应用最需要一段文档才能讲清的区别（催促=没人理才再喊、确认就停；
    // 循环=到点就跑、确认了下一轮照跑）——与其让新用户读文档，不如让他直接读结果。
    // 参数收原始值而不是 Reminder：编辑器要在用户敲字的当下实时重算，那时对象还没构出来。
    public static string AdvancedSummary(int repeatMinutes, string? repeatUntil, int intervalMinutes, string? intervalUntil)
    {
        var parts = new List<string>();
        if (repeatMinutes > 0)
            parts.Add(string.IsNullOrWhiteSpace(repeatUntil)
                ? Strings.Lf("Adv_Nag", repeatMinutes)
                : Strings.Lf("Adv_NagUntil", repeatMinutes, repeatUntil));
        if (intervalMinutes > 0)
            parts.Add(string.IsNullOrWhiteSpace(intervalUntil)
                ? Strings.Lf("Adv_Loop", intervalMinutes)
                : Strings.Lf("Adv_LoopUntil", intervalMinutes, intervalUntil));
        return parts.Count == 0 ? Strings.Get("Adv_Once") : string.Join(" · ", parts);
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
