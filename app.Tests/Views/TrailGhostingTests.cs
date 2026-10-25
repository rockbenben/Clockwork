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
    /// <summary>仓库根下的 app/ 目录（从测试产物往上找，判据是 app/Resources 在不在）。</summary>
    private static string AppDir()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return Path.Combine(d!.FullName, "app");
    }

    private static string AppSource(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    /// <summary>
    /// 文件里第一次造出全屏笔迹覆盖窗的位置；没有则 -1。
    /// <para>
    /// 两种写法都要认：Services/GestureCoordinator.cs 写的是限定名
    /// <c>new Views.GestureTrailWindow()</c>，而 app/DevChecks.cs 因为 using 了 Clockwork.Views
    /// 写的是短名 <c>new GestureTrailWindow()</c>。只认一种的话，另一处就会「找不到」——
    /// 而「找不到」在这类守卫里是最危险的结论：它看起来像通过。
    /// </para>
    /// </summary>
    private static int TrailWindowSite(string src)
    {
        int at = src.IndexOf("new Views.GestureTrailWindow", System.StringComparison.Ordinal);
        return at >= 0 ? at : src.IndexOf("new GestureTrailWindow", System.StringComparison.Ordinal);
    }

    // **开关必须在建窗之前翻。** 幽灵化是「窗口已经摆在那儿」时才可能发生的，翻晚了那一笔
    // 手势就仍然暴露在整条路上——而这个顺序在代码里只是上下两行，挪一下谁也看不出来。
    [Fact]
    public void The_switch_is_flipped_before_the_trail_window_exists()
    {
        var app = AppSource("Services", "GestureCoordinator.cs");
        int flip = app.IndexOf("Win32.DisableWindowGhosting()");
        int trail = app.IndexOf("_trail ??= new Views.GestureTrailWindow()");
        Assert.True(flip >= 0, "GestureCoordinator.cs 里找不到 Win32.DisableWindowGhosting()：全屏笔迹窗的幽灵化没关");
        Assert.True(trail >= 0, "GestureCoordinator.cs 里找不到笔迹窗的创建：这段守卫要跟着它走");
        Assert.True(flip < trail, "幽灵化必须在笔迹窗建出来之前就关掉");
    }

    // **开发期那条路也得关。**
    //
    // 生产路径（Services/GestureCoordinator.cs）早就防住了，但开发期这条路——app/DevChecks.cs 的
    // AllXamlWindows() 里那项 GestureTrail——**不走 ApplyMouseHook**：App.OnStartup 见到
    // --smoke/--shots/--selftest 就 return 了，StartEngines 根本没跑。
    // 于是那条路上幽灵化是开着的，而它偏偏是唯一一条会把全屏覆盖窗真的摆到**活桌面**上的路。
    // 这就是「开发者跑一次自检，整个桌面点不动、只能去杀 dwm.exe」的那条路。
    [Fact]
    public void The_dev_check_path_flips_the_switch_before_its_own_trail_window()
    {
        var dev = AppSource("DevChecks.cs");
        int flip = dev.IndexOf("DisableWindowGhosting()");
        int trail = TrailWindowSite(dev);
        Assert.True(flip >= 0, "DevChecks.cs 里找不到 DisableWindowGhosting()：--smoke 会把全屏覆盖窗摆到活桌面上，而这条路的幽灵化是开着的");
        Assert.True(trail >= 0, "DevChecks.cs 里找不到笔迹窗的创建：这段守卫要跟着它走");
        Assert.True(flip < trail, "幽灵化必须在笔迹窗建出来之前就关掉");
    }

    // **每一个真的造出全屏覆盖窗的地方，都得先翻开关。**
    //
    // 上面两条是逐个点名的，而它们盯的是「已经存在的那几处」。这条扫全仓库：谁造这扇窗，谁就得在
    // 同一文件里、在造它之前先翻开关。以后再有人从别处 new 一扇出来，当场红。
    //
    // 这条守卫的存在理由是一次真事：原来的守卫只钉了 GestureCoordinator.cs 一处，于是开发期那条
    // 路（没人守着的唯一一条）就漏了——而它正是唯一会把全屏窗摆到用户面前的那条。
    [Fact]
    public void Every_full_screen_trail_window_is_built_with_ghosting_disabled_first()
    {
        int sites = 0;
        foreach (var file in Directory.GetFiles(AppDir(), "*.cs", SearchOption.AllDirectories))
        {
            // obj/bin 下面是生成物，扫它们只会让判据变得不可靠（生成物随时会变）。
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var src = File.ReadAllText(file);
            int trail = TrailWindowSite(src);
            if (trail < 0) continue;
            sites++;

            string name = Path.GetFileName(file);
            int flip = src.IndexOf("DisableWindowGhosting()", System.StringComparison.Ordinal);
            Assert.True(flip >= 0, $"{name} 造了全屏笔迹覆盖窗，却没关本进程的窗口幽灵化");
            Assert.True(flip < trail, $"{name}：幽灵化开关必须排在覆盖窗构造之前");
        }
        // 比已知的多没问题（以后有人再加一处，那一处也得守）；比已知少则说明判据本身失效了——
        // 比如有人把 new 换成了别的写法——那它就不再是一道真守卫。
        Assert.True(sites >= 2, $"扫到的全屏覆盖窗构造点只有 {sites} 处（预期至少 2 处）：判据本身可能已失效");
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
