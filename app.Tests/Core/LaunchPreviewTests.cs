using System;
using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// 手动重跑之前摊给用户看的那份清单（LaunchPreview）。
//
// 它是**给人看的决策依据**，不是给人看的报表：用户按的是「重跑」，心里想的是
// 「这一次会做哪几件事」。所以钉住的是三件事——列出来的必须是**这次真会跑的那些**
// （不是全部配置、更不是展开后的子步骤）、摊开要有上限（不然框要滚）、余下的要报数
// （不然用户会以为清单比实际短）。
public class LaunchPreviewTests
{
    private static RootConfig Cfg(params LaunchStep[] steps)
    {
        var c = new RootConfig();
        c.LaunchSteps = new List<LaunchStep>(steps);
        return c;
    }

    // 列出来的就是这次会跑的：没勾的不列。
    [Fact]
    public void The_preview_lists_the_steps_that_will_run()
    {
        var cfg = Cfg(
            new LaunchStep { Kind = "app", Enabled = true, Label = "会跑的那条" },
            new LaunchStep { Kind = "app", Enabled = false, Label = "没勾的那条" });

        var (total, lines, more) = LaunchPreview.Build(cfg, new DateTime(2026, 10, 30, 9, 0, 0));

        Assert.Equal(1, total);
        Assert.Single(lines);
        Assert.Equal(0, more);
        Assert.Contains("会跑的那条", lines[0]);
        Assert.DoesNotContain("没勾的那条", lines[0]);
    }

    // 时间条件与真正运行时同一套：过了点的步骤不进清单（否则框里说的和跑的不一样）。
    [Fact]
    public void The_preview_follows_the_same_time_conditions_as_the_run()
    {
        var cfg = Cfg(new LaunchStep { Kind = "volume", Enabled = true, OnlyBefore8 = true, Label = "只在八点前" });

        Assert.Equal(0, LaunchPreview.Build(cfg, new DateTime(2026, 10, 30, 9, 0, 0)).total);
        Assert.Equal(1, LaunchPreview.Build(cfg, new DateTime(2026, 10, 30, 7, 0, 0)).total);
    }

    // 摊开有上限：清单再长也不能把确认框撑到要滚动（要滚动的确认框多半被直接点掉）。
    // 余下的条数要报出来——用户看到「还有 4 条」才知道清单没被截短。
    [Fact]
    public void The_preview_caps_the_lines_and_reports_the_rest()
    {
        var steps = new List<LaunchStep>();
        for (int i = 0; i < 10; i++)
            steps.Add(new LaunchStep { Kind = "app", Enabled = true, Label = $"第{i + 1}条" });

        var (total, lines, more) = LaunchPreview.Build(Cfg(steps.ToArray()), new DateTime(2026, 10, 30, 9, 0, 0));

        Assert.Equal(10, total);
        Assert.Equal(6, lines.Count);
        Assert.Equal(4, more);
    }

    // 空清单：没有会跑的条目，就没什么可确认的（调用方据此直接放行，不拿空框烦人）。
    [Fact]
    public void An_empty_list_previews_nothing()
    {
        var (total, lines, more) = LaunchPreview.Build(Cfg(), new DateTime(2026, 10, 30, 9, 0, 0));

        Assert.Equal(0, total);
        Assert.Empty(lines);
        Assert.Equal(0, more);
    }
}
