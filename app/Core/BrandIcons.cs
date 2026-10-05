using System.Collections.Generic;

namespace Clockwork.Core;

// 品牌图标：64 枚常用程序的矢量轮廓，数据在 BrandIcons.g.cs（Simple Icons，CC0，
// 重新生成见 tools/gen_brand_icons.py）。
//
// 为什么把 path 编进源码，而不是打包一套图标字体：单文件发布的 exe 旁边没有资源目录，
// 图标字体要么占体积要么得注册；而一条 path + 一个颜色就是一个图标——矢量、不受 DPI 影响、
// 想上品牌色就上品牌色。代价是这 64 枚约 70KB 的字符串进了程序集，换掉的是零外部依赖。
//
// 配置里存的是 `brand:wechat` 而不是第几号字形：字体子集里第 47 格是谁，换一版就可能变，
// 名字不会。所以清单里的 slug 是配置格式的一部分，**只能加不能改**——改了别人机器上的格子就空了。
public static partial class BrandIcons
{
    /// <summary>图标字段里认这个前缀；其余写法照旧（四位十六进制=字形，别的=路径）。</summary>
    public const string Prefix = "brand:";

    /// <summary>一枚品牌图标。Path 已经带上 `F0 ` 前缀（非零环绕），可直接交给 Geometry.Parse。</summary>
    public readonly record struct Icon(string Slug, string Title, uint Hex, string Path);

    /// <summary>选择器铺格子用的全量清单，顺序即铺开的顺序。</summary>
    public static IReadOnlyList<Icon> All { get; }

    private static readonly Dictionary<string, Icon> BySlug;

    // 走静态构造而不是字段初始化器：Entries 住在生成的那一半 partial 里，
    // 而字段初始化器是按文件先后顺序跑的——`All` 会先于 `Entries` 跑到，拿到的是 null。
    static BrandIcons()
    {
        var list = new List<Icon>(Entries.Length);
        var map = new Dictionary<string, Icon>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entries)
        {
            var icon = new Icon(e.Slug, e.Title, e.Hex, "F0 " + e.Path);
            list.Add(icon);
            map[icon.Slug] = icon;
        }
        All = list;
        BySlug = map;
    }

    /// <summary>这串写法是不是想指一个品牌图标（哪怕名字认不出来）。
    /// 认不出的名字要在这里就判掉，别让 "brand:nope" 被当成文件路径去磁盘上找一趟。</summary>
    public static bool IsBrandSyntax(string? icon) =>
        (icon ?? "").Trim().StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>`brand:wechat` → 图标。写法不对或名字不在清单里都返回 false（调用方回退线描字形）。</summary>
    public static bool TryGet(string? icon, out Icon found)
    {
        found = default;
        var s = (icon ?? "").Trim();
        if (!IsBrandSyntax(s)) return false;
        return BySlug.TryGetValue(s[Prefix.Length..].Trim(), out found);
    }

    /// <summary>这一枚该用什么颜色画（RRGGBB）。
    ///
    /// 品牌色是官方给的，但清单里有 8 枚就是纯黑（X、Notion、Vercel、7-Zip、Steam、Rust、TikTok、
    /// Termius）——深色主题下它们会和底色糊成一片，那一格看着像坏了。反过来近白的轮廓在浅色主题下
    /// 同样会消失。所以判据不能是「够不够黑」，得是**和品牌色与底色的对比度**：
    /// 拉不到 WCAG 给图形对象的那条 3:1，就换成正文色——正文色是这套调色板里保证拉得到的那个
    /// （调色板顶上的注释自己就把每档对比度算过一遍）。
    ///
    /// 传进来的是前景色：底色按主题取实值（深底 #191919 / 浅底 #FFFFFF，就是两版调色板的 Slate），
    /// 前景亮 ⇒ 底暗。代价说清楚：深色主题下会有 13 枚变成单色，那是「看得见」优先于「官方色」。
    ///
    /// 2.0 这条线不是拍的：把 64 枚按对比度排开，深底那侧从 1.76（autohotkey）直接跳到 2.22
    /// （dotnet），浅底那侧从 1.99（supabase）跳到 2.21（qq）——两个空隙都装着 2.0。
    /// 线画在空隙里，才不会今天换这枚、明天换那枚。</summary>
    public static uint ColorFor(uint brandHex, uint foregroundHex)
    {
        uint surface = Luma(foregroundHex) > 0.5 ? 0x191919u : 0xFFFFFFu;
        return Contrast(brandHex, surface) < 2.0 ? foregroundHex : brandHex;
    }

    /// <summary>两个颜色的对比度（WCAG：(亮+0.05) / (暗+0.05)），1..21。</summary>
    public static double Contrast(uint a, uint b)
    {
        double x = Luma(a), y = Luma(b);
        return (System.Math.Max(x, y) + 0.05) / (System.Math.Min(x, y) + 0.05);
    }

    /// <summary>sRGB 的相对亮度（线性化后 0.2126R + 0.7152G + 0.0722B），0..1。</summary>
    private static double Luma(uint hex)
    {
        static double Lin(double channel)
            => channel <= 0.03928 ? channel / 12.92 : System.Math.Pow((channel + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(((hex >> 16) & 0xFF) / 255.0)
             + 0.7152 * Lin(((hex >> 8) & 0xFF) / 255.0)
             + 0.0722 * Lin((hex & 0xFF) / 255.0);
    }
}
