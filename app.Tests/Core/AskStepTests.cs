using System.Linq;
using Clockwork.Core;
using Xunit;

// 两个问句步骤：用户输入 / 用户选择。它们是这一轮变量系统的**产出端**——
// 没有它们，变量表永远是空的；没有变量表，它们问来的答案无处可去。
public class AskStepTests
{
    [Theory]
    [InlineData("prompt")]
    [InlineData("choice")]
    public void The_ask_kinds_are_in_the_catalogue(string kind)
    {
        Assert.Contains(kind, StepDisplay.StepKinds);
        // 分节必须收下它，否则「新增 ▾」菜单里根本没有入口
        Assert.Contains(StepDisplay.StepKindSections, sec => sec.Kinds.Contains(kind));
        // 类型名要有译文，不能显示成 "Kind_prompt"
        Assert.NotEqual("Kind_" + kind, StepDisplay.StepKindLabel(kind));
    }

    // 问句本身就是这一步的身份：三条「用户输入」摆在清单上，不写出问的是什么就完全分不开。
    [Theory]
    [InlineData("prompt")]
    [InlineData("choice")]
    public void The_summary_leads_with_the_question(string kind)
        => Assert.Contains("搜什么", StepDisplay.StepSummary(new LaunchStep { Kind = kind, Message = "搜什么？" }));

    // 产出去了哪儿是读这条动作时第二要紧的事——没有它，一串动作里根本看不出数据怎么流。
    [Fact]
    public void The_summary_names_the_output_variable()
    {
        var s = StepDisplay.StepSummary(new LaunchStep { Kind = "prompt", Message = "搜什么？", OutputVar = "关键词" });
        Assert.Contains("关键词", s);
    }

    // 没配输出变量就不缀那一截：拼出来是「问：搜什么？ → 」，一个指向空处的箭头。
    [Fact]
    public void No_output_variable_means_no_arrow()
        => Assert.DoesNotContain("→", StepDisplay.StepSummary(
            new LaunchStep { Kind = "prompt", Message = "搜什么？" }));

    // 什么都没配也不许留白（与 BlankRowTests 同一条规矩，这里点名新类型）。
    [Theory]
    [InlineData("prompt")]
    [InlineData("choice")]
    public void An_unconfigured_ask_step_still_says_something(string kind)
        => Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepSummary(new LaunchStep { Kind = kind })));

    // ── 选项：一行一个 ──

    // 一行一个而不是逗号分隔：选项里出现逗号是常态（「张三, 李四」是一个选项还是两个？），
    // 换行永远没有歧义。
    [Fact]
    public void Options_are_one_per_line()
        => Assert.Equal(new[] { "上班", "下班", "午休" }, StepHelpers.ChoiceOptions("上班\n下班\n午休"));

    [Fact]
    public void Blank_lines_and_padding_are_dropped()
        => Assert.Equal(new[] { "甲", "乙" }, StepHelpers.ChoiceOptions("  甲  \n\n\r\n 乙 \n   \n"));

    // 两个一模一样的选项在列表里点哪个都一样，读起来像是坏了。
    [Fact]
    public void Duplicate_options_collapse()
        => Assert.Equal(new[] { "甲", "乙" }, StepHelpers.ChoiceOptions("甲\n乙\n甲"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void No_options_is_an_empty_list_not_a_crash(string? text)
        => Assert.Empty(StepHelpers.ChoiceOptions(text));

    // 选项里的换行符是 Windows 风格时也得切得开（多行框存进 json 的就是 \r\n）。
    [Fact]
    public void Windows_newlines_split_too()
        => Assert.Equal(new[] { "甲", "乙" }, StepHelpers.ChoiceOptions("甲\r\n乙"));
}
