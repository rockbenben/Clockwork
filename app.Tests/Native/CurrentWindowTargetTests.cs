using System;
using Clockwork.Native;
using Xunit;

// 「当前窗口」目标的选取。手势/面板触发后动作在后台线程跑，读到前台之前可能隔了等待、延时、
// 动作组里抢前台的前一步——这期间前台会变。必须钉住「触发时刻记下的窗口还有效就用它」，
// 否则手势会作用到中途抢入的别的窗口上，外面看就是「手势丢了焦点、没生效」。
public class CurrentWindowTargetTests
{
    private static readonly IntPtr GestureTarget = new(100);
    private static readonly IntPtr Interloper = new(200);   // 中途抢入前台的别的窗口

    [Fact]
    public void Uses_trigger_time_foreground_even_when_live_foreground_changed()
    {
        // baseline 仍有效 → 哪怕此刻前台已经是另一个窗口，也回触发那一刻的目标。
        var h = WindowManager.ResolveCurrentWindowTarget(
            GestureTarget, _ => true, () => Interloper);
        Assert.Equal(GestureTarget, h);
    }

    [Fact]
    public void Falls_back_to_live_when_trigger_window_gone()
    {
        // baseline 句柄已废（窗口被关掉）→ 退回现读，别对着一个死句柄动手。
        var h = WindowManager.ResolveCurrentWindowTarget(
            GestureTarget, _ => false, () => Interloper);
        Assert.Equal(Interloper, h);
    }

    [Fact]
    public void No_baseline_uses_live()
    {
        // 触发那一刻前台是 Clockwork 自己（编辑器里试跑）→ baseline 为 Zero，靠现读。
        var h = WindowManager.ResolveCurrentWindowTarget(
            IntPtr.Zero, _ => true, () => Interloper);
        Assert.Equal(Interloper, h);
    }

    [Fact]
    public void Neither_present_is_zero()
    {
        var h = WindowManager.ResolveCurrentWindowTarget(
            IntPtr.Zero, _ => false, () => IntPtr.Zero);
        Assert.Equal(IntPtr.Zero, h);
    }
}
