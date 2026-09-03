using Clockwork.I18n;

namespace Clockwork.Core;

/// <summary>面板上的一格（还没绑动作）：就是某一页（<see cref="Page"/>）里的某一步（<see cref="Step"/>）。
/// 两者都恒非空——面板上只有这一种格子。想要「跑整个动作」的按钮，那一步的 Kind 就是 group。
/// <see cref="Icon"/> 恒非空：一格没图标会在一片有图标的格子里显得像是坏了。</summary>
//
// Page 从 ActionGroup 换成 PanelPage 是这一版拆分的落点：格子属于**页**，不属于动作。
// 从前它指向动作组，因为「页就是组」；拆开之后再指向动作就说不通了——一页上的格子
// 可以来自任何地方（内联的步骤、指向某个动作的引用格），它们唯一共同的归属是这一页。
public sealed record PanelItem(string Label, PanelIconSpec Icon, bool Enabled, PanelPage Page, LaunchStep Step);

/// <summary>面板一页的**一次排版结果**（View，不是存储）。存储那份是 Core.PanelPage。</summary>
//
// 两者分开命名是因为它们答的不是同一个问题：存储的页答「用户配了什么」（名字、分类、场景、格子），
// 这份答「这一屏该画什么」（标题、这一屏的格子、第几屏）。一页在容量不够时会被切成好几屏，
// 于是一个存储页对应多个 View——名字一样的话，"pages.Count" 到底是几就说不清了。
/// <summary>Title 会显示在页签上，所以它必须是人话——
/// 页名就是用户给这一页起的名字，而不是「第 2 页」这种编号。
/// <see cref="Tab"/> 是这一页归哪一类（空 = 未分类）；
/// <see cref="Screen"/> 是同一页被容量切开之后的第几屏（从 0 起），
/// 左侧页签认的是「哪一页」，腰栏的圆点认的是「这一页的第几屏」——两件事分开。</summary>
/// <param name="Source">这一屏是从哪一页排出来的，恒非空——
/// 别再从 Items[0].Page 去反推：那条路只在「空页不可能存在」时才成立，
/// 而管理器恰恰要显示空页（新建的页天生是空的），那时第一个格子并不存在，
/// 反推出来的是 null，于是那张卡拖不动、也删不掉。</param>
public sealed record PanelPageView(string Title, IReadOnlyList<PanelItem> Items,
                              string Tab = "", int Screen = 0, PanelPage? Source = null);

// 面板上摆哪些格子、分几页、什么顺序。纯函数：不碰 WPF、不绑委托，App 拿到结果再给每格接上真正的动作。
//
// **一页 = 一个 PanelPage，页里每一步各占一格。** 只有这一种来源，没有程序自动拼出来的页。
//
// 页是独立实体，不再是「某个被打了勾的动作」：一页上的格子可以是内联的步骤，
// 也可以是指向某个动作的 group 引用格——「跑整个动作」的按钮就是后者。
// 从前页寄生在动作组身上（ShowInPanel/PanelTab/PanelExpand 一串字段），为此付过好几次代价：
// 一个「页」会混进动作列表、托盘菜单里出现一个跑不动的页、空名页签。PanelSchema 3 把它拆开了。
//
// 分页规则跟着内容走，而不是按容量切：页名写得出「常用」「开发」，翻页索引旁边才有方向感。
// 按容量机械切页（每 8 格一页）也能分出页来，但页与页的边界毫无意义，索引旁边只能写「2/4」。
// 一页装不下时才按 capacity 二次切分（BuildPages），那是容量问题，不是内容问题。
public static class PanelLayout
{
    /// <summary>本页是不是「场景页」——绑定到某个程序，只在那个程序前台时出现。</summary>
    public static bool IsContextual(PanelPage p) => !string.IsNullOrWhiteSpace(p?.ForProcess);

