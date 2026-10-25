using Clockwork.Core;
using Xunit;

public class MessageAreaSizingTests
{
    [Theory]
    [InlineData(0, MessageAreaSizing.Fallback)]
    [InlineData(-1, MessageAreaSizing.Fallback)]
    [InlineData(double.NaN, MessageAreaSizing.Fallback)]
    [InlineData(200, MessageAreaSizing.MinHeight)]
    [InlineData(344, 120.4)]
    [InlineData(464, 162.4)]
    [InlineData(1380, MessageAreaSizing.MaxHeight)]
    [InlineData(4000, MessageAreaSizing.MaxHeight)]
    public void ForWorkArea_clamps_to_the_shared_bounds(double workArea, double expected)
        => Assert.Equal(expected, MessageAreaSizing.ForWorkArea(workArea), 3);

    [Fact]
    public void Fallback_is_within_the_bounds()
    {
        Assert.InRange(MessageAreaSizing.Fallback, MessageAreaSizing.MinHeight, MessageAreaSizing.MaxHeight);
    }
}
