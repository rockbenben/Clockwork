using System;
using System.Collections.Generic;
using Clockwork.Native;
using Xunit;

// 「当前窗口」目标的选取。两件事都钉在这里：
//   1) 手势起笔窗口（划在谁身上）最优先——右键按下被钩子吞掉、系统不激活光标下的窗，
//      在后台窗口上起笔时前台是另一个窗，窗口动作必须打起笔窗口而不是前台；
//   2) 没有起笔窗口（非手势触发）或它已失效时，退回运行开始那一刻记下的前台 baseline，
//      而不是执行线程上现读 GetForegroundWindow——触发后到执行前隔了等待/延时/抢前台的前一步，
//      现读会读到中途抢入的别的窗口。
public class CurrentWindowTargetTests
{
    private static readonly IntPtr Gestured = new(100);   // 手势起笔窗口（可能是后台窗口）
    private static readonly IntPtr Baseline = new(150);   // 运行开始时的前台
    private static readonly IntPtr Interloper = new(200); // 中途抢入前台的别的窗口

    // 探针：给定一组「仍有效」的句柄，返回 stillVisible。
    private static Func<IntPtr, bool> VisibleOf(params IntPtr[] alive)
    {
        var set = new HashSet<IntPtr>(alive);
        return h => set.Contains(h);
    }

    [Fact]
    public void Gesture_origin_wins_even_when_foreground_is_elsewhere()
    {
        // 在后台窗口上起笔：起笔窗口有效，前台（baseline / 现读）都是别的窗——仍回起笔窗口。
        var h = WindowManager.ResolveCurrentWindowTarget(
            Gestured, Baseline, VisibleOf(Gestured, Baseline), () => Interloper);
        Assert.Equal(Gestured, h);
    }

    [Fact]
    public void Closed_gesture_origin_falls_back_to_baseline()
    {
        // 起笔窗口已被关掉（句柄失效）→ 退回触发时刻的前台 baseline。
        var h = WindowManager.ResolveCurrentWindowTarget(
            Gestured, Baseline, VisibleOf(Baseline), () => Interloper);
        Assert.Equal(Baseline, h);
    }

    [Fact]
    public void No_origin_uses_baseline_even_when_live_foreground_changed()
    {
        // 非手势触发（origin=Zero）：baseline 仍有效就用它，哪怕此刻前台已是另一个窗。
        var h = WindowManager.ResolveCurrentWindowTarget(
            IntPtr.Zero, Baseline, VisibleOf(Baseline), () => Interloper);
        Assert.Equal(Baseline, h);
    }

    [Fact]
    public void Origin_and_baseline_gone_falls_back_to_live()
    {
        // 两个记下的句柄都失效 → 退回现读，别对着死句柄动手。
        var h = WindowManager.ResolveCurrentWindowTarget(
            Gestured, Baseline, VisibleOf(), () => Interloper);
        Assert.Equal(Interloper, h);
    }

    [Fact]
    public void Nothing_recorded_uses_live()
    {
        // 触发那一刻前台是 Clockwork 自己（编辑器试跑）→ 两个句柄都 Zero，靠现读。
        var h = WindowManager.ResolveCurrentWindowTarget(
            IntPtr.Zero, IntPtr.Zero, VisibleOf(), () => Interloper);
        Assert.Equal(Interloper, h);
    }

    [Fact]
    public void Nothing_at_all_is_zero()
    {
        var h = WindowManager.ResolveCurrentWindowTarget(
            IntPtr.Zero, IntPtr.Zero, VisibleOf(), () => IntPtr.Zero);
        Assert.Equal(IntPtr.Zero, h);
    }
}
