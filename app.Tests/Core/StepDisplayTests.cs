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

    [Fact] public void Summary_message_modal_is_plain_text()
        => Assert.Equal("喝水", StepDisplay.StepSummary(new LaunchStep { Kind = "message", Message = "喝水" }));

    [Fact] public void Summary_message_card_is_prefixed()
        => Assert.Equal("卡片提示：喝水", StepDisplay.StepSummary(new LaunchStep { Kind = "message", Message = "喝水", Present = "card" }));

    // —— 新命令与新条件的摘要 ——
    [Fact] public void Summary_volume_mic()
        => Assert.Equal("麦克风静音", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "micMute" }));

    // 带参数的系统命令必须把参数写进摘要，否则清单上三条「设置剪贴板文本」长得一模一样。
    [Fact] public void Summary_system_with_text_arg()
        => Assert.Equal("设置剪贴板文本：hi", StepDisplay.StepSummary(new LaunchStep { Kind = "system", Command = "setClipboard", Text = "hi" }));

    [Fact] public void Summary_system_with_level_arg()
        => Assert.Equal("屏幕亮度：40%", StepDisplay.StepSummary(new LaunchStep { Kind = "system", Command = "brightness", Level = 40 }));

    [Fact] public void Summary_after_suffix()
        => Assert.Equal("静音（仅18:30后）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", OnlyAfter = true, AfterHour = 18, AfterMinute = 30 }));

    [Fact] public void Summary_process_condition_suffix()
        => Assert.Equal("静音（Slack 没运行）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", IfProcessMode = "notRunning", IfProcess = "Slack" }));

    [Fact] public void Summary_power_condition_suffix()
        => Assert.Equal("静音（用电池）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", IfPower = "battery" }));

    // 反斜杠路径必须原样出现在摘要里：Windows 上的条件路径几乎都带反斜杠，任何一处
    // 把它当转义处理掉，用户看到的就是一个和自己填的不一样的路径（E:\backup → E:ackup）。
    [Fact] public void Summary_path_condition_keeps_backslashes()
        => Assert.Equal(@"静音（存在 E:\backup）", StepDisplay.StepSummary(new LaunchStep { Kind = "volume", Action = "mute", IfPathExists = @"E:\backup" }));
}
