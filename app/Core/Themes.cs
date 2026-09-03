namespace Clockwork.Core;

// 主题：深色 / 浅色 / 跟随系统。
//
// 换主题换的只是一张颜色表（Palette.Dark.xaml / Palette.Light.xaml）——
// Theme.xaml 里的画笔用 DynamicResource 指过去，所以替换那份字典就是换主题，**不必重启**。
// 这一点和语言不同：换语言要重启，是因为一屋子控件的文案在构造时就取好了；
// 颜色不一样，画笔是共享实例，改它的 Color 全窗口跟着变。
public static class Themes
{
    /// <summary>合法取值。顺序即设置页下拉的顺序。</summary>
    public static readonly string[] All = { "dark", "light", "system" };

    public static string Normalize(string? theme)
        => Array.IndexOf(All, theme ?? "") >= 0 ? theme! : "dark";

    /// <summary>把设置解析成「现在到底该用深色还是浅色」。</summary>
    //
    // 「系统此刻偏好浅色吗」由调用方传进来，本方法不自己去问——那一问要读注册表，
    // 而 Core 是纯逻辑（见 CONTRIBUTING 的目录表）。答案在 Native.SystemTheme.PrefersLight()。
    //
    // 分开还有一个直接好处：这个判据变得**测得动**。它自己去问系统的那一版，用例只能写成
    // `Assert.Equal(!Themes.SystemPrefersLight(), Themes.IsDark("system"))`——把极性写反，
    // 等式两边一起翻，用例照旧绿，等于什么都没守。
    public static bool IsDark(string? theme, bool systemPrefersLight) => Normalize(theme) switch
    {
        "light" => false,
        "system" => !systemPrefersLight,
        _ => true,
    };
}
