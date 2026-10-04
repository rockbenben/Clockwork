using System.Globalization;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

public class StringsTests
{
    private static void WithCulture(string culture, Action body)
    {
        var old = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); body(); }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    // 这一组测的是「切了语言就拿到那份语言的文本」这条路由，不是任何一句文案的写法。
    // 早先每个语言钉一条逐字串（18 条），于是改一句标签就得顺手改一次测试；而改测试的人
    // 只会照着新值再抄一遍——什么都没验，只把当时的写法冻住了。2026-10 给 zh-TW 的
    // 「启动清单」统一用词时就红了这条，而它抓到的根本不是缺陷。
    // 现在判的是性质：每门语言都真的落到自己的卫星资源（不等于中性中文），且 18 门互不相同。
    private static readonly string[] OtherCultures =
        { "en", "zh-TW", "ja", "ko", "es", "fr", "de", "it", "pt", "ru", "ar", "hi", "id", "vi", "th", "tr", "nl" };

    private static string GetIn(string culture, string key)
    {
        string? value = null;
        WithCulture(culture, () => value = Strings.Get(key));
        return value!;
    }

    private static Dictionary<string, string> Entries(string code)
    {
        var set = new System.Resources.ResourceManager("Clockwork.Resources.Strings", typeof(Strings).Assembly)
            .GetResourceSet(CultureInfo.GetCultureInfo(code), createIfNotExists: true, tryParents: false);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null) return d;
        foreach (System.Collections.DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    [Fact]
    public void Every_culture_routes_to_its_own_text()
    {
        var neutral = GetIn("zh-CN", "Tab_Launch");
        Assert.False(string.IsNullOrWhiteSpace(neutral));
        var seen = new Dictionary<string, string>(StringComparer.Ordinal) { [neutral] = "zh-CN" };
        var bad = new List<string>();
        foreach (var culture in OtherCultures)
        {
            var value = GetIn(culture, "Tab_Launch");
            if (string.IsNullOrWhiteSpace(value)) bad.Add($"{culture}：空");
            else if (value == neutral) bad.Add($"{culture}：拿到的是中性中文（没落到卫星资源）");
            else if (seen.TryGetValue(value, out var other)) bad.Add($"{culture} 与 {other} 同值：{value}");
            else seen[value] = culture;
        }
        Assert.True(bad.Count == 0, "Tab_Launch 的语言路由不对：\n" + string.Join("\n", bad));
    }

    // 简繁这条判性质不判句子：TW 全文里不许出现只有简体才写得出的字。
    // 表是手工挑的高频简化偏旁字（每个都有独立繁体写法），当前全文零命中；
    // 一次简转繁的复制粘贴就会撞上它。
    private const string SimplifiedOnly =
        "启单页录动设选时显网关们这为于乐风区医几则华厂厅历参双变叶号吗员问图园圆墙声处备头夹夺奖实宠审写军农冲突净准凉爽凤击递";

    [Fact]
    public void ZhTw_carries_no_simplified_only_characters()
    {
        var values = Entries("zh-TW");
        var bad = new List<string>();
        foreach (var kv in values)
        {
            if (kv.Value == null) continue;
            foreach (var ch in kv.Value.Where(c => SimplifiedOnly.IndexOf(c) >= 0).Distinct())
                bad.Add($"zh-TW/{kv.Key}: 含简体字 {ch} → {kv.Value}");
        }
        Assert.True(bad.Count == 0, "繁体文件里混进简体字：\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Untranslated_culture_falls_back_to_chinese()
        // 无 sw 卫星 → 回退中性（中文）。跟中性自己比，别把某一句抄一遍当基准。
        => Assert.Equal(GetIn("zh-CN", "Tab_Launch"), GetIn("sw", "Tab_Launch"));

    [Fact] public void Unknown_key_returns_key() => Assert.Equal("__nope__", Strings.Get("__nope__"));

    [Theory]
    [InlineData("zh-CN", "zh-CN")]      // 精确
    [InlineData("zh-Hans-CN", "zh-CN")] // 简体
    [InlineData("zh-TW", "zh-TW")]      // 精确繁体
    [InlineData("zh-Hant-TW", "zh-TW")] // 繁体标记
    [InlineData("zh-HK", "zh-TW")]      // 港（无脚本标记）→ 繁体
    [InlineData("zh-MO", "zh-TW")]      // 澳（无脚本标记）→ 繁体
    [InlineData("zh-SG", "zh-CN")]      // 新加坡 → 简体
    [InlineData("zh-Hans-HK", "zh-CN")] // 简体脚本优先，即便地区是港 → 简体（不能只看地区）
    [InlineData("zh-Hant-HK", "zh-TW")] // 繁体脚本 → 繁体
    [InlineData("zh-CHT", "zh-TW")]     // 旧版繁体中性 → 繁体
    [InlineData("zh-CHS", "zh-CN")]     // 旧版简体中性 → 简体
    [InlineData("en-US", "en")]         // 两字母
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "ja")]
    [InlineData("de-DE", "de")]
    [InlineData("pt-BR", "pt")]         // 巴西葡语 → pt
    [InlineData("id-ID", "id")]
    [InlineData("ar-SA", "ar")]
    [InlineData("pl-PL", "en")]         // 不支持 → 英文
    [InlineData("sw-KE", "en")]         // 不支持 → 英文
    public void ResolveFor_maps_system_culture_to_supported(string culture, string expected)
        => Assert.Equal(expected, Languages.ResolveFor(CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData("en", "en")]            // 已支持 → 原样
    [InlineData("EN", "en")]            // 大小写规范化
    [InlineData("zh-cn", "zh-CN")]      // 规范大小写
    [InlineData("pt-BR", "pt")]         // 有效但不在列表 → 映射最接近
    [InlineData("zh-Hant", "zh-TW")]    // 有效但不在列表 → 繁体
    [InlineData("en-US", "en")]
    public void Normalize_deterministic_cases(string input, string expected)
        => Assert.Equal(expected, Languages.Normalize(input));

    [Theory]
    [InlineData("")]                    // 空 → 跟随系统
    [InlineData("   ")]                 // 空白
    [InlineData(null)]                  // null
    [InlineData("not-a-real-culture")]  // 无效文化
    public void Normalize_falls_back_to_a_supported_language(string? input)
        => Assert.True(Languages.IsSupported(Languages.Normalize(input)));   // 结果必是受支持的一门

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ar")]
    // 「键在各语言是否齐全」已由 StringsCoverageTests 自动枚举中性 resx 全量校验，
    // 不再在此维护人工键名清单（清单只能覆盖有人记得加的键）。此处只验 Lf 的占位符带入行为。
    public void KeyInput_warning_keys_resolve_and_format(string culture) => WithCulture(culture, () =>
    {
        Assert.Contains("Ctrl+X", Strings.Lf("Warn_KeyNoMain", "Ctrl+X"));
        var partial = Strings.Lf("Warn_KeyPartial", "Win+D", 2, 4);
        Assert.Contains("Win+D", partial);
        Assert.Contains("2", partial);
        Assert.Contains("4", partial);
    });

    [Fact]
    public void Display_helpers_localize_to_english() => WithCulture("en", () =>
    {
        Assert.Equal("Launch App", StepDisplay.StepKindLabel("app"));
        Assert.Equal("Volume 30%", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "set", Level = 30 }));
        Assert.Equal("Close Window Weixin", StepDisplay.StepSummary(new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" }));
        Assert.Equal("Mon–Fri", StepDisplay.DaysLabel(new List<int> { 1, 2, 3, 4, 5 }));
        Assert.Equal("Every 3 days", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "everyNDays", IntervalDays = 3 }));
        Assert.Equal("At login · before 8:00", ReminderDisplay.TimeLabel(new Reminder { Trigger = "startup", StartupHourMode = "before", StartupHour = 8 }));
        Assert.Equal("Scheduled Task", StartupLabels.TypeLabel("ScheduledTask"));
        Assert.Equal("All users (needs admin)", StartupLabels.ScopeLabel("Machine", true));
    });
}
