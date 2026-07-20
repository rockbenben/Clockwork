using System.Text;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 解析结果：修饰键集合 + 主键 + 是否用 Win。
public sealed class ParsedCombo
{
    public List<string> Modifiers { get; init; } = new();
    public string? Key { get; init; }
    public bool UseWin { get; init; }
}

// 键解析与 SendKeys 转换——纯字符串逻辑，不引用 WinForms/WPF。
public static class KeyCombo
{
    public static ParsedCombo ParseCombo(string combo)
    {
        var mods = new List<string>();
        string? key = null;
        foreach (var part in (combo ?? "").Split('+'))
        {
            var t = part.Trim();
            switch (t.ToLowerInvariant())
            {
                case "win": mods.Add("Win"); break;
                case "ctrl": mods.Add("Ctrl"); break;
                case "control": mods.Add("Ctrl"); break;
                case "alt": mods.Add("Alt"); break;
                case "shift": mods.Add("Shift"); break;
                case "": break;
                default: key = t; break;
            }
        }
        return new ParsedCombo { Modifiers = mods, Key = key, UseWin = mods.Contains("Win") };
    }

    private static readonly Dictionary<string, string> NamedSendKeys = new()
    {
        ["enter"] = "{ENTER}", ["return"] = "{ENTER}", ["tab"] = "{TAB}", ["esc"] = "{ESC}", ["escape"] = "{ESC}", ["space"] = " ",
        ["backspace"] = "{BACKSPACE}", ["bs"] = "{BACKSPACE}", ["del"] = "{DEL}", ["delete"] = "{DEL}", ["ins"] = "{INS}", ["insert"] = "{INS}",
        ["home"] = "{HOME}", ["end"] = "{END}", ["pgup"] = "{PGUP}", ["pageup"] = "{PGUP}", ["pgdn"] = "{PGDN}", ["pagedown"] = "{PGDN}",
        ["up"] = "{UP}", ["down"] = "{DOWN}", ["left"] = "{LEFT}", ["right"] = "{RIGHT}", ["printscreen"] = "{PRTSC}", ["prtsc"] = "{PRTSC}",
        ["back"] = "{BACKSPACE}", ["capslock"] = "{CAPSLOCK}",   // 捕获给的 WPF 名（Back/CapsLock）也要认，否则捕获对话框看似无响应
    };

    // 解析结果 → SendKeys 字符串（非 Win 组合用）。单字母转小写（避免 SendKeys 把大写当 Shift）。
    public static string? ToSendKeysString(ParsedCombo p)
    {
        var prefix = "";
        if (p.Modifiers.Contains("Ctrl")) prefix += "^";
        if (p.Modifiers.Contains("Alt")) prefix += "%";
        if (p.Modifiers.Contains("Shift")) prefix += "+";
        var k = p.Key ?? "";
        if (k.Length == 1 && Regex.IsMatch(k, "[A-Za-z]")) k = k.ToLowerInvariant();
        else if (Regex.IsMatch(k, "^[Ff](1[0-6]|[1-9])$")) k = "{" + k.ToUpperInvariant() + "}";  // 功能键补花括号（SendKeys 支持到 F16）
        else if (NamedSendKeys.TryGetValue(k.ToLowerInvariant(), out var named)) k = named;
        else if (k.Length > 1 && !k.StartsWith("{")) return null;  // 多字符不认且无花括号 → 拒发
        return prefix + k;
    }

    // 窗口步骤「发送按键」能否忠实编码（SendKeys 路径）：Win 不支持，主键须能转 SendKeys 记号。
    // 编码不了会退回原样字符串、被逐字打进目标窗口——故「捕获」按钮先用此校验，拒收编码不了的键。
    public static bool CanEncodeForSendKeys(string combo)
    {
        var p = ParseCombo(combo);
        return !p.UseWin && !string.IsNullOrEmpty(p.Key) && ToSendKeysString(p) != null;
    }


    // 窗口步骤「发送按键」内容的宽容解析：带花括号/非组合形态原样；组合写法自动转 SendKeys；转不出退回原样。
    public static string ToSendKeysSequence(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (Regex.IsMatch(raw, "[{}]") || !Regex.IsMatch(raw, @"^[A-Za-z0-9]+(\+[A-Za-z0-9]+)*$")) return raw;
        var p = ParseCombo(raw);
        if (p.UseWin || string.IsNullOrEmpty(p.Key)) return raw;   // SendKeys 不支持 Win 键
        var s = ToSendKeysString(p);
        return s ?? raw;
    }

