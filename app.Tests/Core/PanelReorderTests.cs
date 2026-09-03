using Clockwork.Core;
using Xunit;

// 拖动落点。这里每一条都对着一次真实的手部动作，而它们在界面上出错的样子都一样：「拖了没反应」。
public class PanelReorderTests
{
    private sealed record Item(string Name)
    {
        public override string ToString() => Name;
    }

    private static List<Item> List(params string[] names) => names.Select(n => new Item(n)).ToList();
    private static string[] Names(IEnumerable<Item> xs) => xs.Select(x => x.Name).ToArray();

    // ── 就是这一条以前是坏的 ──
    // 往后拖：把 A 拖到它紧邻的下一格 B 上。曾经的写法先移除再算下标，
    // 移除让 B 前移一位，插入点正好落回原位——手上拖了，屏幕上纹丝不动。
    [Fact]
    public void Dragging_onto_the_very_next_tile_actually_moves_it()
    {
        var l = List("A", "B", "C", "D");
        Assert.True(PanelReorder.Move(l, l[0], l, l[1]));
        Assert.Equal(new[] { "B", "A", "C", "D" }, Names(l));
    }

    [Fact]
    public void Dragging_forward_several_places_lands_after_the_target()
    {
        var l = List("A", "B", "C", "D");
        Assert.True(PanelReorder.Move(l, l[0], l, l[2]));   // A 拖到 C 上
        Assert.Equal(new[] { "B", "C", "A", "D" }, Names(l));
    }

    // 往前拖一直是对的（被移除的那个在目标后面，不影响目标下标），但也得钉住，别修好一头坏另一头。
    [Fact]
    public void Dragging_backward_lands_before_the_target()
    {
        var l = List("A", "B", "C", "D");
        Assert.True(PanelReorder.Move(l, l[3], l, l[1]));   // D 拖到 B 上
        Assert.Equal(new[] { "A", "D", "B", "C" }, Names(l));
    }

    // 拖到页尾空位 = 追加。同一列表时移除会让长度少一，下标正好越界一格，必须夹回去。
    [Fact]
    public void Dropping_on_an_empty_slot_appends()
    {
        var l = List("A", "B", "C");
        Assert.True(PanelReorder.Move(l, l[0], l, null));
        Assert.Equal(new[] { "B", "C", "A" }, Names(l));
    }

    [Fact]
    public void Dropping_the_last_item_on_the_empty_slot_changes_nothing_but_does_not_break()
    {
        var l = List("A", "B", "C");
        PanelReorder.Move(l, l[2], l, null);
        Assert.Equal(new[] { "A", "B", "C" }, Names(l));
    }

    // 跨页：从一个组搬到另一个组。源列表的移除不影响目标列表的下标。
    [Fact]
    public void Moving_across_pages_takes_it_out_of_the_source()
    {
        var a = List("A1", "A2");
        var b = List("B1", "B2");
        Assert.True(PanelReorder.Move(a, a[0], b, b[1]));
        Assert.Equal(new[] { "A2" }, Names(a));
        Assert.Equal(new[] { "B1", "A1", "B2" }, Names(b));
    }

    [Fact]
    public void Moving_across_pages_onto_the_empty_slot_appends_there()
    {
        var a = List("A1", "A2");
        var b = List("B1");
        Assert.True(PanelReorder.Move(a, a[1], b, null));
        Assert.Equal(new[] { "A1" }, Names(a));
        Assert.Equal(new[] { "B1", "A2" }, Names(b));
    }

    // 原地放下：什么都不做，也不该把它先摘下来再插回去（那会白存一次盘）。
    [Fact]
    public void Dropping_an_item_on_itself_does_nothing()
    {
        var l = List("A", "B");
        Assert.False(PanelReorder.Move(l, l[0], l, l[0]));
        Assert.Equal(new[] { "A", "B" }, Names(l));
    }

    // 拖到一个已经不在列表里的目标上（另一个窗口刚把它删了）：按追加处理，不崩。
    [Fact]
    public void A_target_that_vanished_falls_back_to_appending()
    {
        var l = List("A", "B");
        Assert.True(PanelReorder.Move(l, l[0], l, new Item("已经没了")));
        Assert.Equal(new[] { "B", "A" }, Names(l));
    }

    // 源里根本没有这一项：什么都不做，别凭空插一个进去。
    [Fact]
    public void An_item_not_in_the_source_is_not_inserted()
    {
        var a = List("A");
        var b = List("B");
        Assert.False(PanelReorder.Move(a, new Item("野的"), b, null));
        Assert.Equal(new[] { "B" }, Names(b));
    }

    [Fact]
    public void Null_lists_are_not_a_crash()
    {
        var l = List("A");
        Assert.False(PanelReorder.Move(null, l[0], l, null));
        Assert.False(PanelReorder.Move(l, l[0], null, null));
    }
}
