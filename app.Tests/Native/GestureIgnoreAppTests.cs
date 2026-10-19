using System;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「这个程序里根本不接管手势」在钩子那一层的实际效果。
//
// 这一层与 GestureIgnoreTests（纯判据）是两件事：那边答「这个名字在不在名单里」，
// 这边答「在名单里的时候，那一下右键到底发生了什么」。中间隔着三件事：
//   · 进程名是从**前台窗口**取的（与分应用绑定同一口径：同问一次「我现在在哪个程序里」）；
//   · 判定发生在**按下那一刻**——按下要被吞掉，吞掉之后就还不回来了；
//   · 整笔**原样放行**：右键按下、抬起、菜单，全归程序自己。
//
// 最后这一条是承重的：名单里的程序若只放行一半（吞了按下、放行抬起），
// 用户那边就是「右键菜单按不出来」，比「手势没生效」严重得多。
public class GestureIgnoreAppTests
{
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // 契约是「1 = 吞掉」，其余都是放行。
    private static bool Swallowed(IntPtr verdict) => verdict == new IntPtr(1);

    // holdMs=0：只留手势那一路。post 同步执行，回调里的投递当场跑完，不依赖消息循环。
    // name 就是前台窗口的进程名（探针缓存读，见 HookProbe.ProcessName）。
    private static MouseHook Hook(GestureGate gate, string name, params string[] ignored)
        => new(0, () => { }, a => a(), gesture: gate,
               mayEngage: _ => true,
               procNameForPid: _ => name,
               ignoredApp: proc => GestureGate.IsIgnoredApp(proc, ignored));

    // 名单里的程序：那一下右键**一个字节都不碰**。
    [Fact]
    public void A_listed_app_gets_its_right_click_untouched()
    {
        var gate = new GestureGate(_ => true, 40);   // 什么都匹配得上：闸门必须是唯一的挡住它的东西
        using var hook = Hook(gate, "mstsc", "mstsc");
        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        Assert.False(gate.Pending);                 // gate 根本没起笔
        Assert.False(Swallowed(Send(hook, WM_RBUTTONUP, 200, 200)));
    }

    // 不在名单里：行为一字不变（照旧吞下按下，手势照旧能画）。
    [Fact]
    public void An_app_outside_the_list_still_gets_gestures()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = Hook(gate, "chrome", "mstsc");
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        Assert.True(gate.Pending);
        gate.Reset();
    }

    // 名单为空：到处都接管（这是默认，也必须是从前唯一的行为）。
    [Fact]
    public void An_empty_list_leaves_every_app_alone()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = Hook(gate, "mstsc");
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        gate.Reset();
    }

    // 名字还没算出来（探针缓存未命中）时按「不在名单里」处理：
    // 反过来会让手势在每一个刚切过去的窗口里都失效，而名字只差几十毫秒。
    [Fact]
    public void An_unresolved_name_does_not_silence_the_gesture()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = Hook(gate, "", "mstsc");
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        gate.Reset();
    }

    // 名单里的程序，整笔都不画笔迹：那条线是「我在画一笔」的意思，
    // 而这一笔根本没开始。画出来又立刻收掉，看着像闪了一下。
    [Fact]
    public void No_trail_is_drawn_in_a_listed_app()
    {
        var gate = new GestureGate(_ => true, 40);
        var points = 0;
        var ends = 0;
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true,
            procNameForPid: _ => "mstsc",
            ignoredApp: proc => GestureGate.IsIgnoredApp(proc, new[] { "mstsc" }),
            trailPoint: (x, y, armed) => points++,
            trailEnd: () => ends++);
        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        Assert.Equal(0, points);
        Assert.Equal(1, ends);   // 但仍要把上一笔可能挂着的线收掉
    }
}
