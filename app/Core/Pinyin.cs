using System.Globalization;

namespace Clockwork.Core;

// 汉字 → 拼音首字母。只做首字母，不做全拼。
//
// 为什么需要它：面板上的标签是中文（「截图」「息屏」「清剪贴板」），而搜索框里打的是英文键盘。
// 没有拼音匹配，中文用户要么记住每个动作在第几页，要么切输入法——两条路都比不搜索还慢。
//
// **为什么不引一个拼音库**：全拼需要一张几千条的汉字表，那是一个依赖或一份抄来的数据；
// 而首字母可以借 .NET 自己的 zh-CN 排序规则算出来——zh-CN 的排序本来就是按拼音排的，
// 于是「这个字的首字母是什么」等价于「它落在 a..z 哪两个锚字之间」。
// 二十六个锚字加一次二分，不引任何东西。
//
// 代价说清楚：
//   · 只有首字母，打「jietu」搜不到「截图」，打「jt」可以。实测下来首字母是绝大多数人的打法。
//   · 多音字取排序位置对应的那个音（「重」按 chong 而不是 zhong）。首字母匹配是「宁可多给结果」
//     的场合，少给才是问题，所以这个偏差可以接受。
//   · 依赖 ICU 的 zh-CN 排序是拼音序。这不是文档承诺的行为，所以**有测试钉着**
//     （PinyinTests）：哪天排序规则变了，测试会先叫，而不是用户先发现搜不到东西。
public static class Pinyin
{
    // 每段的锚字。**必须用常用字**：第一版取的是各段起点上的生僻字（呒、丌、妑…），
    // 实测全错——ICU 对这些字没有拼音数据，按回退规则排到了很前面，把一整批字划错了段
    //（「剪」「工」双双被判成 m，因为它们都排在「呒」之后）。
    // 换成 GB2312 各段起始处的常用字，这些字 ICU 一定有正确的拼音权重。
    private const string Anchors = "阿芭擦搭蛾发噶哈击喀垃妈拿哦啪期然撒塌挖昔压匝";
    private const string Letters = "abcdefghjklmnopqrstwxyz";   // 拼音里没有 i / u / v 打头的音节

    private static readonly CompareInfo Zh = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;

    /// <summary>汉字的拼音首字母；不是汉字就原样返回它的小写形式。</summary>
    public static char Initial(char c)
    {
        if (c < 0x4E00 || c > 0x9FFF) return char.ToLowerInvariant(c);

        var s = c.ToString();
        // 二分：找最后一个「不比 c 大」的锚字。锚字表本身就是按拼音序排的。
        int lo = 0, hi = Anchors.Length - 1, hit = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Zh.Compare(s, Anchors[mid].ToString(), CompareOptions.None) >= 0) { hit = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        // 排在第一个锚字之前（理论上不该发生）：交还原字，让调用方按普通字符处理。
        return hit < 0 ? c : Letters[hit];
    }

    /// <summary>整串的拼音首字母。非汉字原样保留（小写），所以「清空剪贴板 v2」→「qkjtb v2」。</summary>
    public static string Initials(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var buf = new char[text.Length];
        for (int i = 0; i < text.Length; i++) buf[i] = Initial(text[i]);
        return new string(buf);
    }
}
