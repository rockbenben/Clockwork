using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

public class SystemStartupReaderTests
{
    [Fact]
    public void GetItems_does_not_throw_and_returns_list()
    {
        var items = SystemStartupReader.GetItems();   // 真机枚举：至少不抛、返回列表
        Assert.NotNull(items);
    }

    [Fact]
    public void SetItemEnabled_readonly_refused_before_any_write()
    {
        // 只读项(CanToggle=false)在任何注册表写入之前就返回 ReadOnly——不会误建 StartupApproved 空子键。
        var item = new SystemStartupItem { Type = "Registry", CanToggle = false, ValueName = "x", RegHive = "HKCU", RegRunKind = "" };
        Assert.Equal("ReadOnly", SystemStartupReader.SetItemEnabled(item, false));
    }

    [Fact]
    public void ToImportedStep_folder()
    {
        var s = SystemStartupReader.ToImportedStep(new SystemStartupItem { Type = "StartupFolder", Name = "X", Command = @"C:\x.lnk" });
        Assert.Equal("app", s.Kind);
        Assert.Equal(@"C:\x.lnk", s.Target);
        Assert.Equal(2000, s.DelayMs);
    }

    [Fact]
    public void ToImportedStep_registry_parses_cmdline()
    {
        var s = SystemStartupReader.ToImportedStep(new SystemStartupItem { Type = "Registry", Name = "Y", Command = "\"C:\\a b\\y.exe\" --f" });
        Assert.Equal(@"C:\a b\y.exe", s.Target);
        Assert.Equal("--f", s.Args);
    }
}
