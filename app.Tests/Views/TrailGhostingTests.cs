using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

// 手势笔迹那扇窗**铺满整块屏**、又置顶，于是它有一条别处没有的故障路径：
//
//   UI 线程卡顿超过 5 秒（HungWindowTimeout）→ 系统认定我们「未响应」→ **DWM** 在笔迹窗的
//   位置上盖一块自己的幽灵窗 → 那块窗不继承我们的 WS_EX_TRANSPARENT、**点得中** →
//   整个桌面点不动，而它属于 dwm.exe，用户只能去杀它（用户报的原话：「偶尔手势操作会造成
//   无法点击，关闭桌面窗口管理器才正常」）。
//
// 唯一的堵法是进程级关掉幽灵化（Win32.DisableWindowGhosting）。这条守卫盯三件事，因为三件
// 都不会在编译期或运行期报错：开关有没有被挪走、有没有被挪到建窗之后、那句 P/Invoke 的名字对不对。
public class TrailGhostingTests
{
    private static string AppSource(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // **开关必须在建窗之前翻。** 幽灵化是「窗口已经摆在那儿」时才可能发生的，翻晚了那一笔
    // 手势就仍然暴露在整条路上——而这个顺序在代码里只是上下两行，挪一下谁也看不出来。
    [Fact]
    public void The_switch_is_flipped_before_the_trail_window_exists()
    {
        var app = AppSource("App.xaml.cs");
        int flip = app.IndexOf("Win32.DisableWindowGhosting()");
        int trail = app.IndexOf("_trail ??= new Views.GestureTrailWindow()");
        Assert.True(flip >= 0, "App.xaml.cs 里找不到 Win32.DisableWindowGhosting()：全屏笔迹窗的幽灵化没关");
        Assert.True(trail >= 0, "App.xaml.cs 里找不到笔迹窗的创建：这段守卫要跟着它走");
        Assert.True(flip < trail, "幽灵化必须在笔迹窗建出来之前就关掉");
    }

    // 那句 P/Invoke 外面包着 catch（它是个单向、不可逆的开关，不值得为一个老到 XP 就有的
    // 入口点把程序拦下来），于是**名字写错时它会静默失效**：编译全绿、开关从没生效过，
    // 而这条路上唯一能自证的信号就是「真的调进去了」。所以这里绕过包装，直接问系统一次。
    [Fact]
    public void The_p_invoke_entry_point_resolves()
    {
        var m = typeof(Clockwork.Native.Win32)
            .GetMethod("DisableProcessWindowsGhosting", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);        // 声明被删了 / 改了名
        m!.Invoke(null, null);    // 入口点名写错 → EntryPointNotFoundException
    }

    // 调用点每次保存配置都会走到（ApplyMouseHook），而那个开关是单向的。这条钉的是包装自己的
    // 闸：不靠调用点自觉，重复调多少次都只真发一次。
    [Fact]
    public void The_wrapper_is_idempotent()
    {
        Clockwork.Native.Win32.DisableWindowGhosting();
        Clockwork.Native.Win32.DisableWindowGhosting();
        Assert.Contains("Interlocked.Exchange(ref _ghostDisabled, 1) == 1", AppSource("Native", "Win32.cs"));
    }
}
