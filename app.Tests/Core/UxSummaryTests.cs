using Clockwork.Core;
using Xunit;

// UX 一轮加的三样纯逻辑：菜单意图分节、摘要修饰段（折叠条标题用）、催促/循环行为句。
public class UxSummaryTests
{
    // 分节必须**至少**覆盖 StepKinds：漏一种 = 那种步骤没了入口。
    //
    // **不再要求「恰好一次」。** 一个类型可以出现在多节里——那是照着 Quicker 官方模块分类来的
    // （原话：「个别模块会属于多个分类」），而「用户输入」这种既是常用又属界面交互、
    // 「获取选中的文字」既在常用又属剪贴板，落在两节里都是对的。
    // 工具箱的「全部」视图按主节去重（见 GroupEditorWindow.RebuildToolbox），
    // 所以多归属不会让同一张列表里出现两条一样的项。
    [Fact]
    public void Menu_sections_cover_every_step_kind_at_least_once()
    {
        var flat = StepDisplay.StepKindSections.SelectMany(s => s.Kinds).ToList();
        foreach (var k in StepDisplay.StepKinds) Assert.Contains(k, flat);
        // 分节表里不该出现目录里没有的类型（那种键拼错了，跑起来会静默不执行）。
        foreach (var k in flat) Assert.Contains(k, StepDisplay.StepKinds);
    }

    [Fact]
    public void Decoration_is_empty_for_a_plain_step()
        => Assert.Equal("", StepDisplay.DecorationSummary(new LaunchStep()));

    // 折叠条标题与列表摘要必须是同一份文案：StepSummary = 主体 + 修饰段，两处永不各说各话。
    [Fact]
    public void Summary_equals_base_plus_decoration()
    {
        var s = new LaunchStep { Kind = "volume", Action = "mute", Days = new() { 1, 2, 3 }, Repeat = 3, IfPower = "battery" };
        Assert.Equal("静音" + StepDisplay.DecorationSummary(s), StepDisplay.StepSummary(s));
        Assert.Equal(" ×3（周一–周三）（用电池）", StepDisplay.DecorationSummary(s));
    }

    // —— 催促 / 循环的行为句 ——
    [Fact]
    public void Advanced_summary_default_is_fire_once()
        => Assert.Equal("到点触发一次", ReminderDisplay.AdvancedSummary(0, "", 0, ""));

    [Fact]
    public void Advanced_summary_nag()
    {
        Assert.Equal("没确认就每 5 分钟再提醒", ReminderDisplay.AdvancedSummary(5, "", 0, ""));
        Assert.Equal("没确认就每 5 分钟再提醒，最多到 11:00", ReminderDisplay.AdvancedSummary(5, "11:00", 0, ""));
    }

    [Fact]
    public void Advanced_summary_loop()
    {
        Assert.Equal("每 30 分钟跑一轮直到当天结束（确认也不停）", ReminderDisplay.AdvancedSummary(0, "", 30, ""));
        Assert.Equal("每 30 分钟跑一轮直到 18:00（确认也不停）", ReminderDisplay.AdvancedSummary(0, "", 30, "18:00"));
    }

    // 两条链可以同时挂着（弹窗模式下各有各的截止），句子也得两段都说。
    [Fact]
    public void Advanced_summary_nag_and_loop_join()
        => Assert.Equal("没确认就每 5 分钟再提醒 · 每 30 分钟跑一轮直到 18:00（确认也不停）",
            ReminderDisplay.AdvancedSummary(5, "", 30, "18:00"));
}
