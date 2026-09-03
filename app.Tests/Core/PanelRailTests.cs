using System.Collections.Generic;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 面板的两条导航栏：**上边是分类，左边是这一类里的页**——父子，不是平行的两处。
//
// 它们和腰栏那排圆点分工明确：**栏回答「哪一页」，圆点回答「这一页的第几屏」**——
// 以前这两件事挤在同一排点里，于是一个装不下的页被切成三屏之后，
// 那三个点看着和「三个不同的页」一模一样。
public class PanelRailTests
{
    private static PanelPage Page(string name, int steps, string tab = "")
    {
        var g = new PanelPage { Id = name, Name = name, Tab = tab };
        for (int i = 0; i < steps; i++) g.Steps.Add(new LaunchStep { Kind = "system", Command = "lockScreen" });
        return g;
    }

    private static List<PanelPage> Groups(params PanelPage[] gs) => gs.ToList();

    // ── 页签：一页一条，不管它被切成几屏 ──

    [Fact]
    public void One_tab_per_page_however_many_screens_it_takes()
    {
        // 6 个步骤、每屏装 2 个 → 切成 3 屏，但页签只该有 1 条。
        var pages = PanelLayout.BuildPages(Groups(Page("手边", 6)), null, capacity: 2);
        Assert.Equal(3, pages.Count);                 // 三屏
        Assert.Single(PanelLayout.Tabs(pages));       // 一条页签
        Assert.Equal("手边", PanelLayout.Tabs(pages)[0].Title);
    }

