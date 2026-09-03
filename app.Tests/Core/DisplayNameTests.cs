using Clockwork.Core;
using Xunit;

// 没起名的「运行程序」步骤，列表里显示什么。
//
// 从前显示整条 Target，而那一列右侧截断——开始菜单里的快捷方式前缀全都一样，
// 截出来是一屏「C:\Users\...\AppData\Roaming\Microsoft\Wind…」，能区分它们的那一截正好被切掉。
public class DisplayNameTests
{
    [Theory]
    // 用户给的原例：开始菜单里的快捷方式
    [InlineData(@"C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\repo-radar.lnk", "repo-radar")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad")]
    [InlineData(@"D:\Work\report.docx", "report")]
    [InlineData("notepad.exe", "notepad")]              // 裸程序名
    [InlineData("notepad", "notepad")]                  // 连扩展名都没有
    [InlineData(@"D:\Work", "Work")]                    // 打开文件夹
    [InlineData(@"D:\Work\", "Work")]                   // 尾部分隔符不该让叶子变成空串
    [InlineData(@"D:\Work\deploy.ps1", "deploy")]
    [InlineData(@"C:\x\report.2024.docx", "report.2024")]   // 只脱最后一节扩展名
    public void A_path_shows_its_leaf(string target, string want)
        => Assert.Equal(want, LaunchTarget.DisplayName(target));

    // 网址原样：它本身就可读，砍成一个词反而丢信息。
    // 判据是「冒号前 ≥2 个字符」而不是「有 ://」：没有斜杠的协议（mailto: / ms-settings: / steam:）
    // 是真实存在的写法，实测漏判会把 mailto:someone@example.com 的 .com 当扩展名剥掉。
    [Theory]
    [InlineData("https://github.com/rockbenben")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ms-settings:display")]
    [InlineData("steam://run/440")]
    public void A_url_stays_whole(string url)
        => Assert.Equal(url, LaunchTarget.DisplayName(url));

    // 退化输入不能返回空串——列表里那会是一行**没有任何内容**的行，比路径更糟。
    [Theory]
    [InlineData(@"D:\", @"D:\")]            // 盘根：没有叶子可取（也验证盘符没被当成协议）
    [InlineData(".gitignore", ".gitignore")] // 整个名字都是「扩展名」
    public void Degenerate_paths_still_say_something(string target, string want)
        => Assert.Equal(want, LaunchTarget.DisplayName(target));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_nothing_out(string? target)
        => Assert.Equal("", LaunchTarget.DisplayName(target));

    // 引号与环境变量走的是 NormalizeTarget 那一套（资源管理器「复制文件地址」给的就带引号）。
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\"", "app")]
    [InlineData("%WINDIR%\\notepad.exe", "notepad")]
    public void It_normalizes_first(string target, string want)
        => Assert.Equal(want, LaunchTarget.DisplayName(target));

    // 起了名字就用名字——这条回落只在名字为空时生效。
    [Fact]
    public void A_named_step_keeps_its_name()
    {
        var s = new LaunchStep { Kind = "app", Target = @"C:\x\repo-radar.lnk", Label = "仓库雷达" };
        Assert.Contains("仓库雷达", StepDisplay.StepSummary(s));
    }

    [Fact]
    public void An_unnamed_step_falls_back_to_the_leaf()
    {
        var s = new LaunchStep { Kind = "app", Target = @"C:\x\y\z\repo-radar.lnk", Label = "" };
        var summary = StepDisplay.StepSummary(s);
        Assert.Contains("repo-radar", summary);
        Assert.DoesNotContain(@"C:\x\y\z", summary);   // 整条路径不该再出现在列表里
    }
}
