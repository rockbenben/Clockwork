using System.Reflection;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 到点唤出不能只靠「线程池闹钟 → PostThreadMessage → 钩子线程泵」那一跳：任一级排队
//（开机线程池饥饿、按住时鼠标事件流占着泵）都会让面板迟到松手那一刻才弹。
// 所以回调本身每条事件都要做一次追赶询问（见 MouseHook.PollFireAndPost 的注释）。
// 这里反射驱动私有 Callback，把 gate 的按下时刻拨到过去来模拟「已经按够了」，不装真钩子。
public class MouseHookCatchUpTests
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private sealed class Rig : IDisposable
    {
        public int Fires;
        public MouseHook Hook = null!;
        public void Dispose() => Hook.Dispose();
    }

    private static Rig NewRig(int moveTolerancePhysical = 0)
    {
        var rig = new Rig();
        rig.Hook = new MouseHook(350, () => rig.Fires++, a => a(), gesture: null,
                                 modifierHeld: () => false, moveTolerancePhysical: moveTolerancePhysical);
        return rig;
    }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static LongPressGate Gate(MouseHook hook)
        => (LongPressGate)typeof(MouseHook).GetField("_gate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hook)!;

    private static void SetDownMsAgo(MouseHook hook, long agoMs)
    {
        var gate = Gate(hook);
        typeof(LongPressGate).GetField("_downMs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(gate, Environment.TickCount64 - agoMs);
    }

    // 闹钟没响也没关系：按住期间任意一条输入事件到点，追赶轮询立刻唤出——不必等松手。
    [Fact]
    public void A_move_event_at_hold_time_fires_the_panel_without_waiting_for_the_alarm()
    {
        using var rig = NewRig();
        var hook = rig.Hook;
        Assert.Equal(new IntPtr(1), Send(hook, WM_MBUTTONDOWN, 200, 200));
        SetDownMsAgo(hook, 400);   // 已按满 350ms（闹钟那条路在本测试里不存在：没装泵）

        Assert.NotEqual(new IntPtr(1), Send(hook, WM_MOUSEMOVE, 200, 200));   // 移动照常放行
        Assert.Equal(1, rig.Fires);
        Assert.Equal(1, hook.MiddleFireCount);
        Assert.False(Gate(hook).Pending);

        // PollFire 只说一次 yes：后续事件不重复弹。
        Send(hook, WM_MOUSEMOVE, 201, 201);
        Assert.Equal(1, rig.Fires);
        // 那次抬起仍是长按的尾巴：吞掉，不能把面板点掉。
        Assert.Equal(new IntPtr(1), Send(hook, WM_MBUTTONUP, 201, 201));
        Assert.Equal(1, rig.Fires);
    }

    // 没按够时长，追赶轮询不能提前弹。
    [Fact]
    public void Catchup_does_not_fire_before_the_hold_is_reached()
    {
        using var rig = NewRig();
        var hook = rig.Hook;
        Send(hook, WM_MBUTTONDOWN, 200, 200);
        SetDownMsAgo(hook, 100);

        Send(hook, WM_MOUSEMOVE, 200, 200);
        Assert.Equal(0, rig.Fires);
        Assert.True(Gate(hook).Pending);
        Gate(hook).Reset();   // 别让 Dispose 补发真按下
    }

    // 拖拽容差按物理像素传入：缩放屏传 9 时，曼哈顿 7 像素的抖动仍是抖动，不打断长按。
    [Fact]
    public void Physical_move_tolerance_is_honored()
    {
        using var rig = NewRig(moveTolerancePhysical: 9);
        var hook = rig.Hook;
        Assert.Equal(new IntPtr(1), Send(hook, WM_MBUTTONDOWN, 100, 100));
        // 越过默认的 6、但没越过传入的 9：必须仍在 pending，闹钟不撤防。
        Assert.NotEqual(new IntPtr(1), Send(hook, WM_MOUSEMOVE, 107, 100));
        Assert.True(Gate(hook).Pending);
        Gate(hook).Reset();   // 别让 Dispose 补发真按下
    }
}
