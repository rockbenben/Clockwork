using System;
using System.Globalization;
using System.IO;
using Xunit;

// 文字三档（Paper/Muted/Faint）压在它们实际落脚的画布上必须 ≥4.5:1（WCAG AA 正文线）。
//
// 为什么钉在十六进制上而不是渲染图：这是一条纯算术不变量，截图只能看出「肉眼觉得虚」，
// 量不出 4.21 和 4.55 的差别——而两套调色板各有一块背景正好卡在线下：
//   浅色 Faint #737373 压画布 Ink #F4F4F3 = 4.31:1（调色板头的实测只量了卡片 Slate）；
//   深色 Faint #7C7C7C 压卡片 Slate #191919 = 4.21:1（头部只量了 Ink）。
// Faint 不是装饰：表头 11.5px、GroupLabel 10.5px、日历列名都是它在扛，全是最小的字。
//
// 判据按「静息底色」给：Faint 不要求压过按钮底 Steel（Faint 文字不出现在 Steel 上；
// 日历非本月格悬停那一瞬是 Steel，瞬态悬停不列进正文闸门，值 #848484 时也有 4.15:1）。
public class PaletteContrastTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // <Color x:Key="Faint">#7C7C7C</Color> —— 从调色板文件现场取值，不抄表。
    private static string Hex(string palette, string key)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "app", $"Palette.{palette}.xaml"));
        var m = System.Text.RegularExpressions.Regex.Match(text, $"x:Key=\"{key}\"\\s*>(#[0-9A-Fa-f]{{6}})<");
        Assert.True(m.Success, $"Palette.{palette}.xaml 里找不到 {key}");
        return m.Groups[1].Value;
    }

    private static double Luminance(string hex)
    {
        double[] c = new double[3];
        for (int i = 0; i < 3; i++)
        {
            double v = int.Parse(hex.Substring(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            c[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Body_tiers_clear_AA_on_every_surface(string palette)
    {
        foreach (var token in new[] { "Paper", "Muted" })
            foreach (var surface in new[] { "Ink", "Slate", "Steel" })
                Assert.True(Contrast(Hex(palette, token), Hex(palette, surface)) >= 4.5,
                    $"{palette} {token} 压 {surface} = {Contrast(Hex(palette, token), Hex(palette, surface)):0.##}:1，低于 4.5");
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Faint_tier_clears_AA_on_the_canvases_it_sits_on(string palette)
    {
        foreach (var surface in new[] { "Ink", "Slate" })
            Assert.True(Contrast(Hex(palette, "Faint"), Hex(palette, surface)) >= 4.5,
                $"{palette} Faint 压 {surface} = {Contrast(Hex(palette, "Faint"), Hex(palette, surface)):0.##}:1，低于 4.5——" +
                "表头/分组标签/日历列名都是这一档，全是最小的字");
    }

    // 档与档之间要还看得出是三层：相邻两档的对比度差不能塌成一样。
    // （只钉「Paper > Muted > Faint」这个次序感：Paper 对某底的读数必须始终高于 Muted 高于 Faint。）
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Tiers_stay_ordered(string palette)
    {
        foreach (var surface in new[] { "Ink", "Slate" })
        {
            var b = Hex(palette, surface);
            double paper = Contrast(Hex(palette, "Paper"), b),
                   muted = Contrast(Hex(palette, "Muted"), b),
                   faint = Contrast(Hex(palette, "Faint"), b);
            Assert.True(paper > muted && muted > faint,
                $"{palette} 在 {surface} 上三层塌序：Paper {paper:0.##} Muted {muted:0.##} Faint {faint:0.##}");
        }
    }
}
