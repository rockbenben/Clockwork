using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class LaunchSequenceTests
{
    public LaunchSequenceTests() => StopSignal.Clear();
    private static DateTime Now() => new DateTime(2026, 7, 15, 10, 0, 0);
    private static StepMark Ok(LaunchStep s) => new StepMark("✓", 0, 0);

    [Fact]
    public void Runs_enabled_steps_and_counts()
    {
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "app", Label = "a" }, new LaunchStep { Kind = "app", Label = "b" } } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(2, r.Summary.Total);
        Assert.Equal(0, r.Summary.Fail);
        Assert.False(r.Summary.Stopped);
        Assert.Equal(2, r.LogLines.Count);
    }

    [Fact]
    public void Counts_warnings()
    {
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "app", Label = "x" } } };
        var r = LaunchSequence.Run(c, false, 10, 3, _ => new StepMark("⚠ boom", 1, 0), Now);
        Assert.Equal(1, r.Summary.Fail);
    }

    [Fact]
    public void Expands_group_step()
    {
        var g = new ActionGroup { Id = "g1", Name = "组", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute" }, new LaunchStep { Kind = "keys", Combo = "Win+D" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "g1" } }, ActionGroups = new() { g } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(2, r.Summary.Total);   // 组内 2 个非 message 子步骤
    }

    [Fact]
    public void Group_not_found_warns()
    {
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "nope" } } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(1, r.Summary.Fail);
    }

    [Fact]
    public void Disabled_group_skipped()
    {
        var g = new ActionGroup { Id = "g1", Name = "组", Enabled = false, Steps = new() { new LaunchStep { Kind = "volume", Action = "mute" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "g1" } }, ActionGroups = new() { g } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(0, r.Summary.Total);
    }

    [Fact]
    public void Loop_repeat_runs_n_times()
    {
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "app", Label = "a", Repeat = 3 } } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(3, r.Summary.Total);
    }

    [Fact]
    public void Stop_before_run_yields_stopped()
    {
        StopSignal.Request();
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "app", Label = "a" } } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.True(r.Summary.Stopped);
        Assert.Equal(0, r.Summary.Total);
        StopSignal.Clear();
    }

    [Fact]
    public void Group_message_substep_skipped_in_expansion()
    {
        var g = new ActionGroup { Id = "g1", Name = "组", Steps = new() { new LaunchStep { Kind = "message", Message = "q" }, new LaunchStep { Kind = "volume", Action = "mute" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "g1" } }, ActionGroups = new() { g } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(1, r.Summary.Total);   // message 跳过，仅 mute 计入
    }
}
