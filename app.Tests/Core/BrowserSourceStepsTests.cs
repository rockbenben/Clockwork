using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 新增的五个步骤类型（读浏览器地址 / 网址映射 / URL 解码 / 正则 / 替换），
// 以及读地址栏时那个「UIA 给的值不带 scheme」的归一。
//
// 分成两半的原因与 Project 约定一致：会碰系统状态的那半（UIA 枚举窗口、ShellExecute 打开文件）
// 不在这里测，只测它决定性的那个纯函数；另一半是纯映射与纯正则，整张判定表都钉得住。
public class BrowserSourceStepsTests
{
    // ---- 注册：漏一个那种步骤就既没入口也没摘要 ----

    [Theory]
    [InlineData("browserUrl")]
    [InlineData("siteMap")]
    [InlineData("urlDecode")]
    [InlineData("regex")]
    [InlineData("replace")]
    public void The_new_kinds_are_in_the_catalogue(string kind)
    {
        Assert.Contains(kind, StepDisplay.StepKinds);
        // 分节必须收下它，否则「新增 ▾」菜单里根本没有入口
        Assert.Contains(StepDisplay.StepKindSections, sec => sec.Kinds.Contains(kind));
    }

    // 空值不许留白（与 BlankRowTests 同一条规矩，这里点名新类型）。
    [Theory]
    [InlineData("browserUrl")]
    [InlineData("siteMap")]
    [InlineData("urlDecode")]
    [InlineData("regex")]
    [InlineData("replace")]
    public void The_new_kinds_never_render_blank(string kind)
    {
        var s = new LaunchStep { Kind = kind };
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepSummary(s)));
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepTitle(s)));
    }

    // 配了变量名的步骤，摘要里要看得见那个名字，否则用户不知道下一步该写 {什么}。
    [Fact]
    public void A_configured_output_variable_shows_up_in_the_summary()
    {
        foreach (var kind in new[] { "browserUrl", "urlDecode", "regex", "replace" })
        {
            var s = new LaunchStep { Kind = kind, OutputVar = "u", Text = "hello", Pattern = "h" };
            Assert.Contains("u", StepDisplay.StepSummary(s));
        }
    }

    // siteMap 的三种打开方式，摘要要说出不同的动词：说「打开」而实际是「定位」，
    // 用户在清单上看到的和发生的就不是一回事。
    // siteMap 的三种打开方式，摘要要说出不同的动词。说「打开」而实际是「只输出路径」，
    // 用户在清单上照着点就什么也不会发生，而界面上看不出任何异常——三档说同一句话，那一档就白配了。
    [Fact]
    public void SiteMap_summary_distinguishes_all_three_open_modes()
    {
        string Summary(string a) => StepDisplay.StepSummary(new LaunchStep { Kind = "siteMap", Action = a });
        var open = Summary("open");
        var reveal = Summary("reveal");
        var none = Summary("none");
        Assert.True(open != reveal && open != none && reveal != none,
            $"三档里有两档摘要相同：open=[{open}] reveal=[{reveal}] none=[{none}]");
    }

    // 表外的打开方式（手改 json、导入）必须落回 open，不能变成空串——
    // 那会让 StepRunner 走 default 分支，什么都不打开，而清单上看着一切正常。
    [Theory]
    [InlineData(null, "open")]
    [InlineData("", "open")]
    [InlineData("  ", "open")]
    [InlineData("OPEN", "open")]
    [InlineData("reveal", "reveal")]
    [InlineData("none", "none")]
    [InlineData("somethingFromTheFuture", "open")]
    public void SiteMap_action_falls_back_to_open(string? raw, string want)
        => Assert.Equal(want, StepDisplay.SiteMapActionOf(new LaunchStep { Action = raw! }));

    // ---- 地址栏归一：UIA 给的值不带 scheme，不补后面全崩 ----

    [Fact]
    public void A_bare_host_gets_https()
    {
        Assert.True(BrowserUrlReader.Normalize("newzone.top/blog.html", out var u));
        Assert.Equal("https://newzone.top/blog.html", u);
    }

    // 本地预览不会上 https，补成 https://localhost:8080 打不开。
    [Fact]
    public void Localhost_gets_http()
    {
        Assert.True(BrowserUrlReader.Normalize("localhost:8080/posts/a.html", out var u));
        Assert.Equal("http://localhost:8080/posts/a.html", u);
    }

    [Theory]
    [InlineData("chrome://settings")]
    [InlineData("edge://extensions")]
    [InlineData("about:blank")]
    [InlineData("chrome-error://chromewebdata/")]
    public void Browsers_own_pages_are_not_addresses(string raw)
    {
        Assert.False(BrowserUrlReader.Normalize(raw, out var u));
        Assert.Equal("", u);
    }

    [Fact]
    public void A_full_address_passes_through_untouched()
    {
        Assert.True(BrowserUrlReader.Normalize("http://newzone.top/", out var u));
        Assert.Equal("http://newzone.top/", u);
    }

    // 空正则在 .NET 里匹配每一处**空**位置。真拿它去跑，产出的是一串被拆开的东西，
    // 比报错难收拾得多。StepRunner 那两支据此先拒空——这里钉住那个守卫真的在。
    //
    // （先前这里写的是「空正则会把每个字都拆开插一条替换文本」，那是猜的 .NET 行为，
    // 实测 .NET 的 Replace 并不那样输出。改成直接断言我们自己的守卫，不替 .NET 立传。）
    [Theory]
    [InlineData("regex")]
    [InlineData("replace")]
    public void An_empty_pattern_is_refused_before_it_reaches_the_regex_engine(string kind)
    {
        var r = Clockwork.Engine.StepRunner.InvokeStepAction(
            new LaunchStep { Kind = kind, Pattern = "", Text = "abc" }, null, selfPaths: []);
        Assert.True(r.HasWarning);
        // ActionResult.Warning 存的是**翻好的一句**，不是键名（UnattendedDestructiveTests 同一条口径）。
        Assert.Equal(Clockwork.I18n.Strings.Get("Warn_RegexNoPattern"), r.Warning);
    }
}
