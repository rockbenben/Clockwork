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
}
