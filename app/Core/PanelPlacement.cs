namespace Clockwork.Core;

// 快捷面板放在哪儿。纯算术，单位是物理像素（调用方负责 DIP↔px 换算与取显示器工作区）。
//
// 规则只有一条：**光标落在面板正中**，然后整体夹进工作区。
//
// 为什么是居中而不是"贴在光标右下角"（最初那版）：面板是拿鼠标去点的，而居中意味着
// 每一格到指针的距离都最短——四周均摊，而不是让右下角那一格跑到最远。Quicker 的面板也是这么放的。
// 副作用是指针一开始就压在某一格上，这是有意的：那一格是「零距离」的那个，不是障碍。
//
// 居中还顺手删掉了一整段边缘翻转逻辑。贴角放置时，光标靠近屏幕右边就得把面板翻到左边去，
// 于是有四个象限、四种翻法，还要处理"翻过去也放不下"；居中之后这些情形全都退化成同一件事：
// 算出中心位置，再夹进工作区。
//
// 抽成纯函数不是为了复用——只有一个调用方——而是为了能测：这段逻辑的所有 bug 都长在边界上
//（光标贴屏幕角、面板比屏幕还大、副屏在主屏左侧因此工作区左边界是负数），
// 那些情形靠手动拖窗口去复现既慢又不全。
public static class PanelPlacement
{
    /// <summary>
    /// 把 w×h 的面板摆成「光标在正中」，结果保证完整落在给定工作区内。
    /// 工作区用 left/top/right/bottom 四个边界传入（right/bottom 为开区间，与 Win32 RECT 一致），
    /// 副屏在主屏左侧或上方时这些值可以是负数——所以夹取一律对着实参算，不能假设从 0 起。
    /// </summary>
    public static (int X, int Y) Place(int cursorX, int cursorY, int w, int h,
                                       int workLeft, int workTop, int workRight, int workBottom)
    {
        // 整除的余数（奇数尺寸时差一像素）不去补：半个像素的偏移没人看得出来，
        // 而为它引入 double 会让这段唯一的好处——整数进整数出、可精确断言——消失。
        int x = cursorX - w / 2;
        int y = cursorY - h / 2;

        // 夹进工作区。顺序是承重的：先顶左上、再收右下——反过来的话，比工作区还大的面板
        // 会被推成负坐标，左上角跑到屏幕外，而那一角正是标题与第一排格子所在的地方。
        if (x < workLeft) x = workLeft;
        if (y < workTop) y = workTop;
        if (x + w > workRight) x = Math.Max(workLeft, workRight - w);
        if (y + h > workBottom) y = Math.Max(workTop, workBottom - h);
        return (x, y);
    }
}
