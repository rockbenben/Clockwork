using Clockwork.Core;
using Xunit;

// 格子/手势行的标题：**「当前窗口」那一档不能显示成一个星号。**
//
// StepDisplay.CurrentWindowMark 是 "*"，存在 Process 字段里当哨兵。TileBase 对窗口步骤
// 有一条快路「有进程名就只显示进程名」（理由是每格的动作词都一样、白占宽度），
// 而那条快路曾经把哨兵值也当成进程名原样返回。
//
// 出厂默认手势躲过了这件事，只因为 RootConfig.DefaultGestures() 显式给每条赋了 Label；
// 从面板 / 手势管理器里新加一个「常用 → 最小化当前窗口」不会自动填 Label
// （编辑器的 window 分支不像 keys / system 那样代填），那时标题就现形了。
public class StepDisplayTitleTests
{
    private static LaunchStep CurrentWindow(string action)
        => new() { Kind = "window", Action = action, Process = StepDisplay.CurrentWindowMark };

    [Theory]
    [InlineData("minimize")]
    [InlineData("close")]
    [InlineData("maximize")]
    [InlineData("topmost")]
    public void A_current_window_tile_is_not_just_the_sentinel(string action)
    {
        var title = StepDisplay.StepTitle(CurrentWindow(action));
        Assert.NotEqual(StepDisplay.CurrentWindowMark, title);
        Assert.DoesNotContain(StepDisplay.CurrentWindowMark, title);
        Assert.False(string.IsNullOrWhiteSpace(title));
    }

    // 具名进程那条快路照旧：标题就是进程名（这才是它存在的理由）。
    [Fact]
    public void A_named_process_tile_still_shows_just_the_process()
        => Assert.Equal("slack.exe",
            StepDisplay.StepTitle(new LaunchStep { Kind = "window", Action = "close", Process = "slack.exe" }));

    // 用户起了名字就永远优先，两条快路都不该盖掉它。
    [Fact]
    public void A_user_label_always_wins()
    {
        var s = CurrentWindow("minimize");
        s.Label = "收起来";
        Assert.Equal("收起来", StepDisplay.StepTitle(s));
    }
}
