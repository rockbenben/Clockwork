using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Xunit;

// **挑错一个图标码位不会报错。** 私用区码位在源码里就是一串十六进制，编译器不管、
// 运行时不抛、日志里没有——错了只有用眼睛看那一屏才发现得了。
//
// 错法有两种，这条测试只管得住一种：
//   码位在字体里**不存在** → 渲染成空心方框。机械可查，就是这条测试做的事。
//   码位存在但**语义不对** → 理直气壮地画出一个无关的图标。只有人眼能判断。
// 第二种更常见也更隐蔽：表圈那颗手势按钮一度写的是 0xF126，实测 segmdl2.ttf 里它有字形，
// 画出来是一个光秃秃的圆环。所以这条测试挡不住那一次——改图标之后仍然要跑 `--shots` 看一眼。
//
// 即便如此它也值得存在：不存在的码位是唯一「连截图评审都可能放过」的那种（一个方框混在
// 一屏图标里并不显眼），而它又是纯机械可判的。
//
// 收集方式是扫源码而不是维护一张清单：清单会漂，而新增一个图标的人不会想到来更新它。
public class GlyphCoverageTests
{
    // Segoe MDL2 Assets。Win10 起随系统内置，但精简 SKU / Server 上可能没有——
    // 那时跳过而不是红：这条测的是「我们挑的码位对不对」，不是「这台机器装没装字体」。
    private const string FontPath = @"C:\Windows\Fonts\segmdl2.ttf";

    // SystemCommands.cs 里的 0xF170 / 0xFFFF 是 Win32 消息常量，不是字形，扫描要绕开。
    private static readonly string[] NotGlyphFiles = { "SystemCommands.cs" };

    private static DirectoryInfo RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!;
    }

    /// <summary>源码里出现的所有私用区码位 → 它出现在哪些文件。</summary>
    private static SortedDictionary<int, SortedSet<string>> Collect()
    {
        var found = new SortedDictionary<int, SortedSet<string>>();
        var appDir = Path.Combine(RepoRoot().FullName, "app");
        foreach (var f in Directory.EnumerateFiles(appDir, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(f) is not (".cs" or ".xaml")) continue;
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var name = Path.GetFileName(f);
            if (NotGlyphFiles.Contains(name)) continue;
            var text = File.ReadAllText(f);
            // 两种写法：C# 的 ConvertFromUtf32(0xE7C4) 与 XAML 的 &#xE76F;
            foreach (Match m in Regex.Matches(text, @"0x([EF][0-9A-Fa-f]{3})\b"))
                Add(found, Convert.ToInt32(m.Groups[1].Value, 16), name);
            foreach (Match m in Regex.Matches(text, @"&#x([EF][0-9A-Fa-f]{3});"))
                Add(found, Convert.ToInt32(m.Groups[1].Value, 16), name);
        }
        // **出厂配置里写着的图标**（默认面板页那几格）。它们是四位十六进制的字符串而不是
        // 0x 字面量，上面两条正则都够不着——而挑错的码位一样是个空心方框，
        // 且首启那一页正是最不能出方框的地方。裸的四位十六进制不能直接当正则扫
        //（会误命中一堆普通字符串），所以把默认配置真的建一份出来读。
        foreach (var page in Clockwork.Core.RootConfig.Default().PanelPages)
            foreach (var step in page.Steps)
                if (Regex.Match(step.Icon ?? "", @"^([EF][0-9A-Fa-f]{3})$") is { Success: true } hit)
                    Add(found, Convert.ToInt32(hit.Groups[1].Value, 16), "RootConfig.Default()");
        return found;
    }

    private static void Add(SortedDictionary<int, SortedSet<string>> d, int cp, string file)
    {
        if (!d.TryGetValue(cp, out var set)) d[cp] = set = new SortedSet<string>();
        set.Add(file);
    }

    [Fact]
    public void Every_icon_codepoint_exists_in_the_font()
    {
        if (!File.Exists(FontPath)) return;   // 没装这套字体的机器上不判（见 FontPath 的注释）
        var map = new GlyphTypeface(new Uri(FontPath)).CharacterToGlyphMap;
        var found = Collect();
        Assert.NotEmpty(found);               // 一个都没扫到 = 正则或目录错了，那才是真失败

        var missing = found.Where(kv => !map.ContainsKey(kv.Key))
                           .Select(kv => $"U+{kv.Key:X4}（{string.Join(", ", kv.Value)}）")
                           .ToList();
        Assert.True(missing.Count == 0,
            "这些码位在 Segoe MDL2 Assets 里没有字形，界面上会画成空心方框：\n  " + string.Join("\n  ", missing));
    }
}
