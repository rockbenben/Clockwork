using System.Linq;
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

    [Fact]
    public void Nested_group_expands_recursively()
    {
        var inner = new ActionGroup { Id = "in", Name = "内", Steps = new() { new LaunchStep { Kind = "keys", Combo = "a" } } };
        var outer = new ActionGroup { Id = "out", Name = "外", Steps = new() { new LaunchStep { Kind = "group", GroupId = "in", Repeat = 3 }, new LaunchStep { Kind = "volume", Action = "mute" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "out" } }, ActionGroups = new() { outer, inner } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(4, r.Summary.Total);   // 内组 1 步 × 引用 3 次 + mute 1
        Assert.False(r.Summary.Stopped);
    }

    [Fact]
    public void Nested_cycle_skipped_with_warning()
    {
        // A→B→A：手改 json 造出的环。沿途访问集挡住回边，记 1 条告警（fail 计 1），其余照跑。
        var a = new ActionGroup { Id = "a", Name = "A", Steps = new() { new LaunchStep { Kind = "group", GroupId = "b" } } };
        var b = new ActionGroup { Id = "b", Name = "B", Steps = new() { new LaunchStep { Kind = "group", GroupId = "a" }, new LaunchStep { Kind = "keys", Combo = "x" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "a" } }, ActionGroups = new() { a, b } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(1, r.Summary.Fail);        // 环告警计 fail
        Assert.Equal(2, r.Summary.Total);       // keys 执行 + 环告警行计入 total
    }

    [Fact]
    public void Sibling_duplicate_references_both_run()
    {
        // 同级引用同一组两次：不是环，各跑一遍。
        var inner = new ActionGroup { Id = "in", Name = "内", Steps = new() { new LaunchStep { Kind = "keys", Combo = "a" } } };
        var outer = new ActionGroup { Id = "out", Name = "外", Steps = new() { new LaunchStep { Kind = "group", GroupId = "in" }, new LaunchStep { Kind = "group", GroupId = "in" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "out" } }, ActionGroups = new() { outer, inner } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(2, r.Summary.Total);
    }

    [Fact]
    public void Group_level_repeat_applies_at_boot()
    {
        var g = new ActionGroup { Id = "g1", Name = "组", Repeat = 3, Steps = new() { new LaunchStep { Kind = "volume", Action = "mute" } } };
        var c = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "group", GroupId = "g1" } }, ActionGroups = new() { g } };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(3, r.Summary.Total);   // 组自身 3 轮
    }

    [Fact]
    public void Budget_truncates_runaway_expansion()
    {
        // 单步 ×999 × 6 条 = 5994 > 5000：截停 + 告警行，不再执行后续。DelayMs=0 必写——默认 100 会让这条测试真睡 500 秒。
        var steps = Enumerable.Range(0, 6).Select(_ => new LaunchStep { Kind = "keys", Combo = "a", Repeat = 999, DelayMs = 0 }).ToList();
        var c = new RootConfig { LaunchSteps = steps };
        var r = LaunchSequence.Run(c, false, 10, 3, Ok, Now);
        Assert.Equal(RunBudget.MaxRunSteps, r.Summary.Total);
        Assert.Contains(r.LogLines, l => l.Contains("5000"));
    }
}
