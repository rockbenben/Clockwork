using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using Clockwork.I18n;
using Xunit;

// **标点口径会一门一门往回漂**，所以这一族判据是纯文本的，跑在 resx 上而不是跑在界面上。
//
// 起因是第三轮设计审查量到的四类漂移：中文标签里半角括号与全角括号并存、短标签用「 / 」
// 并列而长句用顿号、界面串里留着 markdown 的 `**`、「。」后面还悬着一整个括号。
// 每一类单看都不值得为它开一次 review——它们之所以活着，正是因为没人能在扫一眼时
// 判断「这个写法是选过的还是随手写的」。判据把「选过的」那一种钉住，新加的串就必须照做。
//
// 范围是量出来再划的，不是按语言族类推的：
//   · 半角括号那条只管简繁中文。日语与韩语各自的写法里，半角括号包拉丁是常规做法
//     （且数量以百计），把它们一起改是一次纯排版的抖动，不解决任何读不懂的问题。
//   · 引号那条管全部 18 份：每份文件内一律用该语言自己的排版引号，直引号不许出现。
//   · 「。」后不接东西那条把日语算进来，因为它的用法与中文同形，而实测确实漂过。
public class PunctuationStyleTests
{
    private static readonly ResourceManager Rm =
        new("Clockwork.Resources.Strings", typeof(Strings).Assembly);

