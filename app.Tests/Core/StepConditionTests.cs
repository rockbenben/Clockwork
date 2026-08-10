using Clockwork.Core;
using Xunit;

public class StepConditionTests
{
    [Fact]
    public void No_constraints_always_true()
        => Assert.True(StepCondition.IsSatisfied(new LaunchStep(), 23, 3));

    [Fact]
    public void OnlyBefore8_blocks_after_hour()
    {
        var s = new LaunchStep { OnlyBefore8 = true }; // BeforeHour 默认 8
        Assert.True(StepCondition.IsSatisfied(s, 7, 3));
        Assert.False(StepCondition.IsSatisfied(s, 8, 3));
        Assert.False(StepCondition.IsSatisfied(s, 9, 3));
    }

    [Fact]
    public void OnlyBefore8_respects_custom_beforeHour()
    {
        var s = new LaunchStep { OnlyBefore8 = true, BeforeHour = 10 };
        Assert.True(StepCondition.IsSatisfied(s, 9, 3));
        Assert.False(StepCondition.IsSatisfied(s, 10, 3));
    }

    [Fact]
    public void OnlyBefore8_minute_precision()   // 阈值 08:30，按分钟比较（不再只整点）
    {
        var s = new LaunchStep { OnlyBefore8 = true, BeforeHour = 8, BeforeMinute = 30 };
        Assert.True(StepCondition.IsSatisfied(s, 8, 3, 15));    // 08:15 < 08:30 → 满足
        Assert.True(StepCondition.IsSatisfied(s, 8, 3, 29));    // 08:29 < 08:30 → 满足
        Assert.False(StepCondition.IsSatisfied(s, 8, 3, 30));   // 08:30 = 阈值 → 不满足
        Assert.False(StepCondition.IsSatisfied(s, 8, 3, 45));   // 08:45 > 08:30 → 不满足
        Assert.True(StepCondition.IsSatisfied(s, 7, 3, 59));    // 07:59 < 08:30 → 满足
    }

    [Fact]
    public void OnlyBefore8_minute_defaults_to_zero()   // 只给小时的老调用 = 「N:00 前」，行为不变
    {
        var s = new LaunchStep { OnlyBefore8 = true, BeforeHour = 8 };  // 08:00
        Assert.True(StepCondition.IsSatisfied(s, 7, 3));    // currentMinute 默认 0 → 07:00 < 08:00
        Assert.False(StepCondition.IsSatisfied(s, 8, 3));   // 08:00 = 阈值
    }

    [Fact]
    public void Days_filter_matches_iso_day()
    {
        var s = new LaunchStep { Days = new() { 1, 2, 3, 4, 5 } };
        Assert.True(StepCondition.IsSatisfied(s, 12, 5));   // 周五
        Assert.False(StepCondition.IsSatisfied(s, 12, 6));  // 周六
    }

    [Fact]
    public void Empty_days_means_every_day()
        => Assert.True(StepCondition.IsSatisfied(new LaunchStep { Days = new() }, 12, 7));

    [Theory]
    [InlineData(2026, 7, 13, 1)]  // 周一
    [InlineData(2026, 7, 19, 7)]  // 周日
    public void IsoDayOfWeek_maps_sunday_to_7(int y, int m, int d, int iso)
        => Assert.Equal(iso, StepCondition.IsoDayOfWeek(new DateTime(y, m, d)));

    // —— 「仅 N 点后」——
    [Fact]
    public void OnlyAfter_blocks_before_threshold()
    {
        var s = new LaunchStep { OnlyAfter = true, AfterHour = 18, AfterMinute = 30 };
        Assert.False(StepCondition.IsSatisfied(s, 18, 3, 29));
        Assert.True(StepCondition.IsSatisfied(s, 18, 3, 30));   // 含阈值那一分钟——与「仅 N 前」不含正好互补
        Assert.True(StepCondition.IsSatisfied(s, 23, 3));
        Assert.False(StepCondition.IsSatisfied(s, 9, 3));
    }

    // 两条同时开 = 交集（09:00 后且 18:00 前 = 上班时段），不是「或」。
    [Fact]
    public void OnlyBefore_and_OnlyAfter_intersect()
    {
        var s = new LaunchStep { OnlyAfter = true, AfterHour = 9, OnlyBefore8 = true, BeforeHour = 18 };
        Assert.False(StepCondition.IsSatisfied(s, 8, 3));
        Assert.True(StepCondition.IsSatisfied(s, 9, 3));
        Assert.True(StepCondition.IsSatisfied(s, 17, 3, 59));
        Assert.False(StepCondition.IsSatisfied(s, 18, 3));
        Assert.False(StepCondition.IsSatisfied(s, 22, 3));
    }

