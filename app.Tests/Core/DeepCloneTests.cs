using Clockwork.Core;
using Xunit;

// 深拷贝是「取消不留下半截改动」那条纪律的地基：站点表、模板管理器、动作编辑器
// 都在它上面建工作副本。它要是悄悄丢字段，那三处的「取消」就变成了「静默清空」。
public class DeepCloneTests
{
    [Fact]
    public void A_site_survives_the_round_trip_with_every_field()
    {
        var site = new SiteEntry
        {
            Name = "LearnData",
            Hosts = new() { "newzone.top", "www.newzone.top", "localhost:8080" },
            SourceRoot = @"D:\Projects\LearnData\src",
            IndexFile = "index.md",
            PathRules = new() { new SitePathRule { Match = "^posts/", Replace = "_posts/" } },
        };

        var copy = Assert.Single(ConfigStore.DeepClone(new List<SiteEntry> { site }));

        Assert.Equal("LearnData", copy.Name);
        Assert.Equal(new[] { "newzone.top", "www.newzone.top", "localhost:8080" }, copy.Hosts);
        Assert.Equal(@"D:\Projects\LearnData\src", copy.SourceRoot);
        Assert.Equal("index.md", copy.IndexFile);
        var rule = Assert.Single(copy.PathRules);
        Assert.Equal("^posts/", rule.Match);
        Assert.Equal("_posts/", rule.Replace);
    }

    // 列表长度 0 与「根本没这个键」是两回事：站点表里没有规则 vs 有一条空规则，
    // 序列化瘦身的判据（与新鲜实例比较）最容易在这两个之间滑过去。
    [Fact]
    public void An_empty_list_is_not_the_same_as_a_missing_one()
    {
        var copy = Assert.Single(ConfigStore.DeepClone(new List<SiteEntry> { new SiteEntry { Name = "x" } }));
        Assert.Empty(copy.Hosts);
        Assert.Empty(copy.PathRules);
    }

    [Fact]
    public void A_group_and_its_steps_survive()
    {
        var g = new ActionGroup
        {
            Name = "逐行输入",
            Icon = "E700",
            Steps = new()
            {
                new LaunchStep { Kind = "list", OutputVar = "行" },
                new LaunchStep { Kind = "each", Text = "行" },
            },
        };
        var copy = Assert.Single(ConfigStore.DeepClone(new List<ActionGroup> { g }));
        Assert.Equal("逐行输入", copy.Name);
        Assert.Equal("E700", copy.Icon);
        Assert.Equal(2, copy.Steps.Count);
        Assert.Equal("行", copy.Steps[0].OutputVar);
        Assert.Equal("each", copy.Steps[1].Kind);
    }
}
