using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class ActionGroupRunnerTests
{
    public ActionGroupRunnerTests() => StopSignal.Clear();

    private static GroupDeps Deps(List<string> ran, Func<LaunchStep, MsgResult>? msg = null, List<string>? onYes = null)
        => new GroupDeps
        {
            Hour = 10,
            IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            ShowMessage = msg ?? (_ => MsgResult.Ok),
            RunOnYes = s => (onYes ?? new()).Add(s.Label),
            Speak = _ => { },
        };

    [Fact]
    public void Runs_all_non_message_steps()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g1", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "1" }, new LaunchStep { Kind = "keys", Combo = "a", Label = "2" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(new[] { "1", "2" }, ran.ToArray());
    }

    [Fact]
    public void Message_no_aborts_group()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g2", Steps = new() { new LaunchStep { Kind = "message", Message = "q", Label = "m" }, new LaunchStep { Kind = "volume", Action = "mute", Label = "after" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran, msg: _ => MsgResult.No));
        Assert.Empty(ran);   // No → 中止，after 不跑
    }

    [Fact]
    public void Message_yes_runs_onYes_and_continues()
    {
        var ran = new List<string>();
        var yes = new List<string>();
        var g = new ActionGroup { Id = "g3", Steps = new() { new LaunchStep { Kind = "message", Message = "q", Label = "m" }, new LaunchStep { Kind = "volume", Action = "mute", Label = "after" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran, msg: _ => MsgResult.Yes, onYes: yes));
        Assert.Equal(new[] { "m" }, yes.ToArray());
        Assert.Equal(new[] { "after" }, ran.ToArray());
    }

    [Fact]
    public void Disabled_and_condition_skipped()
    {
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "g4",
            Steps = new()
            {
                new LaunchStep { Kind = "volume", Action = "mute", Label = "disabled", Enabled = false },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "wrongday", Days = new() { 6 } },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "ok" },
            }
        };
        ActionGroupRunner.RunGroup(g, Deps(ran));   // Hour=10, IsoDay=3(周三)
        Assert.Equal(new[] { "ok" }, ran.ToArray());
    }

    [Fact]
    public void Loop_repeat_runs_step_n_times()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g5", Steps = new() { new LaunchStep { Kind = "keys", Combo = "a", Label = "x", Repeat = 3 } } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(3, ran.Count);
    }

    [Fact]
    public void Throwing_step_is_contained_group_continues()
    {
        // 某步抛异常（如剪贴板被占用）不得中止整组——收工/睡前组里锁屏必须照跑。
        var ran = new List<string>();
        var errors = new List<string>();
        var deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { if (s.Label == "boom") throw new InvalidOperationException("clipboard busy"); ran.Add(s.Label); },
            OnStepError = (s, _) => errors.Add(s.Label),
        };
        var g = new ActionGroup
        {
            Id = "gerr",
            Steps = new()
            {
                new LaunchStep { Kind = "system", Command = "clearClipboard", Label = "boom" },
                new LaunchStep { Kind = "system", Command = "lockScreen", Label = "after" },
            }
        };
        ActionGroupRunner.RunGroup(g, deps);
        Assert.Equal(new[] { "after" }, ran.ToArray());   // 抛异常步骤后面的步骤仍执行
        Assert.Equal(new[] { "boom" }, errors.ToArray());  // 失败被上报（不静默）
    }
}
