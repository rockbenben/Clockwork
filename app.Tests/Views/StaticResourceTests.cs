using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// 每个 {StaticResource Key} 必须真有其物——StaticResource 在**模板第一次实例化**时才解析，
// 写错一个键名，单元测试与窗口构造全程绿灯，直到用户在真机上打开那扇菜单/弹出层才炸。
// 实测漏过一次：Theme.xaml 的全局 MenuItem 模板写成 FontCode（真名 FontMono），
// 1812 条测试全绿，--smoke 真正弹出菜单时才 XamlParseException。
// 这里做全仓库 XAML 的「引用键 ⊆ 定义键」静态对账；--smoke 继续负责实例化那另一半。
public class StaticResourceTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Every_StaticResource_reference_resolves_to_a_defined_key()
    {
        var defined = new HashSet<string>();
        var refs = new List<(string Key, string File)>();
        foreach (var xaml in Directory.EnumerateFiles(Path.Combine(Root(), "app"), "*.xaml", SearchOption.AllDirectories))
        {
            if (xaml.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || xaml.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;
            var text = File.ReadAllText(xaml);
            var file = Path.GetFileName(xaml);
            foreach (Match m in Regex.Matches(text, @"x:Key=""([^""]+)"""))
                defined.Add(m.Groups[1].Value);
            // 特性写法 {StaticResource Key}（键名必为标识符；带参数的写法取第一个记号）。
            foreach (Match m in Regex.Matches(text, @"\{StaticResource\s+([A-Za-z_][A-Za-z0-9_]*)"))
                refs.Add((m.Groups[1].Value, file));
            // 属性元素写法 <StaticResource ResourceKey="Key"/>。
            foreach (Match m in Regex.Matches(text, @"<StaticResource\s+ResourceKey=""([^""]+)"""))
                refs.Add((m.Groups[1].Value, file));
        }

        Assert.NotEmpty(defined);
        var missing = refs.Where(r => !defined.Contains(r.Key))
                          .Select(r => $"{r.Key} ({r.File})")
                          .Distinct()
                          .OrderBy(x => x)
                          .ToList();
        Assert.True(missing.Count == 0,
            "这些 StaticResource 键在任何 app XAML 里都没有定义（模板实例化时才会炸）：" + string.Join("; ", missing));
    }
}
