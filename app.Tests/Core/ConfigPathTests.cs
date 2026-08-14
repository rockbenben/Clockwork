using Clockwork.Core;
using Xunit;
using System.IO;

public class ConfigPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cwcfg_" + Guid.NewGuid().ToString("N"));
    public ConfigPathTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact] public void Writable_dir_reports_true() => Assert.True(ConfigPath.IsWritable(_dir));
    [Fact] public void Nonexistent_dir_reports_false() => Assert.False(ConfigPath.IsWritable(Path.Combine(_dir, "does", "not", "exist")));

    [Fact]
    public void Resolve_uses_exe_dir_when_writable()
        => Assert.Equal(Path.Combine(_dir, "clockwork.settings.json"), ConfigPath.Resolve(_dir));

    [Fact]
    public void Resolve_falls_back_to_appdata_when_not_writable()
    {
        var p = ConfigPath.Resolve(Path.Combine(_dir, "nope-readonly-xyz"));
        Assert.Contains("Clockwork", p);
        Assert.EndsWith("clockwork.settings.json", p);
    }

    // 下面两条一起钉住「配置不会因为这次启动是否提权而换一边」。装在 Program Files 时，
    // 双击是非提权（目录不可写）、开机自启的计划任务是提权（目录可写），探针结果相反；
    // 若按探针挑，两种启动就各用一份配置，表现为「以管理员重开后设置全空」「开机跑的清单不是我配的」。

    [Fact]
    public void Resolve_keeps_portable_file_even_when_dir_probes_unwritable()
    {
        var exeDir = Path.Combine(_dir, "app");
        Directory.CreateDirectory(exeDir);
        var portable = Path.Combine(exeDir, "cwtest.json");
        File.WriteAllText(portable, "{}");
        // 非提权启动看到的 Program Files：探针说不可写，但配置就在那儿。
        Assert.Equal(portable, ConfigPath.Resolve(exeDir, "cwtest.json", _ => false));
    }

    [Fact]
    public void Resolve_keeps_roaming_file_even_when_dir_probes_writable()
    {
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clockwork");
        Directory.CreateDirectory(appDataDir);
        var name = "cwtest_" + Guid.NewGuid().ToString("N") + ".json";   // 唯一名，绝不碰真实配置
        var roaming = Path.Combine(appDataDir, name);
        File.WriteAllText(roaming, "{}");
        try
        {
            // 提权启动看到的同一台机器：这次探针说可写，但配置在漫游那边，不能改用便携。
            Assert.Equal(roaming, ConfigPath.Resolve(_dir, name, _ => true));
        }
        finally { try { File.Delete(roaming); } catch { } }
    }
}
