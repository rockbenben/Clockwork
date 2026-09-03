using Clockwork.Core;
using Xunit;

// 面板摆哪些页、每页哪些格。模型只有一条：**一页 = 一个 PanelPage，格子 = 页里的步骤**。
// 想要「跑一整个动作」的按钮就放一个 group 类型的步骤——不另立「整组一格」那种东西。
//
// 页从动作组里独立出来之后，「这个组是不是一页」这个问题连同它那三种取值（true/false/null）
// 一起消失了：一个 PanelPage 就是一页，没有第二种状态。原先盯着那个开关的几条测试跟着删掉，
// 它们验的是一个不再存在的分支。
public class PanelLayoutTests
{
    private static PanelPage Page(string name, params LaunchStep[] steps) => new()
    {
        Name = name,
        Steps = new List<LaunchStep>(steps),
    };

    private static LaunchStep Step(string label, string kind = "system") =>
        new() { Kind = kind, Label = label, Command = "lock" };

    [Fact]
    public void A_page_lays_out_its_steps_as_tiles()
    {
        var pages = PanelLayout.BuildPages(new[] { Page("常用", Step("锁屏"), Step("静音")) });
        var p = Assert.Single(pages);
        Assert.Equal("常用", p.Title);
        Assert.Equal(new[] { "锁屏", "静音" }, p.Items.Select(i => i.Label));
        Assert.All(p.Items, i => Assert.NotNull(i.Step));
    }

    // 空页也带着它是哪一页（Source）。管理器要显示空页，而那张卡的拖动、改名、删除
    // 全靠这个引用——从 Items[0] 反推的话，恰恰是空页反推不出来。
    [Fact]
    public void A_page_view_carries_its_source_page()
    {
        var page = Page("常用", Step("a"));
        Assert.Same(page, Assert.Single(PanelLayout.BuildPages(new[] { page })).Source);
        var empty = Page("空的");
        Assert.Same(empty, Assert.Single(PanelLayout.BuildPages(new[] { empty }, keepEmpty: true)).Source);
    }

    // 多个页各自成页，顺序跟随页列表——面板不另存一份页序。
    [Fact]
    public void Pages_follow_the_page_list_order()
    {
        var pages = PanelLayout.BuildPages(new[] { Page("一", Step("a")), Page("二", Step("b")), Page("三", Step("c")) });
        Assert.Equal(new[] { "一", "二", "三" }, pages.Select(p => p.Title));
    }

    // 「跑一整个动作」不是特例，就是一个 group 类型的步骤。
    // 被指的那个是**动作**，不是页：页独立之后没有任何东西能指向一页。
    [Fact]
    public void A_group_step_is_just_another_tile()
    {
        var target = new ActionGroup { Name = "被引用的" };
        var page = Page("入口", new LaunchStep { Kind = "group", Label = "跑那个动作", GroupId = target.Id });
        var items = Assert.Single(PanelLayout.BuildPages(new[] { page }, actions: new[] { target })).Items;
        Assert.Equal("跑那个动作", Assert.Single(items).Label);
    }

    // 引用某个动作的格子，自己没设图标时用那个动作的图标——动作的图标设一次，处处跟着。
    [Fact]
    public void A_group_step_inherits_the_referenced_actions_icon()
    {
        var target = new ActionGroup { Name = "被引用的", Icon = @"D:\art\focus.png" };
        var page = Page("入口", new LaunchStep { Kind = "group", GroupId = target.Id });
        var icon = Assert.Single(Assert.Single(PanelLayout.BuildPages(new[] { page }, actions: new[] { target })).Items).Icon;
        Assert.Equal(PanelIconKind.Image, icon.Kind);
        Assert.EndsWith("focus.png", icon.Value);
    }

    // 步骤自己设了图标就压过被引用动作的——「自定义绝对优先」这条不因引用而失效。
    [Fact]
    public void A_group_steps_own_icon_still_wins()
    {
        var target = new ActionGroup { Name = "被引用的", Icon = @"D:\art\group.png" };
        var page = Page("入口", new LaunchStep { Kind = "group", GroupId = target.Id, Icon = @"D:\art\own.png" });
        var icon = Assert.Single(Assert.Single(PanelLayout.BuildPages(new[] { page }, actions: new[] { target })).Items).Icon;
        Assert.EndsWith("own.png", icon.Value);
    }

    // 不传动作清单时那种格子也照样排出来，只是取不到被引用动作的图标——
    // 拿不到图标不该让整页排不出来（托盘那条路就没有动作清单可传）。
    [Fact]
    public void A_group_step_survives_without_an_action_list()
    {
        var page = Page("入口", new LaunchStep { Kind = "group", Label = "跑那个动作", GroupId = "gone" });
        Assert.Equal("跑那个动作", Assert.Single(Assert.Single(PanelLayout.BuildPages(new[] { page })).Items).Label);
    }

    // 空页不产出：翻到一页什么都没有，只会让人以为面板出错了。
    [Fact]
    public void An_empty_page_is_not_produced()
        => Assert.Empty(PanelLayout.BuildPages(new[] { Page("空的") }));

