using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Views;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这个名字两边都有（同别处那几行）。
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Clockwork;

// 面板的两条导航栏：**上边选分类，左边列这一类里的页**——父子，不是平行的两处。
//
// 这一节自查三件事，全是「代码里看不出来、只有真点一遍才知道」的那一类：
//
//   ① 顶栏点了另一个分类，**左栏必须跟着换**。
//      曾经它是坏的：JumpToCategory 先把 _cat 改成目标类，而 ShowPage → PaintRails →
//      SyncCategory 认的是「**当前页**属于哪一类」，看到 _cat 已经等于目标类，
//      就以为「类别没变」提前返回，左栏于是不重建——格子换到了那一类，
//      左栏还列着上一类的页（用户实报：「切换横分类时竖栏没有对应内容」）。
//
//   ② 两扇窗各自怎么把层级说清楚：**管理器**在两条栏顶上写明「分类」「页」，
//      **快捷面板**不写字，只把左栏整体缩进 12px（面板每一像素都得留给格子，
//      层级靠形状说）。用户实报过「面板上『页』『分类』这个属性名不该显示」——
//      于是那两个词从面板上撤了，只留在管理器里。
//
//   ③ 管理器顶栏那颗「+」：**在新的一类里放一张空页**，一张已有页都不挪走，
//      而且新的一类排在顶栏末尾（用户实报：「新分类不该放在最前面」、
//      「新分类的页应该默认为空的，不该把其他分类的页直接移过来」）。
//
// 为什么在自查里而不在单元测试里：这几条都住在**窗口的接线**上，
// 而单元测试进程里不能建 WPF Application（手势那一族测试明确依赖「没有 Application」，
// 造一个会让它们跨线程炸掉）——所以真窗口的检查走这一节，跑在真进程里。
internal static class CategoryNavChecks
{
    /// <summary>跑一遍两条导航栏的自查。返回是否全过；<paramref name="log"/> 是给人看的那份账。</summary>
    public static bool Run(out string log)
    {
        var fails = new List<string>();
        var notes = new List<string>();

        PanelCategorySwitch(fails, notes);
        BarHeads(fails, notes);
        ManagerNewCategory(fails, notes);

        log = string.Join("；", notes) + (fails.Count > 0 ? "｜失手：" + string.Join("；", fails) : "");
        return fails.Count == 0;
    }

    // 一块有两类、每类两页的面板。
    private static PanelTilePage[] SamplePages() => new[]
    {
        new PanelTilePage("甲一", new[] { new PanelTile("甲", "1", () => { }) }, Tab: "写代码"),
        new PanelTilePage("甲二", new[] { new PanelTile("甲", "1", () => { }) }, Tab: "写代码"),
        new PanelTilePage("乙一", new[] { new PanelTile("乙", "1", () => { }) }, Tab: "下班"),
        new PanelTilePage("乙二", new[] { new PanelTile("乙", "1", () => { }) }, Tab: "下班"),
    };

    // 左栏里那几条页签的页名（页签的内容是一个 TextBlock，不是字符串）。
    private static string[] RailTitles(QuickPanelWindow w)
        => w.PageRail.Children.OfType<Button>()
            .Select(b => (b.Content as TextBlock)?.Text ?? "")
            .ToArray();

    // ① 顶栏点了另一个分类，左栏跟着换。
    private static void PanelCategorySwitch(List<string> fails, List<string> notes)
    {
        try
        {
            var w = new QuickPanelWindow(SamplePages(), look: new PanelLook());
            try
            {
                var before = RailTitles(w);
                var tab = w.TopTabBar.Children.OfType<Button>()
                    .FirstOrDefault(b => (string?)b.Content == "下班");
                if (tab == null)
                {
                    fails.Add("快捷面板·切类：顶栏上找不到「下班」那一格");
                    return;
                }
                tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var after = RailTitles(w);
                if (!before.SequenceEqual(new[] { "甲一", "甲二" }))
                    fails.Add($"快捷面板·切类：切之前左栏就不是当前这一类的页（[{string.Join(",", before)}]）");
                if (!after.SequenceEqual(new[] { "乙一", "乙二" }))
                    fails.Add($"快捷面板·切类：点了「下班」，左栏还是 [{string.Join(",", after)}]，应当换成「下班」那两条");
                notes.Add($"快捷面板·切类 [{string.Join(",", before)}]→[{string.Join(",", after)}]");
            }
            finally { try { w.Close(); } catch { } }
        }
        catch (Exception ex) { fails.Add("快捷面板·切类：自查抛了 " + ex.Message); }
    }

