using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// **拼出来的键（`Strings.Get("Theme_" + id)`）是 i18n 测试网里唯一的一个洞。**
//
// StringKeyReferenceTests 自己写明了放行这一类：它扫的是源码里的字面量，而 "Theme_" 只是前缀，
// 不是键。于是这两个方向都没人守：
//   · id 有、键没有 → 界面上直接显示 "Theme_dark" 这样的原始键名（该测试的注释里记着真的发生过一次）；
//   · 键有、id 没有 → 死键，会被翻成十八种语言、在每次文案审查里被读一遍，而 No_key_is_left_unreferenced
//     对前缀家族是整族放行的，永远不会报它。
// 此前 Sys_ / Kind_ / Day_ / Ed_Trig_ 各自在领域测试里被顺手断言过一两条（如 AskStepTests 对
// "Kind_prompt"），而 Theme_ 与 Size_ 一条都没有——正是本文件的来由。
//
// **id 一律从生产代码那张表现场枚举**，不在这里另抄一份：抄一份就得靠人记得两边同时改，
// 而那恰恰是这类 bug 的成因（同 StringsCoverageTests「不维护人工清单」的理由）。
//
// 只在一个文化下跑就够：ResourceManager 找不到译文时回退中性 resx，所以「键在中性里存不存在」
// 是各语言共同的下限；某一门语言缺不缺译文由 StringsCoverageTests 逐语言盯着，不必在这里重复一遍。
public class ComposedKeyTests
{
    // 前缀 → 该前缀下的全部 id（取自生产代码的表）。
    private static readonly (string Prefix, string[] Ids)[] Families =
    {
        ("Kind_", StepDisplay.StepKinds),
        ("Sys_", StepDisplay.SystemCommandMap().Select(kv => kv.Key).ToArray()),
        ("Day_", new[] { "1", "2", "3", "4", "5", "6", "7" }),
        ("Theme_", Themes.All),
        ("Size_", PanelMetrics.Sizes),
        // 提醒触发：固定的 time / startup 之外就是事件表本身（与 ReminderEditorWindow 的下拉同一来源）。
        // 键名把首字母大写（见 ReminderDisplay），所以这里也照那条规则拼。
        ("Ed_Trig_", new[] { "time", "startup" }.Concat(ReminderEvent.All)
                     .Select(id => char.ToUpperInvariant(id[0]) + id.Substring(1)).ToArray()),
    };

    public static IEnumerable<object[]> AllFamilies => Families.Select(f => new object[] { f.Prefix });

    private static string[] IdsOf(string prefix) => Families.First(f => f.Prefix == prefix).Ids;

    private static T InZh<T>(Func<T> f)
    {
        var save = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        try { return f(); }
        finally { CultureInfo.CurrentUICulture = save; }
    }

    // 方向一：每个 id 都得有文案。少一条，界面上就是一个下划线英文标识符。
    [Theory]
    [MemberData(nameof(AllFamilies))]
    public void Every_id_resolves_to_a_translation(string prefix)
    {
        var missing = InZh(() => IdsOf(prefix)
            .Where(id => Strings.Get(prefix + id) == prefix + id)
            .ToList());
        Assert.True(missing.Count == 0,
            $"这些 id 没有对应文案，界面上会显示键名：{string.Join(", ", missing.Select(id => prefix + id))}");
    }

    // 方向二：resx 里不该有 id 表之外、又没人直接引用的同前缀键（死键）。
    // 前缀家族被 No_key_is_left_unreferenced 整族放行，所以这里是它唯一的守卫。
    //
    // **前缀是会被两拨人共用的**：Sys_ 既是系统命令家族（Sys_ + camelCase id），又住着
    // MainWindow.xaml 里两个直接引用的菜单项（Sys_Takeover / Sys_Delete，系统启动项页的右键菜单）。
    // 所以「不在 id 表里」不等于死键——还得看有没有人拿它当字面量用。这一步是照
    // StringKeyReferenceTests 的同一条规则做的：整键匹配，且不能是别的键的前缀。
    [Theory]
    [MemberData(nameof(AllFamilies))]
    public void No_stale_keys_under_the_prefix(string prefix)
    {
        var want = IdsOf(prefix).Select(id => prefix + id).ToHashSet(StringComparer.Ordinal);
        var src = SourceText();
        var stale = NeutralKeys()
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Where(k => !want.Contains(k))
            .Where(k => !Regex.IsMatch(src, Regex.Escape(k) + @"(?![A-Za-z0-9_])"))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"这些键既不在 {prefix} 家族的 id 表里，也没人直接引用，删掉它们（连同十八份译文）：{string.Join(", ", stale)}");
    }

    // 仓库根（认 app/Resources 这个目录），与 StringKeyReferenceTests 同一条找法。
    private static string SourceText()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var files = Directory.EnumerateFiles(Path.Combine(d!.FullName, "app"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        return string.Concat(files.Select(File.ReadAllText));
    }

    // 扫描本身得确实扫到了东西：id 表取空、前缀写错都会让上面两条静默变成永远绿灯。
    [Theory]
    [MemberData(nameof(AllFamilies))]
    public void The_family_is_not_empty(string prefix)
    {
        Assert.NotEmpty(IdsOf(prefix));
        Assert.NotEmpty(NeutralKeys().Where(k => k.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> NeutralKeys()
    {
        var rm = new System.Resources.ResourceManager("Clockwork.Resources.Strings", typeof(Strings).Assembly);
        var set = rm.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(set);
        foreach (System.Collections.DictionaryEntry e in set!)
            if (e.Key is string k) yield return k;
    }
}
