using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 笔迹旁那句「这将是哪件事」的数据通路：钩子线程认出命中的是**哪一条**，把它捎给 UI 线程。
//
// 这一段路有三处都可能悄悄断掉，而每一处断掉的后果都是同一句「笔迹亮了但不知道是哪件事」：
//   · 认出了形状，却没认出是哪一条（那就只有 bool，没有身份）；
//   · 认出了身份，却在合帧时丢了（合帧只留最新一点，身份也得跟着换）；
//   · 身份捎到了，却捎的是文案（那要在钩子线程上查 i18n 资源——首次加载卫星程序集要几毫秒，
//     而那几毫秒落在 LowLevelHooksTimeout 的预算上，是最不该花的那笔）。
//
// 最后一条钉的是「捎的是**步骤**而不是那句话」：文案由 UI 线程自己算。
public class GestureHitTrailTests
{
    private const int WM_RBUTTONDOWN = 0x0204, WM_MOUSEMOVE = 0x0200;

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

    [Fact]
    public void The_matched_step_travels_with_the_trail_point()
    {
        var hit = new LaunchStep { Gesture = "R", Label = "Copy" };
        var gate = new GestureGate((p, _) => p == "R" ? hit : null, 40);
        var posted = new List<Action>();
        var seen = new List<(bool Armed, LaunchStep? Hit)>();
        using var hook = new MouseHook(0, () => { }, posted.Add, gesture: gate,
            mayEngage: _ => true,
            procNameForPid: _ => "",
            trailPoint: (x, y, armed, h) => seen.Add((armed, h)),
            trailEnd: () => { });

        Send(hook, WM_RBUTTONDOWN, 100, 100);
        for (int i = 1; i <= 12; i++) Send(hook, WM_MOUSEMOVE, 100 + i * 10, 100);
        foreach (var a in posted) a();

        Assert.NotEmpty(seen);
        Assert.True(seen[^1].Armed);
        Assert.Same(hit, seen[^1].Hit);
    }

    // 没命中的时候捎的是 null：一句「这将是哪件事」在没有「哪件事」时必须没有，
    // 否则笔迹旁会挂着一句上一笔的旧话。
    [Fact]
    public void Nothing_is_carried_when_the_stroke_does_not_match()
    {
        var gate = new GestureGate((p, _) => (LaunchStep?)null, 40);
        var posted = new List<Action>();
        var seen = new List<(bool Armed, LaunchStep? Hit)>();
        using var hook = new MouseHook(0, () => { }, posted.Add, gesture: gate,
            mayEngage: _ => true,
            procNameForPid: _ => "",
            trailPoint: (x, y, armed, h) => seen.Add((armed, h)),
            trailEnd: () => { });

        Send(hook, WM_RBUTTONDOWN, 100, 100);
        for (int i = 1; i <= 12; i++) Send(hook, WM_MOUSEMOVE, 100 + i * 10, 100);
        foreach (var a in posted) a();

        Assert.NotEmpty(seen);
        Assert.False(seen[^1].Armed);
        Assert.Null(seen[^1].Hit);
    }
}
