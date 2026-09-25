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
}
