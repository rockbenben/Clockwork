using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

// **`TextWrapping="Wrap"` 在一个宽度无限的父容器里是一句空话。**
//
// WrapPanel 和横向 StackPanel 给子元素的可用宽度是 infinity，于是 TextBlock 量出来的
// 「需要多宽」就是整句话的长度，换行永不触发。作者写了 Wrap，以为封住了；实际那一行会一路
// 长出窗口，把整窗内容往右推（编辑器开着横向滚动）或直接被裁掉。
//
// 这一类缺陷有三个特点，凑起来就是「没人能发现」：
//   · 中文看不出来——中文文案短，一行放得下；只有 en / de / ru 的长译文才顶出去；
//   · 截图也看不出来——出事的那一行可能压根没有对应的 harness 状态（实测：亮度那一行
//     只在「系统命令 = 调亮度」时出现，6000 张里一张都没有它）；
//   · 而它在源码里长得完全正常：有 TextWrapping="Wrap"，看着已经处理过了。
//
// 于是判据只能是结构性的：**在无限宽的父容器里，Wrap 必须配一个真实的宽度上限**
// （显式 Width，或 MaxWidth——见 ReminderEditorWindow 里那段注释）。
// 真正的正解是把它放进 Grid 的 * 列交给布局量宽度（端口 / 系统启动项 / 面板管理器三处页脚都是这么改的），
// 那种写法父容器宽度有限，压根不会被这条用例看到。
//
// 实测抓到两处：面板管理器页脚（WrapPanel + 写死 MaxWidth="700"，820 宽下钻到「关闭」按钮底下），
// 以及亮度提示（Wrap 但什么上限都没有，德语 109 字）。
public class WrappingHintTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> XamlFiles()
        => Directory.EnumerateFiles(Path.Combine(Root(), "app"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    // 在换行方向上把可用宽度给成无限的容器。
    private static bool GivesInfiniteWidth(XElement e)
        => e.Name == X + "WrapPanel"
        || (e.Name == X + "StackPanel" && (string?)e.Attribute("Orientation") == "Horizontal");

    private static IEnumerable<string> Offenders()
    {
        foreach (var f in XamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch (System.Xml.XmlException) { continue; }   // 不是本测试该报的错
            foreach (var parent in doc.Descendants().Where(GivesInfiniteWidth))
                foreach (var tb in parent.Elements(X + "TextBlock"))
                {
                    if ((string?)tb.Attribute("TextWrapping") != "Wrap") continue;
                    if (tb.Attribute("Width") != null || tb.Attribute("MaxWidth") != null) continue;
                    var who = (string?)tb.Attribute("Text")
                              ?? (string?)tb.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")
                              ?? "(匿名)";
                    yield return $"{Path.GetFileName(f)}: {who}";
                }
        }
    }

    [Fact]
    public void No_wrapping_text_is_left_unconstrained()
    {
        var bad = Offenders().OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(bad.Count == 0,
            "这些 TextBlock 写了 TextWrapping=\"Wrap\"，但父容器宽度无限、自己又没有 Width / MaxWidth——"
            + "换行不会发生，长译文会顶出窗口。放进 Grid 的 * 列，或钉一个上限：\n  "
            + string.Join("\n  ", bad));
    }

    // 扫描本身得确实扫到了东西：命名空间写错、路径找错都会让上面那条静默变成永远绿灯。
    [Fact]
    public void The_scan_actually_walks_the_xaml()
    {
        var wrapping = XamlFiles()
            .Select(f => { try { return XDocument.Load(f); } catch (System.Xml.XmlException) { return null; } })
            .Where(d => d != null)
            .SelectMany(d => d!.Descendants(X + "TextBlock"))
            .Count(tb => (string?)tb.Attribute("TextWrapping") == "Wrap");
        Assert.True(wrapping >= 20, $"只扫到 {wrapping} 个换行 TextBlock，扫描大概坏了");
    }
}
