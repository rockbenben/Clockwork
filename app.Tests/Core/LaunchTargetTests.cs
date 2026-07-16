using Clockwork.Core;
using Xunit;
using System.IO;

public class LaunchTargetTests
{
    [Fact]
    public void ParseCommandLine_quoted()
    {
        var c = LaunchTarget.ParseCommandLine("\"C:\\Program Files\\App\\a.exe\" --flag x");
        Assert.Equal(@"C:\Program Files\App\a.exe", c.Target);
        Assert.Equal("--flag x", c.Arguments);
    }

    [Fact]
    public void ParseCommandLine_unquoted()
    {
        var c = LaunchTarget.ParseCommandLine("notepad.exe file.txt");
        Assert.Equal("notepad.exe", c.Target);
        Assert.Equal("file.txt", c.Arguments);
    }

    [Fact] public void ParseCommandLine_empty() => Assert.Equal("", LaunchTarget.ParseCommandLine("").Target);

    [Theory]
    [InlineData("notepad.exe", "notepad")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad")]
    [InlineData("https://github.com", "")]
    [InlineData(@"C:\a\b.ps1", "")]
    [InlineData("game", "game")]
    public void TargetProcessName(string target, string expected)
        => Assert.Equal(expected, LaunchTarget.TargetProcessName(target));

    [Fact]
    public void ResolveLaunchTarget_bare_name_unchanged()
        => Assert.Equal("notepad.exe", LaunchTarget.ResolveLaunchTarget("notepad.exe", ""));

    [Fact]
    public void ResolveLaunchTarget_falls_back_to_existing_alt()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cw_lt_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tmp, "x");
        try
        {
            var missing = @"Z:\nope\gone.exe";
            Assert.Equal(tmp, LaunchTarget.ResolveLaunchTarget(missing, "Y:\\also-missing.exe\n" + tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ResolveLaunchTarget_matches_directory_alt()
    {
        // 打开文件夹的步骤：目录候选也要能匹配（对齐旧 PS 版 Test-Path 文件+目录语义）。
        var dir = Path.Combine(Path.GetTempPath(), "cw_ltd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(dir, LaunchTarget.ResolveLaunchTarget(@"Z:\nope\folder", dir));      // 目录备选命中
            Assert.Equal(dir, LaunchTarget.ResolveLaunchTarget(dir, @"Y:\other"));            // 目录主路径直接命中
        }
        finally { Directory.Delete(dir); }
    }

    [Fact]
    public void IsSelfTarget_matches_case_insensitive()
        => Assert.True(LaunchTarget.IsSelfTarget(@"C:\App\Clockwork.EXE", new[] { @"C:\App\clockwork.exe" }));

    [Fact]
    public void IsSelfTarget_false_for_other()
        => Assert.False(LaunchTarget.IsSelfTarget(@"C:\App\other.exe", new[] { @"C:\App\clockwork.exe" }));

    [Fact]
    public void IsSelfTarget_empty_false()
        => Assert.False(LaunchTarget.IsSelfTarget("", new[] { @"C:\App\clockwork.exe" }));

    [Theory]
    [InlineData(@"C:\a\b.ps1", true)]
    [InlineData(@"C:\a\B.PS1", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPowerShellScript(string? target, bool expected)
        => Assert.Equal(expected, LaunchTarget.IsPowerShellScript(target));

    [Fact]
    public void PowerShellFileArgs_quotes_path_no_extra()
        => Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\s\\a.ps1\"", LaunchTarget.PowerShellFileArgs(@"C:\s\a.ps1"));

    [Fact]
    public void PowerShellFileArgs_appends_extra_args()
        => Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"a.ps1\" -Foo 1", LaunchTarget.PowerShellFileArgs("a.ps1", "-Foo 1"));
}
