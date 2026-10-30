using System.IO;
using System.Linq;
using System.Reflection;
using Clockwork;
using Xunit;

// 托盘菜单的两条规矩：**一个字形只说一件事**，**区段标题与项的文字同列**。
//
// 这张菜单是本程序最常被看见的一屏，此前既没被拍过、也没被测过——
// 于是「查看上次启动日志」与「快速提醒」共用一个时钟字形、「打开快捷面板」与
// 「运行：某组」共用一个播放字形，都是没人发现的：菜单里没有第二处文字提示栏，
// 字形是扫菜单时最先看到的东西，同一个字形挂两件事，那一列就白画了。
//
// 区段标题曾经落在 14px（字形列里），而项的文字在 30px——两边都不靠。
// 标题是它下面那些项的开头，不与项对齐就像飘在字形列里。
public class TrayMenuTests
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

    // 每个字形常量都得是**自己的**码位：两个名字指向同一个码位，
    // 画出来就是同一个图形——菜单里那根字形列于是少了一种说法。
    [Fact]
    public void Every_tray_glyph_is_a_different_codepoint()
    {
        var fields = typeof(TrayGlyph)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral || f.IsInitOnly)
            .Where(f => f.FieldType == typeof(string))
            .ToList();
        Assert.NotEmpty(fields);
        var seen = new System.Collections.Generic.Dictionary<int, string>();
        foreach (var f in fields)
        {
            string glyph = (string)f.GetValue(null)!;
            Assert.False(string.IsNullOrEmpty(glyph), $"{f.Name} 是空字形");
            int cp = char.ConvertToUtf32(glyph, 0);
            Assert.True(!seen.TryGetValue(cp, out var other), $"{f.Name} 与 {other} 是同一个码位 U+{cp:X4}");
            seen[cp] = f.Name;
        }
    }

    // 区段标题的左边距 = 项文字的左边距 = 字形列宽。
    // 两边都用同一列，标题才是它下面那些项的**开头**，而不是飘在字形列里。
    [Fact]
    public void A_section_header_starts_where_the_item_labels_start()
    {
        var header = TrayMenu.Header("段");
        var item = TrayMenu.Item("项", TrayGlyph.Run, null);
        Assert.Equal(TrayMenu.GlyphCol, header.Padding.Left);
        Assert.Equal(TrayMenu.GlyphCol, item.Padding.Left);
    }

    // 画的那一侧也要在字形列上：标题文字的左边界用的就是字形列宽。
    // （项的文字同样从字形列宽开始画，两边同源。）
    [Fact]
    public void The_renderer_draws_headers_at_the_same_left_edge()
    {
        var src = Src("TrayMenuRenderer.cs");
        Assert.Contains("b.Left + TrayMenu.GlyphCol, b.Top, b.Width - TrayMenu.GlyphCol - 10", src);
    }

    // 「查看上次启动日志」与「快速提醒」曾经都是历史时钟字形——现在一个是历史时钟、
    // 一个是秒表。「打开快捷面板」曾经是播放字形（那是「运行某个动作组」的意思）。
    [Fact]
    public void The_items_that_used_to_share_a_glyph_have_their_own()
    {
        var src = Src("TrayIcon.cs");
        Assert.Contains("Tray_ViewLog\"), TrayGlyph.Log", src);
        Assert.Contains("Tray_QuickRemind\"), TrayGlyph.Timer", src);
        Assert.Contains("Tray_Panel\"), TrayGlyph.Tiles", src);
        // 反面：这几个不许再用回撞车的那个字形。
        Assert.DoesNotContain("Tray_QuickRemind\"), TrayGlyph.Log", src);
        Assert.DoesNotContain("Tray_Panel\"), TrayGlyph.Run", src);
        Assert.DoesNotContain("Tray_History\"), TrayGlyph.Log", src);
    }

    // 提醒段的小标题不能是「定时任务」——那是主窗口那一页的名字，
    // 而这一段装的是「暂停提醒 / 快速提醒 / 恢复提醒」。
    [Fact]
    public void The_reminder_section_is_not_titled_after_the_main_window_tab()
    {
        var src = Src("TrayIcon.cs");
        Assert.Contains("Tray_SectionRemind", src);
        Assert.DoesNotContain("Header(Strings.Get(\"Tab_Reminder\"))", src);
    }
}
