using Clockwork.Core;
using Xunit;

// Win32.SendMouseButton 按 MouseButton 的**数值**分派（见那里的注释）：这是本轮唯一的隐式耦合，
// 枚举重排一次就会静默错位——右键点成中键、后退点成前进，而且编译和现有测试全都不会报。
// 这里把数值钉死；改动枚举顺序必然让这条先红。
public class Win32MouseButtonMappingTests
{
    [Theory]
    [InlineData(MouseButton.None, 0)]
    [InlineData(MouseButton.Left, 1)]
    [InlineData(MouseButton.Right, 2)]
    [InlineData(MouseButton.Middle, 3)]
    [InlineData(MouseButton.Back, 4)]
    [InlineData(MouseButton.Forward, 5)]
    public void Enum_values_match_the_dispatch_numbers(MouseButton button, int expected)
        => Assert.Equal(expected, (int)button);
}
