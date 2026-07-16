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
}
