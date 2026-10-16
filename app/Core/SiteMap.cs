using System.IO;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 站点映射：把线上网址落成本地仓库里的源文件。
//
// 存在的理由是 VuePress / VitePress 这类静态站的一个共同形态：**路由和源文件路径不是一一对应**。
// 一个目录名带下划线是为了让构建器跳过它（`src/_posts/a.md` 不被当页面处理），
// 但路由照样会把那条下划线去掉（线上是 `/posts/a.html`）。于是线上 URL 与本地路径之间
// 隔着一层映射，而那层映射是**每个站点不同**的：有的剥扩展名，有的换目录名，
// 有的根路径落到 `index.md` 而不是 `README.md`。这些没法在步骤里写死。
//
// 所以这张表存在配置文件里（RootConfig.Sites），由本文件读。
// **它不在 RunVars 里**：那是一次运行内的变量，跑完即焚，而这张表是长期配置、要手改。
// 混进去就得改掉 RunVars 的作用域定义，那是另一件事（见 RunVars 的注释）。

/// <summary>一条路径改写规则。</summary>
// `match` 是正则，`replace` 是替换文本（.NET 语法，$1 这种分组引用可用）。
// 与 url2local 脚本的 pathRules 同构，方便两边共用同一份心智。
public sealed class SitePathRule
{
    public string Match { get; set; } = "";
    public string Replace { get; set; } = "";

    /// <summary>false = 这条不生效，留着想清楚再开。</summary>
    // 刻意用 bool? 而不是 bool：bool 的默认值 false 会让「没填」和「显式关掉」混成一档，
    // 而 config.json 是手改的，两个意思都得能表达。null = 生效。
    public bool? Enabled { get; set; }

    public bool On => Enabled != false;
}

/// <summary>一个站点：域名 + 本地源码根 + 路径改写规则。</summary>
public sealed class SiteEntry
{
    public string Name { get; set; } = "";

    /// <summary>精确匹配的域名。localhost:8080 这种带端口的写全。</summary>
    // 不用后缀通配：漏配一个真域名只是多写一行，而 example.com 匹配上
    // evil-example.com 才是事故。宁可在文档里说清楚，不给这个口子。
    public List<string> Hosts { get; set; } = new();

    /// <summary>本地源码根目录，末尾不带分隔符。</summary>
    public string SourceRoot { get; set; } = "";

    /// <summary>目录 URL（含站点根）落到哪个文件。</summary>
    public string IndexFile { get; set; } = "README.md";

    public List<SitePathRule> PathRules { get; set; } = new();
}

/// <summary>映射结果为什么失败——调用方据此挑一条人话，而不是把内部状态码透给用户。</summary>
public enum SiteMapError
{
    None,
    NotAbsoluteUrl,   // 压根不是网址
    NoMatchingSite,   // 域名没配
    BadSourceRoot,    // 配了但 sourceRoot 不存在
    NotFound,         // 映射算出来了，但文件不在
}

/// <summary>网址 → 本地路径的映射结果。</summary>
public sealed record SiteMapResult(SiteMapError Error, string Path, string SiteName)
{
    public bool Ok => Error == SiteMapError.None;
}

/// <summary>
/// 网址 → 本地源文件的纯映射。碰 Win32 / UIA / 剪贴板一概不碰，
/// 喂网址出路径，副作用全在调用方——与 SmartOpen 同一套分工，方便把整张判定表钉进单元测试。
/// </summary>
public static class SiteMap
{
    // Windows 文件名不许的字符。URL 路径里理论上能带（最常见的是冒号，%3A 解出来就是）。
    // **只清相对路径那一段**：整条一起清的话盘符的冒号会跟着没，D:\ 变 D_\。
    private static readonly char[] IllegalInName = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>按域名找站点。大小写不敏感；带端口的与不带端口的各试一次。</summary>
    // 两次尝试而不是只比一次：配置里写 "localhost:8080" 时用户填的网址是 http（默认端口 80），
    // Uri.Host 又**不带端口**（那是 Uri.Port / Authority 才带的），只比裸 host 会漏掉这条；
    // 反过来配置里只写 "localhost" 也该同时认 8080 与 80。两次各覆盖一头。
    public static SiteEntry? FindByHost(IEnumerable<SiteEntry>? sites, string host)
    {
        if (sites == null) return null;
        var h = (host ?? "").Trim();
        if (h.Length == 0) return null;

        foreach (var pass in new[] { true, false })
        {
            var want = pass ? h : h.Split(':')[0];
            foreach (var s in sites)
            {
                if (s?.Hosts == null) continue;
                foreach (var cand in s.Hosts)
                {
                    if (string.IsNullOrWhiteSpace(cand)) continue;
                    if (string.Equals(cand.Trim(), want, StringComparison.OrdinalIgnoreCase)) return s;
                }
            }
        }
        return null;
    }

