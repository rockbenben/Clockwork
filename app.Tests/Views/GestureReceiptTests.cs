using System.IO;
using System.Linq;
using Xunit;

// 手势跑完**成功时不弹回执**，失败照弹。
//
// 这不是个能用断言直接跑出来的行为（RunStepAsync 在 App 里、要 WPF 消息泵），
// 但它是个特别容易被顺手改回去的决定：那张卡片对「点了运行按钮」是唯一的结果，
// 而对手势是每天几十次的噪声——十条默认手势里八条效果肉眼直接可见。
// 一刀切成任意一边都会坏掉另一头，所以两个方向都钉住。
//
// 失败那一档尤其不能丢：手势失败是彻底无声的（窗口被未保存对话框挡着关不掉、
// 搜索时其实什么都没选中），没有那张卡就只剩「我画了，没反应」。
public class GestureReceiptTests
{
    private static string AppSource(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // 手势那条路必须走 Gesture 这一档（成功安静 + 不防连点，两件事都挂在它上面）。
    // 不锚到行尾的右括号：这一行还顺带传入手势起笔窗口（gestureOrigin:），多一个命名参数
    // 不该让这条「走没走 Gesture 档」的守卫变红。
    [Fact]
    public void A_gesture_runs_as_a_gesture()
        => Assert.Contains("RunStep(step, by: StepTrigger.Gesture", AppSource("App.xaml.cs"));

    // **防连点闸只对按钮。** 它拦下时是直接 return、什么都不说，而手势要花一秒认真画出来——
    // 画两次就是故意要跑两次（↖ 切换置顶：钉住、再放开）。给手势设闸的表现是
    // 「连着画两次，第二次无声消失」，正是这个功能里最难自查的那种。
    [Fact]
    public void Only_buttons_get_the_double_click_guard()
    {
        var app = AppSource("App.xaml.cs");
        Assert.Contains("bool guard = by == StepTrigger.Button;", app);
        Assert.Contains("if (guard && Interlocked.Exchange(ref _stepRunning, 1) == 1) return;", app);
    }

    // 而回执本身不能连带被删掉：安静的只有成功那一档，失败必须还会弹。
    [Fact]
    public void A_failed_step_still_reports()
        => Assert.Contains("if (!quiet || mark.Fail > 0)", AppSource("App.xaml.cs"));

    // 反面：编辑器里那两颗「运行」按钮不许跟着变安静——那张卡是它们唯一的结果。
    // 路径分段传，不写成一个带分隔符的串：那样得在源码里塞反斜杠，平台一换就错。
    [Fact]
    public void The_main_window_run_button_keeps_its_receipt()
        => Assert.Contains("RunStepAsync(s, this)", AppSource("MainWindow.xaml.cs"));

    [Fact]
    public void The_group_editor_run_button_keeps_its_receipt()
        => Assert.Contains("RunStepAsync(s, this)", AppSource("Views", "GroupEditorWindow.xaml.cs"));
}
