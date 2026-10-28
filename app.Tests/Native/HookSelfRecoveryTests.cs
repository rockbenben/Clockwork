using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// A3（钩子线程自救）与 C（让路）在 MouseHook 这一侧的骨架。
//
// 真装一条 WH_MOUSE_LL 再让它超时，会把测试机的整条输入链拖进去；所以行为测试只驱动
// 不装钩子的回调路径（与 MouseHookThreadTests 同一纪律），线程/泵的形状由源码守卫钉住。
public class HookSelfRecoveryTests
{
    private const int WM_MOUSEMOVE = 0x0200, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "app" }.Concat(parts).ToArray()));

    private static string BodyOf(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到 {signature}");
        int end = src.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{signature} 的方法体没有收尾");
        return src.Substring(start, end - start);
    }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static object? Field(MouseHook hook, string name)
        => typeof(MouseHook).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hook);

    private static bool Swallowed(IntPtr verdict) => verdict == new IntPtr(1);

    // 窗口峰值必须真的「读后清零」，而 lifetime 峰值不能被连带清掉：
    // 前者是让路判据的输入，后者是重装日志的历史。
    [Fact]
    public void Congestion_peaks_are_read_and_cleared_without_touching_the_lifetime_peak()
    {
        using var hook = new MouseHook(0, () => { }, a => a());
        typeof(MouseHook).GetField("_callbackWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 412u);
        typeof(MouseHook).GetField("_replayWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 87);
        typeof(MouseHook).GetField("_callbackMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 999u);

        var (callback, replay) = hook.TakeCongestionPeaks();
        Assert.Equal(412, callback);
        Assert.Equal(87, replay);
        Assert.Equal((0, 0), hook.TakeCongestionPeaks());
        Assert.Equal(999u, hook.CallbackMaxMs);
    }

    // 自救重置 lifetime 账时，窗口峰值必须留给让路采样：超时的证据正是在这两个格里。
    [Fact]
    public void Generation_reset_keeps_the_window_peaks_for_the_yield_sampler()
    {
        using var hook = new MouseHook(0, () => { }, a => a());
        var t = typeof(MouseHook);
        t.GetField("_callbackWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 412u);
        t.GetField("_replayWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 87);
        t.GetField("_callbackMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 999u);
        t.GetMethod("ResetGenerationMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hook, null);

        Assert.Equal((412, 87), hook.TakeCongestionPeaks());
        Assert.Equal(0u, hook.CallbackMaxMs);
    }

    // 让路中右键必须整笔放行，而且不能把 gate 打开——否则「让路」只是换了个地方吞键。
    [Fact]
    public void Yielding_passes_right_buttons_through_and_keeps_the_gate_cold()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad");
        hook.SetYielding(true);
        Assert.True(hook.Yielding);

        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));
        Assert.False(gate.Pending);
        Assert.False(Swallowed(Send(hook, WM_RBUTTONUP, 100, 100)));

        hook.SetYielding(false);
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));   // 恢复后照旧接管
        gate.Reset();   // 别让 Dispose 走还键那一条注入路
    }

    // 笔画中途的修正只能读缓存；若每个采样点都排一次解析，HookProbe 的 64 格队列会被同一 pid 灌满。
    [Fact]
    public void Mid_stroke_name_correction_reads_the_cache_without_enqueuing()
    {
        var probe = Source("Native", "HookProbe.cs");
        int at = probe.IndexOf("public string CachedProcessName(", StringComparison.Ordinal);
        Assert.True(at >= 0, "CachedProcessName 不见了");
        int end = probe.IndexOf(';', at);
        Assert.True(end > at, "CachedProcessName 的方法体没找到收尾");
        Assert.DoesNotContain("Request(", probe.Substring(at, end - at));
        var hook = Source("Native", "MouseHook.cs");
        Assert.Contains("_cachedProcNameForPid(_gesturePid)", hook);
    }

    // 自救只由泵线程执行；回调里投的是一条消息，不是 SetWindowsHookEx。
    [Fact]
    public void Self_recovery_is_a_pump_thread_action_not_callback_work()
    {
        var src = Source("Native", "MouseHook.cs");
        var pump = BodyOf(src, "private void HookThreadRun()");
        Assert.Contains("WM_HOOK_RECOVER", pump);
        Assert.Contains("RecoverHook()", pump);
        Assert.Contains("WM_YIELD", pump);

        var cb = BodyOf(src, "private IntPtr Callback(");
        Assert.Contains("HookWatch.ShouldSelfRecover", cb);
        Assert.Contains("RequestSelfRecovery()", cb);
        Assert.DoesNotContain("SetWindowsHookEx", cb);
        Assert.DoesNotContain("UnhookWindowsHookEx", cb);

        var recover = BodyOf(src, "private void RecoverHook()");
        Assert.Contains("Volatile.Read(ref _disposePosted)", recover);
        Assert.Contains("Volatile.Write(ref _hook", recover);
        Assert.Contains("SetWindowsHookEx", recover);
        Assert.Contains("_selfRecovered?.Invoke(this", recover);
    }

    // 自救结果回到 UI 时必须对号：旧钩子的迟到报告不许改新钩子的状态。
    [Fact]
    public void A_late_self_recovery_report_is_ignored_by_the_coordinator()
    {
        var coord = Source("Services", "GestureCoordinator.cs");
        Assert.Contains("TakeCongestionPeaks", coord);
        Assert.Contains("ReferenceEquals(source, _mouseHook)", coord);
        Assert.Contains("gesture yield: entered", coord);
        Assert.Contains("ClearGestureYield", coord);
        var tray = Source("TrayIcon.cs");
        Assert.Contains("Gesture_YieldResume", tray);
        Assert.Contains("GestureYieldActive", tray);
        var mgr = Source("Views", "GestureManagerWindow.xaml.cs");
        Assert.Contains("GestureWatchState.Yielding", mgr);
        Assert.Contains("Gesture_WatchYielding", mgr);
    }
}