    private static Dictionary<string, string> Entries(string? code)
    {
        // tryParents:false：带回退会让缺键静默取到中性值，闸门就永远绿。
        var set = Rm.GetResourceSet(
            code == null ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code),
            createIfNotExists: true, tryParents: false);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null) return d;
        foreach (DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    // 中性 resx 就是简体中文，没有独立卫星。
    private static readonly string[] HanCultures = { "zh-TW" };
    private static readonly Regex HanThenHalfParen = new("[\\u4e00-\\u9fff]\\(", RegexOptions.Compiled);
    private static readonly Regex HanSlashHan = new("[\\u4e00-\\u9fff] / [\\u4e00-\\u9fff]", RegexOptions.Compiled);
    private static readonly Regex AfterFullStop = new("。\\s|。\\(", RegexOptions.Compiled);
    // *词* 形式的 markdown 强调：两侧星号后面都得是字母，这样 *.exe / *.bat 这类通配符不会被误伤。
    private static readonly Regex MarkdownStar = new(
        "\\*[A-Za-zÀ-ÿ][A-Za-z0-9 ./_+-]{0,30}\\*", RegexOptions.Compiled);

    private static IEnumerable<(string Label, Dictionary<string, string> Values)> HanSets()
    {
        yield return ("zh-CN", Entries(null));
        foreach (var c in HanCultures) yield return (c, Entries(c));
    }

    [Fact]
    public void Han_labels_wrap_specs_in_fullwidth_parens()
    {
        var bad = new List<string>();
        foreach (var (label, values) in HanSets())
            foreach (var kv in values)
                if (kv.Value != null && HanThenHalfParen.IsMatch(kv.Value))
                    bad.Add($"{label}/{kv.Key}: {kv.Value}");
        Assert.True(bad.Count == 0,
            "中文标签里包数值或格式的括号必须是全角（）——同族的「开机延迟（秒）」就是全角，" +
            "半角与全角并存时没人看得出哪个是选过的：\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Short_han_labels_list_with_the_ideographic_comma()
    {
        // 只卡短标签：长说明里的技术枚举（「程序（全路径或裸名）/ 快捷方式 / 脚本」那一类）
        // 用斜杠是有意的，顿号会把层级压平。长度就是「这是标签还是句子」的分界。
        const int LabelCeiling = 24;
        var bad = new List<string>();
        foreach (var (label, values) in HanSets())
            foreach (var kv in values)
                if (!string.IsNullOrEmpty(kv.Value) && kv.Value.Length <= LabelCeiling
                    && HanSlashHan.IsMatch(kv.Value))
                    bad.Add($"{label}/{kv.Key}: {kv.Value}");
        Assert.True(bad.Count == 0,
            "短标签（按钮、复选框、占位符、列头）里的并列必须用顿号，不用「 / 」：\n"
            + string.Join("\n", bad));
    }

    [Fact]
    public void No_markdown_emphasis_survives_into_a_ui_string()
    {
        // WPF 不解析 markdown：星号会原样印在界面上。它出现在串里只有一个来源——
        // 从设计稿或提交信息里抄过来时忘了剥。
        // 单个星号包起来的 *词* 也算：en/Ed_IconHint 就这么带着 *Launch App* 上过线。
        // 通配符不算（*.exe 的星号后面是点），所以正则要求星号两侧都是字母。
        var bad = new List<string>();
        foreach (var code in Languages.All.Select(l => l.Code))
        {
            var values = code == "zh-CN" ? Entries(null) : Entries(code);
            foreach (var kv in values)
                if (kv.Value != null && (kv.Value.Contains("**") || kv.Value.Contains("__")
                                         || MarkdownStar.IsMatch(kv.Value)))
                    bad.Add($"{code}/{kv.Key}: {kv.Value}");
        }
        Assert.True(bad.Count == 0, "界面串里不得有 markdown 强调标记：\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Han_and_japanese_strings_end_the_sentence_before_the_parenthesis()
    {
        var bad = new List<string>();
        foreach (var (label, values) in HanSets())
            foreach (var kv in values)
                if (kv.Value != null && AfterFullStop.IsMatch(kv.Value))
                    bad.Add($"{label}/{kv.Key}: {kv.Value}");
        foreach (var kv in Entries("ja"))
            if (kv.Value != null && AfterFullStop.IsMatch(kv.Value))
                bad.Add($"ja/{kv.Key}: {kv.Value}");
        Assert.True(bad.Count == 0,
            "「。」之后不该再有任何东西：补充信息进括号、括号进句号之内，" +
            "悬在句号后的括号读起来像没写完：\n" + string.Join("\n", bad));
    }

    [Fact]
    public void No_locale_writes_a_ui_string_with_straight_quotes()
    {
        // 从前只管英文（那一轮只归一了英文）。这轮逐语言复核把口径推到全部 18 份：
        // 每份文件里成对的引用一律用该语言自己的排版形态——德语 „…“、法语与西语 «…»、
        // 中文与日语「…」、韩语“…”。直引号是「没选过」的写法，混在弯引号里没人能一眼看出
        // 哪个是有意的，所以它一律不许出现。
        // 两种*排版*形态在同一份文件里并存（韩语「」与“”同现）不在本条范围内：
        // 那是要改上百串口味的取舍，不是用户会读错的标记。
        var bad = new List<string>();
        foreach (var code in Languages.All.Select(l => l.Code))
        {
            var values = code == "zh-CN" ? Entries(null) : Entries(code);
            foreach (var kv in values)
                if (kv.Value != null && kv.Value.Contains('"'))
                    bad.Add($"{code}/{kv.Key}: {kv.Value}");
        }
        Assert.True(bad.Count == 0,
            "界面串里的成对引用必须用排版引号，不用直引号 \" ：\n" + string.Join("\n", bad));
    }

    // 每份语言允许的那**一对**引号码位。写成表、且按对判：德语 „…“ 的后引号与英语 “…”
    // 的前引号是同一个码位 U+201C，按单个字符判会把德语的正常写法读成串家族（第一版就这么假红过）。
    // 表里的形态是逐语言复核定下来的：取各语言自己的规范，而不是"这份文件恰好哪种多"——
    // 印尼语/越南语/荷兰语的主流曾经是 «…»، 那是机翻带进来的写法，不是它们的规范。
    private static readonly (string Code, string Pair)[] QuoteStyle =
    {
        ("zh-CN", "「」"), ("zh-TW", "「」"), ("ja", "「」"),
        ("ko", "“”"), ("en", "“”"), ("nl", "“”"), ("id", "“”"), ("vi", "“”"),
        ("de", "„“"),
        ("fr", "«»"), ("es", "«»"), ("pt", "«»"), ("it", "«»"), ("ru", "«»"),
        ("ar", "«»"), ("hi", "«»"), ("th", "«»"), ("tr", "«»"),
    };

    public static IEnumerable<object[]> QuoteStyleData =>
        QuoteStyle.Select(x => new object[] { x.Code, x.Pair });

    [Theory]
    [MemberData(nameof(QuoteStyleData))]
    public void Each_locale_uses_one_quote_family(string code, string allowedPair)
    {
        var values = code == "zh-CN" ? Entries(null) : Entries(code);
        const string allMarks = "«»„“”「」";
        var offenders = values
            .Where(kv => kv.Value != null && kv.Value.Any(ch =>
                allMarks.IndexOf(ch) >= 0 && allowedPair.IndexOf(ch) < 0))
            .Select(kv => $"{code}/{kv.Key}: {kv.Value}")
            .ToList();
        Assert.True(offenders.Count == 0,
            $"{code} 只该用 {allowedPair[0]}…{allowedPair[1]} 这一对引号，混进了别的家族的码位：\n"
            + string.Join("\n", offenders));
    }
}
