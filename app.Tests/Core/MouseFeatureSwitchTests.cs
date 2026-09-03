using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// 两个「要占用鼠标」的功能各有一个总开关：快捷面板（PanelEnabled）与鼠标手势（GesturesEnabled）。
//
// 这两个开关真正的价值在**关掉之后一个鼠标钩子都不装**：那个钩子必须先吞掉每一次中键/右键按下
// 才能判断意图，正常时无感、出错时的样子是「全系统中键或右键失灵」（见 MouseHook 类头）。
// 所以「我不用这两个功能」必须能表达成「你别碰我的鼠标」，而不只是「别响应」。
public class MouseFeatureSwitchTests
{
    private static readonly LaunchStep OneGesture = new() { Enabled = true, Gesture = "D" };

    [Fact]
    public void Panel_is_on_by_default()
    {
        // 默认必须是开：热键一直有默认值，把既有用户升级成「关」会让天天在用的功能凭空消失。
        Assert.True(new AppSettings().PanelEnabled);
        Assert.True(RootConfig.Default().Settings.PanelEnabled);
    }

    [Fact]
    public void Gestures_are_on_by_default()
        => Assert.True(new AppSettings().GesturesEnabled);

    // 手势那半：总开关关掉就不监听右键，哪怕底下还留着启用中的手势。
    // （判据本身是 GestureGate.ShouldWatch，纯函数；这里锁的是「开关真的能一票否决」。）
    [Fact]
    public void Gesture_master_switch_overrides_configured_gestures()
    {
        Assert.True(GestureGate.ShouldWatch(true, new List<LaunchStep> { OneGesture }));
        Assert.False(GestureGate.ShouldWatch(false, new List<LaunchStep> { OneGesture }));
    }

    // 一条手势都没配时也不装钩子——开关开着也一样。装一个永远不会命中的钩子只有风险没有收益。
    [Fact]
    public void No_gestures_configured_means_no_hook_either()
    {
        Assert.False(GestureGate.ShouldWatch(true, new List<LaunchStep>()));
        Assert.False(GestureGate.ShouldWatch(true, new List<LaunchStep> { new LaunchStep { Enabled = false, Gesture = "D" } }));
        Assert.False(GestureGate.ShouldWatch(true, new List<LaunchStep> { new LaunchStep { Enabled = true, Gesture = "" } }));
    }

    // **洗不动的手势不该让钩子装上。**
    //
    // ConfigStore 故意不把 Normalize 返空的手势写回盘（那是「不合法」的判定，
    // 不是「应该变成空」的修正，写回去等于替用户删绑定），所以手改 json 里的 "R-D" 会原样存着。
    // ShouldWatch 曾经判原串，于是这种绑定会把右键钩子装上——而钩子拿规范形比对，
    // 它永远比不上。代价不是「不生效」而已：每一次右键按下先被吞再补发（晚 30ms，
    // 提权前台下直接被 UIPI 丢掉），换不来任何东西。
    [Theory]
    [InlineData("R-D", false)]      // 连字符：Normalize 整串拒收
    [InlineData("D	R", false)]     // 制表符同理
    [InlineData("XYZ", false)]      // 不是方向字符
    [InlineData("D", true)]         // 合法：该装
    [InlineData("DR", true)]
    public void An_unnormalizable_gesture_does_not_arm_the_hook(string raw, bool expected)
        => Assert.Equal(expected, GestureGate.ShouldWatch(true, new List<LaunchStep> { new() { Enabled = true, Gesture = raw } }));

    // 面板那半的判据：总开关 && 中键长按。中键长按勾着、但面板总开关关了，
    // 也不该装那半个钩子——否则「我不用面板」换不来「你别碰我的中键」，
    // 而后者正是用户勾这个开关时想要的东西。
    //
    // 断言的是 **LongPressGate.ShouldWatch**，不是把 `PanelEnabled && PanelMiddleLongPress` 重敲一遍。
    // 上一版就是重敲的，于是它什么都守不住：把 ApplyMouseHook 里那个判断删掉，它照旧绿。
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]    // 面板开着但没勾中键长按：热键还能用，钩子不装
    [InlineData(false, true, false)]    // 面板关了：中键长按那一勾说什么都不算
    [InlineData(false, false, false)]
    public void Middle_press_hook_needs_both_the_master_switch_and_the_checkbox(bool panelOn, bool middlePress, bool expected)
    {
        var s = new AppSettings { PanelEnabled = panelOn, PanelMiddleLongPress = middlePress };
        Assert.Equal(expected, LongPressGate.ShouldWatch(s.PanelEnabled, s.PanelMiddleLongPress));
    }

    // 两个都关 = 一个鼠标钩子都不装。这是这两个开关存在的全部意义，单独锁一条。
    [Fact]
    public void Both_off_means_no_mouse_hook_at_all()
    {
        var s = new AppSettings { PanelEnabled = false, PanelMiddleLongPress = true, GesturesEnabled = false };
        Assert.False(LongPressGate.ShouldWatch(s.PanelEnabled, s.PanelMiddleLongPress));
        Assert.False(GestureGate.ShouldWatch(s.GesturesEnabled, new List<LaunchStep> { OneGesture }));
    }
}
