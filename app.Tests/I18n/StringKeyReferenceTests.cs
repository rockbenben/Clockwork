using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.I18n;
using Xunit;

// **引用了一个不存在的文案键，界面上就直接显示那个键名。**
//
// Strings.Get 取不到时原样返回键名（那是刻意的：显示 "Panel_ShowTopTabs" 比显示空白好查）。
// 代价是这种错**完全静默**——不抛异常、不进日志，只有人眼看到界面上蹦出一个下划线英文标识符。
// 而既有的 i18n 测试查的是「各语言之间键对不对齐」，管不到「引用的键存不存在」：
// 十八份文件可以整整齐齐地都**没有**那一条。
//
// 实测发生过一次：一次批量改名把 CategoryBar → TopTabBar 这条规则排在了
// Panel_ShowCategoryBar → Panel_ShowTopTabs 前面，于是键被改成了 Panel_ShowTopTabBar，
// 十八份 resx 里一个都没有，面板管理器上就出现了一行 "Panel_ShowTopTabBar"。
// 一整轮渲染跑完才被眼睛发现——这条测试要把那一步提前到构建期。
public class StringKeyReferenceTests
{
    // 从测试程序集往上找仓库根（认 app/Resources 这个目录）。
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string root)
        => Directory.EnumerateFiles(Path.Combine(root, "app"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    // XAML 的 {loc:Loc Xxx}、代码里的 Strings.Get("Xxx") / Strings.Lf("Xxx", …)。
    private static readonly Regex[] Refs =
    {
        new(@"\{loc:Loc\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}", RegexOptions.Compiled),
        // 字面量后面必须紧跟 ) 或 ,——把 Strings.Get("Sys_" + id) 这种**拼出来的**键名排除在外：
        // 那里的 "Sys_" 只是前缀，不是键，当成键查必然查不到（而它其实一直是对的）。
        // 拼接出来的键查不了，只能靠各自那张表自己的测试（如 SystemCommandShortLabelTests）盯着。
        new(@"Strings\.(?:Get|Lf)\(\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*[,)]", RegexOptions.Compiled),
    };

    [Fact]
    public void Every_referenced_key_exists()
    {
        var root = RepoRoot();
        var missing = new SortedSet<string>();
        foreach (var f in SourceFiles(root))
        {
            var text = File.ReadAllText(f);
            foreach (var re in Refs)
                foreach (Match m in re.Matches(text))
                {
                    var key = m.Groups[1].Value;
                    // Strings.Get 取不到就原样返回键名——拿这一点当判据，不必自己解析 resx。
                    if (Strings.Get(key) == key) missing.Add($"{Path.GetFileName(f)}: {key}");
                }
        }
        Assert.True(missing.Count == 0,
            $"这些文案键被引用了但一条译文都没有，界面上会直接显示键名：\n  {string.Join("\n  ", missing)}");
    }

    // 反过来的那一半：**文案表里有没有没人用的死键**。
    //
    // 死键不是无害的：它会被翻成十八种语言、在每次文案审查里被读一遍、
    // 搜索时冒出来让人以为界面上还有那个东西。而它出现得毫无声息——
    // 删掉一个按钮很容易忘了删它那一条。
    //
    // 拼出来的键（Strings.Get("Sys_" + id) 这类）按前缀放行：前缀是从源码里现扫的，
    // 不写死一张白名单——白名单会过期，而这个扫描不会。
    [Fact]
    public void No_key_is_left_unreferenced()
    {
        var root = RepoRoot();
        var src = string.Concat(SourceFiles(root).Select(File.ReadAllText));
        var prefixes = new Regex(@"""([A-Za-z_][A-Za-z0-9_]*_)""\s*\+").Matches(src)
            .Select(m => m.Groups[1].Value).ToHashSet();

        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, "app", "Resources", "Strings.resx"));
        var orphan = doc.Root!.Elements("data")
            .Select(d => (string?)d.Attribute("name") ?? "")
            // 整键匹配，不是子串：Ports_ShowAll 是 Ports_ShowAllTip 的前缀（这样的对子 resx 里有 94 对），
            // 用 Contains 的话前者永远查不出来——而它确实可能已经没人用了。
            .Where(k => k.Length > 0
                        && !Regex.IsMatch(src, Regex.Escape(k) + @"(?![A-Za-z0-9_])")
                        && !prefixes.Any(k.StartsWith))
            .OrderBy(k => k).ToList();

        Assert.True(orphan.Count == 0,
            $"这些文案键已经没人引用了，删掉它们（连同十八份译文）：\n  " +
            string.Join(Environment.NewLine + "  ", orphan));
    }

    // ActionResult.Warn 的第一个实参**必须是键**，不能是已经渲染好的句子。
    //
    // 这条守的是一个只在重构之后才存在的坑：Warn 从 Warn(string message) 变成
    // Warn(string key, params object[] args) 之后，**所有旧写法照样编译得过**——
    // Warn(ex.Message)、Warn(Strings.Lf(...)) 都是一个 string，编译器没话说。
    // 而 Strings.Get 取不到时原样返回入参，于是那句话会被当成「键」原样吐回来，
    // 界面上看着完全正常，只有 WarningEn 悄悄跟着变成用户那门语言——正是这次重构要消灭的东西。
    // 实测确实发生过：app.Tests 里那条 Warn("坏了") 一直是绿的。
    //
    // 上面那条 Every_referenced_key_exists 盯不到它：它的正则只认标识符形状的字面量，
    // 「坏了」这种压根不匹配，于是被静默跳过。这里对**任何**字面量都要求它能翻出东西来。
    [Fact]
    public void Action_result_warnings_are_passed_a_key_not_a_sentence()
    {
        var root = RepoRoot();
        var bad = new SortedSet<string>();
        var re = new Regex(@"ActionResult\.Warn\(\s*""([^""]*)""", RegexOptions.Compiled);
        foreach (var f in SourceFiles(root).Concat(TestFiles(root)))
            foreach (Match m in re.Matches(File.ReadAllText(f)))
            {
                var arg = m.Groups[1].Value;
                if (Strings.Get(arg) == arg) bad.Add($"{Path.GetFileName(f)}: \"{arg}\"");
            }
        Assert.True(bad.Count == 0,
            "ActionResult.Warn 的第一个实参要传 resx 键，不是句子。这些取不到译文（键写错，或者传的是渲染好的话）："
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", bad));
    }

    // 上面那条也要扫测试代码：误用最容易留在测试里（它「看起来是通的」）。
    private static IEnumerable<string> TestFiles(string root)
        => Directory.EnumerateFiles(Path.Combine(root, "app.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    // 扫描本身得确实扫到了东西。正则写坏、目录找错都会让上面那条静默变成永远绿灯。
    [Fact]
    public void The_scan_actually_finds_references()
    {
        var root = RepoRoot();
        int n = SourceFiles(root).Sum(f =>
        {
            var t = File.ReadAllText(f);
            return Refs.Sum(re => re.Matches(t).Count);
        });
        Assert.True(n > 200, $"只扫到 {n} 处文案引用，扫描八成坏了");
    }
}
