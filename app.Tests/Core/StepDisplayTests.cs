using Clockwork.Core;
using Xunit;

public class StepDisplayTests
{
    [Fact] public void KindLabel() => Assert.Equal("启动程序", StepDisplay.StepKindLabel("app"));
    [Fact] public void SystemLabel() => Assert.Equal("锁屏（回来需输密码）", StepDisplay.SystemCommandLabel("lockScreen"));
    [Fact] public void SystemLabel_unknown_passthrough() => Assert.Equal("xyz", StepDisplay.SystemCommandLabel("xyz"));

    [Fact] public void DaysLabel_empty_everyday() => Assert.Equal("每天", StepDisplay.DaysLabel(new List<int>()));
    [Fact] public void DaysLabel_all7_everyday() => Assert.Equal("每天", StepDisplay.DaysLabel(new List<int> { 1, 2, 3, 4, 5, 6, 7 }));
    [Fact] public void DaysLabel_weekdays() => Assert.Equal("一二三四五", StepDisplay.DaysLabel(new List<int> { 1, 2, 3, 4, 5 }));

    [Fact] public void Summary_volume_set() => Assert.Equal("设音量 30%", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "set", Level = 30 }));
    [Fact] public void Summary_window_close() => Assert.Equal("关闭窗口 Weixin", StepDisplay.StepSummary(new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" }));
    [Fact] public void Summary_delay_seconds() => Assert.Equal("延时 2 秒", StepDisplay.StepSummary(new LaunchStep { Kind = "delay", DelayMs = 2000 }));
    [Fact] public void Summary_repeat_suffix() => Assert.Equal("发送 Win+D ×3", StepDisplay.StepSummary(new LaunchStep { Kind = "keys", Combo = "Win+D", Repeat = 3 }));
    [Fact] public void Summary_before8_suffix() => Assert.Equal("静音（仅08:00前）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", OnlyBefore8 = true }));
    [Fact] public void Summary_before_custom_time() => Assert.Equal("静音（仅08:30前）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", OnlyBefore8 = true, BeforeHour = 8, BeforeMinute = 30 }));
    [Fact] public void ListSummary_appends_note() => Assert.Equal("静音（备注）", StepDisplay.StepListSummary(new LaunchStep { Kind = "volume", Action = "mute", Note = "备注" }));
}
