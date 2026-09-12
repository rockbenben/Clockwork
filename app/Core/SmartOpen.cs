using System.IO;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 一键直达的识别结果：类型 + 可以直接交给 ShellExecute（或 RegJump）的目标。
public enum QuickOpenKind { Url, Path, Magnet, Registry }

public sealed record QuickOpenTarget(QuickOpenKind Kind, string Target);

// 「选中文字是什么、该打开什么」的纯判定。RunAny 式一键直达的大脑：
// 不碰剪贴板、不碰进程——喂文字、出目标，副作用全在调用方（SystemCommands / RegJump）。
// 抽成纯函数也是为了把下面这张判定表整张钉进单元测试：这功能的质量全在边界识别上。
public static class SmartOpen
{
    private static readonly Regex MagnetRegex =
        new(@"^magnet:\?.+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FilePathRegex =
        new(@"^([a-zA-Z]:[\\/]|\\\\)", RegexOptions.Compiled);

    private static readonly Regex LineColRegex =
        new(@":\d+(?::\d+)?$", RegexOptions.Compiled);

    private static readonly Regex SchemeUrlRegex =
        new(@"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocalhostRegex =
        new(@"^localhost(?::\d+)?(?:[/?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegistryPrefixRegex =
        new(@"^(?:computer|my computer|计算机|此电脑|電腦|我的電腦)\s*[\\/]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegistryRootRegex =
        new(@"^(?<root>[A-Za-z_]+)(?<rest>[\\/].*)?$", RegexOptions.Compiled);

    // 判定入口。识别不了返回 null——调用方据此如实说一句，绝不猜一个打开。
    public static QuickOpenTarget? Classify(string? selection)
    {
        var t = Clean(selection);
        if (t.Length == 0) return null;

        // 1) 磁力：协议形态足够特殊，最先占。xt 之外还能带 dn/tr 等参数，只认 magnet:? 前缀。
        if (MagnetRegex.IsMatch(t))
            return new QuickOpenTarget(QuickOpenKind.Magnet, t);

        // 2) 注册表路径：HKLM\… / HKEY_LOCAL_MACHINE\…，可带「计算机\」「Computer\」前缀。
        var reg = NormalizeRegistryKey(t);
        if (reg != null) return new QuickOpenTarget(QuickOpenKind.Registry, reg);

        // 3) 文件系统路径：盘符 X:\、X:/ 或 UNC \\server\share。**必须排在 Uri.TryCreate 前面**——
        // 实测 .NET 会把 C:\x 这类盘符路径一律强转成 file URI，而且 LocalPath 把 :行号 后缀原样保留，
        // 排在它后面的话剥后缀逻辑永远轮不到。file:///D:/x 这种正斜杠 URL 形态过不了下面的形态正则，
        // 会自然落到第 4 步 URI 分支，两路不冲突。先展开 %VAR% 环境变量再判形态。
        var expandedPath = Environment.ExpandEnvironmentVariables(t);
        if (FilePathRegex.IsMatch(expandedPath))
        {
            var p = expandedPath;
            // 编辑器/日志复制常带「:行」「:行:列」后缀（C:\a\b.cs:42:10）。存在性先问原文：
            // 原文在（备用数据流等真实冒号场景）就别剥；原文不在再剥（剥完形态仍是根路径才认）。
            if (!PathExists(p))
            {
                var stripped = LineColRegex.Replace(p, "");
                if (stripped != p && FilePathRegex.IsMatch(stripped)) p = stripped;
            }
            return new QuickOpenTarget(QuickOpenKind.Path, p);
        }

        // 4) 带协议的 URI。http(s)/ftp 照开；file:// 落地成本地路径；其余 scheme:// 交给系统协议处理器。
        // 不认识的 URI 形态（example.com:443 会被 .NET 当成 scheme="example.com"）必须**落下去**继续判裸域名，
        // 不能在这一支直接 return null。
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)   // 单字母 scheme 是盘符
        {
            switch (uri.Scheme.ToLowerInvariant())
            {
                case "http": case "https": case "ftp":
                    return new QuickOpenTarget(QuickOpenKind.Url, t);
                case "file":
                    var local = Uri.UnescapeDataString(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(local))
                        return new QuickOpenTarget(QuickOpenKind.Path, local.TrimStart('/'));
                    break;
                default:
                    // mailto: 这类无 // 的也放行（TryCreate 已确认形态合法）；未知协议由系统报错。
                    if (SchemeUrlRegex.IsMatch(t) || uri.Scheme.ToLowerInvariant() is "mailto")
                        return new QuickOpenTarget(QuickOpenKind.Url, t);
                    break;
            }
        }

        // 5) 裸域名：example.com / www.example.com/path。形态之外还要求末段是认识的 TLD——
        // 否则 Clockwork.exe、archive.tar 这种「名字里有点」的普通文件名都会被误当网站打开。
        // localhost 没有点、过不了下面的域名正则，但开发时选中 localhost:8080 是常事。
        if (LocalhostRegex.IsMatch(t))
            return new QuickOpenTarget(QuickOpenKind.Url, "https://" + t);

        var dm = DomainRegex.Match(t);
        if (dm.Success && Tlds.Contains(dm.Groups["tld"].Value))
            return new QuickOpenTarget(QuickOpenKind.Url, "https://" + t);

        return null;
    }

    // 选区清洗。对应真实复制来源：
    //   多行选区（结尾常带换行）→ 取第一个非空行；
    //   终端/Markdown 里包着的 "<url>"、「url」、引号 → 去**成对**包裹（内部不动：Program Files (x86) 合法）；
    //   句末标点（网址贴在句子里复制出来带着 。,）→ 只去尾部分隔符，绝不动冒号/反斜杠（D:\ 会废）。
    private static string Clean(string? selection)
    {
        if (selection == null) return "";
        var line = "";
        foreach (var raw in selection.Split('\n'))
        {
            var l = raw.Trim().Trim('\r');
            if (l.Length > 0) { line = l; break; }
        }
        // 成对包裹符：按「首=开口、尾=对应收口」逐对剥，剥完重新 Trim（引号外可能还有空白）。
        var pairs = new (char Open, char Close)[]
        {
            ('"', '"'), ('\'', '\''), ('<', '>'), ('“', '”'), ('‘', '’'),
            ('「', '」'), ('『', '』'), ('【', '】'),
            ('(', ')'), ('（', '）'), ('[', ']'),
        };
        bool peeled = true;
        while (peeled && line.Length >= 2)
        {
            peeled = false;
            foreach (var (open, close) in pairs)
            {
                if (line[0] == open && line[^1] == close)
                {
                    line = line[1..^1].Trim();
                    peeled = true;
                    break;
                }
            }
        }
        // 尾部「贴在句子里」的标点。斜杠不剥（网址路径可能以 / 结尾），冒号不剥（盘根），
        // 括号也不在这儿单剥——wiki/Foo_(film) 这类合法 URL 以 ) 结尾，单剥会废 URL；包裹括号走上面成对剥。
        line = line.TrimEnd('.', ',', ';', '。', '，', '；', '、', '！', '!', '？', '?');
        return line;
    }

    // 注册表根：缩写 ↔ 长名（regedit 两种写法都认，落 LastKey 统一长名）。
    private static readonly Dictionary<string, string> RootMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCR"] = "HKEY_CLASSES_ROOT",
        ["HKCU"] = "HKEY_CURRENT_USER",
        ["HKLM"] = "HKEY_LOCAL_MACHINE",
        ["HKU"] = "HKEY_USERS",
        ["HKCC"] = "HKEY_CURRENT_CONFIG",
        ["HKPD"] = "HKEY_PERFORMANCE_DATA",
        ["HKEY_CLASSES_ROOT"] = "HKEY_CLASSES_ROOT",
        ["HKEY_CURRENT_USER"] = "HKEY_CURRENT_USER",
        ["HKEY_LOCAL_MACHINE"] = "HKEY_LOCAL_MACHINE",
        ["HKEY_USERS"] = "HKEY_USERS",
        ["HKEY_CURRENT_CONFIG"] = "HKEY_CURRENT_CONFIG",
        ["HKEY_PERFORMANCE_DATA"] = "HKEY_PERFORMANCE_DATA",
    };

    // 是注册表路径则返回规范化长名键；否则 null。前缀「Computer\ / 计算机\ / 此电脑\ / 電腦\」照剥
    // （regedit 地址栏复制自带它）；正斜杠折成反斜杠（键名本身不允许 /，折了无歧义）。
    public static string? NormalizeRegistryKey(string text)
    {
        var t = RegistryPrefixRegex.Replace(text, "");
        var m = RegistryRootRegex.Match(t);
        if (!m.Success || !RootMap.TryGetValue(m.Groups["root"].Value, out var full)) return null;
        var rest = m.Groups["rest"].Value.Replace('/', '\\').TrimEnd('\\');
        return full + rest;
    }

    private static bool PathExists(string p)
    {
        try { return File.Exists(p) || Directory.Exists(p); }
        catch { return false; }
    }

    // 裸域名：至少两段，末段纯字母（TLD 表只放字母后缀），后面可跟 :端口 或 /路径。
    // 不许空白与反斜杠；用户名式的 x@y.com 不拦（@ 不在正则内 → 不匹配，宁可漏判不误开）。
    private static readonly Regex DomainRegex =
        new(@"^(?<host>(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?<tld>[a-z]{2,}))(?::\d+)?(?:[/?#].*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 紧凑常用 TLD 表：宁可漏（用户补 https:// 即可）不可错——错的代价是把普通文件名当网站打开。
    private static readonly HashSet<string> Tlds = new(StringComparer.OrdinalIgnoreCase)
    {
        // 传统 gTLD
        "com", "net", "org", "edu", "gov", "mil", "int", "info", "biz", "name", "pro",
        // 新式常见 gTLD
        "io", "dev", "app", "ai", "co", "me", "tv", "cc", "xyz", "top", "vip", "shop",
        "club", "online", "site", "store", "tech", "cloud", "space", "website", "world",
        "wang", "win", "live", "link", "email", "click", "news", "blog", "today", "asia",
        // 常见国家/地区码
        "cn", "jp", "kr", "tw", "hk", "sg", "us", "uk", "de", "fr", "ru", "it", "es",
        "nl", "br", "in", "au", "ca", "eu", "ch", "se", "no", "fi", "dk", "pl", "tr",
    };
}
