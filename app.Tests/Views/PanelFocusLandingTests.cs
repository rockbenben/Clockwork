using System.IO;
using System.Linq;
using Xunit;

// 唤出时焦点落点的守卫。这条接线断了不报错、不闪、什么症状都没有——
// 只是某天 Ctrl+Alt+Space 按住稍久，第一格（出厂页排得到「锁屏」）就被重复的 Space 激活。
// 键盘焦点流转要真窗口 + 消息泵，单元测试跑不了，只能钉源码（与 PanelClickHookTests 同一套纪律）。
public class PanelFocusLandingTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // Popup/Revive 只许把焦点交给**窗口**；预焦点第一格 = 唤出热键的尾随 Space 直接打在第一格上。
    [Fact]
    public void Summon_focuses_the_window_not_a_tile()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        string WindowFocusInside(string method)
        {
            int a = src.IndexOf(method);
            Assert.True(a >= 0, $"{method} 找不到了：这段守卫要跟着它走");
            int b = src.IndexOf("\n    }", a);
            return src.Substring(a, b - a);
        }
        Assert.Contains("Focus();", WindowFocusInside("public bool Popup()"));
        Assert.DoesNotContain("FocusFirstTile();", WindowFocusInside("public bool Popup()"));
        Assert.Contains("Focus();", WindowFocusInside("public bool Revive()"));
        Assert.DoesNotContain("FocusFirstTile();", WindowFocusInside("public bool Revive()"));
    }

    // 窗口持焦时的第一下方向键/Tab/回车必须「落格并吃掉」：
    // 没有这条，方向键在 DirectionalNavigation 上弦前无处可去（格子间移动要求焦点已在格子区），
    // 键盘唤出就退化成只能 Esc；少了 e.Handled 则「落格的那一下回车」当场跑掉第一格。
    [Fact]
    public void First_navigation_key_lands_on_a_tile_without_running_it()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        Assert.Contains("case Key.Down or Key.Up or Key.Left or Key.Right or Key.Tab or Key.Enter", src);
        Assert.Contains("when !_searching && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase", src);
        int a = src.IndexOf("case Key.Down or Key.Up");
        var body = src.Substring(a, src.IndexOf("return;", a) - a);
        Assert.Contains("FocusFirstTile();", body);
        Assert.Contains("e.Handled = true;", body);
    }

    // GiveUp/半激活观察拍的接线守卫：裁决在 Core（有纯函数用例），但「没有哪一拍见过真前台才观察、
    // Revive 清零计数」这两句活在窗口里，跑不了单测——它们断了没有症状，只会把修好的「失焦不关」放回来。
    [Fact]
    public void Zombie_watch_is_wired_to_the_giveup_state_and_reset_on_revive()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        // 门必须是 hadFocus（OS 真相）而不是 _everActivated：半激活面板 ever=True，
        // 用 ever 判它就永远落在所有关闭分支的缝里（见观察块注释里的部署日志指纹）。
        Assert.Contains("if (!hadFocus && _watchTick > PanelFocus.MaxRetries && !now)", src);
        Assert.Contains("PanelFocus.ZombieShouldDismiss(otherFg, inside, _outsideTicks)", src);
        // 光标判定不了时按「还在用」处理：这一路的失败方向必须是不关。
        int a = src.IndexOf("private bool CursorInsidePanel()");
        Assert.True(a >= 0, "找不到 CursorInsidePanel：这段守卫要跟着它走");
        Assert.Contains("catch { return true; }", src.Substring(a, 600));
        // Revive 清零观察拍：否则救活后面板上还欠着一截「已走开」的计数。
        int r = src.IndexOf("public bool Revive()");
        var revive = src.Substring(r, src.IndexOf("\n    }", r) - r);
        Assert.Contains("_outsideTicks = 0", revive);
    }

    // 前台易主事件快路的接线守卫：事件负责「丢焦点当拍就关」，但它**只许**关见过真前台的
    // 面板——僵尸/半激活那类不会有对自己有意义的事件，观察拍仍是唯一关闭路；接线漏了是退回
    // 250ms 粒度（无症状的降级），接错（让 !hadFocus 也吃事件）则是开机抢焦点时面板当场被弹走。
    [Fact]
    public void Foreground_event_fast_path_is_wired_and_bounded()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        Assert.Contains("new Native.ForegroundWatch", src);
        Assert.Contains("if (_closing || _menuOpen || !hadFocus) return;", src);
        // 关窗必须解钩：每块面板漏一个钩子，而程序设计成常驻托盘数周。
        Assert.Contains("_fgWatch?.Dispose()", src);
    }
}
