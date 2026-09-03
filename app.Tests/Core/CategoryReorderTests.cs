using System.Collections.Generic;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 分类的先后。它没有自己的存储——顺序就是「哪一类的第一页在页列表里更靠前」，
// 所以拖分类就是在那个列表里搬动一整块页。
//
// 整块搬是承重的：只搬第一页的话两类的页会交错，而交错之后再拖任何一页
// 都可能顺带改变分类的先后——那时用户拖的是页，动的却是分类。
public class CategoryReorderTests
{
    private static PanelPage P(string name, string cat) =>
        new() { Id = name, Name = name, Tab = cat };

    private static List<PanelPage> L(params PanelPage[] gs) => gs.ToList();

    private static string[] Cats(List<PanelPage> gs) =>
        PanelLayout.Categories(PanelLayout.BuildPages(gs, null, keepEmpty: true, allContexts: true)).ToArray();

    private static string[] Names(List<PanelPage> gs) => gs.Select(g => g.Name).ToArray();

    [Fact]
    public void A_category_moves_in_front_of_another()
    {
        var gs = L(P("甲1", "甲"), P("甲2", "甲"), P("乙1", "乙"));
        Assert.Equal(new[] { "甲", "乙" }, Cats(gs));
        Assert.True(PanelReorder.MoveCategory(gs, "乙", "甲"));
        Assert.Equal(new[] { "乙", "甲" }, Cats(gs));
        Assert.Equal(new[] { "乙1", "甲1", "甲2" }, Names(gs));
    }

    // 类内的先后是这一类自己的事，拖分类不该动它。
    [Fact]
    public void Moving_a_category_keeps_the_order_inside_it()
    {
        var gs = L(P("乙1", "乙"), P("甲1", "甲"), P("甲2", "甲"), P("甲3", "甲"));
        PanelReorder.MoveCategory(gs, "甲", "乙");
        Assert.Equal(new[] { "甲1", "甲2", "甲3", "乙1" }, Names(gs));
    }

    // 交错的页会被这一次搬动收拢成连续的一段——交错本身就是「拖页会改分类顺序」的根源。
    [Fact]
    public void Interleaved_pages_are_gathered_into_one_run()
    {
        var gs = L(P("甲1", "甲"), P("乙1", "乙"), P("甲2", "甲"), P("乙2", "乙"));
        PanelReorder.MoveCategory(gs, "甲", "乙");
        Assert.Equal(new[] { "甲1", "甲2", "乙1", "乙2" }, Names(gs));
    }

    // 未分类（空串）也是一类，可以搬，也可以被搬到它前面。
    [Fact]
    public void Uncategorised_is_a_category_like_any_other()
    {
        var gs = L(P("散1", ""), P("甲1", "甲"));
        Assert.True(PanelReorder.MoveCategory(gs, "甲", ""));
        Assert.Equal(new[] { "甲", "" }, Cats(gs));
        Assert.Equal(new[] { "甲1", "散1" }, Names(gs));
    }

    // 落到自己身上不算一次移动（同 Move 那条：原地放下）。
    [Fact]
    public void Dropping_a_category_on_itself_does_nothing()
    {
        var gs = L(P("甲1", "甲"), P("乙1", "乙"));
        Assert.False(PanelReorder.MoveCategory(gs, "甲", "甲"));
        Assert.False(PanelReorder.MoveCategory(gs, "甲", "  甲  "));   // 首尾空白不算另一类
        Assert.Equal(new[] { "甲1", "乙1" }, Names(gs));
    }

    [Fact]
    public void An_unknown_category_moves_nothing()
    {
        var gs = L(P("甲1", "甲"));
        Assert.False(PanelReorder.MoveCategory(gs, "没有这一类", "甲"));
        Assert.False(PanelReorder.MoveCategory(null, "甲", "乙"));
        Assert.Equal(new[] { "甲1" }, Names(gs));
    }

