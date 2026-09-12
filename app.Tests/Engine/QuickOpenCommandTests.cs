using System;
using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// 一键直达作为「系统命令」登记进既有下拉表的那几条约定。
// 识别规则本身在 SmartOpenTests 里钉死；这里只验它真的接进了系统命令这一档，
// 且是无参数命令（编辑器不该露出参数框）。
public class QuickOpenCommandTests
{
    [Fact]
    public void It_is_in_the_system_command_dropdown()
    {
        var map = StepDisplay.SystemCommandMap();
        var item = Assert.Single(map, kv => kv.Key == "quickOpen");
        Assert.NotEqual("Sys_quickOpen", item.Value);
        Assert.False(string.IsNullOrWhiteSpace(item.Value));
    }

    // 一键直达不吃参数：识别什么全看选区。编辑器据此收起参数行。
    [Fact]
    public void It_takes_no_text_or_level_argument()
    {
        Assert.False(StepDisplay.SystemCommandTakesText("quickOpen"));
        Assert.False(StepDisplay.SystemCommandTakesLevel("quickOpen"));
    }

    // 标签尾部括号（「…（网址/文件/…/注册表）」）在 88px 面板格子上要被剥掉。
    [Fact]
    public void The_short_label_strips_the_trailing_aside()
    {
        var shortened = StepDisplay.SystemCommandShortLabel("quickOpen");
        Assert.DoesNotContain("(", shortened);
        Assert.DoesNotContain("（", shortened);
    }

    // 识别不了时抛的消息里要带原文——否则 toast 只有一句「无法识别」，用户不知道自己选中了什么。
    // 这里只走纯判定那一支（不碰剪贴板），垃圾输入在打开任何东西之前就抛了。
    [Fact]
    public void Unrecognized_selection_throws_with_the_text_in_the_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SystemCommands.QuickOpenText("这只是一句普通的话 nopenopenope"));
        Assert.Contains("nopenopenope", ex.Message);
    }

    [Fact]
    public void Empty_selection_throws_too()
        => Assert.Throws<InvalidOperationException>(() => SystemCommands.QuickOpenText("   "));
}
