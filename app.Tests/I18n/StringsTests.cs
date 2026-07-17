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

    [Fact] public void Zh_returns_chinese() => WithCulture("zh-CN", () => Assert.Equal("我的启动清单", Strings.Get("Tab_Launch")));
    [Fact] public void En_returns_english() => WithCulture("en", () => Assert.Equal("Startup List", Strings.Get("Tab_Launch")));
    [Fact] public void ZhTw_returns_traditional() => WithCulture("zh-TW", () => Assert.Equal("我的開機清單", Strings.Get("Tab_Launch")));
    [Fact] public void Ja_returns_japanese() => WithCulture("ja", () => Assert.Equal("起動リスト", Strings.Get("Tab_Launch")));
    [Fact] public void Ko_returns_korean() => WithCulture("ko", () => Assert.Equal("시작 목록", Strings.Get("Tab_Launch")));
    [Fact] public void Es_returns_spanish() => WithCulture("es", () => Assert.Equal("Lista de inicio", Strings.Get("Tab_Launch")));
    [Fact] public void Fr_returns_french() => WithCulture("fr", () => Assert.Equal("Liste de démarrage", Strings.Get("Tab_Launch")));
    [Fact] public void De_returns_german() => WithCulture("de", () => Assert.Equal("Startliste", Strings.Get("Tab_Launch")));
    [Fact] public void It_returns_italian() => WithCulture("it", () => Assert.Equal("Elenco di avvio", Strings.Get("Tab_Launch")));
    [Fact] public void Pt_returns_portuguese() => WithCulture("pt", () => Assert.Equal("Lista de inicialização", Strings.Get("Tab_Launch")));
    [Fact] public void Ru_returns_russian() => WithCulture("ru", () => Assert.Equal("Список запуска", Strings.Get("Tab_Launch")));
    [Fact] public void Ar_returns_arabic() => WithCulture("ar", () => Assert.Equal("قائمة البدء", Strings.Get("Tab_Launch")));
    [Fact] public void Hi_returns_hindi() => WithCulture("hi", () => Assert.Equal("स्टार्टअप सूची", Strings.Get("Tab_Launch")));
    [Fact] public void Id_returns_indonesian() => WithCulture("id", () => Assert.Equal("Daftar startup", Strings.Get("Tab_Launch")));
    [Fact] public void Vi_returns_vietnamese() => WithCulture("vi", () => Assert.Equal("Danh sách khởi động", Strings.Get("Tab_Launch")));
    [Fact] public void Th_returns_thai() => WithCulture("th", () => Assert.Equal("รายการเริ่มต้น", Strings.Get("Tab_Launch")));
    [Fact] public void Tr_returns_turkish() => WithCulture("tr", () => Assert.Equal("Başlangıç listesi", Strings.Get("Tab_Launch")));
    [Fact] public void Nl_returns_dutch() => WithCulture("nl", () => Assert.Equal("Opstartlijst", Strings.Get("Tab_Launch")));

    [Fact]
    public void Untranslated_culture_falls_back_to_chinese()
        => WithCulture("sw", () => Assert.Equal("我的启动清单", Strings.Get("Tab_Launch")));   // 无 sw 卫星→回退中性(中文)

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
    public void KeyInput_warning_keys_resolve_and_format(string culture) => WithCulture(culture, () =>
    {
        // 键存在于所有卫星（非空、不等于键名回退），且占位符正确带入。
        foreach (var key in new[] { "Warn_KeyNoMain", "Warn_KeyMultiDigit", "Warn_KeyUnknown", "Warn_KeyRejected", "Warn_KeyPartial", "Warn_TextSendFail",
                                    "Tray_ViewLog", "Tray_NoLog", "Err_ClearClipboard", "Err_EmptyRecycleBin", "Err_UnknownSysCmd" })
            Assert.NotEqual(key, Strings.Get(key));
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
        Assert.Equal("Mon Tue Wed Thu Fri", StepDisplay.DaysLabel(new List<int> { 1, 2, 3, 4, 5 }));
        Assert.Equal("Every 3 days", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "everyNDays", IntervalDays = 3 }));
        Assert.Equal("At logon · before 8:00", ReminderDisplay.TimeLabel(new Reminder { Trigger = "startup", StartupHourMode = "before", StartupHour = 8 }));
        Assert.Equal("Scheduled Task", StartupLabels.TypeLabel("ScheduledTask"));
        Assert.Equal("All users (needs admin)", StartupLabels.ScopeLabel("Machine", true));
    });
}
