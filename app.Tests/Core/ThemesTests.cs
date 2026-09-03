using Clockwork.Core;
using Xunit;

// 主题的取值与解析。配置文件是能手改的，所以「写了个不认识的值会怎样」必须有确定答案。
public class ThemesTests
{
    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("light", "light")]
    [InlineData("system", "system")]
    public void Keeps_the_valid_ones(string input, string expected)
        => Assert.Equal(expected, Themes.Normalize(input));

    // 认不出来就回到深色——这个程序的本色是深色，拿不准时回到本色，
    // 而不是把界面翻成白的吓人一跳。
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Dark")]      // 大小写不同也算不认识：取值表是内部约定，不做模糊匹配
    [InlineData("solarized")]
    public void Falls_back_to_dark(string? input)
        => Assert.Equal("dark", Themes.Normalize(input));

    [Fact]
    public void Dark_and_light_resolve_without_touching_the_registry()
    {
        Assert.True(Themes.IsDark("dark", systemPrefersLight: false));
        Assert.True(Themes.IsDark("dark", systemPrefersLight: true));    // 固定档不看系统
        Assert.False(Themes.IsDark("light", systemPrefersLight: false));
        Assert.False(Themes.IsDark("light", systemPrefersLight: true));
    }

    // 「跟随系统」现在可以真断言了：系统偏好是传进去的，不再由它自己去问注册表。
    //
    // 上一版写的是 `Assert.Equal(!Themes.SystemPrefersLight(), Themes.IsDark("system"))`，
    // 理由是「答案取决于这台机器，只能断言两边一致」——而那正是问题：
    // 把 IsDark 里那个取反删掉，等式两边一起翻，用例照旧绿。一条什么都守不住的断言。
    // 把注册表那一问搬到 Native.SystemTheme 之后，这里就能把两个方向各钉一次。
    [Theory]
    [InlineData(true, false)]    // 系统偏浅 → 浅色
    [InlineData(false, true)]    // 系统偏深（或读不到）→ 深色
    public void System_follows_windows(bool systemPrefersLight, bool expectDark)
        => Assert.Equal(expectDark, Themes.IsDark("system", systemPrefersLight));

    [Fact]
    public void The_default_setting_is_dark()
        => Assert.Equal("dark", Themes.Normalize(new AppSettings().Theme));
}
