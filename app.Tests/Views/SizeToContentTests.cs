using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// **把 DevChecks 顶上那条「只能靠 review 抓」的检查点变成构建期检查。**
//
// 原话（DevChecks.cs 的 --shots 说明里）：
//   · MaxHeight 是 harness 无条件设的，「窗口自己忘了运行时封顶」这类问题截图里永远正常，
//     只能靠 review 抓（检查点：每个 SizeToContent 窗口都要有 FitToWorkArea）。
//
// 也就是说这一类缺陷**结构上**逃得过那 6000 张截图：harness 替窗口把高度封住了，
// 图里一切正常，而真机上窗口会一路长出工作区，最底下那一行（确定 / 取消 / 关闭）
// 落到任务栏底下，点都点不着。人眼 review 是唯一防线，而人眼会忘。
//
// 实测抓到过一次：GestureManagerWindow 是 SizeToContent="Height"，列表行数由用户配了几条手势
// 决定、没有上限，却没有 FitToWorkArea。
public class SizeToContentTests
{
    // 豁免：高度**由构造决定**、压根长不出去的窗口。加进来必须写清为什么，
    // 且下面那条用例会验证豁免本身还成立（别让它变成橡皮图章）。
    private static readonly Dictionary<string, string> Bounded = new()
    {
        // 图标是一张写死的 96 项表、16 列 = 恒定 6 行，长不了。
        ["IconPickerWindow"] = "IconLibrary.Glyphs",
        // 它不封窗口、封的是里面两个 ScrollViewer，且按**目标屏**的工作区算
        // （面板跟着光标走，可能落在另一块屏上，封窗口反而算错那一块）。见 PlaceAtCursor。
        ["QuickPanelWindow"] = "PlaceAtCursor",
    };

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<(string Name, string Xaml, string Code)> SizeToContentWindows()
    {
        var root = Root();
        foreach (var xaml in Directory.EnumerateFiles(Path.Combine(root, "app"), "*.xaml", SearchOption.AllDirectories))
        {
            if (xaml.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
             || xaml.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(xaml);
            if (!text.Contains("SizeToContent=")) continue;
            var cs = xaml + ".cs";
            yield return (Path.GetFileNameWithoutExtension(xaml), text, File.Exists(cs) ? File.ReadAllText(cs) : "");
        }
    }

    [Fact]
    public void Every_size_to_content_window_caps_its_height()
    {
        var missing = SizeToContentWindows()
            .Where(w => !Bounded.ContainsKey(w.Name))
            .Where(w => !w.Code.Contains("FitToWorkArea"))
            .Select(w => w.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0,
            "这些窗口是 SizeToContent 却没有 FitToWorkArea——真机上会长出工作区，最底下那一行点不着："
            + string.Join(", ", missing)
            + "。确实长不出去的话，加进 Bounded 并写明理由。");
    }

    // **XAML 里还要留一个保守的 MaxHeight 兜底。**
    //
    // FitToWorkArea 是在 SourceInitialized 里按窗口所在那块屏探测的，而它**探测失败就直接放手**
    // ——它自己的注释写着「退回 XAML 里那个保守的 MaxHeight，不该因为量不到屏幕就开不了窗」。
    // 那句承诺只有在 XAML 真的写了那个值时才成立：没写的话，探测失败的窗口又变回不封顶，
    // 而探测会失败的场合（受限宿主、无交互桌面、显示器热插拔的中间态、虚拟/远程显示器给出的
    // 退化 rcWork）恰恰与「小屏」高度重合——最需要兜底的那一档正好没有兜底。
    //
    // 值要**保守**（按 1366×768 那一档取），不能图省事写个大数：探测成功时它会被无条件覆盖
    // （WindowSizing 里那句 `window.MaxHeight = cap`），所以大数唯一生效的场合就是探测失败，
    // 而那正是它该管事的时候。写大了等于在兜底路径上把要修的 bug 原样复活。
    [Fact]
    public void Every_size_to_content_window_has_a_conservative_xaml_fallback()
    {
        var missing = SizeToContentWindows()
            .Where(w => !Bounded.ContainsKey(w.Name))
            .Where(w => !Regex.IsMatch(WindowTag(w.Xaml), @"MaxHeight=""\d"))
            .Select(w => w.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0,
            "这些 SizeToContent 窗口的 <Window> 上没有兜底 MaxHeight——FitToWorkArea 探测失败时它们又不封顶了："
            + string.Join(", ", missing));
    }

    // 兜底值必须**够小**：它是给最紧那一档准备的。
    //
    // 这个数不能自己推——权威值就写在 WindowSizing.FitToWorkArea 里：
    // 「实测：1366×768 @150% 的可用高度只有 464」，而 --shots 的档位注释同样把它
    // 列为「最紧的一档」（小本上把缩放调到 150 的人不少）。减去默认的 72 边距 = 392。
    //
    // 曾经写错过一次，错法值得记下来：按 1366×768 **@100%** 算得 728-72≈656。
    // 同一块屏幕、同一个分辨率，只是缩放不同，可用高就差了 264 DIP——
    // 而 656 让七扇窗的兜底值全部“通过”，它们在真正最紧的那一档上一个都装不下。
    // 缩放不是细节，它就是这个数本身。
    [Fact]
    public void The_fallback_caps_fit_the_smallest_supported_screen()
    {
        // 464 (1366×768 @150% 工作区，WindowSizing 实测) - 72 (FitToWorkArea 默认边距)
        const int SmallestUsable = 392;
        var tooTall = new List<string>();
        foreach (var w in SizeToContentWindows())
        {
            var m = Regex.Match(WindowTag(w.Xaml), @"MaxHeight=""(\d+)");
            if (m.Success && int.Parse(m.Groups[1].Value) > SmallestUsable)
                tooTall.Add($"{w.Name}={m.Groups[1].Value}");
        }
        Assert.True(tooTall.Count == 0,
            $"这些兜底值大于 1366×768 那一档能容纳的 {SmallestUsable}，探测失败时兜不住："
            + string.Join(", ", tooTall));
    }

    // 只看 <Window> 自己那一个标签：内部控件上的 MaxHeight（列表限高之类）不是窗口的兜底，
    // 拿整份文件去 grep 会把它们误当成已经封顶。
    private static string WindowTag(string xaml)
    {
        var m = Regex.Match(xaml, @"<Window\b.*?>", RegexOptions.Singleline);
        return m.Success ? m.Value : "";
    }

    // 豁免名单防腐：名单里的窗口若已经不是 SizeToContent（或压根没了），这条会红，
    // 迫使名单跟着代码走，而不是攒成一堆没人敢删的例外。
    [Fact]
    public void The_exemption_list_has_no_stale_entries()
    {
        var actual = SizeToContentWindows().Select(w => w.Name).ToHashSet(StringComparer.Ordinal);
        var stale = Bounded.Keys.Where(k => !actual.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, $"这些窗口已不是 SizeToContent，应从 Bounded 移除：{string.Join(", ", stale)}");
    }

    // 豁免的理由要指向真实存在的东西：写「因为 X 封住了」，X 就得在那个窗口的代码里找得到。
    [Fact]
    public void Each_exemption_names_something_that_exists()
    {
        foreach (var w in SizeToContentWindows())
        {
            if (!Bounded.TryGetValue(w.Name, out var why)) continue;
            Assert.True(w.Code.Contains(why), $"{w.Name} 的豁免理由写着「{why}」，但代码里找不到它");
        }
    }

    // 扫描本身得确实扫到了东西：路径找错、属性改名都会让上面几条静默变成永远绿灯。
    [Fact]
    public void The_scan_actually_finds_windows()
        => Assert.True(SizeToContentWindows().Count() >= 5, "扫到的 SizeToContent 窗口太少，扫描大概坏了");

    // **带系统边框的窗口都要把标题栏染深。**
    //
    // 与上面那条同一类：漏了不崩、不报错，只是那扇窗开出来是浅色标题栏顶着一身深色内容。
    // 而它只在**打开那扇窗**时才看得见，六千张截图里也未必有它（harness 拍的是客户区）。
    // 实测漏过一次：GestureManagerWindow 是唯一没调的那扇，而它和面板管理器平级、并排看格外明显。
    //
    // 判据是「有系统边框」：WindowStyle="None" 的窗口自己画全部外观（快捷面板、气泡、笔迹层），
    // 压根没有标题栏可染，调了也没有意义。
    [Fact]
    public void Every_chrome_bearing_window_darkens_its_titlebar()
    {
        var root = Root();
        var missing = new List<string>();
        foreach (var xaml in Directory.EnumerateFiles(Path.Combine(root, "app"), "*.xaml", SearchOption.AllDirectories))
        {
            if (xaml.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
             || xaml.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(xaml);
            if (!text.Contains("<Window")) continue;                 // 只看窗口，不看字典 / 用户控件
            if (text.Contains("WindowStyle=\"None\"")) continue;      // 自绘外观，没有标题栏
            var cs = xaml + ".cs";
            var code = File.Exists(cs) ? File.ReadAllText(cs) : "";
            if (!code.Contains("DarkWindow.Apply")) missing.Add(Path.GetFileNameWithoutExtension(xaml));
        }
        Assert.True(missing.Count == 0,
            "这些窗口有系统边框却没调 DarkWindow.Apply——开出来是浅色标题栏配深色内容："
            + string.Join(", ", missing.OrderBy(n => n, StringComparer.Ordinal)));
    }
}
