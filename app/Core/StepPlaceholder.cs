using System.Text.RegularExpressions;

namespace Clockwork.Core;

/// <summary>步骤文本里的占位符：<c>{名字}</c> 取一个变量，<c>{clipboard}</c> 取此刻剪贴板里的文字。</summary>
//
// **骨架照着 Quicker：能产出值的步骤有一个「输出到变量」，后面的步骤在文本参数里用花括号引用它。**
// 没抄的是它的类型系统（文本/数字/列表/字典）和表达式引擎——那两样是为了喂它的循环与条件模块，
// 这里没有那两个消费方，加了只会让每个输入框旁边多一个没人看的下拉。所以：**只有字符串**。
//
// `{clipboard}` 是唯一的内置名，读的是真剪贴板，不是变量表里的东西。留着它有两个理由：
// 存量配置里已经写着它；而「选中的文字」本来就常常只在剪贴板里（别的程序刚复制的东西，
// 没有哪一步「产出」过它）。
//
// 只在**送出去的文字**上替换（打开网址的地址、发送文本、系统命令的文本参数），不做成全局：
//   「启动程序」的路径与参数里替换是**注入面**——变量内容会变成命令行的一部分；
//   按键组合、进程名里替换毫无意义。
public static class StepPlaceholder
{
    /// <summary>内置名：此刻剪贴板里的文字。</summary>
    public const string Clipboard = "clipboard";

    // 花括号里一段不含花括号的东西。**不限制字符集**：变量名是中文、带空格、带点都随用户去，
    // 我们只负责按名字去表里查——限制字符集只会让「为什么我的 {关键词 2} 不生效」变成一个新问题。
    //
    // 排除掉花括号本身，`{0}` 这类既有的模板占位（搜索地址里那个）也就自然落进「查不到这个名字」，
    // 而查不到就原样留着，不会被吃掉。
    private static readonly Regex Token = new(@"\{([^{}]*)\}", RegexOptions.Compiled);

    /// <summary>这段文本里有没有占位符。有才值得去取值——没有占位符的步骤一次都不该读剪贴板。</summary>
    public static bool Has(string? text) => !string.IsNullOrEmpty(text) && Token.IsMatch(text);

    /// <summary>这段文本里有没有用到剪贴板。单独一问是因为**读剪贴板要跳一次 STA 线程**，
    /// 而绝大多数占位符引的是变量表里的东西，不该为它们付那笔钱。</summary>
    public static bool UsesClipboard(string? text)
        => !string.IsNullOrEmpty(text)
           && Token.Matches(text).Any(m => string.Equals(m.Groups[1].Value.Trim(), Clipboard, StringComparison.OrdinalIgnoreCase));

    /// <summary>把占位符换成对应的值。</summary>
    /// <param name="vars">变量表；null = 这条路径上没有变量（只认 {clipboard}）。</param>
    /// <param name="clipboard">{clipboard} 的值。调用方先取好——这里不碰系统 API，好留给测试。</param>
    /// <param name="urlEncode">true = 按 URL 查询参数转义（打开网址那一档）；false = 原样（发送文本）。</param>
    //
    // 转义与否按**目的地**决定，不按内容决定：进地址栏的必须转义（选中的话里有空格、&amp;、中文，
    // 不转义拼出来的地址要么打不开、要么把后半截参数吃掉），而「发送文本」是照原样打进窗口，
    // 转义了反而会把一段中文变成一串 %E4%B8%AD。
    //
    // 换行压成空格：取到的往往是**一段**而不是一个词，而这两个目的地都不接受换行——
    // 地址栏会把它编码成 %0A，发送文本则会在半路敲一个回车（可能直接把命令提交了）。
    //
    // **查不到的名字原样留着**，这是与「值是空的」刻意分开的一档：
    //   值是空的 → 换成空串（留着 {clipboard} 只会让人看见一个打不开的地址，还以为是自己语法写错了）；
    //   名字不存在 → 那多半就是**写错了名字**，把 {关键词} 原样显示在地址栏里，
    //     用户一眼看得见哪个词没生效；悄悄换成空串则只留下一个空搜索，无从查起。
    public static string Apply(string? text, string? clipboard, bool urlEncode, RunVars? vars = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return Token.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            string? v = string.Equals(name, Clipboard, StringComparison.OrdinalIgnoreCase)
                ? (clipboard ?? "")
                : vars?.Get(name);
            if (v == null) return m.Value;   // 没有这个名字：原样留着，让写错的那一下看得见
            var flat = string.Join(" ", v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return urlEncode ? Uri.EscapeDataString(flat) : flat;
        });
    }
}
