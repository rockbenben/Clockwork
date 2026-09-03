using Clockwork.I18n;

namespace Clockwork.Core;

// 注入类动作（发键/发文本）的结果：成功但无法证实接收（Unverified，日志标「~ 已发送（未校验）」）、
// 或失败带原因（Warning，日志标「⚠ …」）、或无输出（Empty，如空文本/急停静默）。
// 窗口动作(close/min/max/activate)不用它——那返回「操作了几个窗口」的整数，由引擎按动作类型解读。
public sealed class ActionResult
{
    public bool Unverified { get; init; }

    // **存的是文案键与参数，不是渲染好的句子。**
    //
    // 同一条原因要以两种语言出现：给用户看的地方（气泡、clockwork.run.log）跟界面语言，
    // 而 clockwork.error.log 固定英文——那份是拿去贴 issue 的，读它的人未必读得懂用户那门语言。
    // 从前这里存的是 Strings.Lf(...) 的结果，键在构造那一刻就丢了，于是另一种语言再也拿不回来。
    // 同 Core/ActionGroupResolver 的 GroupSkip，理由一模一样。
    private readonly string? _key;
    private readonly object[] _args = [];

    /// <summary>有没有警告。**守卫一律用它，别用 `Warning != null`**——那样为了判空要把整句渲染一遍
    /// （StepRunner 那条判断之后还要再渲染一次，App 那条之后再渲染两次）。</summary>
    public bool HasWarning => _key != null;

    /// <summary>按当前界面语言渲染的原因；无警告时为 null。</summary>
    public string? Warning => Render(Strings.Get);

    /// <summary>固定英文渲染的原因（错误日志用）；无警告时为 null。</summary>
    public string? WarningEn => Strings.InEnglish(() => Render(Strings.Get));

    // **没有实参时走 Get，不走 Lf。** Lf 会调 string.Format，而 string.Format 对模板里
    // 落单的花括号是抛异常而不是原样输出——于是任意一门译文里多打一个 `{`，就能把
    // 「渲染一句警告」变成启动清单线程上的一个 FormatException。改之前这些调用点是
    // ActionResult.Warn(Strings.Get("Warn_AskNoUi"))，压根不经过格式化；没有实参的那几条
    // （Warn_AskNoUi / Warn_NoForegroundWindow / Warn_RestoreFailed）不该因为这次重构变脆。
    private string? Render(Func<string, string> get)
        => _key == null ? null : _args.Length == 0 ? get(_key) : string.Format(get(_key), _args);

    public static readonly ActionResult Empty = new();
    public static ActionResult Unver() => new() { Unverified = true };

    /// <summary>带原因的失败。<paramref name="key"/> 是 resx 键，不是已经渲染好的句子。</summary>
    //
    // 实参里若还套着另一条文案，用 Strings.Ref("那个键") 而不是 Strings.Get("那个键")——
    // 后者会在这一刻就把内层语言定死，英文那一份就成了「英文的框 + 用户语言的芯」。
    public static ActionResult Warn(string key, params object[] args) => new(key, args);

    private ActionResult() { }

    private ActionResult(string key, object[] args)
    {
        _key = key;
        _args = args;
    }
}
