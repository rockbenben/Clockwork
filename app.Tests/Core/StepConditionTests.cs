using Clockwork.Core;
using Xunit;

public class StepConditionTests
{
    [Fact]
    public void No_constraints_always_true()
        => Assert.True(StepCondition.IsSatisfied(new LaunchStep(), 23, 3));

    [Fact]
    public void OnlyBefore8_blocks_after_hour()
    {
        var s = new LaunchStep { OnlyBefore8 = true }; // BeforeHour 默认 8
        Assert.True(StepCondition.IsSatisfied(s, 7, 3));
        Assert.False(StepCondition.IsSatisfied(s, 8, 3));
        Assert.False(StepCondition.IsSatisfied(s, 9, 3));
    }

    [Fact]
    public void OnlyBefore8_respects_custom_beforeHour()
    {
        var s = new LaunchStep { OnlyBefore8 = true, BeforeHour = 10 };
        Assert.True(StepCondition.IsSatisfied(s, 9, 3));
        Assert.False(StepCondition.IsSatisfied(s, 10, 3));
    }

    [Fact]
    public void Days_filter_matches_iso_day()
    {
        var s = new LaunchStep { Days = new() { 1, 2, 3, 4, 5 } };
        Assert.True(StepCondition.IsSatisfied(s, 12, 5));   // 周五
        Assert.False(StepCondition.IsSatisfied(s, 12, 6));  // 周六
    }

    [Fact]
    public void Empty_days_means_every_day()
        => Assert.True(StepCondition.IsSatisfied(new LaunchStep { Days = new() }, 12, 7));

    [Theory]
    [InlineData(2026, 7, 13, 1)]  // 周一
    [InlineData(2026, 7, 19, 7)]  // 周日
    public void IsoDayOfWeek_maps_sunday_to_7(int y, int m, int d, int iso)
        => Assert.Equal(iso, StepCondition.IsoDayOfWeek(new DateTime(y, m, d)));
}
