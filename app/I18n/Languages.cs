using System.Globalization;
using System.Linq;

namespace Clockwork.I18n;

// 已提供翻译(有对应 Strings.<code>.resx，或中性=中文)的语言清单。语言下拉从此读；
// 加一门语言 = 放一个 Strings.<code>.resx + 在此加一行。code 用 .NET 文化名。
public static class Languages
{
    public static readonly (string Native, string Code)[] All =
    {
        ("中文", "zh-CN"),
        ("繁體中文", "zh-TW"),
        ("English", "en"),
        ("日本語", "ja"),
        ("한국어", "ko"),
        ("Español", "es"),
        ("Français", "fr"),
        ("Deutsch", "de"),
        ("Italiano", "it"),
        ("Português", "pt"),
        ("Русский", "ru"),
        ("العربية", "ar"),
        ("हिन्दी", "hi"),
        ("Bahasa Indonesia", "id"),
        ("Tiếng Việt", "vi"),
        ("ไทย", "th"),
        ("Türkçe", "tr"),
        ("Nederlands", "nl"),
    };

    // 按系统显示语言挑一门支持的语言（「跟随系统」）。用 InstalledUICulture——系统安装的显示语言，
    // 不受本程序后续改 CurrentUICulture 影响。全域包一层 try：受限宿主上取文化失败也不抛，回退英文。
    public static string ResolveForSystem()
    {
        try { return ResolveFor(CultureInfo.InstalledUICulture); }
        catch { return "en"; }
    }

    // code 是否本清单里受支持的语言（大小写不敏感）。
    public static bool IsSupported(string? code)
        => code != null && All.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

    // 把配置里的 language 规范化到「必是 All 里的一门」的单一出处（App 落盘与 ApplyCulture 都走它）：
    // 空 → 跟随系统；已支持 → 规范大小写；其它有效文化 → 映射到最接近（pt-BR→pt、zh-Hant→zh-TW）；无效 → 跟随系统。
    // 这样送进 MainWindow 语言下拉的语言必能匹配，不会因「非空但不在列表」被下拉初始化打回 zh-CN 并重启。
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return ResolveForSystem();
        var hit = All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (hit.Code != null) return hit.Code;
        try { return ResolveFor(CultureInfo.GetCultureInfo(code)); } catch { return ResolveForSystem(); }
    }

    // 把任意文化映射到最接近的受支持语言 code：精确名 → 中文繁简分流 → 两字母 → 回退英文。纯函数、可测。
    public static string ResolveFor(CultureInfo ci)
    {
        var exact = All.FirstOrDefault(x => string.Equals(x.Code, ci.Name, StringComparison.OrdinalIgnoreCase));
        if (exact.Code != null) return exact.Code;                       // 如 zh-CN
        if (string.Equals(ci.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
        {
            var n = ci.Name.ToLowerInvariant();
            if (n.Contains("hant") || n.Contains("cht")) return "zh-TW";  // 繁体脚本优先（zh-Hant-* / 旧 zh-CHT）
            if (n.Contains("hans") || n.Contains("chs")) return "zh-CN";  // 简体脚本优先（zh-Hans-* 含 zh-Hans-HK / 旧 zh-CHS）
            return (n.Contains("-tw") || n.Contains("-hk") || n.Contains("-mo")) ? "zh-TW" : "zh-CN";  // 无脚本标记按地区
        }
        var two = All.FirstOrDefault(x => string.Equals(x.Code, ci.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        if (two.Code != null) return two.Code;                           // 如 en-US→en、de-DE→de、pt-BR→pt
        return "en";                                                     // 系统语言不在支持列表 → 英文（国际默认）
    }
}
