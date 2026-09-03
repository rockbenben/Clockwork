namespace Clockwork.Core;

// 面板搜索的匹配与排序。纯逻辑，不碰 WPF——搜索是一堆「这个词算不算命中、谁该排前面」的判断，
// 那种东西必须能单独测，不能只能靠打开面板打字来验。
//
// 三条规则，都是拿 Quicker 的搜索框对着看之后留下的：
//
//   1. **空格分词，全部命中才算命中。** 「清 板」能搜到「清剪贴板」。
//      一个词一个词地缩小范围，比想出一段连续的子串容易得多——尤其当你只记得动作里有哪几个字、
//      不记得它们连在一起是什么样。
//
//   2. **拼音首字母也算命中。** 「jt」→「截图」。中文标签配英文键盘，没有这条搜索基本没用。
//      （只有首字母，没有全拼，理由见 Pinyin 的类注释。）
//
//   3. **排序按「命中得有多直接」。** 名字开头命中 > 名字中间命中 > 拼音开头 > 拼音中间 > 说明里命中。
//      同一档里按名字中出现的位置排，再同则保持原来的先后——
//      排序必须稳定，否则每多打一个字，结果就重新洗一次牌，眼睛跟不上。
public static class ActionSearch
{
    /// <summary>一个词的命中强度。数越小越直接；<see cref="No"/> = 没命中。</summary>
    private const int NamePrefix = 0, NameContains = 1, PinyinPrefix = 2, PinyinContains = 3, TipContains = 4, No = int.MaxValue;

    /// <summary>按查询词过滤并排序。查询为空则原样返回（刚点开搜索、还没打字时，
    /// 该看到的是「所有页的动作摊在一起」，不是一片空白）。</summary>
    /// <param name="label">这一项显示的名字——排序主要看它。</param>
    /// <param name="tip">这一项的完整说明（悬停时那句）。也搜，但排在最后：
    /// 名字里有的东西比说明里有的东西更可能是用户要找的。</param>
    public static List<T> Rank<T>(IEnumerable<T> items, string? query, Func<T, string> label, Func<T, string?> tip)
    {
        var all = items.ToList();
        var tokens = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return all;

        var scored = new List<(T Item, int Tier, int At, int Ordinal)>();
        for (int i = 0; i < all.Count; i++)
        {
            var name = label(all[i]) ?? "";
            var note = tip(all[i]) ?? "";
            var initials = Pinyin.Initials(name);

            int worst = 0, firstAt = int.MaxValue;
            foreach (var tk in tokens)
            {
                var (tier, at) = Score(name, initials, note, tk);
                if (tier == No) { worst = No; break; }
                if (tier > worst) worst = tier;          // 最弱的那一环决定这一项的档位
                if (at < firstAt) firstAt = at;
            }
            if (worst != No) scored.Add((all[i], worst, firstAt == int.MaxValue ? 0 : firstAt, i));
        }

        return scored
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.At)
            .ThenBy(x => x.Ordinal)   // 稳定：同分的保持原来的先后，多打一个字不会把结果洗牌
            .Select(x => x.Item)
            .ToList();
    }

    private static (int Tier, int At) Score(string name, string initials, string tip, string token)
    {
        // **三处比较必须同一个口径，而那个口径是 Ordinal。**
        //
        // CurrentCulture 跟系统区域设置走，而 tr-TR 里 I 与 i 是两个不同的字母：
        // "Info".IndexOf("i", CurrentCultureIgnoreCase) == -1，而 OrdinalIgnoreCase 是 0。
        // 于是同一个关键词在 en-US 上搜得到、在土耳其语机器上搜不到，而 Strings.tr.resx 是发布语言。
        // 更隐的一半：下面拼音那一段本来就是 Ordinal，所以一个 ASCII 名字可能被拼音那条路
        // “捐回来”，但档位从 NamePrefix 降到 PinyinPrefix——结果能找到，排序却是错的。
        // Ordinal 在这里没有代价：CurrentCulture 同样不忽略变音，所以换过去不丢任何匹配能力。
        int i = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (i == 0) return (NamePrefix, 0);
        if (i > 0) return (NameContains, i);

        // 拼音只对纯字母的词有意义。「截图」这样的词去和首字母串比对不出东西，
        // 白跑一趟还可能误命中（首字母串里也可能出现汉字——非汉字是原样留下的）。
        if (IsAscii(token))
        {
            int p = initials.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (p == 0) return (PinyinPrefix, 0);
            if (p > 0) return (PinyinContains, p);
        }

        int t = tip.IndexOf(token, StringComparison.OrdinalIgnoreCase);   // 同上：与名字 / 拼音两档同口径
        return t >= 0 ? (TipContains, t) : (No, 0);
    }

    private static bool IsAscii(string s)
    {
        foreach (var c in s) if (c > 127) return false;
        return true;
    }
}
