using Clockwork.Core;
using Xunit;

public class DefaultSamplesTests
{
    // 样例是照着改的模板，不该在用户还没看过一眼时就替他动电脑。
    [Fact]
    public void All_samples_start_unticked()
    {
        Assert.All(RootConfig.DefaultLaunchSteps(), s => Assert.False(s.Enabled));
        Assert.All(RootConfig.DefaultReminders(), r => Assert.False(r.Enabled));
    }

    [Fact]
    public void Launch_sample_kinds_are_known_to_the_engine()
    {
        var known = new HashSet<string>(StepDisplay.StepKinds);
        Assert.All(RootConfig.DefaultLaunchSteps(), s => Assert.Contains(s.Kind, known));
    }

    // 条件执行的演示挪到了「打开常用网站」这条：仅工作日，是自证的，不用额外解释。
    [Fact]
    public void Weekday_condition_is_demonstrated_once()
    {
        var withDays = RootConfig.DefaultLaunchSteps().Where(s => s.Days.Count > 0).ToList();
        Assert.Single(withDays);
        Assert.Equal(new List<int> { 1, 2, 3, 4, 5 }, withDays[0].Days);
    }

    // 聊天软件那条靠 windowStyle 一步挂后台，不再"开完再最小化"。
    [Fact]
    public void Chat_sample_starts_minimized()
    {
        Assert.Contains(RootConfig.DefaultLaunchSteps(),
            s => s.Kind == "app" && s.WindowStyle == "minimized");
    }

    // 原来三条提醒全是「每天/工作日」，每月周期一条样例都没有。
    [Fact]
    public void Reminder_samples_cover_monthly()
    {
        Assert.Equal(4, RootConfig.DefaultReminders().Count);
        Assert.Contains(RootConfig.DefaultReminders(),
            r => r.RecurType == "monthly" && r.MonthlyDay == 1);
    }

    // 动作组页首次不再是空白——原来用户得点开「新增 ▾」才知道有模板可用。
    // 与启动清单/提醒样例相反，这两个组是启用的：动作组永远不会自动执行，
    // 只在托盘/热键/引用处被主动触发，所以「不该替用户动电脑」在这里不成立。
    [Fact]
    public void First_run_presets_two_runnable_groups()
    {
        var groups = RootConfig.Default().ActionGroups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.True(g.Enabled));
        Assert.All(groups, g => Assert.NotEmpty(g.Steps));
        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    // 不带热键：开箱就占用一个全局组合键太越界，而且两个预置组会互相抢。
    [Fact]
    public void Preset_groups_claim_no_hotkey()
    {
        Assert.All(RootConfig.Default().ActionGroups, g => Assert.Equal("", g.Hotkey));
    }

    // 每次调用各自新 id：与 ActionGroupTemplates 同一条契约（运行态/引用都按 id 做键）。
    [Fact]
    public void Preset_groups_get_fresh_ids_each_call()
    {
        var a = RootConfig.Default().ActionGroups;
        var b = RootConfig.Default().ActionGroups;
        for (int i = 0; i < a.Count; i++) Assert.NotEqual(a[i].Id, b[i].Id);
    }
}
