using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Clockwork.Core;
using Clockwork.Views;
using Xunit;

// 样式体系的接线守卫：样式数据/渲染器本身有单测，但「管理器/设置窗/HUD 真的用了它」
// 只能靠源码形态 + 截图矩阵。这里钉调用形态，截图负责「看起来对不对」。
public class GesturePresentationTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    [Fact]
    public void The_saved_preview_uses_the_shared_renderer_and_keeps_the_shape_markers()
    {
        var src = Source("Views", "GestureTrailPreview.cs");
        Assert.Contains("new GestureTrailRenderer", src);
        Assert.Contains("GestureGlyph.Fit", src);
        Assert.Contains("includeLine: false", src);
    }

    [Fact]
    public void The_manager_saved_preview_is_not_a_plain_glyph_again()
    {
        var src = Source("Views", "GestureManagerWindow.xaml.cs");
        Assert.Contains("GestureTrailPreview.MakePath", src);
        Assert.Contains("StyleAccent()", src);
        Assert.DoesNotContain("GestureShape.Content = GestureGlyph.Make", src);
    }

    [Fact]
    public void The_settings_style_picker_is_a_visual_strip()
    {
        var xaml = Source("Views", "GestureSettingsWindow.xaml");
        Assert.Contains("x:Name=\"StyleList\"", xaml);
        Assert.Contains("GestureStyleItem", xaml);
        Assert.DoesNotContain("x:Name=\"StyleCbo\"", xaml);

        var code = Source("Views", "GestureSettingsWindow.xaml.cs");
        Assert.Contains("GestureTrailPreview.MakeArc", code);
        Assert.Contains("StyleList_Changed", code);
        Assert.Contains("AutomationProperties.SetName", code);
    }

    [Fact]
    public void The_hud_carries_the_style_accent_and_a_ltr_direction_string()
    {
        var src = Source("Views", "GestureTrailWindow.cs");
        Assert.Contains("_noteBar.Background = accent", src);
        Assert.Contains("_hintBar.Background = accent", src);
        Assert.Contains("FlowDirection.LeftToRight", src);
        Assert.Contains("FontMono", src);
    }

    [Fact]
    public void An_empty_gesture_has_no_saved_preview_but_a_real_one_does()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve("classic");
            var widths = GestureTrailWidths.Resolve("normal");
            var accent = new SolidColorBrush(Colors.SteelBlue);
            var armed = new SolidColorBrush(Colors.White);
            Assert.Null(GestureTrailPreview.MakePath("", style, widths, false, 150, 118, accent, armed));
            Assert.NotNull(GestureTrailPreview.MakePath("RD", style, widths, false, 150, 118, accent, armed));
        });
    }

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }
}
