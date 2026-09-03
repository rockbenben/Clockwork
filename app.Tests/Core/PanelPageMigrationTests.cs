using System.Collections.Generic;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 面板页从动作里独立出来（PanelSchema 2 → 3）。
//
// 这是这次重设计里唯一动数据的一步，而它是**结构性改写**：动作列表会少几个、托盘会少几行。
// 静默完成的话，用户下次打开只会以为程序把配置弄坏了——所以每一条改动都要记账并报出来。
public class PanelPageMigrationTests
{
    private static RootConfig Cfg(params ActionGroup[] groups)
        => new() { ActionGroups = groups.ToList(), Settings = new AppSettings { PanelSchema = 2 } };

    private static ActionGroup Page(string name, int steps, string tab = "", bool tray = false)
        => new()
        {
            Id = "p-" + name, Name = name, ShowInPanel = true, PanelTab = tab, ShowInTray = tray,
            Steps = Enumerable.Range(0, steps).Select(i => new LaunchStep { Kind = "keys", Combo = "Ctrl+" + i }).ToList(),
        };

    // 情形 1（你的三个页全是这种）：只当页用 → 步骤整批搬走，动作列表里不再有它。
    [Fact]
    public void A_page_that_is_only_a_page_moves_out_of_the_actions()
    {
        var cfg = Cfg(Page("手边", 25, "main"), new ActionGroup { Id = "a1", Name = "专注", ShowInPanel = false });
        var log = ConfigStore.MigratePanelPages(cfg);

        Assert.Single(cfg.PanelPages);
        Assert.Equal("手边", cfg.PanelPages[0].Name);
        Assert.Equal("main", cfg.PanelPages[0].Tab);
        Assert.Equal(25, cfg.PanelPages[0].Steps.Count);        // 一格都不能丢
        Assert.Equal("p-手边", cfg.PanelPages[0].Id);            // 沿用原 id，便于事后追溯
        Assert.DoesNotContain(cfg.ActionGroups, g => g.Name == "手边");
        Assert.Contains(cfg.ActionGroups, g => g.Name == "专注"); // 不是页的动作原样留下
        Assert.NotEmpty(log);
    }

    // 情形 2：既当页、又有热键 → **动作留下**，页改放一个指回它的引用格。
    // 丢一页格子可以再摆，丢一个被引用的动作会让提醒和别的步骤一起断。
    [Fact]
    public void A_page_that_is_also_an_action_keeps_the_action()
    {
        var g = Page("双重", 5); g.Hotkey = "Ctrl+Alt+F";
        var cfg = Cfg(g);
        ConfigStore.MigratePanelPages(cfg);

        var kept = Assert.Single(cfg.ActionGroups);
        Assert.Equal("双重", kept.Name);
        Assert.Equal(5, kept.Steps.Count);                       // 动作的步骤原封不动
        Assert.False(kept.ShowInPanel);                          // 面板字段清干净
        Assert.Equal("", kept.PanelTab);

        var page = Assert.Single(cfg.PanelPages);
        var cell = Assert.Single(page.Steps);
        Assert.Equal("group", cell.Kind);
        Assert.Equal(kept.Id, cell.GroupId);                     // 引用格指回那个动作
        Assert.NotEqual(kept.Id, page.Id);                       // 页要新 id，不能和动作抢同一个
    }

    // 被提醒引用的，同样算「也是动作」——抹掉它会让提醒断在半路。
    [Theory]
    [InlineData(true, false)]   // 静默动作
    [InlineData(false, true)]   // 「点是后」
    public void A_page_referenced_by_a_reminder_keeps_the_action(bool silent, bool onYes)
    {
        var cfg = Cfg(Page("被引用", 3));
        cfg.Reminders.Add(new Reminder
        {
            SilentGroupId = silent ? "p-被引用" : "",
            OnYes = onYes ? new OnYes { Type = "group", Target = "p-被引用" } : new OnYes(),
        });
        ConfigStore.MigratePanelPages(cfg);
        Assert.Single(cfg.ActionGroups);
        Assert.Equal("group", Assert.Single(cfg.PanelPages[0].Steps).Kind);
    }

    // 被手势引用的也一样（手势绑的是 group 类型的步骤）。
    [Fact]
    public void A_page_referenced_by_a_gesture_keeps_the_action()
    {
        var cfg = Cfg(Page("手势的", 3));
        cfg.Gestures.Add(new LaunchStep { Kind = "group", GroupId = "p-手势的", Gesture = "RD" });
        ConfigStore.MigratePanelPages(cfg);
        Assert.Single(cfg.ActionGroups);
    }

    // 情形 3：页上挂着托盘项 → 去掉，并记账。页不是可运行的序列，托盘里点它没有意义。
    [Fact]
    public void A_tray_entry_on_a_page_is_reported()
    {
        var cfg = Cfg(Page("一键", 9, tray: true));
        var log = ConfigStore.MigratePanelPages(cfg);
        Assert.Contains(log, l => l.Contains("一键") && l.Contains("托盘"));
    }

    // **幂等**：跑过一次之后不再动。这条是承重的——迁移每次启动都会被调用，
    // 不幂等的话第二次启动会把已经搬好的页再搬一遍，凭空多出一批空页。
    [Fact]
    public void It_runs_exactly_once()
    {
        var cfg = Cfg(Page("手边", 4));
        ConfigStore.MigratePanelPages(cfg);
        cfg.Settings.PanelSchema = 3;                            // FillPanelPageDefaults 会这么干
        var again = ConfigStore.MigratePanelPages(cfg);
        Assert.Empty(again);
        Assert.Single(cfg.PanelPages);
    }

    // 没有面板页的配置：什么都不该发生，也不该报一张空账的卡片。
    [Fact]
    public void A_config_without_pages_is_untouched()
    {
        var cfg = Cfg(new ActionGroup { Id = "a1", Name = "专注", ShowInPanel = false });
        Assert.Empty(ConfigStore.MigratePanelPages(cfg));
        Assert.Empty(cfg.PanelPages);
        Assert.Single(cfg.ActionGroups);
    }
}
