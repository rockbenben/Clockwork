using Clockwork.Core;
using Xunit;

// **钉住「块内类型」这份名单**，以及它真的把下拉分成了两种语境。
//
// 这份名单回答的是「哪些步骤单独放一处没有意义」：它们自己不碰屏幕也不产出可看的东西，
// 存在的理由是夹在别的步骤之间改一改数据或改一改走向。用户报的是「类型下拉里什么都有，
// 很多只有编复杂动作时才用得上」——那份下拉原本 29 项不分场合。
//
// 名单只能变，**判据不能松**：把任何一个块内类型挪到原子那一侧（或者把一个原子类型划进块内）
// 都会让它在面板格子 / 开机清单 / 手势的「类型」下拉里露出来，问题原样回来。
public class StepKindContextTests
{
    // 名单里不能有拼错的 kind：它不在这张目录里，就永远不会出现在任何下拉里——
    // 写下这一行的人以为自己收窄了某个类型，实际什么也没发生。
    [Fact]
    public void Every_block_only_kind_exists()
        => Assert.All(StepDisplay.BlockOnlyKinds, k => Assert.Contains(k, StepDisplay.StepKinds));

    [Fact]
    public void The_two_lists_do_not_overlap()
        => Assert.Empty(StepDisplay.BlockOnlyKinds.Intersect(StepDisplay.KindsFor(insideGroup: false)));

    // 完整那份就是目录本身：编复杂动作时该有的都有，一项不少。
    [Fact]
    public void Inside_a_group_every_kind_is_offered()
        => Assert.Equal(StepDisplay.StepKinds.OrderBy(k => k), StepDisplay.KindsFor(insideGroup: true).OrderBy(k => k));

    // 单步那份去掉的就是「块内」那一族，且**只**去掉它们。
    [Fact]
    public void On_its_own_a_step_offers_only_atomic_kinds()
    {
        var atomic = StepDisplay.KindsFor(insideGroup: false).ToList();
        foreach (var k in StepDisplay.BlockOnlyKinds) Assert.DoesNotContain(k, atomic);
        foreach (var k in atomic) Assert.True(StepDisplay.IsAtomic(k), $"{k} 出现在原子清单里，可 IsAtomic 说不它");
    }

    // 两条最该被收窄的必须是块内：它们改的是「接下来跑哪几步」，单独一格没有可改的东西。
    [Fact]
    public void Flow_control_is_never_atomic()
    {
        Assert.False(StepDisplay.IsAtomic("each"));
        Assert.False(StepDisplay.IsAtomic("skip"));
        // 而「打开网址」「运行程序」「跑一个动作组」恰恰是面板格子上最常见的三种，得留在原子侧。
        Assert.True(StepDisplay.IsAtomic("url"));
        Assert.True(StepDisplay.IsAtomic("app"));
        Assert.True(StepDisplay.IsAtomic("group"));
    }

    // 顺序也要一样：用户在动作组里看到「每个」排在「跳出」之后，单步语境那一截也得是同一个顺序。
    [Fact]
    public void The_atomic_list_keeps_the_catalogue_order()
    {
        var want = StepDisplay.StepKinds.Where(StepDisplay.IsAtomic).ToList();
        Assert.Equal(want, StepDisplay.KindsFor(insideGroup: false).ToList());
    }

    // 「发送按键」要摆在「常用」那一节里。
    //
    // 用户报的是「发送按键 是非常常用的动作，却在很多路径都没有入口」：它原先只在
    // 「系统与窗口」那一节——那一节问的是「我对什么做事」，而敲键不针对任何窗口对象；
    // 它的兄弟「发送文本」一直就在「常用」里。同一个「发送」分成两处摆着，
    // 最常用的那个反倒最难找。而「常用」这一节现在在菜单里被拍平到首层（一格直达）。
    [Fact]
    public void Send_keys_is_a_daily_action_and_lives_in_the_common_section()
    {
        var common = StepDisplay.StepKindSections.First(s => s.SectionKey == StepDisplay.CommonSectionKey);
        Assert.Contains("keys", common.Kinds);
        // 它的兄弟「发送文本」也在这儿——两者是同一族，不该分开摆。
        Assert.Contains("text", common.Kinds);
        // 不再留在「系统」那一节：那一节现在只放「对这台机器本身」的事。
        var sys = StepDisplay.StepKindSections.First(s => s.SectionKey == "Menu_SecSys");
        Assert.DoesNotContain("keys", sys.Kinds);
    }

    // **一种类型只在一节里。**
    //
    // 上一版允许一种类型出现在多节（Quicker 官方原话：「个别模块会属于多个分类」），
    // 看着是给了两条找路，实际在「新增 ▾」菜单里变成**同一个动作在菜单里出现两次**：
    // 「用户输入」既在「常用」又在「界面交互」，「获取选中的文字」既在「常用」又在「剪贴板」。
    // 「按对象找」的那条路没丢——动作组工具箱有搜索框，找不着就搜。
    [Fact]
    public void Every_kind_belongs_to_exactly_one_section()
    {
        var flat = StepDisplay.StepKindSections.SelectMany(s => s.Kinds).ToList();
        Assert.Equal(flat.Count, flat.Distinct().Count());
        Assert.Equal(StepDisplay.StepKinds.OrderBy(k => k), flat.Distinct().OrderBy(k => k));
    }

    // 首层快捷项（每个「新增」菜单的第一层）里必须有「发送按键」：
    // 那是唯一一处「不用先猜分类」的路。缺了它，用户在启动清单、手势、面板管理器、
    // 快捷面板四个入口里都得展开「系统与窗口」才够得着这个最常用的机制。
    [Fact]
    public void Send_keys_has_a_one_click_entry_in_every_add_menu()
    {
        Assert.Contains("keys", Clockwork.Views.StepMenu.QuickKinds);
        // 首层那一项也必须自己站得住：能单独当一格用（面板格子 / 开机清单 / 手势）。
        Assert.All(Clockwork.Views.StepMenu.QuickKinds, k => Assert.True(StepDisplay.IsAtomic(k)));
        // 并且它在目录里（拼错了的话那个名字在哪儿都选不到）。
        Assert.All(Clockwork.Views.StepMenu.QuickKinds, k => Assert.Contains(k, StepDisplay.StepKinds));
    }
}
