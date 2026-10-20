using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 一次性迁移：把共用的 Action / Level 拆到各类型自己的格子里。
//
// 这一趟动的是**每一个用户的配置**，所以它必须做到三件事：
//   · 搬过去，一字不差（音量的 30% 迁完还是 30%，不能变成 0）；
//   · 搬完不留在盘上（旧的两格清空，于是盘上只有一种形状，没有两份真相）；
//   · 没得搬的时候**什么都不动**（一份本来就是新形状的配置不该被写回盘）。
//
// 判据是**字段本身**（旧的两格清成 null 就是「迁过了」），不是版本号——
// 版本号只记「这一版迁过没有」，而手改配置随时可能加进一条带旧字段的步骤。
public class StepParameterMigrationTests
{
    private static RootConfig Config(params LaunchStep[] steps)
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 4 } };
        cfg.LaunchSteps.AddRange(steps);
        return cfg;
    }

    // 音量：动作与音量值各归各的。
    [Fact]
    public void A_volume_step_moves_its_action_and_its_level()
    {
        var cfg = Config(new LaunchStep { Kind = "volume", Action = "set", Level = 30 });
        ConfigStore.Normalize(cfg);
        var s = cfg.LaunchSteps[0];
        Assert.Equal("set", s.VolumeAction);
        Assert.Equal(30, s.VolumeLevel);
    }

    // 其余四个用 Action 的类型，各认领各的。
    // **只认领自己那一格**：别的类型的格子必须还是空的——
    // 给不相干的类型塞一个值，比不搬更糟（那个值从此就在配置里躺着）。
    [Theory]
    [InlineData("window", "close")]
    [InlineData("mouse", "rightClick")]
    [InlineData("skip", "run")]
    [InlineData("siteMap", "reveal")]
    public void The_action_owners_each_take_their_own(string kind, string action)
    {
        var cfg = Config(new LaunchStep { Kind = kind, Action = action });
        ConfigStore.Normalize(cfg);
        var s = cfg.LaunchSteps[0];
        var got = new Dictionary<string, string>
        {
            ["window"] = s.WindowAction, ["mouse"] = s.MouseAction,
            ["skip"] = s.SkipMode, ["siteMap"] = s.SiteMapMode,
        };
        Assert.Equal(action, got[kind]);                 // 自己那一格拿到了它
        Assert.Equal(1, got.Values.Count(x => x.Length > 0));   // 而**只有**这一格
    }

    // 其余五个用 Level 的类型，各认领各的（量纲各不相同）。
    [Theory]
    [InlineData("waitClipboard")]
    [InlineData("waitKey")]
    [InlineData("regex")]
    public void The_level_owners_each_take_their_own(string kind)
    {
        var cfg = Config(new LaunchStep { Kind = kind, Level = 12 });
        ConfigStore.Normalize(cfg);
        var s = cfg.LaunchSteps[0];
        var got = new Dictionary<string, int>
        {
            ["waitClipboard"] = s.WaitSeconds, ["waitKey"] = s.WaitKeySeconds, ["regex"] = s.RegexGroup,
        };
        Assert.Equal(12, got[kind]);
        Assert.Equal(1, got.Values.Count(x => x != 50));   // 而**只有**这一格（50 = 没动过）
    }

    // 系统命令里只有「亮度」用那一格；别的系统命令那一格没有意义，不搬。
    [Fact]
    public void Only_the_brightness_command_takes_the_level()
    {
        var cfg = Config(
            new LaunchStep { Kind = "system", Command = "brightness", Level = 80 },
            new LaunchStep { Kind = "system", Command = "lockScreen", Level = 80 });
        ConfigStore.Normalize(cfg);
        Assert.Equal(80, cfg.LaunchSteps[0].Brightness);
        Assert.Equal(50, cfg.LaunchSteps[1].Brightness);   // 那一格对它没有意义
    }

    // **「没写」与「写了 0」必须分得开。** 旧 Level 的默认值是 50，
    // 于是一条没写 level 的老配置（读进来是 50）和一条明写 "level": 0 的（音量 0%）
    // 在旧模型里分不开——而迁移必须按前者走 50、按后者走 0。
    // 这正是旧字段改成可空的原因。
    [Fact]
    public void An_absent_level_and_an_explicit_zero_are_different()
    {
        var cfg = Config(new LaunchStep { Kind = "volume", Action = "set" });   // 没写 level → 旧默认 50
        ConfigStore.Normalize(cfg);
        Assert.Equal(50, cfg.LaunchSteps[0].VolumeLevel);

        var cfg2 = Config(new LaunchStep { Kind = "volume", Action = "set", Level = 0 });
        ConfigStore.Normalize(cfg2);
        Assert.Equal(0, cfg2.LaunchSteps[0].VolumeLevel);
    }

    // 搬完旧的两格要**清空**：它们于是不会落盘，盘上从此只有一种形状。
    // 留着它们就是两份真相——用户改了 volumeLevel 却没看 action 那一格，改了也不生效。
    [Fact]
    public void The_old_fields_are_cleared_so_they_never_reach_the_disk()
    {
        var cfg = Config(new LaunchStep { Kind = "volume", Action = "set", Level = 30 });
        ConfigStore.Normalize(cfg);
        Assert.Null(cfg.LaunchSteps[0].Action);
        Assert.Null(cfg.LaunchSteps[0].Level);
    }

    // 迁完写回盘，json 里不该再有旧的两格。
    [Fact]
    public void The_written_config_has_no_legacy_fields_left()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw_mig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "c.json");
            var cfg = Config(new LaunchStep { Kind = "volume", Action = "set", Level = 30 });
            ConfigStore.Normalize(cfg);
            ConfigStore.Write(cfg, path);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("\"action\"", text);
            Assert.DoesNotContain("\"level\"", text);
            Assert.Contains("\"volumeAction\"", text);
            Assert.Contains("\"volumeLevel\"", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // 四份清单都要走一遍：漏一份，那一份里的步骤就永远停在旧形状，
    // 而症状是「有的步骤换了类型会串味、有的不会」，最难查的那一种。
    [Fact]
    public void All_four_lists_are_migrated()
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 4 } };
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "volume", Action = "mute" });
        cfg.ActionGroups.Add(new ActionGroup { Name = "g", Steps = new List<LaunchStep> { new() { Kind = "volume", Action = "unmute" } } });
        cfg.PanelPages.Add(new PanelPage { Name = "p", Steps = new List<LaunchStep> { new() { Kind = "volume", Action = "micMute" } } });
        cfg.Gestures.Add(new LaunchStep { Kind = "volume", Action = "micUnmute" });
        ConfigStore.Normalize(cfg);
        Assert.Equal("mute", cfg.LaunchSteps[0].VolumeAction);
        Assert.Equal("unmute", cfg.ActionGroups[0].Steps[0].VolumeAction);
        Assert.Equal("micMute", cfg.PanelPages[0].Steps[0].VolumeAction);
        Assert.Equal("micUnmute", cfg.Gestures[0].VolumeAction);
    }

    // 一份**本来就没得迁**的配置不该被写回盘（那是一次无谓的全量重写），版本号也不该动。
    [Fact]
    public void A_config_with_nothing_to_migrate_is_left_alone()
    {
        var cfg = Config(new LaunchStep { Kind = "volume", VolumeAction = "set", VolumeLevel = 30 });
        bool normalized = ConfigStore.Normalize(cfg);
        Assert.False(normalized);
        Assert.Equal(4, cfg.Settings.PanelSchema);
    }

    // 幂等：迁第二次没有东西可搬，也不再报「改过」。
    [Fact]
    public void The_migration_is_idempotent()
    {
        var cfg = Config(new LaunchStep { Kind = "volume", Action = "set", Level = 30 });
        Assert.True(ConfigStore.Normalize(cfg));
        Assert.False(ConfigStore.Normalize(cfg));
        Assert.Equal("set", cfg.LaunchSteps[0].VolumeAction);
        Assert.Equal(30, cfg.LaunchSteps[0].VolumeLevel);
    }

    // 类型对不上的旧值**不搬**：它对那个类型本来就没有意义，
    // 搬过去反而是给一个不相干的类型塞了一个值。
    [Fact]
    public void A_value_that_does_not_belong_to_the_kind_is_not_carried_over()
    {
        var cfg = Config(new LaunchStep { Kind = "app", Action = "close", Level = 30, Target = "x.exe" });
        ConfigStore.Normalize(cfg);
        var s = cfg.LaunchSteps[0];
        Assert.Equal("", s.VolumeAction);
        Assert.Equal(50, s.VolumeLevel);
    }

    // **改的是配置文件的形状，就该报给用户看。** 静默做完的话，用户打开配置看见
    // 字段名全变了，只会觉得是程序把配置弄坏了——而每一步做什么其实一点没变。
    [Fact]
    public void The_migration_reports_what_it_moved()
    {
        var cfg = Config(
            new LaunchStep { Kind = "volume", Action = "set", Level = 30 },
            new LaunchStep { Kind = "window", Action = "close" });
        ConfigStore.Normalize(cfg);
        Assert.Single(ConfigStore.LastMigrationLog);
        Assert.Contains("2", ConfigStore.LastMigrationLog[0]);
    }
}