    // 停用的东西置灰留位，不是不显示：「它被关掉了」和「它没了」得分得清。
    [Fact]
    public void Disabled_steps_stay_as_greyed_tiles()
    {
        var g = Page("常用", Step("开着的"), Step("关掉的"));
        g.Steps[1].Enabled = false;
        var items = Assert.Single(PanelLayout.BuildPages(new[] { g })).Items;
        Assert.True(items[0].Enabled);
        Assert.False(items[1].Enabled);
    }

    [Fact]
    public void An_unnamed_step_falls_back_to_its_action_description()
    {
        var g = Page("常用", new LaunchStep { Kind = "system", Command = "lock" });
        var label = Assert.Single(Assert.Single(PanelLayout.BuildPages(new[] { g })).Items).Label;
        Assert.False(string.IsNullOrWhiteSpace(label));
    }

    // ── 场景页（按呼出面板那一刻的前台程序）──

    private static PanelPage Scene(string name, string process, params LaunchStep[] steps)
    {
        var p = Page(name, steps);
        p.ForProcess = process;
        return p;
    }

    [Fact]
    public void A_page_with_no_process_is_global()
    {
        var g = Page("常用", Step("a"));
        Assert.False(PanelLayout.IsContextual(g));
        Assert.True(PanelLayout.MatchesContext(g, "Code"));
        Assert.True(PanelLayout.MatchesContext(g, ""));
        Assert.True(PanelLayout.MatchesContext(g, null));
    }

    // 三种写法说的是同一个程序：选择器可能给回完整路径，用户可能手打 code.exe，大小写也不该计较。
    [Theory]
    [InlineData("Code", "Code")]
    [InlineData("code", "Code")]
    [InlineData("Code.exe", "Code")]
    [InlineData("Code", @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
    public void Process_matching_ignores_case_path_and_extension(string configured, string foreground)
        => Assert.True(PanelLayout.MatchesContext(Scene("开发", configured, Step("a")), foreground));

    [Fact]
    public void A_scene_page_stays_hidden_in_other_apps()
    {
        var pages = PanelLayout.BuildPages(
            new[] { Page("常用", Step("a")), Scene("开发", "Code", Step("起服务")) }, "explorer");
        Assert.Equal("常用", Assert.Single(pages).Title);
    }

    // 前台读不到（受限进程 / 没有前台窗口）时场景页也不出现：宁可少一页，不要在错的程序上冒出来。
    [Fact]
    public void A_scene_page_needs_a_readable_foreground()
    {
        var g = Scene("开发", "Code", Step("a"));
        Assert.False(PanelLayout.MatchesContext(g, ""));
        Assert.False(PanelLayout.MatchesContext(g, null));
    }

    // 页序即优先级，第 0 页是面板打开时停的那一页：在 VS Code 里呼出就该直接停在「开发」。
    [Fact]
    public void The_matching_scene_page_comes_first()
    {
        var pages = PanelLayout.BuildPages(
            new[] { Page("常用", Step("a")), Scene("开发", "Code", Step("起服务")) }, "Code");
        Assert.Equal(new[] { "开发", "常用" }, pages.Select(p => p.Title));
    }

    // ── 版面：上下两条带 + 按容量续页 ──

    private static PanelPage Many(string name, int n)
    {
        var g = Page(name);
        for (int i = 1; i <= n; i++) g.Steps.Add(Step("动作" + i));
        return g;
    }

    [Fact]
    public void Bands_split_at_top_rows_times_columns()
    {
        var items = Enumerable.Range(1, 20).Select(i => i.ToString()).ToList();
        var (top, bottom) = PanelLayout.SplitBands(items, topRows: 3, columns: 4);
        Assert.Equal(12, top.Count);
        Assert.Equal(8, bottom.Count);
        Assert.Equal("13", bottom[0]);
    }

    [Fact]
    public void An_underfilled_top_band_leaves_the_bottom_empty()
    {
        var (top, bottom) = PanelLayout.SplitBands(new[] { "a", "b", "c" }, topRows: 3, columns: 4);
        Assert.Equal(3, top.Count);
        Assert.Empty(bottom);
    }

    [Fact]
    public void Split_handles_null_without_crashing()
    {
        var (top, bottom) = PanelLayout.SplitBands<string>(null, 3, 4);
        Assert.Empty(top);
        Assert.Empty(bottom);
    }

    // 一页装不下就续到下一页，续页沿用同一个页名——续页是「同一批东西的下半截」。
    [Fact]
    public void Overflow_spills_onto_continuation_pages_keeping_the_name()
    {
        var pages = PanelLayout.BuildPages(new[] { Many("常用", 30) }, null, capacity: 28);
        Assert.Equal(2, pages.Count);
        Assert.Equal(28, pages[0].Items.Count);
        Assert.Equal(2, pages[1].Items.Count);
        Assert.All(pages, p => Assert.Equal("常用", p.Title));
    }

    [Fact]
    public void An_exact_fit_does_not_add_an_empty_page()
        => Assert.Single(PanelLayout.BuildPages(new[] { Many("常用", 28) }, null, capacity: 28));

    [Fact]
    public void Capacity_zero_means_unlimited()
        => Assert.Equal(50, Assert.Single(PanelLayout.BuildPages(new[] { Many("常用", 50) }, null, capacity: 0)).Items.Count);

    [Fact]
    public void Null_input_is_not_a_crash()
    {
        Assert.Empty(PanelLayout.BuildPages(null));
        Assert.Empty(PanelLayout.BuildPages(Array.Empty<PanelPage>()));
    }
}
