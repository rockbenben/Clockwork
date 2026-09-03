using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// **列表里绝不出现一整行空白。**
//
// 步骤编辑器只校验两件窄事（发送键语法、自定义搜索地址），目标 / 组合键 / 进程名 / 消息
// 全都可以留空就保存——「新增 → 运行程序 → 直接确定」当场造出一行。而空白行看起来是
// 程序坏了，不是「这一步还没配」：既说不清它是什么，也说不清该去补什么。
//
// 这条测试把所有类型的「关键字段为空」都过一遍。加新 kind 时它会跟着红，
// 那正是提醒你在 StepBaseCore 里补上那一档的时候。
public class BlankRowTests
{
    public static TheoryData<string, LaunchStep> Empties() => new()
    {
        { "app 空目标",     new LaunchStep { Kind = "app", Target = "", Label = "" } },
        { "keys 空组合",    new LaunchStep { Kind = "keys", Combo = "" } },
        { "mouse 空动作",   new LaunchStep { Kind = "mouse", Action = "" } },
        { "volume 空动作",  new LaunchStep { Kind = "volume", Action = "" } },
        { "window 空进程",  new LaunchStep { Kind = "window", Action = "close", Process = "" } },
        { "window 空动作",  new LaunchStep { Kind = "window", Action = "", Process = "notepad" } },
        { "system 空命令",  new LaunchStep { Kind = "system", Command = "" } },
        { "group 空 id",    new LaunchStep { Kind = "group", GroupId = "", Label = "" } },
        { "message 空文本", new LaunchStep { Kind = "message", Message = "" } },
        { "text 空文本",    new LaunchStep { Kind = "text", Text = "" } },
        { "未知类型",        new LaunchStep { Kind = "" } },
        { "全空",           new LaunchStep() },
    };

    [Theory]
    [MemberData(nameof(Empties))]
    public void No_step_ever_renders_an_empty_row(string name, LaunchStep s)
    {
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepSummary(s)), name + "：列表摘要是空白");
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepTitle(s)), name + "：格子标题是空白");
    }

    // 动词后面必须有宾语——「关闭窗口 」这种半句话和空白一样读不出意思。
    [Theory]
    [MemberData(nameof(Empties))]
    public void No_summary_ends_with_a_dangling_verb(string name, LaunchStep s)
    {
        var sum = StepDisplay.StepSummary(s);
        Assert.False(sum.TrimEnd() != sum, name + "：摘要以空白结尾，说明宾语没填上");
    }

    // 每一条都得原样能跑：配全了的步骤不该被占位污染。
    [Fact]
    public void A_configured_step_is_untouched()
    {
        var s = new LaunchStep { Kind = "app", Target = @"C:\x\repo-radar.lnk" };
        Assert.Equal("repo-radar", StepDisplay.StepSummary(s));
    }

    // 提醒同理：没正文、也没绑静默组的一条，从前在列表里是一整行空白。
    [Fact]
    public void An_empty_reminder_still_says_something()
        => Assert.False(string.IsNullOrWhiteSpace(ReminderDisplay.TextSummary(new Reminder { Message = "" })));

    // **删除确认里点名的那句话，和列表里那一行是同一句。**
    //
    // 各处删除都走 BrandDialog.ConfirmDelete(label)，而那句文案只有一个「名字」的位置。
    // 一旦有人把**原始字段**塞进去（而不是列表用的那个摘要），空值就会漏出来变成
    // 「确定删除「」吗？」——点名点了个寂寞。实际发生过：提醒那一条传的是 Message 原文，
    // 而「静默运行动作」的提醒本来就没有正文。
    [Fact]
    public void A_silent_reminder_still_has_something_to_name_when_deleting()
    {
        // 没有正文、只绑了一个动作的提醒——列表与确认框都靠这一句认它
        var r = new Reminder { Message = "", SilentGroupId = "g1" };
        var groups = new List<ActionGroup> { new() { Id = "g1", Name = "专注" } };
        var label = ReminderDisplay.TextSummary(r, groups);
        Assert.False(string.IsNullOrWhiteSpace(label), "删除确认会变成「确定删除「」吗？」");
        Assert.Contains("专注", label);   // 而且要认得出是哪一条：点名得点到东西
    }

    // 面板页签也不许出现一块没写字的标签。
    //
    // 从界面走不到这一步——面板里的就地改名清空会回落到「新建页」。
    // 这条挡的是**手改 json 与导入的配置**：那两条绕过全部编辑器，而 PanelPageView 的约定
    // （「Title 显示在页签上，必须是人话」）此前没有任何东西在守。
    [Fact]
    public void A_panel_page_never_gets_a_blank_tab()
    {
        var cfg = new List<PanelPage>
        {
            new() { Id = "blank", Name = "",
                    Steps = new List<LaunchStep> { new() { Kind = "keys", Combo = "Ctrl+C" } } },
        };
        var pages = PanelLayout.BuildPages(cfg, null, 0);
        Assert.NotEmpty(pages);
        Assert.All(pages, p => Assert.False(string.IsNullOrWhiteSpace(p.Title), "页签标题是空白"));
    }
}
