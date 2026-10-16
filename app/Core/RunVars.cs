namespace Clockwork.Core;

/// <summary>一次运行里的变量。名字 → 一段文字或一个列表。</summary>
//
// **作用域是「这一次运行」**：一次热键、一道手势、一次面板点击、一整个动作（含它引用的子动作）
// 共用一份，跑完就没。没有全局变量、不落盘、不跨动作——那些要回答「什么时候失效」「谁能改它」
// 「导出配置带不带它」一串问题，而目前一个用得上的场景都还没有。
//
// **文本与列表分成两张表，不是一个带类型的字典。** 三种做法，这里选最笨的那种：
//   · Dictionary<string, object>  —— 每次取值都要判类型再强转，漏一处就是 InvalidCastException；
//   · 一个字符串 + 分隔符拼起来 —— 元素里带分隔符就再也切不回来；
//   · 两张 Dictionary            —— 取文本的代码一行都不用改（绝大多数步骤只关心文本），
//     取列表的代码得先问「有没有列表」再取值，漏问就是 null，而 null 在这里不是空列表。
//
// **为什么要列表**：循环要遍历的东西。以前这一层刻意只有文本（见下面的历史说明），
// 那个判断的前提是「没有循环与条件模块消费它」——现在两样都加了，前提不再成立。
//
// **名字大小写不敏感**，与 {clipboard} 同一条理由：这串东西是人手打进输入框的，
// 而「大小写写错了所以没替换」的表现是地址栏里赫然出现一个 {关键词}——看得见，但看不懂为什么。
//
// ponytail: 普通 Dictionary，不加锁。一次运行是顺序执行的（嵌套子动作也在同一个线程上跑完再回来），
// 两次并发的运行各拿各的一份。真出现「一次运行里有并行分支」那天再说。
public sealed class RunVars
{
    private readonly Dictionary<string, string> _v = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 写入一个文本变量。同名时**列表那份一并清掉**（见 <see cref="SetList"/>）。
    /// </summary>
    public void Set(string? name, string? value)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return;
        _v[n] = value ?? "";
        _lists.Remove(n);
    }

    /// <summary>
    /// 写入一个列表变量。同名时**文本那份一并清掉**（反之亦然）——
    /// 一个名字同时是文本和列表，之后每个读它的人都要先猜这一轮拿到的是哪种，
    /// 而猜错的那个分支在界面上完全看不出来。
    /// </summary>
    public void SetList(string? name, IReadOnlyList<string>? items)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return;
        _lists[n] = items ?? Array.Empty<string>();
        _v.Remove(n);
    }

    /// <summary>
    /// 取一个变量。**没有这个名字返回 null**，而不是空串——两者要分得清：
    /// 「这一步产出了空文字」该替换成空，「你把名字写错了」不该悄悄替换成空
    /// （那会得到一个打不开的地址，而用户看不出原因）。
    /// </summary>
    public string? Get(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return null;
        // 名字被当成列表用过时返回 null：文本地方读到一个列表，最诚实的回答是「这里没有文本」。
        return _v.TryGetValue(n, out var v) ? v : null;
    }

    /// <summary>取一个列表变量；不是列表就返回 null（理由同 <see cref="Get"/>）。</summary>
    public IReadOnlyList<string>? GetList(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Length > 0 && _lists.TryGetValue(n, out var v) ? v : null;
    }

    /// <summary>这个变量在本次运行里存过东西吗（文本或列表都算）。</summary>
    public bool Has(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Length > 0 && (_v.ContainsKey(n) || _lists.ContainsKey(n));
    }

    /// <summary>现有的变量名。编辑器拿它列「这条动作里现在有哪些变量可用」。</summary>
    public IReadOnlyCollection<string> Names
    {
        get
        {
            var all = new HashSet<string>(_v.Keys, StringComparer.OrdinalIgnoreCase);
            all.UnionWith(_lists.Keys);
            return all;
        }
    }

    /// <summary>列表里共有几个变量。编辑器据此把「列表」单独列出来，而不是混在变量名里让人猜。</summary>
    public IReadOnlyCollection<string> ListNames => _lists.Keys;

    /// <summary>
    /// 把给定的几个变量名存档，返回的东西 Dispose 时原样还回去。
    ///
    /// **只管传进来的那几个名字，不是有界的整个表。** 循环体里产出的其它变量（累加、收集结果）
    /// 必须留在外面——那正是循环的常见用法。真正会被外层改坏的是循环变量本身：
    /// 内层循环一跑，外层的「当前项」就没了，而嵌套循环里两个 item 同名。
    ///
    /// 用法：<c>using (vars.Scope(names)) { … }</c>。中途抛异常也会还回去（using 的语义）。
    /// </summary>
    public IDisposable Scope(params string?[] names)
    {
        var saved = new List<(string Name, string? Text, IReadOnlyList<string>? List)>();
        foreach (var raw in names ?? Array.Empty<string?>())
        {
            var n = (raw ?? "").Trim();
            if (n.Length == 0) continue;
            _v.TryGetValue(n, out var t);
            _lists.TryGetValue(n, out var l);
            saved.Add((n, t, l));
        }
        return new Restore(this, saved);
    }

    private sealed class Restore(RunVars owner, List<(string, string?, IReadOnlyList<string>?)> saved) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (n, t, l) in saved)
            {
                if (t != null) { owner._v[n] = t; owner._lists.Remove(n); }
                else if (l != null) { owner._lists[n] = l; owner._v.Remove(n); }
                else { owner._v.Remove(n); owner._lists.Remove(n); }
            }
        }
    }
}
