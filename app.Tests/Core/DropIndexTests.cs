using Clockwork.Core;
using Xunit;

// 特征化测试：DataGridReorder.Drop 摘出来的落点算术（below 调整 / src<to 减一 / 上下界夹取）
// 是整个分支里最容易错一位的纯逻辑，摘出来之前一个测试都没有。这里不测「返回的数字对不对」，
// 而是真把 DropIndex 的结果套回一次「移除源、插入目标」的重排，断言最终顺序——更贴近实际用途，
// 差一位这类错误会直接体现成错的列表而不是需要脑内换算的数字。
public class DropIndexTests
{
    private static string Reordered(int src, int hit, bool below, params string[] items)
    {
        int to = DropIndexCalc.DropIndex(src, hit, below, items.Length);
        var list = items.ToList();
        var item = list[src];
        list.RemoveAt(src);
        list.Insert(to, item);
        return string.Concat(list);
    }

    // 4 项列表 A,B,C,D，拖中间项 B(索引1) 落在每一行的上半/下半——覆盖「每一行的上半与下半」这个最小要求，
    // 顺带覆盖自落（hit=1 的两种）与相邻边界重合（hit=0 下半 与 hit=1 上半应算成同一条缝，hit=2 上半同理）。
    [Theory]
    [InlineData(0, false, "BACD")]   // 落 A 上半 → 插到最前
    [InlineData(0, true, "ABCD")]    // 落 A 下半 → 插到 A 之后 = B 已经在那，原地不动
    [InlineData(1, false, "ABCD")]   // 自落·上半 → 原地不动
    [InlineData(1, true, "ABCD")]    // 自落·下半 → 原地不动
    [InlineData(2, false, "ABCD")]   // 落 C 上半 → 与 hit=1 下半是同一条缝，原地不动
    [InlineData(2, true, "ACBD")]    // 落 C 下半 → 插到 C 之后
    [InlineData(3, false, "ACBD")]   // 落 D 上半 → 与 hit=2 下半是同一条缝
    [InlineData(3, true, "ACDB")]    // 落 D 下半 → 插到末尾
    public void Drop_B_on_each_rows_upper_and_lower_half(int hit, bool below, string expected)
        => Assert.Equal(expected, Reordered(1, hit, below, "A", "B", "C", "D"));

    [Fact]
    public void Drag_to_end_moves_first_item_to_last()
        => Assert.Equal("BCDA", Reordered(0, 3, true, "A", "B", "C", "D"));

    [Fact]
    public void Drag_to_start_moves_last_item_to_first()
        => Assert.Equal("DABC", Reordered(3, 0, false, "A", "B", "C", "D"));

    // hit 越界（TargetIndex 正常不会给出，但纯函数自己必须夹得住，不能算出越界索引）与单项列表这类
    // 退化输入：不炸、结果落回合法范围即可，不追求「有意义」。
    [Theory]
    [InlineData(0, -1, false, 4, 0)]   // hit 为负 → 钳到 0
    [InlineData(0, 99, true, 4, 3)]    // hit 远超列表长度 → 钳到末尾
    [InlineData(0, 0, false, 1, 0)]    // 单项列表：唯一位置
    [InlineData(2, 2, true, 1, 0)]     // src/hit 本身已越界（不该发生）：仍不越界崩，钳到 0
    public void Out_of_range_inputs_clamp_without_throwing(int src, int hit, bool below, int count, int expected)
        => Assert.Equal(expected, DropIndexCalc.DropIndex(src, hit, below, count));
}
