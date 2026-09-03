using System.Linq;
using Clockwork.Core;
using Xunit;

// 这一轮新增的两个 Windows 操作：播放声音（系统命令）/ 回到刚才那个窗口（窗口动作）。
// 两个都没有新增步骤类型——它们进的是既有的两张下拉表，所以这里验的是那两张表的约定，
// 而不是执行本身（真去放一次声音、真去抢一次前台，单元测试都办不到）。
public class WindowsOpsTests
{
    // ── 播放声音 ──

    // 在系统命令下拉里，且下拉项的文案不是键名（少一条译文就会显示 "Sys_playSound"）。
    [Fact]
    public void Play_sound_is_in_the_system_command_dropdown()
    {
        var map = StepDisplay.SystemCommandMap();
        var item = Assert.Single(map.Where(kv => kv.Key == "playSound"));
        Assert.NotEqual("Sys_playSound", item.Value);
        Assert.False(string.IsNullOrWhiteSpace(item.Value));
    }

    // 括号里的补充必须挂在**句尾**：短标签靠 StripTrailingAside 把它剥掉，而面板格子只有 88 宽。
    // 剥不掉的话那一格显示的是「播放声音（提示音或 .wa...」——比只写「播放声音」更难认。
    // 日语和印地语的机翻正是把括号放到了句中，这条测试就是为它们留的。
    [Fact]
    public void The_play_sound_label_keeps_its_aside_at_the_end()
    {
        var full = StepDisplay.SystemCommandLabel("playSound");
        var shortened = StepDisplay.SystemCommandShortLabel("playSound");
        Assert.DoesNotContain("(", shortened);
        Assert.DoesNotContain("（", shortened);
        Assert.True(shortened.Length < full.Length, $"括号补充没被剥掉：{full}");
    }

    // 参数留空是**常态**（= 系统提示音），摘要就该只写命令本身。
    // 拼成「播放声音：」后面跟一片空白，读起来像是配漏了一个必填项。
    [Fact]
    public void An_empty_sound_path_summarises_as_just_the_command()
    {
        var s = StepDisplay.StepSummary(new LaunchStep { Kind = "system", Command = "playSound" });
        Assert.Equal(StepDisplay.SystemCommandShortLabel("playSound"), s);
    }

    // 填了路径就要看得见是哪个文件——三条「播放声音」摆在清单上，不写文件名就分不出来。
    [Fact]
    public void A_sound_path_shows_up_in_the_summary()
        => Assert.Contains("ding.wav", StepDisplay.StepSummary(
            new LaunchStep { Kind = "system", Command = "playSound", Target = "", Text = @"D:\snd\ding.wav" }));

    // 它得吃那个文本参数，否则编辑器里那一行根本不出现，路径无处可填。
    [Fact]
    public void Play_sound_takes_a_text_argument()
    {
        Assert.True(StepDisplay.SystemCommandTakesText("playSound"));
        Assert.False(StepDisplay.SystemCommandTakesLevel("playSound"));
    }

    // ── 回到刚才那个窗口 ──

    [Fact]
    public void Restore_is_in_the_window_action_dropdown()
    {
        var item = Assert.Single(StepDisplay.WindowActionMap().Where(kv => kv.Key == "restore"));
        Assert.NotEqual("Win_restore", item.Value);
        Assert.False(string.IsNullOrWhiteSpace(item.Value));
    }

    // **这条是承重的**：它是这张表里唯一不选目标的动作，编辑器据此把进程名那几行整片收起来。
    // 判反了的表现是「一个填了也不生效的进程名框」——用户填完发现没用，却查不出为什么。
    [Fact]
    public void Only_restore_needs_no_target()
    {
        Assert.False(StepDisplay.WindowActionNeedsTarget("restore"));
        foreach (var (action, _) in StepDisplay.WindowActions)
            if (action != "restore")
                Assert.True(StepDisplay.WindowActionNeedsTarget(action), $"{action} 应该要选目标");
    }

    // 摘要不拼目标：拼出来是「回到刚才那个窗口 （未设置）」，那个括号会让人以为自己漏配了。
    [Fact]
    public void The_restore_summary_carries_no_target()
    {
        var s = StepDisplay.StepSummary(new LaunchStep { Kind = "window", Action = "restore" });
        Assert.Equal(StepDisplay.WindowActionMap().First(kv => kv.Key == "restore").Value, s);
    }

    // 进程名被手改 json 填上了也一样不显示——它对这个动作没有意义，显示出来就是在撒谎。
    [Fact]
    public void A_stray_process_name_does_not_leak_into_the_restore_summary()
    {
        var s = StepDisplay.StepSummary(new LaunchStep { Kind = "window", Action = "restore", Process = "Code" });
        Assert.DoesNotContain("Code", s);
    }
}
