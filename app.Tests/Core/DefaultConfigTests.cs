using Clockwork.Core;
using Xunit;

public class DefaultConfigTests
{
    [Fact]
    public void Default_has_expected_collections()
    {
        var c = RootConfig.Default();
        Assert.Equal(5, c.LaunchSteps.Count);           // 精简后的代表性样例：5 条
        Assert.Equal(3, c.Reminders.Count);             // 精简后的代表性样例：3 条
        Assert.Empty(c.ActionGroups);
        Assert.Equal(30, c.Settings.TickSeconds);
        Assert.Equal("Ctrl+Alt+Q", c.Settings.StopHotkey);
    }

    // 样例是照着改的模板，不该在用户还没看过一眼时就替他动电脑：首启必须什么都不执行。
    [Fact]
    public void Default_samples_are_all_disabled()
    {
        var c = RootConfig.Default();
        Assert.All(c.LaunchSteps, s => Assert.False(s.Enabled));
        Assert.All(c.Reminders, r => Assert.False(r.Enabled));
    }

    // 样例文案必须走 resx：直接返回键名说明键漏加了，非中文用户会在界面上看到 Smp_* 原始键名。
    [Fact]
    public void Default_samples_are_localized()
    {
        var c = RootConfig.Default();
        Assert.All(c.LaunchSteps, s => Assert.DoesNotContain("Smp_", s.Label, StringComparison.Ordinal));
        Assert.All(c.Reminders, r => Assert.DoesNotContain("Smp_", r.Message, StringComparison.Ordinal));
        Assert.All(c.LaunchSteps, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));
        Assert.All(c.Reminders, r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
    }

    [Fact]
    public void Default_first_step_is_mute_only_before_8()
    {
        var s = RootConfig.Default().LaunchSteps[0];
        Assert.Equal("volume", s.Kind);
        Assert.Equal("mute", s.Action);
        Assert.True(s.OnlyBefore8);
    }

    [Fact]
    public void New_launchstep_defaults_match_ps()
    {
        var s = new LaunchStep();
        Assert.True(s.Enabled);
        Assert.Equal(50, s.Level);
        Assert.Equal(8, s.BeforeHour);
        Assert.Equal("{ENTER}", s.SendKey);
        Assert.Equal(1, s.Repeat);
        Assert.Equal("none", s.OnYes.Type);
    }

    [Fact]
    public void New_reminder_gets_unique_id()
    {
        Assert.NotEqual(new Reminder().Id, new Reminder().Id);
        Assert.False(string.IsNullOrWhiteSpace(new Reminder().Id));
    }
}
