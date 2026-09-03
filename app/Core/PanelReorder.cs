using System.Linq;
namespace Clockwork.Core;

// 拖动排序的落点计算。抽成纯函数不是为了复用，是因为这段的 bug 全长在**索引错位**上，
// 而错位在界面上表现成"拖了没反应"——没有比这更难从截图里看出来的缺陷。
//
// 唯一的坑：**必须在移除源元素之前算好目标下标**。
// 反过来的话，往后拖时移除会把目标往前挤一位，插入点整体早一格：
// 拖到紧邻的下一格算出来正好是原位，看起来就是完全没动。往前拖却是对的
//（被移除的那个在目标后面，不影响目标下标），于是表现为"只有从前往后拖失败"。
public static class PanelReorder
{
    /// <summary>把 <paramref name="item"/> 从 <paramref name="from"/> 移到 <paramref name="to"/>，
    /// 落在 <paramref name="before"/> 之前；<paramref name="before"/> 为 null 表示追加到末尾。
    /// <paramref name="from"/> 与 <paramref name="to"/> 可以是同一个列表（页内换位），
    /// 也可以是两个列表（把一个动作拖到另一页）。返回是否真的动了。</summary>
    public static bool Move<T>(IList<T>? from, T item, IList<T>? to, T? before) where T : class
    {
        if (from == null || to == null || item == null) return false;
        if (ReferenceEquals(item, before)) return false;   // 原地放下

        // 先算下标，再移除——顺序是承重的，理由见类头注释。
        int at = before == null ? to.Count : to.IndexOf(before);
        if (at < 0) at = to.Count;

        if (!from.Remove(item)) return false;
        // 同一个列表时移除会让长度少一（追加的情形下标正好越界一格），夹回去。
        if (at > to.Count) at = to.Count;
        to.Insert(at, item);
        return true;
    }

    /// <summary>把 <paramref name="page"/> 并到它那一类的段尾。归类之后调一次。</summary>
    //
    // 不并的话它在列表里原地不动，两类的页于是交错。而分类的先后是「哪一类的第一页更靠前」，
    // 所以交错之后再拖同一类里的页，可能把另一类的第一页越过去——**用户拖的是页，动的却是分类**。
    // 归类时并到段尾，交错就不会产生；老配置里已有的交错由 MoveCategory 那一次整块搬收拢。
    public static bool PlaceInCategory(IList<PanelPage>? pages, PanelPage? page)
    {
        if (pages == null || page == null) return false;
        var cat = PanelLayout.CategoryOf(page);
        int last = -1;
        for (int i = 0; i < pages.Count; i++)
            if (!ReferenceEquals(pages[i], page) && PanelLayout.SameCategory(pages[i]?.Tab, cat))
                last = i;
        if (last < 0) return false;   // 这一类只有它自己：它待的地方就是这一类的位置
        int now = pages.IndexOf(page);
        // 已经紧跟在段尾那一位后面就不动——否则每次归类都报「配置变了」，白写一次盘。
        // （now < last 时它在段中间，这个判据自然为假，会照常并到段尾去。）
        if (now < 0 || now == last + 1) return false;

        pages.RemoveAt(now);
        if (now < last) last--;       // 摘掉自己之后，落点往前挪一格
        pages.Insert(last + 1, page);
        return true;
    }

    /// <summary>把 <paramref name="src"/> 这一整类挪到 <paramref name="dst"/> 这一类之前。</summary>
    //
    // 分类没有自己的存储，它的先后就是「哪一类的第一页在页列表里更靠前」（PanelLayout.Categories）。
    // 所以「把一类挪到另一类前面」= 把它名下的页**整块**搬到目标那一类的第一页之前。
    //
    // 整块搬而不是只搬第一页：只搬第一页的话，两类的页会交错，
    // 而交错之后再拖任何一页都可能顺带改变分类的先后——那时用户拖的是页，动的却是分类。
    // 整块搬同时把这一类的页收拢成连续的一段，交错会随着每次拖动自己消失。
    //
    // 类内的先后原样保留：这次拖的是分类，不是分类里的页。
    public static bool MoveCategory(IList<PanelPage>? pages, string? src, string? dst)
    {
        if (pages == null) return false;
        if (PanelLayout.SameCategory(src, dst)) return false;

        var block = pages.Where(p => PanelLayout.SameCategory(p?.Tab, src)).ToList();
        if (block.Count == 0) return false;

        var before = pages.ToList();   // 只为最后判断「到底动了没有」，见下

        foreach (var p in block) pages.Remove(p);

        // 摘掉之后再找落点。**这里与类头那条规矩不冲突，形状不一样**：
        // Move<T> 算的是「目标元素在 to 里的下标」，那个下标要在移除之前取；
        // 这里算的是「整块搬走之后，目标那一类的第一页落在哪」——它本来就该在移除后的列表上量。
        // （原注释写着「同 Move 那条」，那是把两种形状读混了。）
        int at = pages.Count;
        for (int i = 0; i < pages.Count; i++)
            if (PanelLayout.SameCategory(pages[i]?.Tab, dst)) { at = i; break; }

        for (int i = 0; i < block.Count; i++) pages.Insert(at + i, block[i]);

        // 顺序没变就别说「动了」。src 本来就紧挨在 dst 前面时（把一类拖到它后面那一类上），
        // 上面那趟搬运结果与原样完全相同，而返回 true 会让调用方存一次盘、报一次「配置变了」。
        // 同 PlaceInCategory 里那条判据的理由：白写一次盘。
        return !before.SequenceEqual(pages);
    }
}
