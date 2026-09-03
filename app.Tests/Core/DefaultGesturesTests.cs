using System.Linq;
using Clockwork.Core;
using Xunit;

// 首份配置里的示例手势。
//
// **默认是启用的**，与示例清单、示例提醒相反（那两处不启用）。三样东西的性质不同：
// 清单会去开程序、提醒会弹窗打断，都是「替你动了电脑」；而手势只在你主动画出一条轨迹时
// 才发生，画之前它什么也不做。装完得先逐条勾选才有反应的话，多数人翻不到那一屏。
//
// 代价是右键在那期间被接管（右键按住拖放用不了），出口是总开关 Settings.GesturesEnabled。
// 这两条在文档里并排写着——钉在这儿是因为「样例的启用状态」是个会被顺手改掉的值。
public class DefaultGesturesTests
{
    [Fact]
    public void Samples_ship_enabled()
    {
        var g = RootConfig.Default().Gestures;
        Assert.NotEmpty(g);
        Assert.All(g, s => Assert.True(s.Enabled));
    }

    // 启用的样例意味着装完就接管右键，所以那个出口必须真的管用。
    //
    // 这条一度写成「赋值 false 再读回 false」——那测的是自动属性，测的是编译器，
    // 任何实现都过得了。真正让出口成立的是「关掉之后一条钩子都不装」，
    // 那句判据现在在 GestureGate.ShouldWatch 里（App.ApplyMouseHook 用的就是它）。
    [Fact]
    public void The_escape_hatch_actually_stops_the_hook()
    {
        var cfg = RootConfig.Default();
        Assert.True(GestureGate.ShouldWatch(cfg.Settings.GesturesEnabled, cfg.Gestures));   // 装完就监听
        Assert.False(GestureGate.ShouldWatch(false, cfg.Gestures));                          // 总开关一关，不监听
    }

    // 反过来也得成立：总开关开着但一条启用的手势都没有时，同样不监听——
    // 「没配手势时右键完全不受影响」是文档里承诺过的。
    [Fact]
    public void With_nothing_enabled_the_right_button_is_untouched()
    {
        var cfg = RootConfig.Default();
        foreach (var g in cfg.Gestures) g.Enabled = false;
        Assert.False(GestureGate.ShouldWatch(true, cfg.Gestures));
        Assert.False(GestureGate.ShouldWatch(true, new List<LaunchStep>()));
        Assert.False(GestureGate.ShouldWatch(true, null));
    }

    // 启用了但笔迹是空的，不算数：那样的一条什么也匹配不上，却会白装一个全局钩子。
    [Fact]
    public void An_enabled_gesture_without_a_stroke_does_not_arm_the_hook()
        => Assert.False(GestureGate.ShouldWatch(true, new List<LaunchStep>
        {
            new() { Kind = "keys", Combo = "Ctrl+C", Enabled = true, Gesture = "" },
        }));

    // 每一条都得真有一条笔迹，而且笔迹要过量化器那一关——存一条画不出来的样例，
    // 用户照着改的时候会以为是自己画得不对。
    [Fact]
    public void Every_sample_has_a_usable_stroke()
    {
        foreach (var s in RootConfig.Default().Gestures)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Gesture));
            Assert.Equal(s.Gesture, GestureGate.Normalize(s.Gesture));
        }
    }

    // 笔迹不能重复：两条一样的笔迹，第二条永远轮不到（RunByGesture 取的是第一条匹配）。
    [Fact]
    public void No_two_samples_share_a_stroke()
    {
        var paths = RootConfig.Default().Gestures.Select(s => s.Gesture).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    // 每一条都得有名字：列表、气泡回执、读屏念的都是它。
    [Fact]
    public void Every_sample_is_named()
        => Assert.All(RootConfig.Default().Gestures, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));

    // 总开关默认开着：老配置里没有这个键，反序列化落到初始值上，行为得与从前一致。
    [Fact]
    public void The_master_switch_defaults_to_on()
    {
        Assert.True(new AppSettings().GesturesEnabled);
        Assert.True(RootConfig.Default().Settings.GesturesEnabled);
    }

    // 这一组照的是同类工具通行的那套默认动作（用户给的参照），外加 ← → 补成后退 / 前进。
    // 钉住笔迹与动作的对应：轴向四条给最常用的四件事、**四个斜角凑齐窗口的四件事**、
    // 两条两笔的给搜索与到底。改动它得是有意的——这张表同时是文档里那张表的唯一真相。
    [Fact]
    public void The_sample_set_is_the_one_we_documented()
    {
        var byPath = RootConfig.Default().Gestures.ToDictionary(s => s.Gesture);
        Assert.Equal(10, byPath.Count);

        Assert.Equal("Ctrl+C", byPath["U"].Combo);       // ↑ 复制
        Assert.Equal("Ctrl+V", byPath["D"].Combo);       // ↓ 粘贴
        Assert.Equal("Alt+Left", byPath["L"].Combo);     // ← 后退
        Assert.Equal("Alt+Right", byPath["R"].Combo);    // → 前进
        Assert.Equal("Ctrl+End", byPath["RD"].Combo);    // →↓ 到最底部

        // 四个角 = 窗口四件事，方向本身就是意思：往左下收、往右上撑、往左上钉、往右下扫走。
        Assert.Equal("minimize", byPath["1"].Action);    // ↙ 最小化
        Assert.Equal("maximize", byPath["9"].Action);    // ↗ 最大化
        Assert.Equal("topmost", byPath["7"].Action);     // ↖ 置顶（开关：同一条手势管钉住和放开）
        // ↘ 关闭。放在斜角上是有意的：斜角扇区只有 34°，画不准就不会中——
        // 一个会关掉窗口的动作，「不容易误画」正是它该有的性质。
        Assert.Equal("close", byPath["3"].Action);
        // ↑↓ 而不是 ∧（↗↘）：人画尖角自然是 65–80°，那就是 ↑↓ 不是 ↗↘（见 DefaultGestures 的注释）。
        // 选 ↑↓ 而不是 ↓↑ 是因为前缀落空时跑「复制」无害，跑「粘贴」会改掉内容。
        Assert.Equal("searchSelection", byPath["UD"].Command);   // ↑↓ 搜索选中的文字
    }

    // 两条窗口动作认的是「当前窗口」那个标记，不是空进程名——空的话
    // WindowManager.Handles("") 会匹配到一堆真实窗口（实测 19 个），最小化的不知是谁。
    [Fact]
    public void The_window_samples_target_the_current_window()
    {
        foreach (var g in RootConfig.Default().Gestures.Where(s => s.Kind == "window"))
            Assert.True(StepDisplay.IsCurrentWindow(g), $"{g.Gesture} 没指向当前窗口");
    }

    // 每一条组合键都得真发得出去——存一条按不出去的样例，用户会以为是自己画错了。
    // CanEncodeForSendKeys 是真实发送那条路自己的判据，不是另写一套。
    [Fact]
    public void Every_key_sample_can_actually_be_sent()
    {
        foreach (var g in RootConfig.Default().Gestures.Where(s => s.Kind == "keys"))
        {
            Assert.False(KeyCombo.HasUnknownModifier(g.Combo), $"{g.Gesture}：{g.Combo} 里有认不出的修饰键");
            Assert.True(KeyCombo.CanEncodeForSendKeys(g.Combo), $"{g.Gesture}：{g.Combo} 发不出去");
        }
    }
}
