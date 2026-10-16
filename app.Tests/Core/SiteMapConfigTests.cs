using System.IO;
using System.Text.Json;
using Clockwork.Core;
using Xunit;

// 「站点映射表」进配置之后的两件事：
//   1. 存盘 → 读回，它原样还在（JsonOptions 用的是与现有键同一套命名策略）；
//   2. 读回时手改出来的 null 元素被洗掉，不让下游 NPE。
//
// 第 1 条尤其不能省：这张表是**手改**的（它就在 clockwork.settings.json 里），
// 而 ConfigStore 用的 JsonOptions 与既有键共用一套策略。键名一旦对不上，
// 用户填的整张表在下次启动时静静变成空数组 —— 没有报错、没有提示、没有备份。

public class SiteMapConfigTests
{
    private static JsonSerializerOptions Opts()
    {
        // 与 ConfigStore 同一套命名策略：camelCase、不忽略默认值。
        var o = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return o;
    }

    [Fact]
    public void Sites_survive_a_write_read_round_trip()
    {
        var cfg = new RootConfig
        {
            Sites =
            {
                new SiteEntry
                {
                    Name = "LearnData",
                    Hosts = { "newzone.top", "localhost:8080" },
                    SourceRoot = @"D:\blog\src",
                    IndexFile = "README.md",
                    PathRules = { new SitePathRule { Match = "^posts/", Replace = "_posts/" } },
                },
            },
        };

        var back = JsonSerializer.Deserialize<RootConfig>(JsonSerializer.Serialize(cfg, Opts()), Opts())!;

        var s = Assert.Single(back.Sites);
        Assert.Equal("LearnData", s.Name);
        Assert.Equal(new[] { "newzone.top", "localhost:8080" }, s.Hosts);
        Assert.Equal(@"D:\blog\src", s.SourceRoot);
        Assert.Equal("README.md", s.IndexFile);
        var rule = Assert.Single(s.PathRules);
        Assert.Equal("^posts/", rule.Match);
        Assert.Equal("_posts/", rule.Replace);
        Assert.True(rule.On);          // 没写 enabled = 生效，与 bool? 的设计一致
    }

    // enabled:false 是「想清楚再开」的那一档，存读之后必须仍然是关的——
    // 变成 null（=生效）的话，用户以为还关着，实际规则已经在改路径了。
    [Fact]
    public void A_disabled_rule_stays_disabled_across_a_round_trip()
    {
        var cfg = new RootConfig
        {
            Sites =
            {
                new SiteEntry
                {
                    Name = "s", Hosts = { "a.com" }, SourceRoot = @"C:\x",
                    PathRules = { new SitePathRule { Match = "^a/", Replace = "b/", Enabled = false } },
                },
            },
        };
        var back = JsonSerializer.Deserialize<RootConfig>(JsonSerializer.Serialize(cfg, Opts()), Opts())!;
        Assert.False(back.Sites[0].PathRules[0].On);
    }

    // 手改 json 写出的 "sites":[null] 与各字段的 null，Normalize 要洗掉：
    // 这张表是手改的，而 NullReferenceException 会在用户启动那一刻炸出来。
    [Fact]
    public void Hand_edited_nulls_are_washed_into_a_usable_shape()
    {
        var cfg = JsonSerializer.Deserialize<RootConfig>(
            """
            {
              "sites": [ null, {
                "name": null, "hosts": [ "a.com", null ], "sourceRoot": null,
                "indexFile": null,
                "pathRules": [ null, { "match": null, "replace": null } ]
              } ]
            }
            """, Opts())!;

        ConfigStore.Normalize(cfg);

        var s = Assert.Single(cfg.Sites);
        Assert.Equal("", s.Name);
        Assert.Equal("", s.SourceRoot);
        Assert.Equal("", s.IndexFile);
        Assert.Equal(new[] { "a.com" }, s.Hosts);
        Assert.Equal("", s.PathRules[0].Match);
        Assert.Equal("", s.PathRules[0].Replace);
    }

    // 规范化**不得**替用户改写他写下的内容：域名大小写、路径末尾的反斜杠，
    // 原样留着。启动时静默"修好"一份他没写过的东西，比不修更难排查。
    [Fact]
    public void Normalize_does_not_rewrite_what_the_user_typed()
    {
        var cfg = new RootConfig
        {
            Sites =
            {
                new SiteEntry
                {
                    Name = "  保留原样  ", Hosts = { "  NewZone.TOP  " },
                    SourceRoot = @"D:\blog\src\", IndexFile = "  README.md  ",
                    PathRules = { new SitePathRule { Match = "  ^posts/  ", Replace = "  _posts/  " } },
                },
            },
        };

        ConfigStore.Normalize(cfg);

        var s = cfg.Sites[0];
        Assert.Equal("  保留原样  ", s.Name);
        Assert.Equal("  NewZone.TOP  ", s.Hosts[0]);
        Assert.Equal(@"D:\blog\src\", s.SourceRoot);
        Assert.Equal("  README.md  ", s.IndexFile);
        Assert.Equal("  ^posts/  ", s.PathRules[0].Match);
    }

    // 两种「表不存在」：键**缺失**（属性初始化器已经给了空表）与显式写 null
    // （"sites": null 会覆盖掉初始化器，那个 null 一路传到 Normalize 才被补回）。
    [Theory]
    [InlineData("""{ "launchSteps": [] }""")]                  // 键缺失
    [InlineData("""{ "launchSteps": [], "sites": null }""")]   // 显式 null
    public void A_config_without_a_usable_sites_table_ends_up_empty(string json)
    {
        var cfg = JsonSerializer.Deserialize<RootConfig>(json, Opts())!;
        ConfigStore.Normalize(cfg);
        Assert.NotNull(cfg.Sites);
        Assert.Empty(cfg.Sites);
    }
}
