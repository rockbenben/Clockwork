namespace Clockwork.Core;

// 拖拽重排的落点算术（DataGridReorder.Drop 用）。摘成纯函数是因为它是这条 WPF 事件处理链里
// 唯一算得上「逻辑」的部分——below 调整、源在目标前的减一、上下界夹取——三步叠在一起最容易错一位，
// 焊在事件处理器里既测不到也读不清楚，摘出来才能上 xUnit 覆盖（见 app.Tests/Core/DropIndexTests.cs）。
public static class DropIndexCalc
{
    // src：被拖动行的原索引。hit：命中行的索引（或列表为空/两端溢出时的边界索引，由 TargetIndex 给出）。
    // below：落在 hit 行的下半（插到它之后）。count：列表当前长度。
    //
    // 算法：先把「插入点」按 below 换算成「插入到 hit 之后」还是「插入到 hit 本身之前」；源若在这个插入点
    // 之前，移除源会让插入点前的所有项整体前移一位，插入点要相应减一（否则会偏右一位）；最后把结果夹回
    // [0, count-1]——插入点算法允许算出 count（插到末尾之后），但这里返回的是「目标行索引」而非「插入缝隙」，
    // 越界的输入（含 hit 本身就越界的degenerate 情形）一律钳到合法范围，不炸也不越界写。
    public static int DropIndex(int src, int hit, bool below, int count)
    {
        int to = below ? hit + 1 : hit;
        if (src < to) to--;
        if (to < 0) to = 0;
        if (to >= count) to = count - 1;
        return to;
    }
}
