using System.Globalization;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// Strings.InEnglish 是 clockwork.error.log 能整行英文的支点：那份日志是拿去贴 issue 的，
// 而步骤摘要（StepDisplay.StepSummary）本身层层走 resx，会跟着界面语言变。
//
// 这两条盯的是它的两个失败方向，都不会崩、只会静默：
//   · 没换到英文 → 日志里混进用户那门语言，贴到 issue 里没人认得出那是哪一步；
//   · 换了没还原 → 整个界面从那一刻起变成英文（CurrentUICulture 是线程局部的，
//     而写日志这条路可能跑在 UI 线程上）。
public class InEnglishTests
{
    private static readonly LaunchStep Volume = new() { Kind = "volume", Action = "set", Level = 30 };

    private static T UnderZh<T>(Func<T> f)
    {
        var save = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        try { return f(); }
        finally { CultureInfo.CurrentUICulture = save; }
    }

    [Fact]
    public void Step_summary_renders_in_english_while_the_ui_is_chinese()
    {
        var (zh, en) = UnderZh(() => (StepDisplay.StepSummary(Volume),
                                      Strings.InEnglish(() => StepDisplay.StepSummary(Volume))));
        Assert.Equal("设音量 30%", zh);       // 界面那一份不受影响
        Assert.DoesNotContain("音量", en);     // 日志那一份不该带中文
        Assert.Contains("30", en);             // 参数照样填进去了
        Assert.NotEqual(zh, en);
    }

    [Fact]
    public void The_ui_culture_is_restored_afterwards()
    {
        var after = UnderZh(() =>
        {
            Strings.InEnglish(() => StepDisplay.StepSummary(Volume));
            return CultureInfo.CurrentUICulture.Name;
        });
        Assert.Equal("zh-CN", after);
    }

    // ActionResult 存键而不是句子，所以同一个警告能出两种语言。
    [Fact]
    public void Action_result_warning_renders_in_both_languages()
    {
        var r = ActionResult.Warn("Warn_LaunchFail", "notepad.exe", "boom");
        var (zh, en) = UnderZh(() => (r.Warning, r.WarningEn));
        Assert.StartsWith("启动失败", zh);
        Assert.StartsWith("Launch failed", en);
        Assert.Contains("notepad.exe", en);      // 参数照样填进去
    }

    // **Strings.Ref 的整个存在理由。** 实参里套着另一条文案时，用 Strings.Get 会在构造 ActionResult
    // 的那一刻就把内层语言定死，英文那一份就成了「英文的框 + 中文的芯」——那正是日志里最难读的一种。
    [Fact]
    public void A_nested_message_argument_follows_the_outer_language()
    {
        var r = ActionResult.Warn("Warn_LaunchFail", "a.ps1", new Strings.Ref("Err_ScriptMissing"));
        var (zh, en) = UnderZh(() => (r.Warning, r.WarningEn));
        Assert.Contains("找不到脚本文件", zh);
        Assert.Contains("Script file not found", en);
        Assert.DoesNotContain("找不到", en);      // 芯也得跟着换过去
    }

    [Fact]
    public void No_warning_means_null_in_both_renderings()
    {
        Assert.Null(ActionResult.Empty.Warning);
        Assert.Null(ActionResult.Empty.WarningEn);
        Assert.Null(ActionResult.Unver().Warning);
    }

    // 渲染函数抛出时也必须还原——finally 少写一次，界面就会卡在英文上。
    [Fact]
    public void The_ui_culture_is_restored_even_when_the_render_throws()
    {
        var after = UnderZh(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => Strings.InEnglish<string>(() => throw new InvalidOperationException()));
            return CultureInfo.CurrentUICulture.Name;
        });
        Assert.Equal("zh-CN", after);
    }
}
