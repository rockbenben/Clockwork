using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Clockwork.Native;
using Xunit;

// 手势按下那一刻要问一句「光标底下这个窗是谁家的程序」，答案决定绑定了某个 app 的手势
// 这次算不算命中（见 GestureGate.ContextProcess）。从前这一句是
//     Process.GetProcessById(pid).ProcessName
// 而它跑在**低级鼠标钩子的回调里**——那条路有 LowLevelHooksTimeout（默认 300ms）的硬预算，
// 超了 Windows 会把钩子静默摘掉。
//
// 现场证据（clockwork.error.log，2026-10-03）：
//     11:04:16 ui thread slow: hook-proc-name took 3187ms (thread=bg)
//     11:04:16 ui thread slow: hook-callback took 3187ms (thread=bg) chain=0ms ours=3187ms
// 两行时长相等 = 整个回调的时间就是这一句；chain=0 排除了 CallNextHookEx 与注入。
// 钩子线程被按住的这 3.2 秒里，心跳停住 → 自愈判定钩子死了 → 它在 UI 线程上 Join 等满 2 秒
// → 那 2 秒里覆盖窗在屏幕上（trailUp=up），收笔的计时器没人泵。用户看到的就是「轨迹卡住不动」。
//
// 本机量过的两种写法（377 个活进程各查一次）：Process 组件 278ms，裸查询 3ms，差约 90 倍；
// 而且组件每次都要 new 一个 Process 对象 + 若干字符串——把一次阻塞式 gen2 GC 拽进钩子回调的
// 正是这种分配。所以这里钉两件事：答案要对，且**不许再用那个组件**。
public class ProcessNameQueryTests
{
    private static string Src()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d.FullName, "app", "Native", "Win32.cs"));
    }

    // ── 答案正确 ──

    [Fact]
    public void Own_pid_resolves_to_our_process_name()
    {
        var expected = Process.GetCurrentProcess().ProcessName;
        Assert.Equal(expected, Win32.ProcessNameForPid((uint)Environment.ProcessId));
    }

    // 一个刚退出的 pid：旧写法在这里抛 ArgumentException 并被 catch 吞掉；新写法必须同样答空串，
    // 而不是把一个异常漏进钩子回调（回调里漏出去就是进程当场没了，见 MouseHook.CallbackCore 的 try）。
    [Fact]
    public void Unknown_pid_answers_empty_instead_of_throwing()
    {
        Assert.Equal("", Win32.ProcessNameForPid(uint.MaxValue));
        Assert.Equal("", Win32.ProcessNameForPid(0));
    }

    [Fact]
    public void Zero_window_handle_answers_empty()
    {
        Assert.Equal("", Win32.ProcessNameForWindow(IntPtr.Zero));
    }

    // 拿得到名字的窗，答案必须是**进程名**而不是整条路径：per-app 规则比对的是
    // StepHelpers.ToProcessName 归一化后的名字，多带目录或多带 .exe 都会让规则静默失配。
    [Fact]
    public void Result_is_a_bare_name_not_a_path()
    {
        var name = Win32.ProcessNameForPid((uint)Environment.ProcessId);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain(".exe", name);   // 与旧写法同形：ProcessName 不带扩展名
    }

    // ── 不许退回那个组件 ──
    //
    // 这一条是整份文件里唯一会「看着像洁癖」的断言，所以把理由写全：把它换成 Process 组件
    // 在功能上完全等价、测试全绿、手感也正常——直到某一次右键正好压在一個正在退出或加载器
    // 吊死的进程上。那种事没有复现步骤，而代价是整个钩子被摘、手势当场失灵、屏幕上留一条线。
    [Fact]
    public void The_hook_path_does_not_use_the_Process_component()
    {
        var src = Src();
        int start = src.IndexOf("public static string ProcessNameForPid");
        Assert.True(start >= 0, "ProcessNameForPid 不在了：钩子回调靠它取进程名，换实现可以，换回去不行");
        int end = src.IndexOf("\n    }", start);
        var body = src.Substring(start, end - start);
        Assert.DoesNotContain("GetProcessById", body);
        Assert.DoesNotContain("Process.GetProcesses", body);
        Assert.Contains("QueryFullProcessImageName", body);
    }

    // 窗口那条入口必须只是「句柄 → pid」再加上面这一格，别在中间偷偷塞回组件调用。
    [Fact]
    public void Window_entry_delegates_to_the_pid_query()
    {
        var src = Src();
        int start = src.IndexOf("public static string ProcessNameForWindow");
        Assert.True(start >= 0, "ProcessNameForWindow 不在了");
        var body = src.Substring(start, src.IndexOf("\n    }", start) - start);
        Assert.Contains("ProcessNameForPid(pid)", body);
        Assert.DoesNotContain("GetProcessById", body);
    }
}
