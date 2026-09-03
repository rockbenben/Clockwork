using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Xunit;

// **无人值守的路径上不许弹确认框。**
//
// 开机清单是登录时自己跑的，那条路上没有人。而把会话掲掉的那三条（注销 / 重启 / 关机）
// 都要先过 confirmDestructive，而 App.ConfirmDestructive 是 BrandDialog.Confirm(owner: null)
// —— 一个置顶模态。于是开机清单里放一条「关机」会在登录几秒后弹出一个框，
// 把整条清单卡在那儿等人来点，而急停键只在步骤之间被检查、解不开它。
//
// **清空回收站不在这三条里。** 它要问（有人时），但没人时照跑——它是用户主动配进
// 清单的日常自动化，2.7.1 起就这么跑。曾经把它也归进「没人就不跑」，
// 靠的就是这段注释里那句错话（说开机清单里的「清空回收站」会弹框）——main 上那个 case
// 压根不查 confirmDestructive，它从来不弹。参见 SystemCommands.RequiresHuman。
//
// **不要把 emptyRecycleBin 加回下面那条 Theory。** 它现在不再被拦，真调下去就会
// 清空跑测试这台机器的回收站。它的行为只能用判据断言（见
// Emptying_the_recycle_bin_still_runs_unattended）。
//
// 判据是 confirmDestructive == null：调用方传 null 就是在说「这条路上没有可以问的人」，
// 与 prompt/choice 那一支（Warn_AskNoUi）同一个道理、同一种交代法。
// 也不能静默当作「用户点了取消」：那会在 run.log 里记一个 ✓，而事情压根没做。
public class UnattendedDestructiveTests
{
    private static LaunchStep Sys(string command) => new() { Kind = "system", Command = command };

    [Theory]
    [InlineData("signOut")]
    [InlineData("restart")]
    [InlineData("shutdown")]
    public void Destructive_system_command_is_skipped_when_there_is_nobody_to_ask(string command)
    {
        // confirmDestructive: null = 无人值守。真调下去会注销/重启/关机，所以这条用例
        // 恰恰依赖那道守卫先返回——它要是失效了，测试机就会被关掉，这算是最硬的断言。
        var r = StepRunner.InvokeStepAction(Sys(command), null, selfPaths: []);
        Assert.True(r.HasWarning);
        Assert.Equal(Strings.Get("Warn_DestructiveNoUi"), r.Warning);
        Assert.NotEqual("Warn_DestructiveNoUi", r.Warning);   // 键真的被翻出来了
    }

    [Fact]
    public void The_skip_shows_up_as_a_warning_in_the_run_log_not_a_tick()
    {
        var mark = StepRunner.RunStepMark(Sys("shutdown"), null, selfPaths: []);
        Assert.Equal(1, mark.Fail);                       // 记成失败/警告，不是 ✓
        Assert.StartsWith("⚠", mark.Mark);
        Assert.NotEqual("✓", mark.Mark);
    }

    [Fact]
    public void IsDestructive_covers_exactly_the_four_gated_commands()
    {
        Assert.True(SystemCommands.IsDestructive("signOut"));
        Assert.True(SystemCommands.IsDestructive("restart"));
        Assert.True(SystemCommands.IsDestructive("shutdown"));
        Assert.True(SystemCommands.IsDestructive("emptyRecycleBin"));
        Assert.False(SystemCommands.IsDestructive("lockScreen"));
        Assert.False(SystemCommands.IsDestructive("openSettings"));
        Assert.False(SystemCommands.IsDestructive(null));
        Assert.False(SystemCommands.IsDestructive(""));
    }

    // 「有人在时该不该问」与「没人在时能不能跑」是两个问题，差别就是清空回收站。
    //
    // 曾经合成一个（硬闸直接拿 IsDestructive 去拦），于是把一件 2.7.1 起就能用的
    // 自动化改成了永不跑：开机清单里的「清空回收站」从此只在 run.log 里留一个 ⚠。
    // 这两条把那次回归钉死：回收站要问（有人时），但不能因为没人就不做。
    [Fact]
    public void Emptying_the_recycle_bin_still_runs_unattended()
    {
        Assert.True(SystemCommands.IsDestructive("emptyRecycleBin"));    // 有人在时仍然要问
        Assert.False(SystemCommands.RequiresHuman("emptyRecycleBin"));   // 但没人时照跑
    }

    // 把会话掲掉的那三条反过来：没人时就是不能跑。
    [Theory]
    [InlineData("signOut")]
    [InlineData("restart")]
    [InlineData("shutdown")]
    public void Session_ending_commands_need_a_human(string id)
    {
        Assert.True(SystemCommands.RequiresHuman(id));
        Assert.True(SystemCommands.IsDestructive(id));
    }

    [Fact]
    public void RequiresHuman_is_a_strict_subset_of_IsDestructive()
    {
        foreach (var id in new[] { "signOut", "restart", "shutdown", "emptyRecycleBin",
                                   "lockScreen", "openSettings", "", null })
            if (SystemCommands.RequiresHuman(id))
                Assert.True(SystemCommands.IsDestructive(id),
                    $"{id} 说「没人不能跑」却不在「要问」名单里——那它有人时也不会问，两头都漏");
    }

    // **名单防漂**：IsDestructive 与 switch 里真正门控的那几支必须是同一份。
    // 加一条新的破坏性命令、只在 switch 里加了 confirmDestructive 而忘了这张名单，
    // 那条命令就会在开机路径上重新弹出模态——这条用例把那件事变成红灯。
    [Fact]
    public void IsDestructive_matches_the_commands_actually_gated_in_the_switch()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var src = File.ReadAllText(Path.Combine(d!.FullName, "app", "Engine", "SystemCommands.cs"));

        // switch 里的门控写法一律是 confirmDestructive(Strings.Get("Sys_<id>"))
        var gated = Regex.Matches(src, @"confirmDestructive\(Strings\.Get\(""Sys_([A-Za-z][A-Za-z0-9]*)""\)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(gated);   // 正则写坏了就不是绿灯，是这里先红

        foreach (var id in gated)
            Assert.True(SystemCommands.IsDestructive(id),
                $"switch 里 {id} 走了 confirmDestructive，但 IsDestructive 说它不是破坏性的——" +
                "开机清单上它会重新弹出一个没人能点的模态");
    }
}