    /// <summary>把一个网址映射成本地文件路径。失败时 Error 说明原因，Path 为空。</summary>
    public static SiteMapResult Resolve(IEnumerable<SiteEntry>? sites, string url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri))
            return new(SiteMapError.NotAbsoluteUrl, "", "");

        // scheme 是 http/https 才算「线上网址」。file:// 与 chrome:// 落到这里没有意义。
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != "http" && scheme != "https")
            return new(SiteMapError.NotAbsoluteUrl, "", "");

        // Uri.Host **不带端口**（8080 那种在 Uri.Port / Uri.Authority 上），所以带端口的写法
        // 单独再试一次——配置里写 "localhost:8080" 而用户填 http://localhost:8080/… 是本地预览的常态，
        // 只比裸 host 会让整条站点配置在开发期静默失联。
        var hostKey = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var site = FindByHost(sites, hostKey) ?? FindByHost(sites, uri.Host);
        if (site == null) return new(SiteMapError.NoMatchingSite, "", "");
        if (string.IsNullOrWhiteSpace(site.SourceRoot))
            return new(SiteMapError.BadSourceRoot, "", site.Name ?? "");

        var root = site.SourceRoot.Trim().TrimEnd('\\', '/');
        if (!Directory.Exists(root)) return new(SiteMapError.BadSourceRoot, "", site.Name ?? "");

        var full = Path.Combine(root, RelativePath(uri, site));
        if (!File.Exists(full) && !Directory.Exists(full))
            return new(SiteMapError.NotFound, full, site.Name ?? "");

        return new(SiteMapError.None, full, site.Name ?? "");
    }

    /// <summary>
    /// 网址路径 → 相对本地路径（正斜杠形态，不含根目录）。拆出来是为了能单独测。
    ///
    /// 顺序有讲究，每一步都在消掉一种 URL 形态：
    ///   1. UnescapeDataString —— 地址栏显示中文、剪贴板给的是 %E4%B8%AD，不解就拼出一个
    ///      名字里带 % 的文件，然后如实告诉你「文件不存在」，而真正的原因看不懂。
    ///   2. 去尾斜杠 → 补 indexFile（目录 URL 指目录下的那个文件）。
    ///   3. 剥 .html/.htm/.md 再补 .md（静态站的产物是 .html，源文件是 .md）。
    ///   4. pathRules —— 放最后，因为它可能要匹配前面几步的产物（比如 ^posts/ 只在去域名后成立）。
    /// </summary>
    internal static string RelativePath(Uri uri, SiteEntry site)
    {
        var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');

        var index = string.IsNullOrWhiteSpace(site.IndexFile) ? "README.md" : site.IndexFile.Trim();
        if (rel.Length == 0 || rel.EndsWith('/'))
        {
            rel = rel.TrimEnd('/');
            rel = rel.Length == 0 ? index : rel + "/" + index;
        }
        else
        {
            rel = Regex.Replace(rel, @"\.(html?|md)$", "", RegexOptions.IgnoreCase) + ".md";
        }

        foreach (var rule in site.PathRules)
        {
            if (rule == null || !rule.On || string.IsNullOrEmpty(rule.Match)) continue;
            try
            {
                rel = Regex.Replace(rel, rule.Match, rule.Replace ?? "");
            }
            catch (ArgumentException)
            {
                // 规则里的正则写错了就跳过这一条，不让整条规则把这一步拖死——
                // 用户要的是「打开这一页」，不是「因为第 3 条规则少个括号所以什么也不发生」。
                // 真正的问题是这条规则没生效，而下一步文件找不到会如实报出来。
            }
        }

        var segs = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segs.Length; i++)
            segs[i] = string.Join("", segs[i].Split(IllegalInName));

        return string.Join(Path.DirectorySeparatorChar, segs);
    }
}
