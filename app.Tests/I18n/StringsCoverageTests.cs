using System.Collections;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using Clockwork.I18n;
using Xunit;

// 多语言资源的自动覆盖校验：键从中性 resx 现场枚举，不维护人工清单——
// 人工清单只能挡住「有人记得往清单里加」的键，挡不住忘记加的那些（历史上 Config_* 等一批键就漏在清单外）。
public class StringsCoverageTests
{
    private static readonly ResourceManager Rm = new("Clockwork.Resources.Strings", typeof(Strings).Assembly);

    // 中性 resx（Strings.resx = 简体中文源）的全部键值。
    private static Dictionary<string, string> Neutral()
        => Entries(Rm.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true));

    // 某语言卫星 resx「自身」的键值。tryParents:false 是关键——带回退的话缺失键会静默取到中性值，
    // 测试就永远绿，正是要防的那种假绿。
    private static Dictionary<string, string> Satellite(string code)
        => Entries(Rm.GetResourceSet(CultureInfo.GetCultureInfo(code), createIfNotExists: true, tryParents: false));

    private static Dictionary<string, string> Entries(ResourceSet? set)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null) return d;
        foreach (DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    // 提取 string.Format 占位符序号（{0}、{1,-8}、{2:N1} 等）。先掩掉 {{ }} 转义，
    // 避免把字面花括号（如发送按键说明里的 {{} ）误当占位符。
    private static SortedSet<int> Placeholders(string s)
    {
        var masked = s.Replace("{{", "").Replace("}}", "");
        var set = new SortedSet<int>();
        foreach (Match m in Regex.Matches(masked, @"\{(\d+)[^}]*\}"))
            set.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        return set;
    }

    // zh-CN 即中性 resx 本身，没有独立卫星，故不参与「卫星是否齐全」的比对。
    public static IEnumerable<object[]> Satellites =>
        Languages.All.Where(l => l.Code != "zh-CN").Select(l => new object[] { l.Code });

    [Fact]
    public void Neutral_resx_is_not_empty()
        => Assert.True(Neutral().Count > 100, "中性 resx 没读到键——资源名或程序集引用变了");

    // 每门语言都必须覆盖中性 resx 的全部键：漏一个，该语言用户就会在界面上看到原始键名。
    [Theory]
    [MemberData(nameof(Satellites))]
    public void Every_neutral_key_exists_in_satellite(string code)
    {
        var missing = Neutral().Keys.Except(Satellite(code).Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{code} 缺 {missing.Count} 个键：{string.Join(", ", missing)}");
    }

    // 允许为空/纯空白的「词序后缀 / 分隔符」键——空值在这些键上是有意义的翻译结果，不是漏译：
    //   Days_Sep      星期之间的分隔符：中日韩写「周一周二」不加分隔（空），英文写「Mon Tue」用空格。
    //   Ed_HourBefore 中文语序的后缀「仅 [8:00] 前执行」；英文语序把话说在时间框之前，后缀自然为空。
    // 新增此类键时要往这里加一行并写清理由；漏加会让下面的用例失败，不会静默放过。
    private static readonly HashSet<string> AffixKeys = new(StringComparer.Ordinal) { "Days_Sep", "Ed_HourBefore" };

    // 除词序后缀/分隔符外，译文不得为空白：空值同样会让界面出现空标签，而「键是否存在」检查发现不了。
    [Theory]
    [MemberData(nameof(Satellites))]
    public void No_blank_translations(string code)
    {
        var blank = Satellite(code).Where(kv => !AffixKeys.Contains(kv.Key) && string.IsNullOrWhiteSpace(kv.Value))
                                   .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(blank.Count == 0, $"{code} 有 {blank.Count} 个空译文：{string.Join(", ", blank)}");
    }

    // 豁免名单本身要防腐：某个键在所有语言都非空了，说明它已不是词序后缀，应从名单里移除，
    // 否则名单会慢慢变成「什么都允许为空」的橡皮图章。
    [Fact]
    public void Affix_allowlist_has_no_stale_entries()
    {
        var stale = AffixKeys.Where(k => Languages.All.All(l =>
        {
            var v = l.Code == "zh-CN" ? Neutral().GetValueOrDefault(k) : Satellite(l.Code).GetValueOrDefault(k);
            return !string.IsNullOrWhiteSpace(v);
        })).ToList();
        Assert.True(stale.Count == 0, $"这些键在所有语言里都非空，应从 AffixKeys 移除：{string.Join(", ", stale)}");
    }

    // 占位符集合必须与中性一致：译文多一个 {N} 会让 string.Format 在运行时抛 FormatException
    //（少一个则静默丢信息），两种都只在该语言下才复现，最难靠人工发现。
    [Theory]
    [MemberData(nameof(Satellites))]
    public void Placeholders_match_neutral(string code)
    {
        var neutral = Neutral();
        var sat = Satellite(code);
        var bad = new List<string>();
        foreach (var (key, nv) in neutral)
        {
            if (!sat.TryGetValue(key, out var tv)) continue;   // 缺键由上面的用例报
            var want = Placeholders(nv);
            var got = Placeholders(tv);
            if (!want.SetEquals(got))
                bad.Add($"{key}(中性 {{{string.Join(",", want)}}} ≠ 译文 {{{string.Join(",", got)}}})");
        }
        Assert.True(bad.Count == 0, $"{code} 占位符不一致 {bad.Count} 处：{string.Join("; ", bad)}");
    }

    // 卫星里不该有中性没有的键：多出来的通常是键名拼错（拼错的那个永远取不到，界面显示原始键名）。
    [Theory]
    [MemberData(nameof(Satellites))]
    public void No_orphan_keys_in_satellite(string code)
    {
        var orphan = Satellite(code).Keys.Except(Neutral().Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(orphan.Count == 0, $"{code} 有 {orphan.Count} 个中性里不存在的键（疑似拼写错误）：{string.Join(", ", orphan)}");
    }

    // 语言清单与实际资源必须对得上：列表里多一门而没有 resx，用户选了就会整界面回退中文。
    [Theory]
    [MemberData(nameof(Satellites))]
    public void Declared_language_has_a_satellite(string code)
        => Assert.True(Satellite(code).Count > 0, $"Languages.All 声明了 {code}，但没有对应的 Strings.{code}.resx 卫星程序集");
}
