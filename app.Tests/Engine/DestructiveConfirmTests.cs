using System.IO;
using System.Text.RegularExpressions;
using Xunit;

// **会丢东西的系统命令都必须经 confirmDestructive 门控。**
//
// 这条测不了行为：跑一次「清空回收站」就真的把文件删了，跑一次「关机」就真的关机。
// 所以照 EmptyPanelCtaTests 的先例改在源码上钉——crude，但它挡的东西是真的：
// 少接一道确认不会报错、不会有任何征兆，只会在某个人误点一下之后才被发现，而那时已经没有回头路。
//
// 清空回收站是这一组里唯一**不可逆丢数据**的：注销/重启/关机只是打断你，重开就回来了；
// 而且它传的是 SHERB_NOCONFIRMATION——系统自己那道确认也被关了，不接这一道就完全没有拦截。
public class DestructiveConfirmTests
{
    private static string Source()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!.FullName, "app", "Engine", "SystemCommands.cs"));
    }

    // 每条命令的 case 到下一个 case 之间，必须出现 confirmDestructive。
    [Theory]
    [InlineData("signOut")]
    [InlineData("restart")]
    [InlineData("shutdown")]
    [InlineData("emptyRecycleBin")]
    public void A_destructive_command_asks_first(string command)
    {
        var src = Source();
        int at = src.IndexOf($"case \"{command}\":", System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"找不到 {command} 的分支");
        int next = src.IndexOf("case \"", at + 8, System.StringComparison.Ordinal);
        var body = next > at ? src[at..next] : src[at..];
        Assert.Contains("confirmDestructive", body);
    }

    // 而回收站那一条还有个附加要求：**确认要排在「查过、确实有东西」之后**。
    // 反过来的话，一个本来就空的回收站也会弹一句「确定清空吗」——问的是一件不会发生的事，
    // 问多了人就开始闭眼点确定，那道确认也就白设了。
    [Fact]
    public void The_recycle_bin_only_asks_when_there_is_something_to_lose()
    {
        var src = Source();
        int at = src.IndexOf("case \"emptyRecycleBin\":", System.StringComparison.Ordinal);
        var body = src[at..(src.IndexOf("case \"", at + 8, System.StringComparison.Ordinal))];
        int queried = body.IndexOf("queriedEmpty =", System.StringComparison.Ordinal);
        int asked = body.IndexOf("confirmDestructive", System.StringComparison.Ordinal);
        Assert.True(queried >= 0 && asked > queried, "确认必须排在数条目之后");
        // 而且是「没空才问」，不是无条件问
        Assert.Matches(new Regex(@"!queriedEmpty\s*&&\s*confirmDestructive"), body);
    }
}