    // ② 层级靠什么说清楚：管理器写字，快捷面板缩进。
    private static void BarHeads(List<string> fails, List<string> notes)
    {
        // 快捷面板：**不写字**。那两个词是「属性名」样的说明词，面板上不该有。
        try
        {
            var w = new QuickPanelWindow(SamplePages(), look: new PanelLook());
            try
            {
                if (w.TopTabBar.Children.OfType<TextBlock>().Any())
                    fails.Add("快捷面板·顶栏上出现了说明词（「分类」/「页」那样的）——面板上不摆这些");
                if (w.FindName("RailHead") != null)
                    fails.Add("快捷面板·左栏上出现了说明词（「分类」/「页」那样的）——面板上不摆这些");
                // 缩进是面板上留下来的那条层级线索：左栏整体往右挪，与顶栏那几格错开。
                var rail = w.PageRail;
                if (rail.Margin.Left < 8)
                    fails.Add($"快捷面板·左栏没有缩进（左边距 {rail.Margin.Left}，应当 ≥ 8）：它与顶栏那几格上下对齐就成了同一个列表");
                notes.Add($"快捷面板·层级靠缩进（左 {rail.Margin.Left}px），无说明词");
            }
            finally { try { w.Close(); } catch { } }
        }
        catch (Exception ex) { fails.Add("快捷面板·栏头：自查抛了 " + ex.Message); }

        // 管理器：两条栏各自写明自己装的是什么。
        try
        {
            var cfg = RootConfig.Default();
            cfg.PanelPages.Clear();
            cfg.PanelPages.Add(new PanelPage { Name = "甲", Tab = "写代码" });
            cfg.PanelPages.Add(new PanelPage { Name = "乙", Tab = "下班" });
            var w = new PanelManagerWindow(cfg, () => { });
            try
            {
                var head = w.TopTabBar.Children.OfType<TextBlock>().FirstOrDefault();
                if (head == null || head.Text != Strings.Get("Panel_Category"))
                    fails.Add($"管理器·顶栏没写明它装的是「分类」（现在写着「{head?.Text}」）");
                var railHead = w.FindName("RailHead") as TextBlock;
                if (railHead == null || railHead.Text != Strings.Get("Panel_Pages"))
                    fails.Add($"管理器·左栏没写明它装的是「页」（现在写着「{railHead?.Text}」）");
                notes.Add("管理器·栏头 " + head?.Text + "/" + railHead?.Text);
            }
            finally { try { w.Close(); } catch { } }
        }
        catch (Exception ex) { fails.Add("管理器·栏头：自查抛了 " + ex.Message); }
    }

    // ③ 管理器顶栏那颗「+」：新的一类里放一张空页，谁也不挪，而且它排在末尾。
    private static void ManagerNewCategory(List<string> fails, List<string> notes)
    {
        try
        {
            var cfg = RootConfig.Default();
            cfg.PanelPages.Clear();
            var a = new PanelPage { Name = "甲", Tab = "main" };
            var b = new PanelPage { Name = "乙", Tab = "main" };
            cfg.PanelPages.Add(a);
            cfg.PanelPages.Add(b);
            int saves = 0;
            var w = new PanelManagerWindow(cfg, () => saves++);
            try
            {
                var plus = w.TopTabBar.Children.OfType<Button>()
                    .LastOrDefault(x => (string?)x.Content == "+");
                if (plus == null)
                {
                    fails.Add("管理器·新建分类：顶栏上找不到那颗「+」");
                    return;
                }
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // 一张已有页都不许被挪走：按下「+」的人要的是多一类，不是把手边那一页搬走。
                if (a.Tab != "main" || b.Tab != "main")
                    fails.Add($"管理器·新建分类：别的页被挪了（{a.Name}→{a.Tab}、{b.Name}→{b.Tab}）");
                // 新的一类里落下一张**空页**（页是分类唯一的容身之处）。
                var fresh = cfg.PanelPages.LastOrDefault();
                if (fresh == null || fresh.Tab == "main")
                    fails.Add("管理器·新建分类：新的一类里没有落下那张空页");
                else if (fresh.Steps.Count != 0)
                    fails.Add("管理器·新建分类：新落下的那张页不是空的");
                // 落盘了，这一类在配置里站得住（关掉管理器再打开它还在）。
                if (saves == 0)
                    fails.Add("管理器·新建分类：没有落盘");
                var shown = PanelLayout.BuildPages(cfg.PanelPages, keepEmpty: true, allContexts: true);
                var cats = PanelLayout.Categories(shown);
                if (fresh != null && !cats.Contains(fresh.Tab))
                    fails.Add("管理器·新建分类：这一类在配置里没有容身之处（关掉管理器就没了）");
                // **新的一类排在末尾**：顶栏的先后就是页的先后，新建一类不该把某一页从队首挪走。
                if (fresh != null && cats.Count > 1 && cats[0] == fresh.Tab)
                    fails.Add("管理器·新建分类：新的一类跑到了最前面（应当排在末尾）");
                notes.Add($"管理器·新建分类 →「{fresh?.Tab}」，顶栏顺序 [{string.Join(",", cats)}]");
            }
            finally { try { w.Close(); } catch { } }
        }
        catch (Exception ex) { fails.Add("管理器·新建分类：自查抛了 " + ex.Message); }
    }
}
