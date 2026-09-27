using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 「引用启动清单项」（kind="step"）的守卫。裁决本体在 StepRefResolver（纯函数，下面直接测）；
// 执行分流/统计聚合/删除联动跑不了单测（碰 UI 线程与钩子），按本仓纪律钉源码形状——
// 它们断了都没有症状：格子点了没反应、权重记错地方、删条目留悬空格子，全是要人肉撞破的。
public class StepRefTests
{
    private static LaunchStep Item(string id, string label, bool enabled = true)
        => new() { Id = id, Kind = "app", Label = label, Target = @"C:\x.exe", Enabled = enabled };

    [Fact]
    public void Resolve_finds_by_id_and_shrugs_at_junk()
    {
        var items = new List<LaunchStep> { Item("a", "甲"), Item("b", "乙") };
        Assert.Equal("甲", StepRefResolver.Resolve(items, "a")!.Label);
        Assert.Null(StepRefResolver.Resolve(items, "missing"));
        Assert.Null(StepRefResolver.Resolve(items, ""));
        Assert.Null(StepRefResolver.Resolve(null, "a"));
    }

    // 三种结局各钉一次（正一反都得上，缺分类=措辞与 severity 会被各写一份）。
    [Fact]
    public void ResolveForRun_classifies_missing_and_disabled()
    {
        var items = new List<LaunchStep> { Item("on", "开着的", true), Item("off", "关掉的", false) };
        var ok = StepRefResolver.ResolveForRun(items, "on");
        Assert.NotNull(ok.Step); Assert.Null(ok.Skip);

        var gone = StepRefResolver.ResolveForRun(items, "nope");
        Assert.Null(gone.Step);
        Assert.Equal("Skip_RefNotFound", gone.Skip!.Key);
        Assert.False(gone.Skip.Benign);          // 目标丢了 = 坏配置，走 Warn

        var dis = StepRefResolver.ResolveForRun(items, "off");
        Assert.Null(dis.Step);
        Assert.Equal("Skip_RefDisabled", dis.Skip!.Key);
        Assert.True(dis.Skip.Benign);            // 被禁用 = 正常状态，走 Info
        Assert.Equal("关掉的", dis.Skip.Arg);    // 参数带名字，不是 id
    }

    [Fact]
    public void Panel_ref_counting_and_removal_agree_on_the_predicate()
    {
        var pages = new List<PanelPage>
        {
            new() { Name = "p1", Steps = new() { Item("a", "甲"), Item("t", "引用", true) } },
            new() { Name = "p2", Steps = new() { Item("t", "又引用", true) } },
        };
        pages[0].Steps[1].Kind = "step"; pages[0].Steps[1].RefId = "a";
        pages[1].Steps[0].Kind = "step"; pages[1].Steps[0].RefId = "a";
        Assert.Equal(2, StepRefResolver.CountPanelRefs(pages, "a"));
        Assert.Equal(2, StepRefResolver.RemovePanelRefs(pages, "a"));
        Assert.Equal(0, StepRefResolver.CountPanelRefs(pages, "a"));
        Assert.Equal(1, pages[0].Steps.Count);   // 被引用者本人的正常步骤一步不少
        Assert.Equal(0, pages[1].Steps.Count);
        // 清自己不该误伤：引用别的 id 的一格都不能动
        var other = new List<PanelPage> { new() { Name = "p", Steps = new() { new LaunchStep { Kind = "step", RefId = "z" } } } };
        Assert.Equal(0, StepRefResolver.RemovePanelRefs(other, "a"));
        Assert.Single(other[0].Steps);
    }
}

// 接线守卫（见文件头）。锚文本各挑「只此一处、改了必然换形状」的最小片段。
public class StepRefWiringTests
{
    private static string Read(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void RunStep_dispatches_refs_before_the_group_branch()
    {
        var src = Read("Services", "PanelCoordinator.cs");
        int r = src.IndexOf("public void RunStep(LaunchStep step");
        Assert.True(r >= 0, "找不到 RunStep：这段守卫要跟着它走");
        int refAt = src.IndexOf("step.Kind == StepDisplay.RefKind", r);
        int groupAt = src.IndexOf("step.Kind == \"group\"", r);
        Assert.True(refAt >= 0 && groupAt >= 0, "RunStep 里引用分流不见了");
        Assert.True(refAt < groupAt, "引用分流与组分流是互斥的 if——顺序本身无对错，但两份必须都在");
    }

    [Fact]
    public void Panel_click_stats_aggregate_to_the_referenced_item()
    {
        var src = Read("Services", "PanelCoordinator.cs");
        Assert.Contains("_usage.RecordClick(usage.Id)", src);
        Assert.Contains("StepRefResolver.Resolve(_config.LaunchSteps, step.RefId) is { } tgt", src);
    }

    [Fact]
    public void Deleting_a_list_item_clears_the_tiles_referencing_it()
    {
        var src = Read("MainWindow.xaml.cs");
        Assert.Contains("StepRefResolver.CountPanelRefs(_config?.PanelPages, sel.Id)", src);
        Assert.Contains("StepRefResolver.RemovePanelRefs(_config?.PanelPages, sel.Id)", src);
        // 承诺与兑现必须在同一次点击里：确认文案的键也要在场
        Assert.Contains("Confirm_DeleteStepRefs", src);
    }

    [Fact]
    public void Editor_exposes_the_ref_type_only_in_panel_context()
    {
        var src = Read("Views", "StepEditorWindow.xaml.cs");
        Assert.Contains("listItems != null ? StepDisplay.StepKinds.Append(StepDisplay.RefKind)", src);
        Assert.Contains("case \"step\": r.RefId = ComboVal(RefCombo)", src);
        var xaml = Read("Views", "StepEditorWindow.xaml");
        Assert.Contains("x:Name=\"PanRef\"", xaml);
    }

    [Fact]
    public void Tiles_inherit_the_referenced_item_icon()
    {
        var src = Read("Core", "PanelLayout.cs");
        Assert.Contains("s.Kind == StepDisplay.RefKind", src);
        Assert.Contains("StepRefResolver.Resolve(listItems, s.RefId)?.Icon", src);
    }
}
