namespace Clockwork.Core;

// 建启动计划。
// 遍历 launchSteps，跳过未勾选、跳过时间条件不满足的，其余按序收集。
public static class LaunchPlan
{
    public static List<LaunchStep> Build(RootConfig config, int currentHour, int currentIsoDay, int currentMinute = 0)
    {
        if (currentIsoDay <= 0) currentIsoDay = StepCondition.IsoDayOfWeek(DateTime.Now);
        var plan = new List<LaunchStep>();
        foreach (var s in config.LaunchSteps ?? new())
        {
            if (!s.Enabled) continue;
            if (!StepCondition.IsSatisfied(s, currentHour, currentIsoDay, currentMinute)) continue;
            plan.Add(s);
        }
        return plan;
    }
}
