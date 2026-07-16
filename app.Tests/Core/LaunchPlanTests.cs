using Clockwork.Core;
using Xunit;

public class LaunchPlanTests
{
    [Fact]
    public void Disabled_steps_excluded()
    {
        var c = new RootConfig
        {
            LaunchSteps = new()
            {
                new LaunchStep { Kind = "app", Enabled = true, Label = "a" },
                new LaunchStep { Kind = "app", Enabled = false, Label = "b" },
            }
        };
        var plan = LaunchPlan.Build(c, 12, 3);
        Assert.Single(plan);
        Assert.Equal("a", plan[0].Label);
    }

    [Fact]
    public void Condition_filters_by_hour_and_day()
    {
        var c = new RootConfig
        {
            LaunchSteps = new()
            {
                new LaunchStep { Kind = "volume", Enabled = true, OnlyBefore8 = true, Label = "mute" },
                new LaunchStep { Kind = "app", Enabled = true, Days = new() { 6, 7 }, Label = "weekend" },
            }
        };
        var plan = LaunchPlan.Build(c, 9, 3);   // 9点、周三
        Assert.Empty(plan);                     // mute 被 8点前挡，weekend 非周末
        var plan2 = LaunchPlan.Build(c, 7, 6);  // 7点、周六
        Assert.Equal(2, plan2.Count);
    }

    [Fact]
    public void Preserves_order()
    {
        var c = new RootConfig
        {
            LaunchSteps = new()
            {
                new LaunchStep { Kind = "app", Label = "1" },
                new LaunchStep { Kind = "app", Label = "2" },
                new LaunchStep { Kind = "app", Label = "3" },
            }
        };
        Assert.Equal(new[] { "1", "2", "3" }, LaunchPlan.Build(c, 12, 3).ConvertAll(s => s.Label).ToArray());
    }
}
