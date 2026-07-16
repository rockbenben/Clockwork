using Clockwork.Core;
using Xunit;

public class StepHelpersTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(999, 999)]
    [InlineData(1000, 999)]
    [InlineData(50, 50)]
    public void ClampRepeat_bounds(int input, int expected)
        => Assert.Equal(expected, StepHelpers.ClampRepeat(input));

    [Theory]
    [InlineData(0, 8)]
    [InlineData(24, 8)]
    [InlineData(-1, 8)]
    [InlineData(8, 8)]
    [InlineData(23, 23)]
    [InlineData(1, 1)]
    public void BeforeHour_defaults_to_8_when_out_of_range(int raw, int expected)
        => Assert.Equal(expected, StepHelpers.BeforeHour(new LaunchStep { BeforeHour = raw }));

    [Theory]
    [InlineData(-1, 3, 3)]
    [InlineData(5, 3, 3)]
    [InlineData(0, 3, 1)]
    [InlineData(2, 3, 3)]
    [InlineData(1, 3, 2)]
    public void InsertPosition_matches_ps(int index, int count, int expected)
        => Assert.Equal(expected, StepHelpers.InsertPosition(index, count));

    [Theory]
    [InlineData(@"C:\Program Files\Notepad++\notepad++.exe", "notepad++")]
    [InlineData("msedge.exe", "msedge")]
    [InlineData("Weixin", "Weixin")]
    [InlineData(@"D:\a\foo.bar", "foo.bar")]
    public void ToProcessName_strips_dir_and_exe(string input, string expected)
        => Assert.Equal(expected, StepHelpers.ToProcessName(input));

    [Fact]
    public void Ellipsis_truncates() => Assert.Equal("abc…", StepHelpers.Ellipsis("abcdef", 3));

    [Fact]
    public void Ellipsis_keeps_short() => Assert.Equal("abc", StepHelpers.Ellipsis("abc", 3));

    [Fact]
    public void Ellipsis_does_not_split_surrogate_pair()
    {
        // "ab" + 😀(代理对，两个 UTF-16 码元) + "cd"。max=3 会切在代理对中间 → 应回退到 2，不产生半个 emoji。
        var s = "ab😀cd";
        var r = StepHelpers.Ellipsis(s, 3);
        Assert.Equal("ab…", r);
        Assert.DoesNotContain('\uD83D', r);   // 无落单的高位代理
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(30, 30)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    [InlineData(999999, 600)]
    public void ClampStartupDelay_bounds(int input, int expected)
        => Assert.Equal(expected, StepHelpers.ClampStartupDelay(input));
}
