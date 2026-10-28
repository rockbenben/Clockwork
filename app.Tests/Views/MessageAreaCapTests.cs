using System.IO;
using Clockwork.Views;
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

    // 单卡封顶之后，堆叠还得算总账：旧逻辑按 EstToastHeight=130 估算能放几张，
    // 而 320 DIP 的长正文让单卡可以到约 400——两张就把小屏顶穿了。
    [Fact]
    public void Two_full_length_cards_do_not_fit_a_laptop_work_area()
        => Assert.False(NotificationToast.FitsInWorkArea(new[] { 380.0, 380.0 }, 380, 768));

    [Fact]
    public void Short_cards_still_fit_the_same_work_area()
        => Assert.True(NotificationToast.FitsInWorkArea(new[] { 130.0, 130.0 }, 130, 768));

    [Fact]
    public void The_stack_height_is_checked_before_a_new_card_is_added()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "NotificationToast.xaml.cs"));
        Assert.Contains("FitsInWorkArea", code);
        Assert.Contains("HeightOf(this)", code);
    }

    // 屏幕被常驻提醒占满时，状态卡不该把未读提醒顶掉；已有状态卡可挤时才留新的状态卡。
    [Fact]
    public void A_status_card_is_dropped_when_only_persistent_reminders_fill_the_screen()
    {
        Assert.True(NotificationToast.ShouldDropIncoming(incomingPersistent: false, hasStatusCardToEvict: false));
        Assert.False(NotificationToast.ShouldDropIncoming(incomingPersistent: false, hasStatusCardToEvict: true));
        Assert.False(NotificationToast.ShouldDropIncoming(incomingPersistent: true, hasStatusCardToEvict: false));
    }

    // 合并不是新增：卡片是在屏上长高的，必须在规模变化后重算总高度，而不是只在 OnLoaded 算一次。
    [Fact]
    public void A_growing_card_rechecks_the_stack_height_after_layout()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "NotificationToast.xaml.cs"));
        Assert.Contains("QueueTrimAfterGrowth", code);
        Assert.Contains("TrimOthersToWorkArea", code);
        Assert.Contains("SizeChanged +=", code);
    }
}
