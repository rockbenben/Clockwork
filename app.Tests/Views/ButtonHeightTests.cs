using System;
using System.IO;
using System.Linq;
using Xunit;

// 文字按钮的固定 Height 只要比「一行字 + 上下 Padding」小，最后一行字的底边就会被裁。
// 这类裁切在截图里常常只差 1px、看不出，但运行时时会真的少一条像素；这里钉住
// 用户报过的几颗按钮和对话框的共用按钮样式：它们要用 MinHeight，让内容自己决定最终高度。
public class ButtonHeightTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string App(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    [Fact]
    public void Dialog_buttons_use_min_height_so_wrapped_text_can_grow()
    {
        var theme = App("Theme.xaml");
        foreach (var key in new[] { "DialogButton", "DialogPrimaryButton" })
        {
            int at = theme.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            Assert.True(at >= 0, $"Theme.xaml 找不到 {key}");
            int end = theme.IndexOf("</Style>", at, StringComparison.Ordinal);
            Assert.True(end > at);
            var block = theme.Substring(at, end - at);
            Assert.Contains("MinHeight", block);
            Assert.DoesNotContain("Property=\"Height\" Value=\"34\"", block);
        }
    }

    [Fact]
    public void The_reported_text_buttons_let_the_content_set_the_height()
    {
        var settings = App("Views", "GestureSettingsWindow.xaml");
        Assert.Matches(@"<Button[^>]*Content=""\{loc:Loc Btn_Close\}""[^>]*MinHeight=""32""", settings);

        var panel = App("Views", "PanelManagerWindow.xaml");
        Assert.Matches(@"<Button[^>]*x:Name=""ResetUsageBtn""[^>]*MinHeight=""32""", panel);
        Assert.Matches(@"<Button[^>]*x:Name=""CloseBtn""[^>]*MinHeight=""32""", panel);

        var quick = App("Views", "QuickPanelWindow.xaml");
        Assert.Matches(@"<Button[^>]*x:Name=""EmptyAction""[^>]*MinHeight=""30""", quick);
    }
}