    /// <summary>本页此刻该不该出现。<paramref name="foreground"/> 是呼出面板那一刻的前台进程名。</summary>
    //
    // 大小写不敏感 + 走 ToProcessName 归一：用户可能填 "Code"、"code.exe"，
    // 也可能从选择器里挑到一条完整路径，三种写法说的是同一个程序。
    public static bool MatchesContext(PanelPage p, string? foreground)
    {
        if (!IsContextual(p)) return true;   // 全局页：任何时候都在
        var want = StepHelpers.ToProcessName(p.ForProcess);
        var have = StepHelpers.ToProcessName(foreground ?? "");
        return have.Length > 0 && string.Equals(want, have, StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="foreground">呼出面板那一刻的前台进程名（裸名或路径皆可）。
    /// 必须在面板显示**之前**取——面板一显示就把前台抢走了。传 null / 空则只出全局页。</param>
    /// <param name="capacity">一页装得下多少格（见 <see cref="PanelMetrics.PageCapacity"/>）。
    /// 装不下的自动续到下一页，续页沿用同一个页名——续页是「同一批东西的下半截」，
    /// 给它另起一个名字（「常用 2」之类）是在假装那是另一类东西。&lt;=0 表示不限，整页一次放完。</param>
    /// <param name="keepEmpty">连一步都没有的页也留下。**面板不要、管理器要**：
    /// 面板上翻到一张空页只会让人以为出错了；而管理器正是你往空页里放动作的地方，
    /// 新建的页天生就是空的，不显示出来就等于「点了添加什么也没发生」。</param>
    /// <param name="allContexts">不按前台筛，绑了程序的页也一并产出。
    /// **面板不要、管理器要**：面板是「此刻该出现哪些页」，而管理器是「我一共有哪些页」——
    /// 按前台筛的话，绑给别的程序的页在管理器里整批消失，用户只能靠想起自己给它绑过程序
    /// 才知道该去哪儿找它。绑哪个程序是每一页自己的一个字段，不是一个导航维度。</param>
    /// <param name="actions">动作清单，只用来给「运行动作」格子取被引用那个动作的图标。
    /// 传 null 只是那种格子回落到按 Kind 取默认图标，页照排不误。</param>
    public static List<PanelPageView> BuildPages(IReadOnlyList<PanelPage>? pages, string? foreground = null,
                                             int capacity = 0, bool keepEmpty = false, bool allContexts = false,
                                             IReadOnlyList<ActionGroup>? actions = null)
    {
        var built = BuildContentPages(pages, foreground, keepEmpty, allContexts, actions);
        if (capacity <= 0) return built;
        var chunked = new List<PanelPageView>();
        foreach (var p in built)
        {
            // 空页切不出任何一屏（循环一次都不进），会在这里第二次消失。留着它。
            if (p.Items.Count == 0) { chunked.Add(p); continue; }
            for (int i = 0; i < p.Items.Count; i += capacity)
                chunked.Add(p with { Items = p.Items.Skip(i).Take(capacity).ToList(), Screen = i / capacity });
        }
        return chunked;
    }

    /// <summary>左侧页签要画哪几条：一页一条。同一页被容量切开的那几屏只算一条——
    /// 页签回答「哪一页」，腰栏的圆点回答「这一页的第几屏」，两件事不该挤在同一处。</summary>
    public static List<PanelPageView> Tabs(IReadOnlyList<PanelPageView>? pages)
        => pages == null ? new List<PanelPageView>() : pages.Where(p => p.Screen == 0).ToList();

    /// <summary>这一页归哪一类。空串 = 未分类，它自己也算一类（顶栏上有它的位置）。</summary>
    public static string CategoryOf(PanelPageView p) => Category(p?.Tab);

    /// <summary>同上，取存储那份页的分类。</summary>
    public static string CategoryOf(PanelPage p) => Category(p?.Tab);

    /// <summary>两个分类名指的是不是同一类。**这是「同一类」的唯一定义**——
    /// 去首尾空白 + 不区分大小写。</summary>
    //
    // 抄成七份的代价已经付过一次：其中两份写成了区分大小写的 Ordinal，于是分别填了
    // 「Dev」和「dev」的两页在顶栏上被折成一格（这里忽略大小写），你在那一格上改名，
    // 却只有拼写一致的那些页跟着走——另一半留在一个已经不存在的分类里，从顶栏上消失，
    // 而且没有任何东西会报错。谁算同一类这件事，只能有一个答案。
    public static bool SameCategory(string? a, string? b)
        => string.Equals(Category(a), Category(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>把一个分类名归一（去首尾空白）。面板那边拿的是另一个类型（PanelTilePage），
    /// 够不到上面两个重载，直接用它。</summary>
    public static string Category(string? tab) => (tab ?? "").Trim();

    /// <summary>顶栏要画哪几个分类。</summary>
    //
    // **两条栏是父子：上边选类，左边选这一类里的页。** 顶栏装的是分类名，不是页——
    // 页只有一个落点（左栏），所以不存在「这一页到底在哪条栏上」这个问题。
    //
    // 顺序按第一次出现的先后，不排序：分类的先后就是页列表的先后，
    // 那个列表本来就能上下移动，另排一次早晚会和它分歧。
    // 未分类的那一堆也占一格（空串）：不给它位置的话，没填分类的页就无处可去。
    public static List<string> Categories(IReadOnlyList<PanelPageView>? pages)
    {
        var seen = new List<string>();
        foreach (var p in Tabs(pages))
        {
            var c = CategoryOf(p);
            if (!seen.Any(x => SameCategory(x, c))) seen.Add(c);
        }
        return seen;
    }

    /// <summary>左栏要画哪几条页签：<paramref name="category"/> 这一类里的页。</summary>
    public static List<PanelPageView> PagesIn(IReadOnlyList<PanelPageView>? pages, string? category)
    {
        return Tabs(pages).Where(p => SameCategory(CategoryOf(p), category)).ToList();
    }

    // 按内容分页（不管容量）：场景页在前，全局页在后。
    private static List<PanelPageView> BuildContentPages(IReadOnlyList<PanelPage>? pages, string? foreground,
                                                     bool keepEmpty = false, bool allContexts = false,
                                                     IReadOnlyList<ActionGroup>? actions = null)
    {
        var result = new List<PanelPageView>();
        if (pages == null) return result;

        var contextPages = new List<PanelPageView>();  // 场景页：排在最前，面板打开就停在它上面
        var globalPages = new List<PanelPageView>();   // 全局页

        foreach (var pg in pages)
        {
            // 一页就是一个 PanelPage。没有第二种来源，也没有程序自动拼出来的页。
            if (pg == null) continue;
            if (!allContexts && !MatchesContext(pg, foreground)) continue;   // 场景不对：这一页这次整个不出现

            var items = new List<PanelItem>();
            foreach (var s in pg.Steps ?? new List<LaunchStep>())
            {
                if (s == null) continue;
                // 置灰而不是不显示：「它被关掉了」和「它没了」得分得清，
                // 否则用户会以为自己配错了、去重新加一遍一个其实还在的动作。
                // 「运行动作」的格子：这一步自己没设图标时，用被引用那个动作的图标。
                // 动作的图标设一次，所有指向它的格子都跟着——否则同一个动作在三页上出现就得配三次。
                var icon = s.Icon;
                if (string.IsNullOrWhiteSpace(icon) && s.Kind == "group")
                    icon = actions == null ? "" : ActionGroupResolver.Resolve(actions, s.GroupId)?.Icon ?? "";
                items.Add(new PanelItem(StepDisplay.StepTitle(s),
                                        PanelIcon.Resolve(icon, s.Kind, s.Target, s.AltTargets),
                                        s.Enabled, pg, s));
            }
            // 空页不产出：面板上翻到一张什么都没有的页，只会让人以为出错了。
            // 但管理器要（keepEmpty）——新建的页天生是空的，不显示出来就是「点了添加什么也没发生」。
            if (items.Count == 0 && !keepEmpty) continue;
            // allContexts（管理器）时一律进 globalPages，也就是**不排序**、完全跟随页列表。
            // 场景页排前面是给面板用的（「呼出来就停在匹配的那一页」）；管理器里没有「匹配」，
            // 那个排序在这儿只会制造分歧：显示顺序 ≠ 列表顺序，而拖页签改的是列表顺序，
            // 于是把一个全局页拖到一个绑定页前面，重画后它又被排回后面——读起来就是「拖了没用」。
            // 页名兜底。**这不是给界面用的**——面板管理器里的就地改名（清空即回落到「新建页」）
            // 已经保证页名非空，从界面走不出一个没名字的页。
            // 挡的是**手改 json 与导入进来的配置**：那两条绕过全部编辑器，而一个空标题在页签栏上
            // 就是一块点得到、却什么都没写的空白，看着像页签坏了。
            // 用同一句「新建页」而不是另造一条文案：就地改名清空时用的就是它，一处空名一种说法。
            (!allContexts && IsContextual(pg) ? contextPages : globalPages).Add(new PanelPageView(
                string.IsNullOrWhiteSpace(pg.Name) ? Strings.Get("Panel_NewPage") : pg.Name,
                items, CategoryOf(pg),
                Source: pg));
        }

        // 页序即优先级，第 0 页是面板打开时停的那一页：场景页在前，全局页在后。
        // 在 VS Code 里呼出就直接停在绑给它的那一页，不必再翻——这正是场景页存在的理由。
        //
        // 同一类之内完全跟随页列表的顺序：那个列表本来就能上下移动，面板不另存一份页序。
        // 两份顺序早晚会分歧，那时用户在一处调完发现另一处没动，只会觉得排序坏了。
        result.AddRange(contextPages);
        result.AddRange(globalPages);
        return result;
    }

    /// <summary>把一页的格子切成上下两条带：上带最多 topRows 行，其余归下带。
    /// 上带没填满时下带为空（两条带是**上限**，不是预留的空位——预留会让只有三个动作的面板
    /// 底下空出四行，看着像没加载完）。</summary>
    //
    // 泛型是因为两边的元素类型不同：Core 这层是 PanelItem，视图那层已经换成了绑好动作的 PanelTile。
    // 切分的公式只有一条，放在这里两边共用——抄两份的话，改了行数上限只会改中一处。
    public static (List<T> Top, List<T> Bottom) SplitBands<T>(IReadOnlyList<T>? items, int topRows, int columns)
    {
        var all = items ?? (IReadOnlyList<T>)Array.Empty<T>();
        int cut = PanelMetrics.ClampRows(topRows) * PanelMetrics.ClampColumns(columns);
        return (all.Take(cut).ToList(), all.Skip(cut).ToList());
    }

}
