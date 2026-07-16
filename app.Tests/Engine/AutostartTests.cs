using Clockwork.Engine;
using Xunit;

public class AutostartTests
{
    [Fact]
    public void TaskName_is_clockwork() => Assert.Equal("Clockwork", Autostart.TaskName);

    [Fact]
    public void IsRegistered_does_not_throw()
    {
        var _ = Autostart.IsRegistered();   // 真机查询：不抛（已注册与否都返回 bool）
    }
}