    [Fact]
    public void Screens_are_numbered_within_their_page()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 5), Page("乙", 3)), null, capacity: 2);
        Assert.Equal(new[] { 0, 1, 2, 0, 1 }, pages.Select(p => p.Screen));
    }

    [Fact]
    public void Two_pages_give_two_tabs()
        => Assert.Equal(2, PanelLayout.Tabs(PanelLayout.BuildPages(Groups(Page("甲", 1), Page("乙", 1)), null)).Count);

    // ── 空页：面板不要，管理器要 ──
    //
    // 「点了添加动作页什么也没发生」的真相：页**建出来了也存进配置了**，只是不产出、画不出来，
    // 于是每点一次就多一个看不见的空组。空页对面板确实该藏（翻到一张什么都没有的页
    // 只会让人以为出错了），但管理器正是你往空页里放动作的地方。
    [Fact]
    public void An_empty_page_is_hidden_from_the_panel()
        => Assert.Empty(PanelLayout.BuildPages(Groups(Page("新页", 0)), null));

    [Fact]
    public void An_empty_page_is_kept_for_the_manager()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("新页", 0)), null, keepEmpty: true);
        Assert.Single(pages);
        Assert.Equal("新页", pages[0].Title);
        Assert.Empty(pages[0].Items);
    }

    // 切屏那一步会第二次弄丢它：一页零格切不出任何一屏，循环一次都不进。
    [Fact]
    public void An_empty_page_survives_capacity_chunking()
        => Assert.Single(PanelLayout.BuildPages(Groups(Page("新页", 0)), null, capacity: 4, keepEmpty: true));

    // 空页也照样记得自己归哪一类，否则它在管理器里会从那一类里消失。
    [Fact]
    public void An_empty_page_keeps_its_category()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("新页", 0, "写代码")), null, keepEmpty: true);
        Assert.Equal("写代码", PanelLayout.CategoryOf(pages[0]));
    }

    // 每一页都得知道自己是哪个组——管理器靠它接上「拖动」和「打开页设置」。
    // 从第一个格子反推的老写法在空页上给出 null，那张卡就此拖不动也点不开，
    // 而新建的页天生是空的：「添加动作页」得到的正好是一张动不了的卡。
    [Fact]
    public void Every_page_knows_its_group_even_when_empty()
    {
        var empty = Page("新页", 0);
        var full = Page("满页", 3);
        var pages = PanelLayout.BuildPages(Groups(empty, full), null, keepEmpty: true);
        Assert.Same(empty, pages.Single(p => p.Title == "新页").Source);
        Assert.Same(full, pages.Single(p => p.Title == "满页").Source);
    }

    // 切屏之后每一屏也得记得（管理器不切屏，但这条不变式别留缺口）。
    [Fact]
    public void Every_screen_knows_its_group()
    {
        var g = Page("甲", 6);
        var pages = PanelLayout.BuildPages(Groups(g), null, capacity: 2);
        Assert.All(pages, p => Assert.Same(g, p.Source));
    }

    // ── 两条栏：父子 ──
    //
    // 上边选类，左边列这一类里的页。页只有一个落点（左栏），所以不存在
    // 「这一页到底在哪条栏上」这个问题——那正是把它们做成平行两条时的毛病。

    // 分类按**第一次出现**的先后排，不按字母：分类的先后就是动作组列表的先后，
    // 而那个列表本来就能上下移动。另排一次，早晚会和它分歧。
    [Fact]
    public void Categories_come_in_first_appearance_order()
    {
        var pages = PanelLayout.BuildPages(
            Groups(Page("甲", 1, "乙类"), Page("乙", 1), Page("丙", 1, "甲类"), Page("丁", 1, "乙类")), null);
        Assert.Equal(new[] { "乙类", "", "甲类" }, PanelLayout.Categories(pages));
    }

    // 未分类的那一堆也占一格：不给它位置的话，没填分类的页就无处可去。
    [Fact]
    public void Uncategorised_pages_get_a_tab_of_their_own()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 1, "写代码"), Page("乙", 1)), null);
        Assert.Equal(new[] { "写代码", "" }, PanelLayout.Categories(pages));
        Assert.Equal(new[] { "乙" }, PanelLayout.PagesIn(pages, "").Select(p => p.Title));
    }

    // 一类都不填时只有一格——面板据此整条不画，版面与没有分类栏时逐像素相同。
    [Fact]
    public void With_nothing_categorised_there_is_one_category()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 1), Page("乙", 1)), null);
        Assert.Single(PanelLayout.Categories(pages));
        Assert.Equal(2, PanelLayout.PagesIn(pages, "").Count);
    }

    // 左栏只列当前这一类：**上边那条是筛选器**，这正是它与「平行两条」的分界。
    [Fact]
    public void The_left_bar_lists_only_the_current_category()
    {
        var pages = PanelLayout.BuildPages(
            Groups(Page("甲", 1, "写代码"), Page("乙", 1), Page("丙", 1, "写代码")), null);
        Assert.Equal(new[] { "甲", "丙" }, PanelLayout.PagesIn(pages, "写代码").Select(p => p.Title));
        Assert.Equal(new[] { "乙" }, PanelLayout.PagesIn(pages, "").Select(p => p.Title));
        // 各类加起来正好是全部，没有页掉出去也没有页出现两次
        Assert.Equal(PanelLayout.Tabs(pages).Count,
                     PanelLayout.Categories(pages).Sum(c => PanelLayout.PagesIn(pages, c).Count));
    }

    // 一页被切成几屏，在它那一类里只算一条页签。
    [Fact]
    public void Splitting_a_page_does_not_duplicate_its_tab()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 6, "写代码")), null, capacity: 2);
        Assert.Equal(3, pages.Count);
        Assert.Single(PanelLayout.PagesIn(pages, "写代码"));
    }

    // 切屏之后每一屏都还记得自己归哪一类——否则翻到第二屏，顶栏的高亮会跳走。
    [Fact]
    public void Every_screen_keeps_its_category()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 6, "写代码")), null, capacity: 2);
        Assert.All(pages, p => Assert.Equal("写代码", PanelLayout.CategoryOf(p)));
    }

    // 分类名去掉首尾空白再比，大小写不敏感：手打的名字里多一个空格、或者
    // 一处写 "Dev" 一处写 "dev"，都不该在顶栏上裂成两格。
    [Theory]
    [InlineData("写代码", "写代码")]
    [InlineData("  写代码  ", "写代码")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void A_category_name_is_trimmed(string tab, string want)
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 1, tab)), null);
        Assert.Equal(want, PanelLayout.CategoryOf(pages[0]));
    }

    [Fact]
    public void Case_does_not_split_a_category_in_two()
    {
        var pages = PanelLayout.BuildPages(Groups(Page("甲", 1, "Dev"), Page("乙", 1, "dev")), null);
        Assert.Single(PanelLayout.Categories(pages));
        Assert.Equal(2, PanelLayout.PagesIn(pages, "DEV").Count);
    }

    // ── 管理器要看见全部页，面板只看见此刻该出现的那些 ──
    //
    // 「只在哪个程序上出现」是每一页自己的一个字段。面板按它筛（那正是这个字段的用途），
    // 管理器不筛——不然绑给别的程序的页在管理器里整批消失，而你要改它的绑定
    // 恰恰得先看得见它。这条曾经真的发生过：管理器顶上是一条场景筛选栏，
    // 停在「任何程序」时，绑给 Code 的那一页在管理器里根本不存在。
    [Fact]
    public void The_manager_sees_pages_bound_to_other_apps()
    {
        var mine = Page("通用", 2);
        var his = Page("写代码", 2);
        his.ForProcess = "Code.exe";

        // 面板：前台不是 Code，那一页不出现。
        Assert.Single(PanelLayout.BuildPages(Groups(mine, his), foreground: null));
        // 面板：前台是 Code，两页都出现。
        Assert.Equal(2, PanelLayout.BuildPages(Groups(mine, his), foreground: "Code.exe").Count);
        // 管理器：不问前台是谁，两页都在。
        Assert.Equal(2, PanelLayout.BuildPages(Groups(mine, his), foreground: null, allContexts: true).Count);
    }

    // 空页同理：管理器要（keepEmpty），而它同时可能绑着别的程序——两个开关得能一起用。
    [Fact]
    public void An_empty_page_bound_to_another_app_still_shows_in_the_manager()
    {
        var g = Page("刚建的", 0);
        g.ForProcess = "Code.exe";
        Assert.Empty(PanelLayout.BuildPages(Groups(g), foreground: null));
        Assert.Single(PanelLayout.BuildPages(Groups(g), foreground: null, keepEmpty: true, allContexts: true));
    }

    // 面板把绑了程序的页排在最前（呼出来就停在匹配的那一页）。管理器不排——
    // 那儿没有「匹配」，而排序会让显示顺序 ≠ 动作组列表顺序，
    // 于是拖页签改了列表顺序、重画后那一页又被排回原处，读起来就是「拖了没用」。
    [Fact]
    public void The_manager_keeps_the_plain_list_order()
    {
        var first = Page("全局甲", 1);
        var bound = Page("绑给Code", 1);
        bound.ForProcess = "Code.exe";
        var groups = Groups(first, bound);   // 列表顺序：全局甲 → 绑给Code

        // 面板（前台是 Code）：场景页排到最前。
        Assert.Equal(new[] { "绑给Code", "全局甲" },
                     PanelLayout.BuildPages(groups, "Code.exe").Select(p => p.Title).ToArray());
        // 管理器：原样，就是列表顺序。
        Assert.Equal(new[] { "全局甲", "绑给Code" },
                     PanelLayout.BuildPages(groups, null, allContexts: true).Select(p => p.Title).ToArray());
    }
}
