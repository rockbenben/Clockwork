using Clockwork.Core;
using Xunit;

public class DefaultConfigTests
{
    [Fact]
    public void Default_has_expected_collections()
    {
        var c = RootConfig.Default();
        Assert.Equal(11, c.LaunchSteps.Count);          // Get-DefaultLaunchSteps 共 11 条
        Assert.Equal(5, c.Reminders.Count);             // Get-DefaultReminders 共 5 条
        Assert.Empty(c.ActionGroups);
        Assert.Equal(30, c.Settings.TickSeconds);
        Assert.Equal("Ctrl+Alt+Q", c.Settings.StopHotkey);
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
