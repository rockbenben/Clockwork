using Clockwork.Engine;
using Xunit;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.1.0", "1.0.9", 1)]     // 次版本压过修订
    [InlineData("2.0", "1.9.9", 1)]
    [InlineData("1.0.0", "1.0", 0)]        // 缺段按 0
    [InlineData("1.0.0.0", "1.0.0", 0)]
    [InlineData("1.2.0-beta", "1.2.0", 0)] // 预发布后缀按数字段忽略
    public void CompareVersions_orders_correctly(string a, string b, int expectedSign)
        => Assert.Equal(expectedSign, Math.Sign(UpdateChecker.CompareVersions(a, b)));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    [InlineData(" 1.0.0 ", "1.0.0")]
    [InlineData("", "")]
    public void NormalizeVersion_strips_leading_v(string tag, string expected)
        => Assert.Equal(expected, UpdateChecker.NormalizeVersion(tag));
}