    // —— 环境条件（进程 / 电源 / 路径）：探针注入，不碰真实机器 ——
    private static StepEnv Env(bool procRunning = false, bool onAc = true, bool pathExists = false)
        => new(_ => procRunning, () => onAc, _ => pathExists);

    [Fact]
    public void IfProcess_running_and_not_running()
    {
        var want = new LaunchStep { IfProcessMode = "running", IfProcess = "Slack" };
        Assert.True(StepCondition.IsSatisfied(want, 12, 3, 0, Env(procRunning: true)));
        Assert.False(StepCondition.IsSatisfied(want, 12, 3, 0, Env(procRunning: false)));

        var wantNot = new LaunchStep { IfProcessMode = "notRunning", IfProcess = "Slack" };
        Assert.False(StepCondition.IsSatisfied(wantNot, 12, 3, 0, Env(procRunning: true)));
        Assert.True(StepCondition.IsSatisfied(wantNot, 12, 3, 0, Env(procRunning: false)));
    }

    // 选了模式却没填进程名 = 没配完，按「不限」放行，绝不因此把步骤永久锁死。
    [Fact]
    public void IfProcess_without_a_name_is_no_constraint()
        => Assert.True(StepCondition.IsSatisfied(new LaunchStep { IfProcessMode = "running", IfProcess = " " }, 12, 3, 0, Env(procRunning: false)));

    // 进程名要过同一套归一（去目录 + 去 .exe），否则填完整路径的条件永远不成立。
    [Fact]
    public void IfProcess_normalizes_the_name_before_probing()
    {
        string? seen = null;
        var env = new StepEnv(n => { seen = n; return true; }, () => true, _ => true);
        StepCondition.IsSatisfied(new LaunchStep { IfProcessMode = "running", IfProcess = @"C:\Apps\Slack.exe" }, 12, 3, 0, env);
        Assert.Equal("Slack", seen);
    }

    [Fact]
    public void IfPower_ac_and_battery()
    {
        var ac = new LaunchStep { IfPower = "ac" };
        Assert.True(StepCondition.IsSatisfied(ac, 12, 3, 0, Env(onAc: true)));
        Assert.False(StepCondition.IsSatisfied(ac, 12, 3, 0, Env(onAc: false)));

        var bat = new LaunchStep { IfPower = "battery" };
        Assert.False(StepCondition.IsSatisfied(bat, 12, 3, 0, Env(onAc: true)));
        Assert.True(StepCondition.IsSatisfied(bat, 12, 3, 0, Env(onAc: false)));
    }

    [Fact]
    public void IfPathExists_gates_on_the_probe()
    {
        var s = new LaunchStep { IfPathExists = @"E:\backup" };
        Assert.True(StepCondition.IsSatisfied(s, 12, 3, 0, Env(pathExists: true)));
        Assert.False(StepCondition.IsSatisfied(s, 12, 3, 0, Env(pathExists: false)));
        Assert.True(StepCondition.IsSatisfied(new LaunchStep { IfPathExists = "  " }, 12, 3, 0, Env(pathExists: false)));
    }

    // 没配环境条件的步骤一次探针都不该跑——每条开机步骤白白枚举一遍进程表是真实的代价。
    [Fact]
    public void Unconfigured_conditions_never_touch_the_probes()
    {
        int calls = 0;
        var env = new StepEnv(_ => { calls++; return true; }, () => { calls++; return true; }, _ => { calls++; return true; });
        Assert.True(StepCondition.IsSatisfied(new LaunchStep { OnlyAfter = true, AfterHour = 9 }, 12, 3, 0, env));
        Assert.Equal(0, calls);
    }

    // 条件之间是 AND：任一不满足即跳过。
    [Fact]
    public void All_conditions_must_hold()
    {
        var s = new LaunchStep { Days = new() { 1 }, IfPower = "battery", IfPathExists = "X" };
        Assert.True(StepCondition.IsSatisfied(s, 12, 1, 0, Env(onAc: false, pathExists: true)));
        Assert.False(StepCondition.IsSatisfied(s, 12, 2, 0, Env(onAc: false, pathExists: true)));   // 星期不对
        Assert.False(StepCondition.IsSatisfied(s, 12, 1, 0, Env(onAc: true, pathExists: true)));    // 接着电源
        Assert.False(StepCondition.IsSatisfied(s, 12, 1, 0, Env(onAc: false, pathExists: false)));  // 路径不在
    }
}
