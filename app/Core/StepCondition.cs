namespace Clockwork.Core;

// 步骤时间条件（仅星期 / 仅 N 点前）是否满足。
// 顶层启动清单与动作组内步骤统一遵守——不满足即跳过。缺失字段按「无限制」处理。
public static class StepCondition
{
    // .NET DayOfWeek（周日=0）→ ISO（周一=1..周日=7）。
    public static int IsoDayOfWeek(DateTime d)
    {
        var iso = (int)d.DayOfWeek;
        return iso == 0 ? 7 : iso;
    }

    // 哨兵解析：hour<0 / isoDay<=0 约定为「取当前」，统一在此解析为具体值，避免各调用点各写一份。
    // now 由调用方传入——开机序列用可注入时钟(nowDt)、动作组用 DateTime.Now，各保留自己的时间源。
    public static (int hour, int isoDay) ResolveSentinels(int hour, int isoDay, DateTime now)
        => (hour < 0 ? now.Hour : hour, isoDay <= 0 ? IsoDayOfWeek(now) : isoDay);

    public static bool IsSatisfied(LaunchStep s, int currentHour, int currentIsoDay)
    {
        if (currentIsoDay <= 0) currentIsoDay = IsoDayOfWeek(DateTime.Now);
        if (s.OnlyBefore8 && currentHour >= StepHelpers.BeforeHour(s)) return false;
        var days = s.Days ?? new();
        if (days.Count > 0 && !days.Contains(currentIsoDay)) return false;
        return true;
    }
}
