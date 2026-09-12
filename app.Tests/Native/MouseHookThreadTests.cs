using System.IO;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 两类新骨架的回归测试：
//   1. WH_MOUSE_LL 装在**专用钩子线程**上，而不是 WPF UI 线程（MouseHook 类头第 1 条）。
//      构造函数不许偷开线程；Install 起泵，Dispose 必须把线程收干净，且可重复调。
//   2. 笔迹采样点合帧：一笔里几百个采样点只允许在 UI 队列里挂一个重绘，
//      drain 跑过之后新点才允许挂下一个，且画的是最新一点。
//
// 状态机部分仍照 MouseHookModifierBailTests 的办法反射驱动私有 Callback，不依赖真实鼠标输入。
public class MouseHookThreadTests
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_RBUTTONDOWN = 0x0204;

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

    private static object? Field(MouseHook hook, string name)
        => typeof(MouseHook).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook);

    // ── 专用钩子线程生命周期 ──

    [Fact]
    public void Constructor_does_not_start_any_thread()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null);
        Assert.Null(Field(hook, "_thread"));
    }

    [Fact]
    public void Install_starts_a_pump_thread_and_Dispose_joins_it()
    {
        using var hook = new MouseHook(350, () => { }, a => a(), gesture: null);
        bool ok = hook.Install();
        // 非交互会话（无头 CI）装不上低级钩子：判不了的环境直接放，不算回归。
        if (!ok) return;
        var t = Assert.IsType<System.Threading.Thread>(Field(hook, "_thread"));
        Assert.False(t.IsThreadPoolThread);

        hook.Dispose();
        Assert.Null(Field(hook, "_thread"));
        Assert.True(t.Join(2000), "钩子线程必须在 Dispose 后退出");
        hook.Dispose();   // 重复 Dispose 不抛
    }

    [Fact]
    public void Dispose_without_Install_neither_starts_thread_nor_throws()
    {
        var hook = new MouseHook(350, () => { }, a => a(), gesture: null);
        hook.Dispose();
        Assert.Null(Field(hook, "_thread"));
        hook.Dispose();
    }

    // ── 笔迹合帧 ──

    [Fact]
    public void Trail_points_coalesce_into_one_queued_redraw_and_paint_the_latest()
    {
        // minLeg=40 → 采样间隔 10px。匹配 "R"：笔画够长后 armed 翻 true。
        var gate = new GestureGate((p, _) => p == "R", 40);
        var posted = new List<Action>();
        var points = new List<(int X, int Y, bool Armed)>();
        var ends = 0;
        var hook = new MouseHook(0, () => { }, posted.Add, gesture: gate,
            trailPoint: (x, y, armed) => points.Add((x, y, armed)),
            trailEnd: () => ends++);

        // 按下：先收上一笔、再落起点，两个独立操作。
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONDOWN, 200, 200));
        Assert.Equal(2, posted.Count);   // trailEnd + 唯一一个 drain

        // 连续采样：30/60px 两个新点都够 step，但不得再排队。
        Send(hook, WM_MOUSEMOVE, 230, 200);
        Send(hook, WM_MOUSEMOVE, 260, 200);
        Assert.Equal(2, posted.Count);

        // 按序执行：先 Finish，再 drain——drain 画的必须是**最新一点**，且此刻已命中 R。
        foreach (var a in posted) a();
        Assert.Equal(1, ends);
        Assert.Single(points);
        Assert.Equal((260, 200, true), points[0]);

        // drain 放号之后再来的点才允许挂下一个，且不补画中间跳过的点。
        posted.Clear();
        Send(hook, WM_MOUSEMOVE, 290, 200);
        Assert.Single(posted);
        foreach (var a in posted) a();
        Assert.Equal(2, points.Count);
        Assert.Equal((290, 200, true), points[1]);

        gate.Reset();   // 别让 Dispose 走「还欠一次真按下」的 SendInput 路
        hook.Dispose();
    }

    // ── 防回归：线程模型不许退回「装在 UI 线程上」 ──

    [Fact]
    public void Hook_runs_on_its_own_thread_with_a_message_pump()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "app", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "app", "Native", "MouseHook.cs"));
        Assert.Contains("PostThreadMessage", src);
        Assert.Contains("GetMessage", src);
        Assert.Contains("Clockwork.MouseHook", src);
    }
}
