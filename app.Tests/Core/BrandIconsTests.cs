using System.Linq;
using System.Windows.Media;
using Clockwork.Core;
using Xunit;

// 内置的品牌图标（Simple Icons，CC0；数据由 tools/gen_brand_icons.py 生成）。
// 这里钉的是「清单的规矩」和「配置里的写法怎么认」——画得好不好看只有眼睛能判，
// 那条路走 --shots 里的 IconPicker 与 QuickPanel 那几张。
public class BrandIconsTests
{
    // 选择器是 16 列 UniformGrid，品牌图标和字形共用同一个格子阵：总数缺角就看着像库坏了。
    [Fact]
    public void The_picker_grid_stays_whole()
    {
        Assert.Equal(0, BrandIcons.All.Count % 16);
        Assert.Equal(0, (BrandIcons.All.Count + IconLibrary.Glyphs.Length) % 16);
    }

    // slug 是配置格式的一部分：改了别人机器上的那一格就空了，所以只许加不许改。
    [Theory]
    [InlineData("wechat")]
    [InlineData("nodedotjs")]
    [InlineData("7zip")]
    [InlineData("vlcmediaplayer")]
    public void The_names_people_already_type_stay_in_the_list(string slug)
        => Assert.Contains(BrandIcons.All, i => i.Slug == slug);

    [Fact]
    public void Slugs_are_unique_and_bare_lowercase()
    {
        var slugs = BrandIcons.All.Select(i => i.Slug).ToArray();
        Assert.Equal(slugs.Length, slugs.Distinct().Count());
        Assert.All(slugs, s => Assert.Matches("^[a-z0-9][a-z0-9.-]*$", s));
    }

    // 每条轮廓都得能被 WPF 的迷你语言解析，而且带着非零环绕那一位（F0）——
    // 少了它，微信/QQ 这种带镂空的 logo 会把洞填成实心，而程序一声不吭。
    // 起手允许小写 m（相对 moveTo）：GitLab、Vercel 的官方 path 就是那么给的。
    [Fact]
    public void Every_path_parses_and_keeps_the_svg_fill_rule()
    {
        Assert.All(BrandIcons.All, i =>
        {
            Assert.Matches(@"^F0 [Mm]", i.Path);
            var g = Geometry.Parse(i.Path);
            Assert.False(g.Bounds.IsEmpty);
            Assert.True(g.Bounds.Width > 1 && g.Bounds.Height > 1, $"{i.Slug} 的轮廓小得不像图标");
        });
    }

    [Fact]
    public void Every_color_is_a_full_rgb()
        => Assert.All(BrandIcons.All, i => Assert.InRange(i.Hex, 0x000000u, 0xFFFFFFu));

    // 清单里有 8 枚的品牌色就是纯黑（X、Notion、Vercel、7-Zip、Steam、Rust、TikTok、Termius）：
    // 深色主题下它们看不见。判据是品牌色与**底色**的对比度，两头都要成立。
    [Theory]
    [InlineData(0x000000, 0xEAEAEA, 0xEAEAEA)]   // 黑 logo + 浅正文（= 深底）→ 换成正文色
    [InlineData(0x000000, 0x1A1A1A, 0x000000)]   // 黑 logo + 深正文（= 浅底）→ 保留黑色
    [InlineData(0xFFFFFF, 0x1A1A1A, 0x1A1A1A)]   // 白 logo + 浅底 → 换
    [InlineData(0xFFFFFF, 0xEAEAEA, 0xFFFFFF)]   // 白 logo + 深底 → 保留白色
    [InlineData(0x07C160, 0xEAEAEA, 0x07C160)]   // 微信绿在深底上 7.4:1 → 保留品牌色
    [InlineData(0x07C160, 0x1A1A1A, 0x07C160)]   // 在浅底上 2.4:1，仍过 2.0 那条线 → 也保留
    public void Near_background_colors_swap_to_the_foreground(uint brand, uint ink, uint expected)
        => Assert.Equal(expected, BrandIcons.ColorFor(brand, ink));

    // 换色判据不许把整张清单换成单色：真出问题时，一眼看得见的品牌色才是这个功能的意义。
    // 实测（阈值 2.0，落在两侧对比度分布的空隙里）：浅色主题换 4 枚，深色主题换 13 枚。
    [Fact]
    public void Most_brands_keep_their_own_color_on_both_themes()
    {
        const uint lightInk = 0x1A1A1A, darkInk = 0xEAEAEA;
        Assert.InRange(BrandIcons.All.Count(i => i.Hex == BrandIcons.ColorFor(i.Hex, lightInk)), 48, 64);
        Assert.InRange(BrandIcons.All.Count(i => i.Hex == BrandIcons.ColorFor(i.Hex, darkInk)), 48, 64);
    }

    // 认写法的这一层要宽：手打大小写、前后空格都算同一个人。
    [Theory]
    [InlineData("brand:wechat", "wechat")]
    [InlineData("brand:WECHAT", "wechat")]
    [InlineData("  brand:wechat  ", "wechat")]
    [InlineData("brand: wechat", "wechat")]
    public void The_brand_syntax_is_tolerant(string icon, string slug)
    {
        Assert.True(BrandIcons.TryGet(icon, out var found));
        Assert.Equal(slug, found.Slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("E756")]                       // 码位是字形，不是品牌
    [InlineData(@"D:\pics\wechat.png")]        // 路径也不是
    [InlineData("wechat")]                     // 少了前缀就别猜，那是个文件名
    public void Anything_else_is_not_a_brand_icon(string? icon)
        => Assert.False(BrandIcons.TryGet(icon, out _));

    // 一格图标的三种写法各走各的通道，互不吞掉。
    [Fact]
    public void Resolve_routes_the_three_spellings_apart()
    {
        Assert.Equal(PanelIconKind.Brand, PanelIcon.Resolve("brand:docker", "app").Kind);
        Assert.Equal("docker", PanelIcon.Resolve("brand:docker", "app").Value);
        Assert.Equal(PanelIconKind.Glyph, PanelIcon.Resolve("E756", "app").Kind);
        Assert.Equal(PanelIconKind.Image, PanelIcon.Resolve(@"D:\art\g.png", "app").Kind);
    }

    // 名字认不出来要就地回退成线描字形，**不能**掉进「当路径处理」那条：
    // 那会拿 "brand:nope" 去磁盘上找一趟文件图标（还是一次 IO），最后照样回退。
    [Fact]
    public void An_unknown_brand_name_falls_back_without_touching_the_disk()
    {
        var spec = PanelIcon.Resolve("brand:notarealapp", "app");
        Assert.Equal(PanelIconKind.Glyph, spec.Kind);
        Assert.Equal(PanelGlyph.ForKind("app"), spec.Value);
        Assert.DoesNotContain("brand:", spec.Value);
    }

    // 品牌那档也得带着回退字形：面板上那一格不能因为一个轮廓画不出来就空着。
    [Fact]
    public void A_brand_spec_still_carries_a_line_glyph_as_fallback()
    {
        var spec = PanelIcon.Resolve("brand:github", "app");
        Assert.False(string.IsNullOrWhiteSpace(spec.Fallback));
        Assert.Equal(1, spec.Fallback.Length);
        Assert.Equal(PanelGlyph.ForKind("app"), spec.Fallback);
    }

    // 引用一个组/清单项的格子（stepKind=null）同样能上品牌图标——图标字段与步骤类型无关。
    [Fact]
    public void A_brand_icon_works_on_a_group_tile_too()
        => Assert.Equal(PanelIconKind.Brand, PanelIcon.Resolve("brand:obsidian", null).Kind);
}
