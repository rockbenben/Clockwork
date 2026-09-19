using System.IO;
using System.Linq;
using Xunit;

// 面板点击统计的接线是一条「断了也不报错」的链：有人把格子的点击回调改回直接 RunStep，
// 面板照常工作，只是计数永远不动；管理器里少传一个参数，界面照常打开，只是永远不显示角标。
// 这类东西没法靠运行时断言，只能钉源码（与 GestureReceiptTests 同一种守卫）。
public class PanelClickHookTests
{
    private static string Source(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // 每个格子的点击必须经过唯一漏斗 RecordPanelClick：鼠标点、打字回车、方向键回车都汇到这里，
    // 计数与 RunStep 在同一个方法里发生，没法数了数却没跑（或跑了没数）。
    [Fact]
    public void Every_panel_tile_click_goes_through_the_record_funnel()
    {
        var app = Source("App.xaml.cs");
        Assert.Contains("() => RecordPanelClick(it.Step)", app);
        // 旧写法不许复活：直接把 RunStep 塞给格子，计数就被整个绕过。
        Assert.DoesNotContain("() => RunStep(it.Step)", app);
    }

    // 管理器必须拿到那份活的统计字典和清空动作；参数可选（DevChecks 那几个调用点不传），
    // 所以漏传编译照样过，表现只是角标和按钮永远不出现。
    [Fact]
    public void The_panel_manager_is_opened_with_the_usage_map()
        => Assert.Contains("_panelUsage, ResetPanelUsage", Source("App.xaml.cs"));

    // 步骤编辑器保存时是**重建**对象而不是改原对象（ConditionProbe()）。身份那一行一旦漏掉，
    // 这个格子改一次参数就变成「从未点过」：计数归零、不报错。
    [Fact]
    public void The_step_editor_carries_the_id_across_its_rebuild()
        => Assert.Contains("r.Id = _original.Id;", Source("Views", "StepEditorWindow.xaml.cs"));

    // 启动时剪掉的墓碑键必须真的落盘。PruneUnreferenced 只改内存字典，SaveUsage 头上有脏标记闸，
    // 「if (... > 0) SaveUsage()」那种不接返回值的写法会让剪枝空转：墓碑键每次启动读回来再忘，文件只增不减。
    [Fact]
    public void Startup_prune_is_captured_and_persisted()
        => Assert.Contains("var pruned = Core.PanelUsageStore.PruneUnreferenced(_panelUsage, liveIds)", Source("App.xaml.cs"));
}
