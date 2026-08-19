using Clockwork.Core;
using Xunit;
using System.IO;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw_" + Guid.NewGuid().ToString("N"));
    public ConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Read_missing_file_returns_default()
    {
        var c = ConfigStore.Read(Path.Combine(_dir, "nope.json"));
        Assert.Equal(RootConfig.Default().LaunchSteps.Count, c.LaunchSteps.Count);
    }

    [Fact]
    public void Write_then_read_roundtrips()
    {
        var path = Path.Combine(_dir, "cfg.json");
        var cfg = RootConfig.Default();
        cfg.LaunchSteps[0].Label = "自定义标签";
        cfg.Settings.StartupDelaySeconds = 45;
        ConfigStore.Write(cfg, path);
        var back = ConfigStore.Read(path);
        Assert.Equal("自定义标签", back.LaunchSteps[0].Label);
        Assert.Equal(45, back.Settings.StartupDelaySeconds);
    }

    [Fact]
    public void Write_is_atomic_replace_over_existing()
    {
        var path = Path.Combine(_dir, "cfg.json");
        ConfigStore.Write(RootConfig.Default(), path);
        var cfg = RootConfig.Default();
        cfg.Settings.TickSeconds = 99;
        ConfigStore.Write(cfg, path);                 // 覆盖已存在
        Assert.False(File.Exists(path + ".tmp"));     // 无残留临时文件
        Assert.Equal(99, ConfigStore.Read(path).Settings.TickSeconds);
    }

    [Fact]
    public void Written_json_is_utf8_without_bom_and_camelCase()
    {
        var path = Path.Combine(_dir, "cfg.json");
        ConfigStore.Write(RootConfig.Default(), path);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF); // 无 BOM
        var text = File.ReadAllText(path);
        Assert.Contains("\"launchSteps\"", text);      // camelCase 键
        Assert.Contains("开机先静音", text);            // 中文未被转义
    }

    [Fact]
    public void Read_corrupt_json_returns_default()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ this is not json");
        Assert.Equal(RootConfig.Default().LaunchSteps.Count, ConfigStore.Read(path).LaunchSteps.Count);
    }

    [Fact]
    public void Read_regenerates_blank_ids_to_distinct_nonempty()
    {
        // 两条空 id 的提醒 + 一个空 id 的动作组：运行态/组引用都按 id 做键，空串会串在一起。
        var path = Path.Combine(_dir, "blankids.json");
        File.WriteAllText(path,
            "{\"reminders\":[{\"id\":\"\",\"message\":\"a\"},{\"id\":\"\",\"message\":\"b\"}]," +
            "\"actionGroups\":[{\"id\":\"\",\"name\":\"g\"}]}");
        var c = ConfigStore.Read(path);
        Assert.False(string.IsNullOrWhiteSpace(c.Reminders[0].Id));
        Assert.False(string.IsNullOrWhiteSpace(c.Reminders[1].Id));
        Assert.NotEqual(c.Reminders[0].Id, c.Reminders[1].Id);   // 不再共用一份运行态
        Assert.False(string.IsNullOrWhiteSpace(c.ActionGroups[0].Id));
    }

    [Fact]
    public void Read_regenerates_duplicate_reminder_ids()
    {
        // 两条提醒共用同一非空 id（复制粘贴/手改）：运行态按 id 做键会互相压制，应把重复的一条重发 id。
        var path = Path.Combine(_dir, "dupids.json");
        File.WriteAllText(path,
            "{\"reminders\":[{\"id\":\"same\",\"message\":\"a\"},{\"id\":\"same\",\"message\":\"b\"}]}");
        var c = ConfigStore.Read(path);
        Assert.NotEqual(c.Reminders[0].Id, c.Reminders[1].Id);
        Assert.False(string.IsNullOrWhiteSpace(c.Reminders[0].Id));
        Assert.False(string.IsNullOrWhiteSpace(c.Reminders[1].Id));
    }

    [Fact]
    public void Read_reports_normalization_so_caller_can_write_back()
    {
        // 重发的提醒 id 若不写回文件，每次启动都换新 id、运行态永远接不上（被去重那条每次重启都重弹）。
        // Read 须报告「发生了重启后有影响的规范化」，调用方据此写回；写回后的再读应是干净的。
        var path = Path.Combine(_dir, "norm.json");
        File.WriteAllText(path,
            "{\"reminders\":[{\"id\":\"same\",\"message\":\"a\"},{\"id\":\"same\",\"message\":\"b\"},null]}");
        var c = ConfigStore.Read(path, out bool normalized);
        Assert.True(normalized);
        ConfigStore.Write(c, path);                        // 调用方写回
        var c2 = ConfigStore.Read(path, out bool again);
        Assert.False(again);                               // 干净读，不再规范化
        Assert.Equal(c.Reminders[1].Id, c2.Reminders[1].Id);   // 重发的 id 已固化，重启不再变
    }

    [Fact]
    public void Reminder_sound_and_temporary_survive_a_round_trip()
    {
        // 两个都是「悄悄丢了也看不出来」的布尔：sound 丢了只是提醒从此没声音，temporary 丢了
        // 会让快速提醒响完不自删、在列表里堆死行。序列化用的是 camelCase 策略，字段名一改就静默归默认值。
        var path = Path.Combine(_dir, "flags.json");
        var cfg = new RootConfig();
        cfg.Reminders.Add(new Reminder { Message = "m", Sound = true, Temporary = true });
        ConfigStore.Write(cfg, path);
        Assert.Contains("\"sound\": true", File.ReadAllText(path));       // camelCase 落盘，不是 Sound
        var back = ConfigStore.Read(path, out _);
        Assert.True(back.Reminders[0].Sound);
        Assert.True(back.Reminders[0].Temporary);
        // 旧配置（没有这两个键）读入应为 false，而不是抛或变 true——升级不该给存量提醒平白加上声音。
        File.WriteAllText(path, "{\"reminders\":[{\"id\":\"x\",\"message\":\"old\"}]}");
        var old = ConfigStore.Read(path, out _);
        Assert.False(old.Reminders[0].Sound);
        Assert.False(old.Reminders[0].Temporary);
    }

    [Fact]
    public void Read_clean_file_reports_no_normalization()
    {
        var path = Path.Combine(_dir, "clean.json");
        ConfigStore.Write(RootConfig.Default(), path);
        ConfigStore.Read(path, out bool normalized);
        Assert.False(normalized);
    }

    [Fact]
    public void Read_normalizes_empty_group_onyes_to_none()
    {
        // 旧版编辑器可存下「点是后=组」但目标为空（下拉留在「（无）」）——点「是」什么都不做。
        // 读入归一成 none 并报告 normalized（调用方写回），运行期不再对残留误报「组被删」。
        var path = Path.Combine(_dir, "emptygroup.json");
        File.WriteAllText(path,
            "{\"reminders\":[{\"id\":\"r1\",\"message\":\"a\",\"onYes\":{\"type\":\"group\",\"target\":\"\"}}]," +
            "\"launchSteps\":[{\"kind\":\"message\",\"onYes\":{\"type\":\"group\",\"target\":\"gid\"}}]}");
        var c = ConfigStore.Read(path, out bool normalized);
        Assert.True(normalized);
        Assert.Equal("none", c.Reminders[0].OnYes.Type);       // 空目标 → 归一
        Assert.Equal("group", c.LaunchSteps[0].OnYes.Type);    // 有目标 → 保留
    }

    [Fact]
    public void Group_hotkey_roundtrips_and_defaults_blank()
    {
        // 全局热键字段：写读往返保留；旧配置（无该键）读入默认空=不绑定；运行快照带上（热键触发跑的是快照）。
        // Default() 现在预置两个组，新增的这条排在它们后面——用 [^1]（最后一条）钉住"刚加的那条"，不依赖它是不是索引 0。
        var path = Path.Combine(_dir, "ghk.json");
        var cfg = RootConfig.Default();
        cfg.ActionGroups.Add(new ActionGroup { Name = "专注", Hotkey = "Ctrl+Alt+F" });
        ConfigStore.Write(cfg, path);
        var back = ConfigStore.Read(path);
        Assert.Equal("Ctrl+Alt+F", back.ActionGroups[^1].Hotkey);

        var legacy = Path.Combine(_dir, "ghk_legacy.json");
        File.WriteAllText(legacy, "{\"actionGroups\":[{\"name\":\"旧组\"}]}");
        Assert.Equal("", ConfigStore.Read(legacy).ActionGroups[0].Hotkey);

        Assert.Equal("Ctrl+Alt+F", cfg.ActionGroups[^1].SnapshotForRun().Hotkey);
    }

    // unreadable 是「别写回」的开关：App.LoadConfig 读到它为 true 就跳过写回。
    // 不区分的话，解析失败返回的默认配置会被语言规范化顺手写回原路径——用户漏打一个逗号，全部设置就没了。
    [Fact]
    public void Read_corrupt_json_reports_unreadable()
    {
        var path = Path.Combine(_dir, "corrupt.json");
        File.WriteAllText(path, "{\"launchSteps\": [ , ] 这不是 json");
        ConfigStore.Read(path, out _, out bool unreadable);
        Assert.True(unreadable);
    }

    [Fact]
    public void Read_missing_file_is_not_unreadable()   // 首次启动：写默认配置是对的，不该被当成坏文件
    {
        ConfigStore.Read(Path.Combine(_dir, "nope.json"), out _, out bool unreadable);
        Assert.False(unreadable);
    }

    [Fact]
    public void Read_valid_file_is_not_unreadable()
    {
        var path = Path.Combine(_dir, "ok.json");
        ConfigStore.Write(RootConfig.Default(), path);
        ConfigStore.Read(path, out _, out bool unreadable);
        Assert.False(unreadable);
    }

    [Fact]
    public void Read_null_array_elements_are_dropped_not_crashed()
    {
        // 手改配置在数组里留下 null 元素：反序列化成功但后续按元素解引用会 NRE，
        // 且这些兜底在 try/catch 之外，一崩就违背「解析失败落默认、绝不崩」。应剔除 null、保留正常项。
        var path = Path.Combine(_dir, "nulls.json");
        File.WriteAllText(path,
            "{\"launchSteps\":[null,{\"kind\":\"app\",\"label\":\"ok\"}]," +
            "\"reminders\":[null,{\"message\":\"r\"}]," +
            "\"actionGroups\":[null,{\"name\":\"g\",\"steps\":[null,{\"kind\":\"system\"}]}]}");
        var c = ConfigStore.Read(path);   // 不抛
        Assert.Single(c.LaunchSteps);
        Assert.Equal("ok", c.LaunchSteps[0].Label);
        Assert.Single(c.Reminders);
        Assert.Single(c.ActionGroups);
        Assert.Single(c.ActionGroups[0].Steps);
        Assert.Equal("system", c.ActionGroups[0].Steps[0].Kind);
    }

    [Fact]
    public void New_step_default_delay_is_100ms()
        => Assert.Equal(100, new LaunchStep().DelayMs);

    // 钉死「改默认值不动老配置」的前提：JsonOptions 不忽略默认值，盘上显式 delayMs:0 必须原样读回，
    // 不被新的 C# 默认 100 覆盖。这条一红，说明有人给 JsonOptions 加了 DefaultIgnoreCondition。
    [Fact]
    public void Explicit_zero_delay_on_disk_survives_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw_cfg_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new RootConfig { LaunchSteps = new() { new LaunchStep { Kind = "app", Label = "x", DelayMs = 0 } } };
            ConfigStore.Write(cfg, path);
            Assert.Equal(0, ConfigStore.Read(path).LaunchSteps[0].DelayMs);
        }
        finally { File.Delete(path); }
    }

    // 快照浅拷贝手抄字段的典型漏项守卫：漏带 Repeat/RepeatDelayMs 时后台运行永远只跑 1 轮。
    [Fact]
    public void Group_snapshot_carries_repeat_fields()
    {
        var g = new ActionGroup { Name = "g", Repeat = 3, RepeatDelayMs = 250 };
        var snap = g.SnapshotForRun();
        Assert.Equal(3, snap.Repeat);
        Assert.Equal(250, snap.RepeatDelayMs);
    }

    [Fact]
    public void Group_repeat_fields_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw_cfg_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new RootConfig { ActionGroups = new() { new ActionGroup { Name = "g", Repeat = 5, RepeatDelayMs = 100 } } };
            ConfigStore.Write(cfg, path);
            var back = ConfigStore.Read(path).ActionGroups[0];
            Assert.Equal(5, back.Repeat);
            Assert.Equal(100, back.RepeatDelayMs);
        }
        finally { File.Delete(path); }
    }

    // —— 「在托盘菜单显示」的迁移 ——
    // 老配置里没有 showInTray 字段，读进来是 null。若直接当 false，升级后老用户托盘里的
    // 动作组会一起消失、看着像功能坏了；故 Normalize 一律补 true 保持升级前的外观。
    [Fact]
    public void ShowInTray_missing_in_old_config_migrates_to_true()
    {
        var cfg = new RootConfig { ActionGroups = new() { new ActionGroup { Name = "老组" } } };
        Assert.Null(cfg.ActionGroups[0].ShowInTray);          // 模型默认就是「没表过态」
        bool normalized = ConfigStore.Normalize(cfg);
        Assert.True(normalized);                              // 需要写回，否则每次启动都补一遍
        Assert.True(cfg.ActionGroups[0].ShowInTray);
    }

    // 显式关掉的必须活下来——否则用户每次重启都会看到自己藏起来的组又冒回托盘。
    [Fact]
    public void ShowInTray_explicit_false_survives_normalize()
    {
        var cfg = new RootConfig { ActionGroups = new() { new ActionGroup { Name = "藏起来", ShowInTray = false } } };
        ConfigStore.Normalize(cfg);
        Assert.False(cfg.ActionGroups[0].ShowInTray);
    }

    [Fact]
    public void ShowInTray_roundtrips_through_disk_and_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw_cfg_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new RootConfig { ActionGroups = new()
            {
                new ActionGroup { Name = "显示", ShowInTray = true },
                new ActionGroup { Name = "隐藏", ShowInTray = false },
            } };
            ConfigStore.Write(cfg, path);
            var back = ConfigStore.Read(path).ActionGroups;
            Assert.True(back[0].ShowInTray);
            Assert.False(back[1].ShowInTray);
            // 后台跑组用的是快照，漏带这个字段会让托盘状态在快照里失真
            Assert.False(back[1].SnapshotForRun().ShowInTray);
        }
        finally { File.Delete(path); }
    }

    // 首启预置的两个组必须进托盘：那时没有任何提醒/热键指向它们，托盘是唯一顺手的入口。
    [Fact]
    public void Default_preset_groups_are_shown_in_tray()
        => Assert.All(RootConfig.Default().ActionGroups, g => Assert.True(g.ShowInTray));
}
