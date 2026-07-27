using Clockwork.Core;
using Xunit;

public class StopHintTests
{
    [Fact]
    public void Appends_hotkey_in_parens()
        => Assert.Equal("停止正在运行的动作 (Ctrl+Alt+Q)", StopHint.Compose("停止正在运行的动作", "Ctrl+Alt+Q"));

    [Fact]  // 热键被清空 → 只剩说明，不留一对空括号
    public void Empty_hotkey_leaves_label_alone()
        => Assert.Equal("Stop Running Actions", StopHint.Compose("Stop Running Actions", ""));

    [Fact] public void Null_hotkey_leaves_label_alone() => Assert.Equal("Stop", StopHint.Compose("Stop", null));

    [Fact] public void Whitespace_hotkey_leaves_label_alone() => Assert.Equal("Stop", StopHint.Compose("Stop", "   "));
}