    // SendKeys 花括号关键字全集（.NET WinForms SendKeys 关键字表；大小写不敏感）。
    // 注意 ESC/ESCAPE、DEL/DELETE 等双写法都在表里，漏一个就会拒存能正常运行的步骤。
    private static readonly HashSet<string> SendKeysKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BACKSPACE", "BS", "BKSP", "BREAK", "CAPSLOCK", "CLEAR", "DEL", "DELETE", "DOWN", "END",
        "ENTER", "ESC", "ESCAPE", "HELP", "HOME", "INS", "INSERT", "LEFT", "NUMLOCK", "PGDN", "PGUP",
        "PRTSC", "RIGHT", "SCROLLLOCK", "TAB", "UP", "ADD", "SUBTRACT", "MULTIPLY", "DIVIDE",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15", "F16",
    };

    // 「发送按键」内容是否为合法 SendKeys 序列——逐条镜像 WinForms SendKeys.ParseKeys 的真实行为
    //（以 dotnet/winforms 源码为准，不是文档、更不是猜测），只拦 SendWait 必抛的串。
    // 必抛而拦：花括号组未闭合/空键名、未知多字符键名、次数缺数字或数字后不紧跟 }、组外孤立 }、
    //          圆括号不配对、圆括号嵌套超过 3 层。
    // 合法而放行：字面转义（{{} {}}）、「{} n}」发 n 个字面 } 的特例、单字符组（{a} {%}）、
    //          重复次数（{LEFT 4}：恰好一个空白 + 纯数字）、圆括号嵌套 ≤3 层。
    // 宁可漏拦不可误伤——误伤会让能正常运行的旧步骤连保存都过不去（旧「花括号计数」校验正是因此被移除）。
    public static bool IsValidSendKeys(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{')
            {
                i = BraceGroupEnd(s, i);
                if (i < 0) return false;
            }
            else if (c == '}') return false;                  // 组外孤立 }
            else if (c == '(') { if (++depth > 3) return false; }   // 真实解析器 cGrp>3 才抛（SendKeysNestingError）
            else if (c == ')') { if (--depth < 0) return false; }   // 无起括号的 )
        }
        return depth == 0;   // 未闭合的 ( 亦必抛
    }

    // 从 { 起解析一个花括号组，合法则返回收尾 } 的下标，否则 -1。与真实解析器同构：
    // ① 紧随 { 的 } 且后文还有收尾 } → 属「{} n}」特例，这个 } 是键名本身（{}} 也走此分支）；
    // ② 键名取到第一个空白或 }；③ 有空白则恰好跳过一个，随后必须是 1+ 个数字并紧跟 }。
    private static int BraceGroupEnd(string s, int open)
    {
        int j = open + 1;
        if (j + 1 < s.Length && s[j] == '}')
        {
            int final = j + 1;
            while (final < s.Length && s[final] != '}') final++;
            if (final < s.Length) j++;   // 键名就是这个 }，从它后面找次数/收尾
        }
        while (j < s.Length && s[j] != '}' && !char.IsWhiteSpace(s[j])) j++;
        if (j >= s.Length) return -1;
        var name = s.Substring(open + 1, j - (open + 1));
        if (char.IsWhiteSpace(s[j]))
        {
            j++;   // 恰好一个空白：真实解析器不容忍双空格/尾随空格/正负号（都会抛 InvalidSendKeysRepeat）
            int d = j;
            while (j < s.Length && char.IsDigit(s[j])) j++;
            if (j == d) return -1;
        }
        if (j >= s.Length || s[j] != '}') return -1;
        if (name.Length == 0) return -1;
        if (name.Length > 1 && !SendKeysKeywords.Contains(name)) return -1;
        return j;
    }

    // 字面文本 → SendKeys 序列：转义元字符（+ ^ % ~ ( ) [ ] { }），换行→{ENTER}，Tab→{TAB}。
    public static string ToSendKeysLiteral(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        var s = text.Replace("\r\n", "\n");   // 先归一 CRLF→LF，避免 {ENTER}{ENTER}
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\n': sb.Append("{ENTER}"); break;
                case '\t': sb.Append("{TAB}"); break;
                case '+': sb.Append("{+}"); break;
                case '^': sb.Append("{^}"); break;
                case '%': sb.Append("{%}"); break;
                case '~': sb.Append("{~}"); break;
                case '(': sb.Append("{(}"); break;
                case ')': sb.Append("{)}"); break;
                case '[': sb.Append("{[}"); break;
                case ']': sb.Append("{]}"); break;
                case '{': sb.Append("{{}"); break;
                case '}': sb.Append("{}}"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
