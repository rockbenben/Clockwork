using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 步骤 Id 的一次性迁移（PanelSchema 3 → 4）。Id 是面板点击统计（PanelUsageStore）的键，
// 这个文件钉的是整条链里最容易静默坏掉的一环：**id 必须在重启后保持不变**。
//
// 最大的坑写在 ConfigStore.AssignStepIds 的注释里：LaunchStep.Id 带 Guid 初始值，
// JSON 缺 id 键的步骤反序列化完已经拿着一个全新非空 Guid，「id 为空」判据永远不成立。
// 没有 schema 门的表现是：每次启动所有 id 全换一遍，统计永远攒不起来，且不报错。
public class StepIdMigrationTests
{
    // 四个清单各放一个步骤。新对象带着初始化器发的 Guid——模拟「盘上没有 id 键，
    // 反序列化后每个步骤各拿一个临时 Guid」的升级现场。
    private static RootConfig OldConfigWithOneStepEach()
    {
        var cfg = new RootConfig();   // new RootConfig() 的 schema 是 0
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "app", Label = "清单项" });
        cfg.Gestures.Add(new LaunchStep { Kind = "window", Action = "minimize", Gesture = "LD" });
        var g = new ActionGroup { Name = "组" };
        g.Steps.Add(new LaunchStep { Kind = "keys", Combo = "^c" });
        cfg.ActionGroups.Add(g);
        cfg.PanelPages.Add(new PanelPage { Name = "页" });
        cfg.PanelPages[0].Steps.Add(new LaunchStep { Kind = "system", Command = "lockScreen" });
        return cfg;
    }

    private static System.Collections.Generic.List<string> AllIds(RootConfig cfg) =>
        cfg.LaunchSteps.Select(s => s.Id)
            .Concat(cfg.Gestures.Select(s => s.Id))
            .Concat(cfg.ActionGroups.SelectMany(x => x.Steps.Select(s => s.Id)))
            .Concat(cfg.PanelPages.SelectMany(p => p.Steps.Select(s => s.Id)))
            .ToList();

    [Fact]
    public void The_upgrade_reissues_every_id_once_and_bumps_the_schema()
    {
        var cfg = OldConfigWithOneStepEach();
        var before = AllIds(cfg).ToHashSet();

        Assert.True(ConfigStore.Normalize(cfg));
        Assert.Equal(4, cfg.Settings.PanelSchema);

        var after = AllIds(cfg);
        Assert.Equal(4, after.Count);
        Assert.All(after, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(after.Count, after.Distinct().Count());        // 四份清单之间也不许撞
        Assert.Empty(after.Intersect(before));                     // 批发：临时 Guid 一个不留

        // 幂等：第二遍门已关，一个 id 都不再动，也不再报「配置变了」。
        Assert.False(ConfigStore.Normalize(cfg));
        Assert.Equal(after, AllIds(cfg));
    }

    // 这是这个功能的命门：写盘再读盘，id 必须原样回来，且 Read 不报告 normalized。
    // 「每次读盘悄悄重发 id」没有任何用户可见症状，只有统计永远是 0。
    [Fact]
    public void Ids_survive_a_save_load_round_trip_unchanged()
    {
        var cfg = OldConfigWithOneStepEach();
        ConfigStore.Normalize(cfg);
        var ids = AllIds(cfg);

        var path = Path.Combine(Path.GetTempPath(), "cw_cfgids_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ConfigStore.Write(cfg, path);
            var reread = ConfigStore.Read(path, out bool normalized, out bool unreadable);
            Assert.False(unreadable);
            Assert.False(normalized);
            Assert.Equal(ids, AllIds(reread));
            Assert.Equal(4, reread.Settings.PanelSchema);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // 盘上的字面键名钉成 camelCase 的 "id"——它是两个文件（settings / usage）的连接键。
    [Fact]
    public void The_id_is_serialized_as_a_literal_id_key()
    {
        var cfg = OldConfigWithOneStepEach();
        ConfigStore.Normalize(cfg);
        var path = Path.Combine(Path.GetTempPath(), "cw_cfgids_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ConfigStore.Write(cfg, path);
            Assert.Contains("\"id\":", File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // 门关上之后，已经落盘的 id 是身份，一个都不许重发。
    [Fact]
    public void After_the_gate_closes_existing_ids_are_never_regenerated()
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 4 } };
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "app", Id = "keep-1" });
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "app", Id = "keep-2" });

        Assert.False(ConfigStore.Normalize(cfg));
        Assert.Equal("keep-1", cfg.LaunchSteps[0].Id);
        Assert.Equal("keep-2", cfg.LaunchSteps[1].Id);
    }

    // 但门只挡住「整批发号」：手写 json 抹出的空白 id 仍要随见随修，否则那个格子永远攒不起统计。
    [Fact]
    public void A_blank_id_is_still_repaired_after_the_gate_closed()
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 4 } };
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "app", Id = "  " });

        Assert.True(ConfigStore.Normalize(cfg));
        Assert.False(string.IsNullOrWhiteSpace(cfg.LaunchSteps[0].Id));
        Assert.Equal(4, cfg.Settings.PanelSchema);                 // 单条修补不抬版本号
    }

    // 复制粘贴会造出跨清单的重复 id——两个格子共用一份计数。去重账本跨四份清单共用。
    [Fact]
    public void Duplicate_ids_are_disambiguated_even_after_the_gate_closed()
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 4 } };
        cfg.LaunchSteps.Add(new LaunchStep { Kind = "app", Id = "dup" });
        cfg.Gestures.Add(new LaunchStep { Kind = "window", Id = "dup", Gesture = "LD" });

        Assert.True(ConfigStore.Normalize(cfg));
        Assert.NotEqual(cfg.LaunchSteps[0].Id, cfg.Gestures[0].Id);
        Assert.Equal(4, cfg.Settings.PanelSchema);
    }

    // 四个清单全空：没有东西需要身份，版本号停在 3。以后真加了步骤，下一次启动再批发也不迟，
    // 统计那时还不存在，没有东西可丢。
    [Fact]
    public void An_empty_config_does_not_bump_the_schema()
    {
        var cfg = new RootConfig { Settings = new AppSettings { PanelSchema = 3 } };
        Assert.False(ConfigStore.Normalize(cfg));
        Assert.Equal(3, cfg.Settings.PanelSchema);
    }
}
