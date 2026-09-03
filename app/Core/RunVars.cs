namespace Clockwork.Core;

/// <summary>一次运行里的变量。名字 → 一段文字，仅此而已。</summary>
//
// **作用域是「这一次运行」**：一次热键、一道手势、一次面板点击、一整个动作（含它引用的子动作）
// 共用一份，跑完就没。没有全局变量、不落盘、不跨动作——那些要回答「什么时候失效」「谁能改它」
// 「导出配置带不带它」一串问题，而目前一个用得上的场景都还没有。
//
// **只有字符串，没有类型。** Quicker 那套有文本/数字/布尔/列表/字典，是因为它还有表达式引擎和
// 循环模块要消费类型；这里没有那两样东西，加了类型就只是让每个输入框旁边多一个没人看的下拉。
//
// **名字大小写不敏感**，与 {clipboard} 同一条理由：这串东西是人手打进输入框的，
// 而「大小写写错了所以没替换」的表现是地址栏里赫然出现一个 {关键词}——看得见，但看不懂为什么。
//
// ponytail: 普通 Dictionary，不加锁。一次运行是顺序执行的（嵌套子动作也在同一个线程上跑完再回来），
// 两次并发的运行各拿各的一份。真出现「一次运行里有并行分支」那天再说。
public sealed class RunVars
{
    private readonly Dictionary<string, string> _v = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写入一个变量。名字为空即什么都不做——「输出到变量」留空是常态（那一步只是不产出值）。</summary>
    public void Set(string? name, string? value)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return;
        _v[n] = value ?? "";
    }

    /// <summary>取一个变量。**没有这个名字返回 null**，而不是空串——两者要分得清：
    /// 「这一步产出了空文字」该替换成空，「你把名字写错了」不该悄悄替换成空
    /// （那会得到一个打不开的地址，而用户看不出原因）。</summary>
    public string? Get(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Length > 0 && _v.TryGetValue(n, out var v) ? v : null;
    }

    /// <summary>现有的变量名。编辑器拿它列「这条动作里现在有哪些变量可用」。</summary>
    public IReadOnlyCollection<string> Names => _v.Keys;
}
