using System.Globalization;
using System.Resources;

namespace Clockwork.I18n;

// 本地化字符串查找。resx 卫星程序集：中性(Strings.resx)=中文源、Strings.en.resx=英文；按 CurrentUICulture 取，
// 未找到回退中性(中文)。M7 补其余语言时各加一个 Strings.<lang>.resx 即可。
public static class Strings
{
    private static readonly ResourceManager Rm = new("Clockwork.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    // 取本地化模板并格式化占位符。数字/日期按当前区域(CurrentCulture)格式化——.NET 惯例：
    // UICulture 选文案语言、Culture 定数值格式；本应用只改 UICulture、区域保持系统设置。
    // App 与 MainWindow 共用，避免各写一份。
    public static string Lf(string key, params object[] args)
        => string.Format(Get(key), args);

    // 当前 UI 文化是否从右向左（阿拉伯语等）。窗口据此设 FlowDirection。
    public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    // 按 settings.language 设置进程 UI 文化。须在建任何窗口前调用（XAML 的 Loc 在加载时取当前文化）。
    public static void ApplyCulture(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) lang = "zh-CN";
        try
        {
            var ci = CultureInfo.GetCultureInfo(lang);
            CultureInfo.CurrentUICulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;
        }
        catch { }
    }
}
