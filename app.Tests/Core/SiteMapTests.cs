using System.IO;
using Clockwork.Core;
using Xunit;

// 「看见线上文章、打开它的本地源文件」那条链路的纯函数部分。
//
// 映射逻辑刻意不碰 UIA / 剪贴板 / 进程——那部分在 Native.BrowserUrlReader 与 StepRunner 里，
// 副作用全在那边。这张判定表能整张钉在这里，是因为它本来就是纯的：喂网址，出路径。
//
// 临时目录是真的建出来的：Exists 与 BadSourceRoot 两支判的就是文件系统的事实，
// 用假的根目录去测，那两支就永远走不到。
public class SiteMapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-sitemap-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _learnPosts;

    public SiteMapTests()
    {
        // <root>/posts/2024-03-01-blog_example.md + <root>/README.md
        _learnPosts = Path.Combine(_root, "_posts");
        Directory.CreateDirectory(_learnPosts);
        File.WriteAllText(Path.Combine(_learnPosts, "2024-03-01-blog_example.md"), "x");
        File.WriteAllText(Path.Combine(_root, "README.md"), "x");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    // VuePress 这套站点的真实映射：线上 /posts/x.html，本地 src/_posts/x.md
    // ——路由把目录名前的下划线去掉了，靠一条 pathRule 补回来。
    private SiteEntry Blog => new()
    {
        Name = "LearnData",
        Hosts = { "newzone.top", "www.newzone.top", "localhost:8080" },
        SourceRoot = _root,
        IndexFile = "README.md",
        PathRules = { new SitePathRule { Match = "^posts/", Replace = "_posts/" } },
    };

    private static SiteMapResult R(SiteEntry s, string url) => SiteMap.Resolve(new[] { s }, url);

    [Theory]
    [InlineData("https://newzone.top/posts/2024-03-01-blog_example.html")]
    [InlineData("https://www.newzone.top/posts/2024-03-01-blog_example.html")]
    [InlineData("http://localhost:8080/posts/2024-03-01-blog_example.html")]
    public void A_post_maps_to_the_underscored_folder(string url)
    {
        var r = R(Blog, url);
        Assert.True(r.Ok, r.Error.ToString());
        Assert.Equal(Path.Combine(_learnPosts, "2024-03-01-blog_example.md"), r.Path);
    }

    // 问号和井号在 AbsolutePath 之外，不剥掉的话会被当成文件名的一部分。
    [Theory]
    [InlineData("https://newzone.top/posts/2024-03-01-blog_example.html?x=1")]
    [InlineData("https://newzone.top/posts/2024-03-01-blog_example.html#frag")]
    public void Query_and_hash_do_not_reach_the_filename(string url)
    {
        var r = R(Blog, url);
        Assert.True(r.Ok, r.Error.ToString());
        Assert.Equal(Path.Combine(_learnPosts, "2024-03-01-blog_example.md"), r.Path);
    }

    [Theory]
    [InlineData("https://newzone.top/")]
    [InlineData("https://newzone.top")]
    public void The_site_root_lands_on_the_index_file(string url)
    {
        var r = R(Blog, url);
        Assert.True(r.Ok, r.Error.ToString());
        Assert.Equal(Path.Combine(_root, "README.md"), r.Path);
    }

    // 地址栏里显示中文、剪贴板给的是 %E4%B8%AD。不解就会得到一个名字里带 % 的文件。
    [Fact]
    public void Percent_encoded_chinese_becomes_real_characters()
    {
        var site = new SiteEntry
        {
            Name = "s", Hosts = { "newzone.top" }, SourceRoot = _root,
            PathRules = { new SitePathRule { Match = "^zh/", Replace = "zh/" } },
        };
        Directory.CreateDirectory(Path.Combine(_root, "zh"));
        File.WriteAllText(Path.Combine(_root, "zh", "中文名.md"), "x");

        var r = R(site, "https://newzone.top/zh/%E4%B8%AD%E6%96%87%E5%90%8D.html");
        Assert.True(r.Ok, r.Error.ToString());
        Assert.Equal(Path.Combine(_root, "zh", "中文名.md"), r.Path);
        Assert.DoesNotContain('%', r.Path);
    }

    // 盘符的冒号不许被「清理非法字符」吃掉——D:\ 变 D_\ 就整条路径废了。
    [Fact]
    public void The_drive_letter_survives_illegal_character_cleaning()
    {
        var r = R(Blog, "https://newzone.top/posts/2024-03-01-blog_example.html");
        Assert.True(r.Ok);
        Assert.StartsWith(Path.GetPathRoot(Path.GetTempPath())!, r.Path);
    }

    [Fact]
    public void An_unconfigured_domain_says_so_instead_of_guessing()
    {
        var r = R(Blog, "https://github.com/rockbenben/LearnData");
        Assert.False(r.Ok);
        Assert.Equal(SiteMapError.NoMatchingSite, r.Error);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("chrome://settings")]
    [InlineData("file:///C:/x.md")]
    public void Only_web_addresses_are_mapped(string url)
    {
        var r = R(Blog, url);
        Assert.False(r.Ok);
        Assert.Equal(SiteMapError.NotAbsoluteUrl, r.Error);
    }

    // 路径算得出来但文件不在，与「目录压根没配」是两回事，用户要查的东西完全不同。
    [Fact]
    public void A_mapped_but_missing_file_is_distinct_from_a_missing_root()
    {
        var r = R(Blog, "https://newzone.top/posts/does-not-exist.html");
        Assert.False(r.Ok);
        Assert.Equal(SiteMapError.NotFound, r.Error);
        Assert.Equal(Path.Combine(_learnPosts, "does-not-exist.md"), r.Path);   // 路径仍如实给出去
    }

    [Fact]
    public void A_source_root_that_does_not_exist_says_that_instead()
    {
        var site = new SiteEntry
        {
            Name = "gone", Hosts = { "newzone.top" },
            SourceRoot = Path.Combine(_root, "no-such-dir"),
        };
        var r = R(site, "https://newzone.top/");
        Assert.False(r.Ok);
        Assert.Equal(SiteMapError.BadSourceRoot, r.Error);
    }

    // disabled=false 的规则不生效。想清楚再开的那一条，开之前它得真的不生效。
    [Fact]
    public void A_disabled_rule_is_not_applied()
    {
        var site = Blog;
        site.PathRules[0].Enabled = false;

        var r = R(site, "https://newzone.top/posts/2024-03-01-blog_example.html");
        Assert.False(r.Ok);                       // 不再补下划线 → 落到 _root\posts\… 那个不存在的目录
        Assert.Equal(SiteMapError.NotFound, r.Error);
        Assert.Contains("posts", r.Path);
    }

    // 规则里的正则写错了，只跳过那一条，不让整步炸掉。
    // 用户要的是「打开这一页」，不是「因为第 3 条规则少个括号所以什么也不发生」。
    [Fact]
    public void A_broken_rule_is_skipped_rather_than_thrown()
    {
        var site = Blog;
        site.PathRules.Insert(0, new SitePathRule { Match = "([unclosed", Replace = "x" });

        var r = R(site, "https://newzone.top/posts/2024-03-01-blog_example.html");
        Assert.True(r.Ok, r.Error.ToString());    // 后一条好规则仍然生效
    }

    // hosts 大小写不敏感；手改 json 时把域名写成大写不该让站点整个失联。
    [Fact]
    public void Host_matching_ignores_case()
    {
        var site = Blog;
        site.Hosts[0] = "NewZone.TOP";
        Assert.True(R(site, "https://newzone.top/posts/2024-03-01-blog_example.html").Ok);
    }

    // 只认精确域名。newzone.top 匹配上 evil-newzone.top 才是事故。
    [Fact]
    public void Host_matching_does_not_do_suffix_wildcards()
    {
        Assert.Null(SiteMap.FindByHost(new[] { Blog }, "evil-newzone.top"));
        Assert.Null(SiteMap.FindByHost(new[] { Blog }, "notnewzone.top"));
    }
}
