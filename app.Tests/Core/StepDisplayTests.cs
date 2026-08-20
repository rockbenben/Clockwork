using System.Linq;
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

    [Theory]
    [InlineData("down", "向下滚")]
    [InlineData("up", "向上滚")]
    [InlineData("left", "向左滚")]
    [InlineData("right", "向右滚")]
    [InlineData("leftClick", "左键单击")]
    [InlineData("doubleClick", "左键双击")]
    [InlineData("rightClick", "右键单击")]
    [InlineData("middleClick", "中键单击")]
    [InlineData("back", "后退（侧键）")]
    [InlineData("forward", "前进（侧键）")]
    public void Summary_mouse(string action, string expected)
        => Assert.Equal(expected, StepDisplay.StepSummary(new LaunchStep { Kind = "mouse", Action = action }));

    // 次数由摘要共用的 ×N 修饰段负责，不在鼠标分支里再写一遍——写两遍就会出现「向下滚 5 次 ×5」。
    [Fact] public void Summary_mouse_repeat_comes_from_the_shared_decoration()
        => Assert.Equal("向下滚 ×5", StepDisplay.StepSummary(new LaunchStep { Kind = "mouse", Action = "down", Repeat = 5 }));

    // 缺省/手改 json 漏填 action → 向下滚，与编辑器下拉首项一致，不该变成空白摘要。
    [Fact] public void Summary_mouse_defaults_to_scroll_down()
        => Assert.Equal("向下滚", StepDisplay.StepSummary(new LaunchStep { Kind = "mouse" }));
    [Fact] public void Summary_mouse_unknown_action_falls_back()
        => Assert.Equal("向下滚", StepDisplay.StepSummary(new LaunchStep { Kind = "mouse", Action = "nonsense" }));

    // 动作表是编辑器下拉 / 摘要 / 执行三处的唯一来源：每个动作的文案键必须有译文，
    // 伪键必须是 KeyCombo 认识的——漏一条就是「下拉里选得到、跑起来没反应」。
    [Fact]
    public void Mouse_action_table_is_self_consistent()
    {
        Assert.NotEmpty(StepDisplay.MouseActions);
        foreach (var (action, labelKey, combo) in StepDisplay.MouseActions)
        {
            Assert.NotEqual(labelKey, Clockwork.I18n.Strings.Get(labelKey));   // 有译文（取不到时 Get 原样返回键名）
            Assert.NotNull(KeyCombo.Mouse(combo));                             // 伪键真的能解析
            Assert.Equal(combo, StepDisplay.MouseActionCombo(action));         // 反查一致
        }
        Assert.Equal(StepDisplay.MouseActions.Length, StepDisplay.MouseActions.Select(m => m.Action).Distinct().Count());
    }
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
