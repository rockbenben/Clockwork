using System.IO;
using System.Linq;
using Xunit;

// 笔迹旁那句「这将是哪件事」的四处纪律。
//
// 这一格是手势的**分应用绑定**逼出来的：线变亮只说明认出了**形状**，不说明认出了**哪一个动作**。
// 而同一个 ↑ 在浏览器里是「搜索」、在编辑器里是「复制」，于是「亮了」本身是有歧义的——
// 一个不确定的反馈比没有反馈更糟，它让人以为已经知道会发生什么，于是不再确认。
//
// 这四条每一条破了都不会报错，只会让那句话悄悄不对：
//   1. **收笔即收**——它描述的是「正在画的那一笔」，那一笔结束了它就没有对象了；
//      留在屏上就成了「上一笔的旧话」，而那正是分应用绑定最会骗人的地方。
//   2. **开关要真的管住它**——有人要那种「只有一条线」的手感，这一格是给他留的出口。
//   3. **文案在 UI 线程上算**——查 i18n 资源首次要加载卫星程序集，那几毫秒落在钩子回调里
//      是 LowLevelHooksTimeout 预算上最不该花的那笔。
//   4. **所以钩子捎的是「那一步」而不是那句话**——见第三条。
public class TrailActionLabelTests
{
    private static string Source(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    private static string TrailSource() => Source("Views", "GestureTrailWindow.cs");

    // 取一个方法的方法体：从签名到它那一层缩进的收尾大括号（与 TrailStaleBitmapTests 同口径）。
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature);
        Assert.True(start >= 0, $"源码里找不到 {signature}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", start);
        Assert.True(end > start, $"{signature} 的方法体没找到收尾括号");
        return src.Substring(start, end - start);
    }

    // 收笔即收。放在 Finish 里（与 SetArmed(false) 同一处）：那一笔结束了，这句话就没有对象了。
    [Fact]
    public void The_label_is_taken_down_when_the_stroke_ends()
    {
        Assert.Contains("HideHint()", Body(TrailSource(), "public void Finish()"));
    }

    // 关掉之后这一格要真的什么都不显示：只把开关记下来、却忘了把已经摆出来的那句收掉，
    // 用户看到的仍是那句话——而设置页上写着「关」。
    [Fact]
    public void Turning_it_off_also_takes_down_the_label()
    {
        var body = Body(TrailSource(), "public void ShowActionLabel(bool on)");
        Assert.Contains("if (!on) HideHint();", body);
    }

    // 摆出来之前先问那一句有没有内容：没认出来的时候没有「哪件事」，也就没有那句话。
    [Fact]
    public void The_label_is_only_shown_when_there_is_something_to_say()
    {
        var body = Body(TrailSource(), "private void UpdateHint(");
        Assert.Contains("string.IsNullOrEmpty(label)", body);
        Assert.Contains("HideHint()", body);
    }

    // **文案在 UI 线程上算。** 那句是 i18n 的（StepDisplay.StepTitle 查资源），
    // 而查资源第一次要加载卫星程序集——几毫秒。落在钩子回调里就是那几毫秒，
    // 而那条路有 LowLevelHooksTimeout 的 300ms 预算。
    [Fact]
    public void The_text_is_computed_in_the_trail_window_not_in_the_hook()
    {
        Assert.Contains("Core.StepDisplay.StepTitle(hit)", TrailSource());
        // 钩子那边不许出现显示层的东西。
        Assert.DoesNotContain("StepDisplay", Source("Native", "MouseHook.cs"));
    }

    // 钩子捎的是**那一步**，不是那句话——上一条的下半句。
    [Fact]
    public void The_hook_carries_the_step_not_the_sentence()
    {
        var hook = Source("Native", "MouseHook.cs");
        Assert.Contains("_gesture.LiveStep", hook);
    }
}
