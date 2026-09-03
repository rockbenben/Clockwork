using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Clockwork.I18n;
using Xunit;

// **「译文位置上放着英文原句」是这张测试网漏掉的最后一类。**
//
// 已有的检查都过得去，因为它们各自问的都不是这件事：
//   Every_neutral_key_exists_in_satellite  —— 键在，只是内容没翻
//   No_blank_translations                  —— 不是空的
//   Placeholders_match_neutral             —— 占位符当然一致（就是同一句话）
//   B 类扫描（译文里有没有残留汉字）        —— 里面一个汉字都没有，全是英文
// 实测漏过一批：Mig_* 四条迁移提示在 15 门语言里全是英文原句，Mig_ 这一族恰好是
// 「加键时顺手填了英文、之后没人回来翻」的典型——而它只在升级那一次弹出来，
// 平时点不到，所以人眼永远撞不上。
//
// 判据是「成句的散文」而不是「任何相同的值」：**大量键本来就该与英文逐字相同**——
// PID / URL / OK / Normal / Port / Filter / Active Setup 这类专名与借词，
// "{0} min" / "{0} h" / ", " / ":00" / " → {0}" 这类纯格式模板。
// 按空白切出 >= 4 个词才算散文，正好把两类分开：实测阈值为 3 时会误报
// fr 的 "Clockwork · Question" 与 es 的 "Clockwork · Error"——那两条是真巧合，
// 那些词在两种语言里就是同形。所以不需要豁免名单（名单迟早会变成橡皮图章）。
public class UntranslatedProseTests
{
    private const int ProseWords = 4;

    private static readonly ResourceManager Rm =
        new("Clockwork.Resources.Strings", typeof(Strings).Assembly);

    private static Dictionary<string, string> Entries(ResourceSet? set)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null) return d;
        foreach (DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    // tryParents:false 是承重件：带回退的话缺键会静默取到中性值，这条测试就永远绿。
    private static Dictionary<string, string> Satellite(string code)
        => Entries(Rm.GetResourceSet(CultureInfo.GetCultureInfo(code), true, false));

    private static Dictionary<string, string> English() => Satellite("en");

    // 英文自己不参与比对；zh-CN 是中性 resx 本身，没有独立卫星。
    public static IEnumerable<object[]> Satellites =>
        Languages.All.Where(l => l.Code is not ("zh-CN" or "en"))
                     .Select(l => new object[] { l.Code });

    [Theory]
    [MemberData(nameof(Satellites))]
    public void No_english_prose_left_in_a_satellite(string code)
    {
        var en = English();
        var sat = Satellite(code);
        var untranslated = en
            .Where(kv => kv.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= ProseWords)
            .Where(kv => sat.TryGetValue(kv.Key, out var v) && v == kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(untranslated.Count == 0,
            $"{code} 有 {untranslated.Count} 条还是英文原句（键在、非空、占位符也对，所以别的用例都发现不了）："
            + string.Join(", ", untranslated));
    }

    // 阈值本身要有据可依：英文 resx 里必须确实存在「短到不算散文」的值，
    // 否则这条判据等于「所有相同都算漏译」，那会把 PID / OK / {0} min 全报上来。
    [Fact]
    public void The_prose_threshold_actually_excludes_labels()
    {
        var en = English();
        Assert.NotEmpty(en.Where(kv =>
            kv.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < ProseWords));
        Assert.NotEmpty(en.Where(kv =>
            kv.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= ProseWords));
    }
}
