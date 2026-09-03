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

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    /// <summary>在英文文化下取值。给 clockwork.error.log 用——那份日志是拿去贴 issue 的。</summary>
    //
    // **不能用 InvariantCulture 代替**：本项目的中性 resx（Strings.resx）就是中文源，
    // 所以「不变文化」拿到的是中文，不是英文。英文在 Strings.en.resx 里，必须显式指名 en。
    //
    // 为什么是「临时换文化」而不是给 Get/Lf 加一个 culture 参数：需要英文的地方是
    // StepDisplay.StepSummary 这类**层层调用 Strings.Get 的组合函数**，加参数得一路穿到底，
    // 每个新写的分支都得记着传。CurrentUICulture 是线程局部的，作用域内只做纯字符串格式化，
    // 出了 finally 一定还原——换文化在这里是最小的那个改动，不是偷懒。
    public static T InEnglish<T>(Func<T> render)
    {
        var save = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = English;
        try { return render(); }
        finally { CultureInfo.CurrentUICulture = save; }
    }

    /// <summary>当占位符的**实参本身也是一条文案**时，用它代替 <c>Get(key)</c>。</summary>
    //
    // 区别在渲染时机。`Lf("Warn_LaunchFail", label, Get("Err_ScriptMissing"))` 会在**调用那一刻**
    // 就把内层那条按当前语言定死；外层之后再用 InEnglish 渲染，也只能得到「英文的框 + 用户语言的芯」。
    // Ref 把内层推迟到 string.Format 调 ToString() 的那一刻，于是它跟着外层的文化走，整句一致。
    public readonly struct Ref(string key)
    {
        public override string ToString() => Get(key);
    }

    // 按 settings.language 设置进程 UI 文化。须在建任何窗口前调用（XAML 的 Loc 在加载时取当前文化）。
    public static void ApplyCulture(string? lang)
    {
        lang = Languages.Normalize(lang);   // 空/不支持/无效 → 规范到受支持 code（单一出处，与 App 落盘同口径）
        try
        {
            var ci = CultureInfo.GetCultureInfo(lang);
            CultureInfo.CurrentUICulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;
        }
        catch { }
    }
}
