using System;
using Clockwork.Core;
using Xunit;

// 「打开文件或文件夹」步骤：与「打开网址」对称的一档。
// Kind 全集那批测试（分节覆盖、字形唯一、图标回退、18 语覆盖）已经自动罩住结构性事实，
// 这里补的是那几条罩不到的：摘要写全路径、空值不留白、与 app/url 共用 Target、面板自动取目标图标。
public class PathStepTests
{
    [Fact]
    public void It_is_in_the_catalogue_and_the_open_section()
    {
        Assert.Contains("path", StepDisplay.StepKinds);
        Assert.Contains(StepDisplay.StepKindSections,
            sec => sec.SectionKey == "Menu_SecOpen" && sec.Kinds.Contains("path"));
    }

    // 路径原样写进摘要：D:\Work 与 E:\Work 的区别全在前缀上，像 app 那样只取叶子名就分不开了。
    [Fact]
    public void A_path_step_shows_the_full_path()
    {
        var s = new LaunchStep { Kind = "path", Target = @"D:\Work\report.docx" };
        var sum = StepDisplay.StepSummary(s);
        Assert.Contains(@"D:\Work\report.docx", sum);
        Assert.DoesNotContain("Sum_OpenPath", sum);   // 键名不许漏到界面上
    }

    // 空值不许留白（与 NewStepKindsTests 里 url 那条同一条规矩）。
    [Fact]
    public void A_blank_target_never_renders_blank()
    {
        var s = new LaunchStep { Kind = "path" };
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepSummary(s)));
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepTitle(s)));
    }

    // 粘贴来源常带引号（资源管理器「复制文件地址」）：摘要里也按 Target 同一口径规范化掉。
    [Fact]
    public void The_summary_strips_surrounding_quotes()
    {
        var s = new LaunchStep { Kind = "path", Target = @"""D:\Work""" };
        Assert.DoesNotContain('"', StepDisplay.StepSummary(s));
    }

    // **path 与 app/url 共用 Target 字段。** 三个「打开」类型在编辑器里互改类型，已填的目标不丢。
    [Fact]
    public void A_path_lives_in_the_same_field_as_a_program()
    {
        var s = new LaunchStep { Kind = "app", Target = @"D:\Work" };
        s.Kind = "path";                       // 只改类型，不动字段
        Assert.Contains(@"D:\Work", StepDisplay.StepSummary(s));
    }

    // 自动图标取自目标本身（文件/文件夹都算）：一屏彩色真实图标是「打开类」格子的共同质感。
    [Fact]
    public void The_panel_icon_is_taken_from_the_target()
    {
        var dir = Environment.SystemDirectory;   // C:\Windows\System32，任何 Windows 测试机都在
        var spec = PanelIcon.Resolve(null, "path", dir);
        Assert.Equal(PanelIconKind.Image, spec.Kind);
        // 与输入精确比对（不假定大小写——实测有的机器是 C:\WINDOWS\system32）。
        Assert.Equal(dir, spec.Value);
    }
}
