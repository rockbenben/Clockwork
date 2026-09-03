using System.Linq;
using Clockwork.Core;
using Xunit;

public class DefaultConfigTests
{
    [Fact]
    public void Default_has_expected_collections()
    {
        var c = RootConfig.Default();
        Assert.Equal(5, c.LaunchSteps.Count);           // 一个真实的早晨：5 步
        Assert.Equal(4, c.Reminders.Count);             // 工作日两条 + 每天一条 + 每月一条：4 条
        Assert.Equal(2, c.ActionGroups.Count);          // 首启预置「离开一下」+「下班」两个可运行的动作组
        Assert.Equal(30, c.Settings.TickSeconds);
        Assert.Equal("Ctrl+Alt+Q", c.Settings.StopHotkey);
    }

    // 样例是照着改的模板，不该在用户还没看过一眼时就替他动电脑：首启必须什么都不执行。
    [Fact]
    public void Default_samples_are_all_disabled()
    {
        var c = RootConfig.Default();
        Assert.All(c.LaunchSteps, s => Assert.False(s.Enabled));
        Assert.All(c.Reminders, r => Assert.False(r.Enabled));
    }

    // 样例文案必须走 resx：直接返回键名说明键漏加了，非中文用户会在界面上看到 Smp_* 原始键名。
    [Fact]
    public void Default_samples_are_localized()
    {
        var c = RootConfig.Default();
        Assert.All(c.LaunchSteps, s => Assert.DoesNotContain("Smp_", s.Label, StringComparison.Ordinal));
        Assert.All(c.Reminders, r => Assert.DoesNotContain("Smp_", r.Message, StringComparison.Ordinal));
        Assert.All(c.LaunchSteps, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));
        Assert.All(c.Reminders, r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
    }

    // 条件执行的演示挪到了「打开常用网站」那一步（仅工作日），这条静音不再带时间限制。
    [Fact]
    public void Default_first_step_is_mute()
    {
        var s = RootConfig.Default().LaunchSteps[0];
        Assert.Equal("volume", s.Kind);
        Assert.Equal("mute", s.Action);
        Assert.False(s.OnlyBefore8);
    }

    [Fact]
    public void New_launchstep_defaults_match_ps()
    {
        var s = new LaunchStep();
        Assert.True(s.Enabled);
        Assert.Equal(50, s.Level);
        Assert.Equal(8, s.BeforeHour);
        Assert.Equal("{ENTER}", s.SendKey);
        Assert.Equal(1, s.Repeat);
        Assert.Equal("none", s.OnYes.Type);
    }

    [Fact]
    public void New_reminder_gets_unique_id()
    {
        Assert.NotEqual(new Reminder().Id, new Reminder().Id);
        Assert.False(string.IsNullOrWhiteSpace(new Reminder().Id));
    }

    // ── 出厂那一页面板 ──
    //
    // 装完第一次呼出面板看到的就是它。三条要求，每一条都被真实地踩过或差点踩到。

    // ① 每一格**装完就能按**：只放 Windows 自带的系统动作，不放「打开某某软件」——
    //    那种格子在别人的机器上就是个死格（路径 / 进程名都是这台机器上才成立的）。
    [Fact]
    public void Every_default_tile_works_on_a_fresh_machine()
    {
        var page = Assert.Single(RootConfig.Default().PanelPages);
        Assert.False(string.IsNullOrWhiteSpace(page.Name));
        foreach (var s in page.Steps)
        {
            Assert.Contains(s.Kind, new[] { "system", "group" });
            if (s.Kind == "system")
            {
                // 命令得是引擎认识的（下拉里有的），否则那一格点了什么都不会发生
                Assert.Contains(s.Command, StepDisplay.SystemCommandMap().Select(kv => kv.Key));
                Assert.True(string.IsNullOrEmpty(s.Target), "出厂格子不该带这台机器才有的路径");
            }
        }
    }

    // ② **不放通知开关**：它改注册表且不会自己恢复，新用户按一下之后可能好几天
    //    都不知道通知去哪了。开箱内容不该替人做这种会留下痕迹、又不提示的事。
    [Fact]
    public void The_default_page_ships_no_sticky_switches()
    {
        var page = Assert.Single(RootConfig.Default().PanelPages);
        Assert.DoesNotContain(page.Steps, s => s.Command is "notificationsOff" or "notificationsOn");
    }

    // ③ **每一格的图标各不相同**。面板的前提是「图标一眼说明这是哪一类东西」，
    //    而系统命令这一整类共用一个齿轮（PanelIcon 按 Kind 取字形，看不见 Command）——
    //    不逐格指定的话，首启那一页就是八个一模一样的齿轮，那句前提当场落空。
    //    （码位本身是否存在于字体里由 GlyphCoverageTests 盯着。）
    [Fact]
    public void The_default_tiles_do_not_all_look_alike()
    {
        var sys = RootConfig.Default().PanelPages.Single().Steps.Where(s => s.Kind == "system").ToList();
        Assert.All(sys, s => Assert.False(string.IsNullOrWhiteSpace(s.Icon), "系统格子没指图标，会退回统一的齿轮"));
        Assert.Equal(sys.Count, sys.Select(s => s.Icon).Distinct().Count());
    }
}