    // 目标那一类不存在时挪到末尾，而不是原地不动——拖到哪儿都得有个结果。
    [Fact]
    public void Moving_before_a_missing_category_puts_it_last()
    {
        var gs = L(P("甲1", "甲"), P("乙1", "乙"));
        Assert.True(PanelReorder.MoveCategory(gs, "甲", "没有这一类"));
        Assert.Equal(new[] { "乙1", "甲1" }, Names(gs));
    }

    // ── 归类时并进段尾 ──
    //
    // 不并的话两类的页交错，而分类的先后是「哪一类的第一页更靠前」——
    // 交错之后再拖同一类里的页，可能把另一类的第一页越过去：用户拖的是页，动的却是分类。

    [Fact]
    public void A_newly_categorised_page_joins_the_end_of_its_run()
    {
        var gs = L(P("甲1", "甲"), P("甲2", "甲"), P("散1", ""), P("散2", ""));
        gs[3].Tab = "甲";                       // 把最后一页归进「甲」
        Assert.True(PanelReorder.PlaceInCategory(gs, gs[3]));
        Assert.Equal(new[] { "甲1", "甲2", "散2", "散1" }, Names(gs));
        Assert.Equal(new[] { "甲", "" }, Cats(gs));   // 分类的先后没被这一下改掉
    }

    // 归类之后再拖同一类里的页，不会越过另一类的第一页。
    [Fact]
    public void Reordering_inside_a_run_cannot_reorder_the_categories()
    {
        var gs = L(P("散1", ""), P("甲1", "甲"), P("甲2", "甲"));
        var before = Cats(gs);
        PanelReorder.Move(gs, gs[2], gs, gs[1]);      // 甲2 拖到 甲1 前面
        Assert.Equal(before, Cats(gs));
        Assert.Equal(new[] { "散1", "甲2", "甲1" }, Names(gs));
    }

    // 这一类只有它自己时不动：它待的地方就是这一类的位置。
    [Fact]
    public void The_only_page_of_a_category_stays_put()
    {
        var gs = L(P("散1", ""), P("甲1", "甲"), P("散2", ""));
        Assert.False(PanelReorder.PlaceInCategory(gs, gs[1]));
        Assert.Equal(new[] { "散1", "甲1", "散2" }, Names(gs));
    }

    // 已经在段尾了就不动（否则每次归类都报「配置变了」，白写一次盘）。
    [Fact]
    public void A_page_already_at_the_end_of_its_run_does_not_move()
    {
        var gs = L(P("甲1", "甲"), P("甲2", "甲"), P("散1", ""));
        Assert.False(PanelReorder.PlaceInCategory(gs, gs[1]));
        Assert.Equal(new[] { "甲1", "甲2", "散1" }, Names(gs));
    }

    // ── 「同一类」只有一条规则：去首尾空白 + 不区分大小写 ──
    //
    // 这条规则原来在三个文件里抄了七份，其中两份写成了区分大小写的 Ordinal。后果是实打实的：
    // 分别填了 Dev 和 dev 的两页在顶栏上被折成一格（Categories 忽略大小写），你在那一格上改名，
    // 却只有拼写一致的那些页跟着走——另一半留在一个已经不存在的分类里，从顶栏上消失。
    // 现在只有 PanelLayout.SameCategory 一处定义，这几条盯着它别再分叉。

    [Theory]
    [InlineData("Dev", "dev")]
    [InlineData("Dev", " Dev ")]
    [InlineData("", "   ")]
    [InlineData(null, "")]
    public void Case_and_padding_do_not_make_a_new_category(string? a, string? b)
        => Assert.True(PanelLayout.SameCategory(a, b));

    [Theory]
    [InlineData("Dev", "Design")]
    [InlineData("", "Dev")]
    [InlineData("Dev ", "De v")]
    public void Different_names_are_different_categories(string a, string b)
        => Assert.False(PanelLayout.SameCategory(a, b));

    // 大小写不同的两页属于同一类，所以整块搬的时候必须一起走——落下一页，
    // 它就成了那一类在列表里的新「第一页」，分类的先后当场变了。
    [Fact]
    public void Moving_a_category_takes_its_case_variants_along()
    {
        var gs = L(P("甲1", "Dev"), P("乙1", "乙"), P("甲2", "dev"));
        Assert.True(PanelReorder.MoveCategory(gs, "DEV", "乙"));
        Assert.Equal(new[] { "甲1", "甲2", "乙1" }, Names(gs));
        Assert.Single(Cats(gs).Where(c => PanelLayout.SameCategory(c, "dev")));
    }

