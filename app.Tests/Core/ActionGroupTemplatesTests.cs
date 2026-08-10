using Clockwork.Core;
using Xunit;

public class ActionGroupTemplatesTests
{
    // 数量是刻意钉死的：收录标准见 ActionGroupTemplates 头部注释（常用 + 演示别处没有的能力）。
    // 曾膨胀到 10 又砍回 7——这条红了说明有人加/删了模板，去核对那份标准，别顺手改数字了事。
    [Fact]
    public void Every_template_has_a_name_steps_and_is_enabled()
    {
        var all = ActionGroupTemplates.All();
        Assert.Equal(7, all.Count);
        Assert.All(all, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
        Assert.All(all, g => Assert.NotEmpty(g.Steps));
        Assert.All(all, g => Assert.True(g.Enabled));
    }

    // 关通知 / 麦克风静音 / 扬声器静音都是有状态的开关：改完不会自己恢复。模板里每出现一个「关」，
    // 「恢复常态」里就必须有对应的「开」，否则用户第二天会以为通知坏了。
    // 这条测试就是那个出口的看门人——「恢复常态」被删或被改瘦，它立刻红。
    [Fact]
    public void Every_stateful_switch_turned_off_has_a_way_back()
    {
        var all = ActionGroupTemplates.All();
        var restore = all.Single(g => g.Name == Clockwork.I18n.Strings.Get("Tpl_Restore"));
        var offOn = new (Func<LaunchStep, bool> Off, Func<LaunchStep, bool> On, string What)[]
        {
            (s => s.Kind == "system" && s.Command == "notificationsOff", s => s.Kind == "system" && s.Command == "notificationsOn", "通知"),
            (s => s.Kind == "volume" && s.Action == "micMute", s => s.Kind == "volume" && s.Action == "micUnmute", "麦克风"),
            (s => s.Kind == "volume" && s.Action == "mute", s => s.Kind == "volume" && s.Action == "unmute", "扬声器"),
        };
        foreach (var (off, on, what) in offOn)
            if (all.SelectMany(g => g.Steps).Any(off))
                Assert.True(restore.Steps.Any(on), $"模板里有人关掉了{what}，「恢复常态」却没有把它开回来");
    }

    [Fact]
    public void Each_call_generates_fresh_ids()   // 重复添加同一模板不撞 id（运行态/引用都按 id 做键）
    {
        var a = ActionGroupTemplates.All();
        var b = ActionGroupTemplates.All();
        for (int i = 0; i < a.Count; i++) Assert.NotEqual(a[i].Id, b[i].Id);
        Assert.Equal(a.Count, a.Select(g => g.Id).Distinct().Count());   // 同一批内也不重复
    }

    [Fact]
    public void Steps_kinds_are_valid()   // 模板步骤类型必须是引擎认识的（防手滑写错 kind 静默不执行）
    {
        var known = new HashSet<string>(StepDisplay.StepKinds);
        foreach (var g in ActionGroupTemplates.All())
            foreach (var s in g.Steps)
                Assert.Contains(s.Kind, known);
    }

    // 回归：会议模板绝不能静音。volume/mute 静的是默认输出设备的主音量
    //（AudioController.SetMute → IAudioEndpointVolume.SetMute），开会静音＝听不到会议声音。
    // 这条曾经就是这么写的，别再改回去。
    [Fact]
    public void Meeting_template_sets_volume_instead_of_muting()
    {
        var meeting = ActionGroupTemplates.All()
            .Single(g => g.Name == Clockwork.I18n.Strings.Get("Tpl_Meeting"));

        Assert.DoesNotContain(meeting.Steps, s => s.Kind == "volume" && s.Action == "mute");
        Assert.Contains(meeting.Steps, s => s.Kind == "volume" && s.Action == "set");
    }

    // 久坐模板是唯一演示「整组循环」的模板：没有它，Repeat/RepeatDelayMs 这对能力在模板里零曝光。
    [Fact]
    public void Sedentary_template_loops_the_whole_group()
    {
        var g = ActionGroupTemplates.All()
            .Single(x => x.Name == Clockwork.I18n.Strings.Get("Tpl_Sedentary"));

        Assert.True(g.Repeat > 1, "整组重复轮数应大于 1");
        Assert.True(g.RepeatDelayMs > 0, "每轮间隔应大于 0");
    }
}
