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

    // 发送键串的花括号是否成对。发送键框可自由打字（支持 {TAB}{ENTER}/字面文本），无法做完整的 SendKeys 语法校验；
    // 但「花括号不成对」（如 "{ENTE"）在运行时 SendWait 必抛——存盘前挡下这一明显笔误，合法序列必然平衡、不误伤。
    public static bool BracesBalanced(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        int depth = 0;
        foreach (var ch in s)
        {
            if (ch == '{') depth++;
            else if (ch == '}' && --depth < 0) return false;
        }
        return depth == 0;
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
