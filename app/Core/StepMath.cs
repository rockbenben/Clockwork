using System.Globalization;

namespace Clockwork.Core;

/// <summary>一次算术的结果。失败时带一句人话，不用异常——调用方要把它当告警显示，不是当崩溃处理。</summary>
public readonly record struct ArithResult(bool Ok, string Value, string? Error)
{
    public static ArithResult Fail(string why) => new(false, "", why);
    public static ArithResult Good(string v) => new(true, v, null);
}

/// <summary>
/// 「算一步」的表达式求值。只认 <c>左 操作符 右</c> 这一种形状，运算为 + - * /。
///
/// **为什么不做完整的表达式引擎**：这里要解决的是「计数」——
/// 循环里的第几项、上一项加一、翻倍。完整的优先级、括号、函数、变量求值
/// （Quicker 的表达式引擎干的那一整套）会把「算个数」变成一门语言，
/// 而语言要有语法高亮、要有错误定位、要能被文档完整描述——为一个计数器不值得。
///
/// 形状单一换来的好处是**错误几乎只可能来自用户**：左操作数不是数、右边多了个字、
/// 或者除以零，三种。而不是「表达式第 7 个记号处出现了不该有的东西」那种没法说的话。
///
/// 两侧都允许是变量占位符：占位替换发生在本文件之外，所以这里拿到的已经是替换后的文本
/// （例如 `{当前ID}+1` 先变成 `3+1` 再进来）。
/// </summary>
public static class StepMath
{
    /// <summary>求值。失败时 Error 是**本地化之前的中文短语**，由调用方翻好再显示。</summary>
    public static ArithResult Evaluate(string? expr)
    {
        var t = (expr ?? "").Trim();
        if (t.Length == 0) return ArithResult.Fail("表达式为空");

        // 找运算符：**试每一个位置，取第一个左右两侧都读得出数字的那个。**
        //
        // 早先那版是「扫到第一个 + - * / 就用」，它在 "3 + 1"（带空格）上直接找不到——
        // 而带空格恰恰是用户最自然的写法。现在两侧都要能读成数，于是：
        //   "-3+1"  第 2 位的 +：左边 "-3"、右边 "1"    ✓（负号不会被当成运算符）
        //   "3 + 1" 第 3 位的 +：左边 "3 "、右边 " 1"  ✓
        //   "1e+5"  第 3 位的 +：左边 "1e" 读不出数 → 跳过，不把它算成 1+5
        int opAt = -1;
        for (int i = 1; i < t.Length; i++)
        {
            var c = t[i];
            if (c is not ('+' or '-' or '*' or '/')) continue;
            if (!TryNum(t[..i], out _)) continue;
            if (!TryNum(t[(i + 1)..], out _)) continue;
            opAt = i;
            break;
        }

        // 整个串自己就是一个数（"42"、"1e5"）：没有运算符，但也不是写错了——
        // 用户写它多半是想让这一步把常量送进变量。照原样给回去，比说「没有找到运算符」有用。
        if (opAt < 0)
        {
            if (TryNum(t, out var only)) return ArithResult.Good(Num(only));
            return ArithResult.Fail("没有找到运算符");
        }

        var left = t[..opAt].Trim();
        var op = t[opAt];
        var right = t[(opAt + 1)..].Trim();

        if (!TryNum(left, out var a)) return ArithResult.Fail($"左边读不出数字：{left}");
        if (!TryNum(right, out var b)) return ArithResult.Fail($"右边读不出数字：{right}");

        switch (op)
        {
            case '+': return ArithResult.Good(Num(a + b));
            case '-': return ArithResult.Good(Num(a - b));
            case '*': return ArithResult.Good(Num(a * b));
            case '/':
                if (Math.Abs(b) < 1e-12) return ArithResult.Fail("除数为 0");
                return ArithResult.Good(Num(a / b));
            default: return ArithResult.Fail("不认识的运算符");
        }
    }

    /// <summary>文本能不能读成数。</summary>
    public static bool TryNum(string? s, out double v)
    {
        v = 0;
        var t = (s ?? "").Trim();
        if (t.Length == 0) return false;
        // InvariantCulture：这一步算的是「个数 / 倍数 / 序号」，那不该随区域设置变。
        // 用当前文化的话，德语下 "1.5" 会被读成 15，而用户完全看不出发生了什么。
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>输出去掉多余的零：1.0 写成 1，2.50 写成 2.5。循环里累加出来的数不该带一串零。</summary>
    private static string Num(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-12 && Math.Abs(v) < 1e15)
            return ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        return v.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    // ---- 条件侧的比较 ----

    /// <summary>
    /// 比较两个文本，满足 op 则为真。op 空 = 不限制（调用方不该问）。
    ///
    /// 大于 / 小于**先试数值、试不成再比字符串**：用户写「{当前ID} &gt; 10」得到的是数值比较，
    /// 写「{文件名} &gt; b」比的是字符串（不区分大小写）。两种都要能写出来，
    /// 而它们在界面上是同一栏——所以判据是**能不能读成数**，不是用户选了哪个下拉。
    /// </summary>
    public static bool Compare(string? left, string? op, string? right)
    {
        var a = left ?? "";
        var b = right ?? "";
        var o = (op ?? "").Trim().ToLowerInvariant();
        return o switch
        {
            "eq" or "" => string.Equals(a, b, StringComparison.Ordinal),
            "ne" => !string.Equals(a, b, StringComparison.Ordinal),
            "contains" => a.Contains(b, StringComparison.Ordinal),
            "notcontains" => !a.Contains(b, StringComparison.Ordinal),
            "gt" or "lt" or "ge" or "le" => CompareOrdered(a, o, b),
            "empty" => a.Length == 0,
            "notempty" => a.Length > 0,
            _ => false,
        };
    }

    /// <summary>大小比较：两边都读得成数就按数比，否则按字符串比（不区分大小写）。</summary>
    // 之所以要两条路：用户写「{当前ID} &gt; 10」要的是数值比较（"9" 不大于 "10"），
    // 而写「{文件名} &gt; b」要比的是文字。它们在界面上是同一个下拉里的同一个选项，
    // 判据只能是「能不能读成数」，而不是让用户先声明类型——本程序没有类型系统。
    private static bool CompareOrdered(string a, string op, string b)
    {
        int c;
        if (TryNum(a, out var x) && TryNum(b, out var y)) c = x.CompareTo(y);
        else c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "gt" => c > 0,
            "lt" => c < 0,
            "ge" => c >= 0,
            "le" => c <= 0,
            _ => false,
        };
    }
}
