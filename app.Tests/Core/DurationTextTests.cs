using Clockwork.Core;
using Xunit;

public class DurationTextTests
{
    [Theory]
    [InlineData("9:00", "09:00")]
    [InlineData("09:00", "09:00")]
    [InlineData("23:30", "23:30")]
    [InlineData("", "")]
    [InlineData("garbage", "garbage")]
    public void FormatTimeHHmm(string text, string expected)
        => Assert.Equal(expected, DurationText.FormatTimeHHmm(text));
}
