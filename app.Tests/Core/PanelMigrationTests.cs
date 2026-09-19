using System.Linq;
using Clockwork.Core;
using Xunit;

// 「作为面板的一页」这个字段在升级时的行为。
//
// 这个文件原本测的是一段「老面板模型」的迁移，而那个老模型从未发布过 —— 详见
// ConfigStore.FillPanelPageDefaults 上的说明。更要命的是：**原来的夹具不等于现实**，
// 每个组都显式写了 ShowInPanel = true，而真实的老配置里那个键压根不存在（反序列化成 null）。
// 于是迁移在测试里一路绿灯，在真实升级路径上却把用户的每一个动作组都扫进一张凭空造出来的页。
//
// 所以这一版的夹具刻意**不写**那两个字段 —— 老配置长什么样，它就长什么样。
public class PanelPageDefaultsTests
{
    // 一个来自「还没有快捷面板的那个版本」的动作组：panel 相关字段一个都没有。
    private static ActionGroup Legacy(string name) =>
        new() { Name = name, Steps = { new LaunchStep { Kind = "system", Command = "lockScreen" } } };

    // 老配置：PanelSchema 默认 0。
    private static RootConfig Old(params ActionGroup[] groups)
        => new() { ActionGroups = new List<ActionGroup>(groups) };

    // 升级不该凭空造出任何东西。这是这组测试存在的全部理由。
    [Fact]
    public void Upgrading_never_manufactures_a_group()
    {
        var cfg = Old(Legacy("专注"), Legacy("收工"), Legacy("睡前"));
        ConfigStore.Normalize(cfg);
        Assert.Equal(3, cfg.ActionGroups.Count);
        Assert.DoesNotContain(cfg.ActionGroups, g => g.Steps.Any(s => s.Kind == "group"));
    }

    // 没表过态（null）一律不是页 —— 升级上来面板是空的，由空态引导建第一页。
    [Fact]
    public void A_null_flag_settles_to_not_a_page()
    {
        var cfg = Old(Legacy("专注"));
        ConfigStore.Normalize(cfg);
        Assert.False(cfg.ActionGroups[0].ShowInPanel);
        Assert.Empty(cfg.PanelPages);
    }

    // 用户自己勾成页的组：schema 3 之后会被搬进 cfg.PanelPages（页不再是动作）。
    // 内容一格不丢，而动作列表里只剩那些本来就不是页的。
    [Fact]
    public void An_explicit_page_becomes_a_panel_page()
    {
        var mine = Legacy("常用");
        mine.ShowInPanel = true;
        var cfg = Old(mine, Legacy("专注"));
        ConfigStore.Normalize(cfg);
        Assert.Equal("常用", Assert.Single(cfg.PanelPages).Name);
        Assert.Equal("专注", Assert.Single(cfg.ActionGroups).Name);
        Assert.DoesNotContain(cfg.ActionGroups, g => g.ShowInPanel == true);
    }

    // 显式关掉的仍然是关的。
    [Fact]
    public void An_explicit_false_stays_false()
    {
        var hidden = Legacy("藏起来");
        hidden.ShowInPanel = false;
        var cfg = Old(hidden);
        ConfigStore.Normalize(cfg);
        Assert.False(hidden.ShowInPanel);
    }

    // 幂等：跑几次都不该报「配置变了」，否则每次读盘都白写一次盘。
    [Fact]
    public void Filling_defaults_is_idempotent()
    {
        var cfg = Old(Legacy("专注"));
        Assert.True(ConfigStore.Normalize(cfg));
        // 0 → 3（面板页默认值）之后又被步骤 id 迁移抬到 4：Legacy 组里带着一个步骤，
        // 升级那一趟给四个清单里所有步骤发号。空配置才停在 3，见 StepIdMigrationTests。
        Assert.Equal(4, cfg.Settings.PanelSchema);
        Assert.False(ConfigStore.Normalize(cfg));
    }

    // 托盘的默认值与面板**相反**，两者不能互相污染：
    // 托盘补 true（保住升级前的外观），面板补 false（这一版才有的东西，没有外观可保）。
    [Fact]
    public void Tray_and_panel_defaults_point_opposite_ways()
    {
        var cfg = Old(Legacy("专注"));
        ConfigStore.Normalize(cfg);
        Assert.True(cfg.ActionGroups[0].ShowInTray);
        Assert.False(cfg.ActionGroups[0].ShowInPanel);
    }

    // PanelTab 一度存的是「页签摆哪边」，只有 "" 和 "top" 两个值。现在它存的是**分类名**，
    // 那个 "top" 会原样变成一个名叫「top」的分类：一个用户从没起过的名字，却在顶栏上占一格，
    // 而且再也没有别的页会落进去。实测用户配置里确实有一页是 "top"（试了那个换边方块），
    // 所以这不是理论上的可能。
    [Fact]
    public void The_old_top_side_marker_becomes_uncategorised()
    {
        var g = Legacy("离开一下");
        g.ShowInPanel = true;
        g.PanelTab = "top";
        var cfg = Old(g);
        ConfigStore.Normalize(cfg);
        Assert.Equal("", cfg.PanelPages[0].Tab);
    }

    // 注：schema 3 之后，「作为面板的一页」的组会被 MigratePanelPages 搬进 cfg.PanelPages，
    // 所以这些断言看的是 PanelPages[0].Tab 而不再是 ActionGroups[0].PanelTab。
    // 「top → 未分类」这条行为本身没变——变的只是它最终落在哪个实体上。

    // 大小写也算：手改 json 写成 "TOP" 同样是那个旧值。
    [Fact]
    public void The_old_marker_is_matched_regardless_of_case()
    {
        var g = Legacy("离开一下");
        g.ShowInPanel = true;
        g.PanelTab = " TOP ";
        var cfg = Old(g);
        ConfigStore.Normalize(cfg);
        Assert.Equal("", cfg.PanelPages[0].Tab);
    }

    // **迁移过一次之后，「top」就只是一个普通分类名了。** 之前这段没有 schema 门，
    // 理由写的是「清空本身就是已迁移」——那句话在字段只有两个取值时成立，而它现在是自由文本：
    // 一个真叫「top」的分类（英文用户起这名再自然不过）会被每一次读配置清掉并写回盘，
    // 每次启动都再来一遍，没有提示也找不回来。下面那条「不误伤」测的是 topics / Top 常用 /
    // toplevel——刻意的擦边，偏偏漏了正主。
    [Fact]
    public void After_migrating_once_top_is_just_a_name()
    {
        var g = Legacy("甲");
        g.ShowInPanel = true;
        g.PanelTab = "top";
        var cfg = Old(g);
        ConfigStore.Normalize(cfg);          // 第一次：旧值，清掉
        Assert.Equal("", cfg.PanelPages[0].Tab);

        // 之后用户自己建了一个就叫 top 的分类：再读多少次配置都得原样留着。
        cfg.PanelPages[0].Tab = "top";
        ConfigStore.Normalize(cfg);
        ConfigStore.Normalize(cfg);
        Assert.Equal("top", cfg.PanelPages[0].Tab);
    }

    // 真的分类名不能被误伤——「topics」「Top 常用」这种以 top 开头的都得原样留着。
    [Fact]
    public void A_real_category_name_is_left_alone()
    {
        foreach (var name in new[] { "topics", "Top 常用", "顶", "toplevel" })
        {
            var g = Legacy("甲");
            g.ShowInPanel = true;
            g.PanelTab = name;
            var cfg = Old(g);
            ConfigStore.Normalize(cfg);
            Assert.Equal(name, cfg.PanelPages[0].Tab);
        }
    }
}
