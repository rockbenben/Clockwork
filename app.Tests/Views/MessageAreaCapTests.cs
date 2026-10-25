using System.IO;
using Xunit;

// 正文上限的接线检查：MessageAreaSizing 的单测只证明公式，证明不了两个窗口真的用了它。
// 这两扇窗的共同失效方式正好是「窗口有 MaxHeight、内容没有」，而离屏截图又会把按钮画全，
// 所以把接线写成构建期检查，别留给 review 的记性。
public class MessageAreaCapTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Reminder_popup_caps_its_message_to_the_monitor_work_area()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "app", "Views", "ReminderPopupWindow.xaml"));
        Assert.Matches(@"<ScrollViewer[^>]*x:Name=""MsgScroll""[^>]*MaxHeight=""\d+""", xaml);

        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "ReminderPopupWindow.xaml.cs"));
        Assert.Contains("MsgScroll.MaxHeight", code);
        Assert.Contains("WindowSizing.WorkAreaHeight", code);
        Assert.Contains("MessageAreaSizing.ForWorkArea", code);
    }

    [Fact]
    public void Toast_caps_its_message_to_the_work_area()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "app", "Views", "NotificationToast.xaml"));
        Assert.Matches(@"<ScrollViewer[^>]*x:Name=""MsgScroll""[^>]*MaxHeight=""\d+""", xaml);

        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "NotificationToast.xaml.cs"));
        Assert.Contains("MsgScroll.MaxHeight", code);
        Assert.Contains("MessageAreaSizing.ForWorkArea", code);
    }
}
