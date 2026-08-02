using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.ViewModels;
using Xunit;

public class GroupRowVmSummaryTests
{
    private static GroupRowVm Row(ActionGroup g) => new(g, () => { });

    private static ActionGroup Group(params LaunchStep[] steps)
        => new() { Name = "g", Steps = steps.ToList() };

    // 空组：建了组忘了加步骤时，触发它什么都不会发生——列表必须一眼看得出来。
    [Fact]
    public void Empty_group_shows_placeholder()
    {
        Assert.Equal(Strings.Get("Group_Empty"), Row(Group()).Summary);
    }

    [Fact]
    public void Three_steps_are_joined_without_ellipsis()
    {
        var g = Group(
            new LaunchStep { Kind = "system", Command = "lockScreen" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
            new LaunchStep { Kind = "volume", Action = "mute" });
        var s = Row(g).Summary;

        // 整串比对而不是逐条 Contains：分隔符「 · 」与步骤顺序都是列表可读性的一部分，
        // 只断言「包含」的话，把分隔符改成「, 」或把顺序倒过来都照样绿。
        Assert.Equal(string.Join(" · ", g.Steps.Select(StepDisplay.StepSummary)), s);
        Assert.DoesNotContain("…", s);
    }

    // 超过 3 步只显示前 3 步 + 省略号：窄列里塞不下整组，但要让人知道后面还有。
    [Fact]
    public void More_than_three_steps_truncates_with_ellipsis()
    {
        var g = Group(
            new LaunchStep { Kind = "system", Command = "lockScreen" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "system", Command = "showDesktop" });
        var s = Row(g).Summary;

        Assert.EndsWith("…", s);
        Assert.DoesNotContain(StepDisplay.StepSummary(g.Steps[3]), s);
    }

    [Fact]
    public void Hotkey_label_is_the_combo_or_blank()
    {
        Assert.Equal("Ctrl+Alt+F", Row(new ActionGroup { Hotkey = "Ctrl+Alt+F" }).HotkeyLabel);
        Assert.Equal("", Row(new ActionGroup { Hotkey = "" }).HotkeyLabel);
    }
}
