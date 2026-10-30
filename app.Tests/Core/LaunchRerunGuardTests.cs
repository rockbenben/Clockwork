using System.IO;
using Xunit;

// 「重新运行启动清单」这个入口的**安全边界**。
//
// 这颗按钮一度被撤掉：它排在面板底栏第一颗、紧挨着格子阵，而底栏那排全是没文字的图标，
// 认错一格的成本不是「按错了一个快捷键」，而是**把清单里的关窗口、发按键全重做一遍**。
//
// 现在它回到那一排了，因为那道闸换了个地方承担：**跑之前弹一道整清单确认**，
// 把这一次会做哪几件事摊开给你看（见 RunOrchestrator.ConfirmRerunLaunchList），
// 框本身还有 600ms 误触保护，连点两下也点不中「是」。
// 于是位置的风险交给内容去挡，位置换回的是「四颗均分才是一排开关带」与
// 「这个程序最核心的能力不该只剩右键托盘一个二级入口」。
//
// 这份测试钉的就是那个交换：**它必须在**，而且**必须走会弹确认的那条路**。
public class LaunchRerunGuardTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var all = new System.Collections.Generic.List<string> { d!.FullName, "app" };
        all.AddRange(parts);
        return File.ReadAllText(Path.Combine(all.ToArray()));
    }

    // 面板底栏有这一颗（连同停止 / 勿扰 / 打开窗口，四颗均分）。
    [Fact]
    public void The_panel_bottom_bar_offers_the_whole_list_rerun()
        => Assert.Contains("Tray_Rerun", Src("Services", "PanelCoordinator.cs"));

    // **它必须走手动那条路**（boot=false）——手动那条路上才有那道整清单确认。
    // 写成 RunLaunchAsync(true) 或直接调底层编排，就等于绕开确认：面板上点一下即执行。
    [Fact]
    public void The_panel_rerun_goes_through_the_confirmed_path()
        => Assert.Contains("Tray_Rerun\"), TrayGlyph.Rerun, () => _runner.RunLaunchAsync(false)",
                          Src("Services", "PanelCoordinator.cs"));

    // 截图用的样例也得跟上：样例里少一颗，截出来的图就比产品少一颗按钮，
    // 拿它验收面板布局就是拿假图当证据。
    [Fact]
    public void The_screenshot_sample_matches_the_real_panel()
    {
        var src = Src("DevChecks.cs");
        int at = src.IndexOf("var ops = new[]");
        Assert.True(at >= 0, "找不到样例自带操作那一段");
        int end = src.IndexOf("};", at);
        Assert.True(end > at, "样例自带操作那一段没有闭合");
        Assert.Contains("Tray_Rerun", src[at..end]);
    }

    // 能力不止一处：托盘里也在（面板之外还有一条路，不至于一处坏了就彻底没辙）。
    [Fact]
    public void The_tray_still_offers_it()
        => Assert.Contains("Tray_Rerun", Src("TrayIcon.cs"));

    // 手动重跑先问一句；开机那一次不问（那时没人在座，问就是问空气）。
    [Fact]
    public void The_manual_rerun_asks_first_and_the_boot_run_does_not()
    {
        var src = Src("Services", "RunOrchestrator.cs");
        int at = src.IndexOf("public void RunLaunchAsync(bool boot)");
        Assert.True(at >= 0, "找不到 RunLaunchAsync");
        string body = src.Substring(at, Math.Min(1500, src.Length - at));
        Assert.Contains("ConfirmRerunLaunchList", body);
        Assert.Contains("!boot", body);   // 那道闸只挂在手动那条路上
    }

    // 问的时候要摊出清单来——只问一句「确定吗」而不说做什么，等于没问。
    [Fact]
    public void The_confirmation_shows_what_the_list_will_do_again()
        => Assert.Contains("LaunchPreview.Build", Src("Services", "RunOrchestrator.cs"));

    // 取消之后要把并发闸放回去：否则这一次的闸永远留在 1，「重跑清单」从此点不动。
    [Fact]
    public void Cancelling_the_confirmation_releases_the_concurrency_gate()
    {
        var src = Src("Services", "RunOrchestrator.cs");
        int at = src.IndexOf("ConfirmRerunLaunchList()", src.IndexOf("public void RunLaunchAsync(bool boot)"));
        Assert.True(at >= 0, "找不到那道确认");
        string after = src.Substring(at, Math.Min(400, src.Length - at));
        Assert.Contains("Interlocked.Exchange(ref _launchRunning, 0)", after);
    }
}
