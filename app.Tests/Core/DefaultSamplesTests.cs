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
}