    // 归到段尾时同理：认不出大小写不同的同类，就会把自己并到一个空段里去。
    [Fact]
    public void Joining_a_run_recognises_case_variants()
    {
        var gs = L(P("甲1", "Dev"), P("散1", ""), P("甲2", "dev"));
        gs[1].Tab = "DEV";                       // 把中间那页也归进这一类
        Assert.True(PanelReorder.PlaceInCategory(gs, gs[1]));
        Assert.Equal(new[] { "甲1", "甲2", "散1" }, Names(gs));
    }

    // ── 合并两类：改名成一个**已存在**的分类名 ──
    //
    // 管理器的改名先整块搬、再改名。顺序是承重的：反过来（先改名再逐个并段尾）时，
    // 两边已经成了同一类，PlaceInCategory 只会把列表原地轮转一圈，一个都收拢不了。
    // 交错的后果见类头：此后拖这一类里的页，可能把别的分类整个越过去。
    private static void RenameCategory(List<PanelPage> pages, string from, string to)
    {
        if (!PanelLayout.SameCategory(to, from) && pages.Any(p => PanelLayout.SameCategory(p.Tab, to)))
            PanelReorder.MoveCategory(pages, from, to);
        foreach (var p in pages) if (PanelLayout.SameCategory(p.Tab, from)) p.Tab = to;
    }

    [Fact]
    public void Merging_two_categories_leaves_one_contiguous_run()
    {
        var gs = L(P("甲1", "甲"), P("乙1", "乙"), P("甲2", "甲"), P("乙2", "乙"));
        RenameCategory(gs, "甲", "乙");
        Assert.Equal(new[] { "甲1", "甲2", "乙1", "乙2" }, Names(gs));
        Assert.Single(Cats(gs));
    }

    // 合并不能把**别的**分类夹在中间：丙 原本在两段之间，合并后它得整个在一边。
    [Fact]
    public void Merging_does_not_strand_a_third_category_in_the_middle()
    {
        var gs = L(P("甲1", "甲"), P("丙1", "丙"), P("乙1", "乙"), P("甲2", "甲"));
        RenameCategory(gs, "甲", "乙");
        var names = Names(gs);
        int first = Array.IndexOf(names, "甲1"), last = Array.IndexOf(names, "乙1");
        // 甲1 / 甲2 / 乙1 三页现在同属一类，必须连续
        var run = names.Skip(System.Math.Min(first, last)).Take(3).ToArray();
        Assert.All(run, n => Assert.Contains(n, new[] { "甲1", "甲2", "乙1" }));
        Assert.Equal(2, Cats(gs).Length);
    }

    // 解散一类 = 改名成空（页回到未分类），走的是同一条路，因此同样不该留下交错。
    [Fact]
    public void Dissolving_a_category_joins_the_uncategorised_run()
    {
        var gs = L(P("甲1", "甲"), P("散1", ""), P("甲2", "甲"));
        RenameCategory(gs, "甲", "");
        Assert.Single(Cats(gs));
        Assert.Equal("", Cats(gs)[0]);
    }

    // 改成自己的名字是空操作，别把列表搅一遍（每次都报「配置变了」，白写一次盘）。
    [Fact]
    public void Renaming_a_category_to_itself_changes_nothing()
    {
        var gs = L(P("甲1", "甲"), P("散1", ""), P("甲2", "甲"));
        var before = Names(gs);
        RenameCategory(gs, "甲", "甲");
        Assert.Equal(before, Names(gs));
    }

    [Fact]
    public void Placing_a_null_page_moves_nothing()
    {
        var gs = L(P("甲1", "甲"));
        Assert.False(PanelReorder.PlaceInCategory(gs, null));
        Assert.False(PanelReorder.PlaceInCategory(null, gs[0]));
    }
}
