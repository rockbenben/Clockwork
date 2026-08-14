using Clockwork.Core;
using Xunit;
using System.IO;
using System.Linq;
using System.Text;

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

    // —— 无引号命令行的探盘切分（注册表 Run 键接管）——
    // 纯切分会把 "C:\Program Files\X\a.exe --flag" 截成 "C:\Program"；接管流程会先禁用原启动项，
    // 于是被接管的程序彻底不再自启。本机注册表里这种写法实测有 7 条。

    [Fact]
    public void ParseCommandLineProbing_finds_real_exe_behind_spaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw pcp " + Guid.NewGuid().ToString("N"));   // 目录名含空格
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "app.exe");
        File.WriteAllText(exe, "x");
        try
        {
            var c = LaunchTarget.ParseCommandLineProbing(exe + " --flag x");
            Assert.Equal(exe, c.Target);
            Assert.Equal("--flag x", c.Arguments);

            var noArgs = LaunchTarget.ParseCommandLineProbing(exe);   // 整串都是路径、没有参数
            Assert.Equal(exe, noArgs.Target);
            Assert.Equal("", noArgs.Arguments);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ParseCommandLineProbing_falls_back_when_nothing_exists()   // 裸程序名/已删除的路径：退回纯切分
    {
        var c = LaunchTarget.ParseCommandLineProbing("rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
        Assert.Equal("rundll32.exe", c.Target);
        Assert.Equal("powrprof.dll,SetSuspendState 0,1,0", c.Arguments);
    }

    [Fact]
    public void ParseCommandLineProbing_keeps_quoted_behaviour()
        => Assert.Equal(@"C:\Program Files\App\a.exe",
                        LaunchTarget.ParseCommandLineProbing("\"C:\\Program Files\\App\\a.exe\" --flag").Target);

    // —— 目标写法规范化 ——

    [Fact]
    public void NormalizeTarget_strips_paired_quotes_and_space()
        => Assert.Equal(@"C:\s\a.ps1", LaunchTarget.NormalizeTarget("  \"C:\\s\\a.ps1\"  "));

    [Fact]
    public void NormalizeTarget_keeps_unpaired_quote()   // 单边引号是写错了，别猜，留着让报错指出来
        => Assert.Equal("\"C:\\s\\a.ps1", LaunchTarget.NormalizeTarget("\"C:\\s\\a.ps1"));

    [Fact]
    public void NormalizeTarget_expands_env_var()
    {
        var want = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal(want + @"\x.ps1", LaunchTarget.NormalizeTarget(@"%WINDIR%\x.ps1"));
    }

    [Fact]
    public void NormalizeTarget_leaves_url_percent_escapes_alone()   // %E4%B8%AD 不是环境变量，别被吃掉
        => Assert.Equal("https://a.com/%E4%B8%AD", LaunchTarget.NormalizeTarget("https://a.com/%E4%B8%AD"));

    [Fact]
    public void ResolveLaunchTarget_strips_quotes()   // 资源管理器「复制文件地址」带引号，不脱就认不出 .ps1
        => Assert.Equal(@"C:\s\a.ps1", LaunchTarget.ResolveLaunchTarget("\"C:\\s\\a.ps1\"", ""));

    // —— .ps1 解释器选择 ——
    // Windows PowerShell 5.1 对没有 BOM 的脚本按系统 ANSI 代码页解码，中文机器上是 GBK。

    private static string WriteTempScript(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "cw_ps_" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static void WithTempScript(byte[] bytes, Action<string> check)
    {
        var p = WriteTempScript(bytes);
        try { check(p); } finally { try { File.Delete(p); } catch { } }
    }

    [Fact]
    public void NeedsUtf8PowerShell_true_for_bomless_utf8_with_cjk()
        => WithTempScript(Encoding.UTF8.GetBytes("# 中文注释\r\nWrite-Host hi\r\n"),   // GetBytes 不带 BOM
                          p => Assert.True(LaunchTarget.NeedsUtf8PowerShell(p)));

    [Fact]
    public void NeedsUtf8PowerShell_false_when_bom_present()   // 有 BOM，5.1 自己认得
        => WithTempScript(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# 中文\r\n")).ToArray(),
                          p => Assert.False(LaunchTarget.NeedsUtf8PowerShell(p)));

    [Fact]
    public void NeedsUtf8PowerShell_false_for_pure_ascii()     // 任何代码页解出来都一样
        => WithTempScript(Encoding.ASCII.GetBytes("Write-Host hi\r\n"),
                          p => Assert.False(LaunchTarget.NeedsUtf8PowerShell(p)));

    // 防倒退：老的 ANSI/GBK 脚本同样「无 BOM + 含非 ASCII」，但它在 5.1 下本来就是对的，
    // 换成按 UTF-8 解码的 pwsh 反而会变乱码。0xB0 0xA1 是 GBK 的「啊」，不是合法 UTF-8 序列。
    [Fact]
    public void NeedsUtf8PowerShell_false_for_legacy_ansi()
        => WithTempScript(new byte[] { 0x23, 0x20, 0xB0, 0xA1, 0x0D, 0x0A },
                          p => Assert.False(LaunchTarget.NeedsUtf8PowerShell(p)));

    [Fact]
    public void NeedsUtf8PowerShell_false_for_missing_file()
        => Assert.False(LaunchTarget.NeedsUtf8PowerShell(@"Z:\nope\gone.ps1"));

    [Fact]
    public void PowerShellExeFor_keeps_windows_powershell_when_decodable()
        => WithTempScript(Encoding.ASCII.GetBytes("Write-Host hi\r\n"),
                          p => Assert.Equal(LaunchTarget.PowerShellExe, LaunchTarget.PowerShellExeFor(p)));

    // 无 BOM 的 UTF-8 中文脚本绝不能再交给 powershell.exe：装了 pwsh 就用它的完整路径，
    // 没装则返回 null 让调用方报「装 PowerShell 7 或加 BOM」。两种结果都不等于 powershell.exe。
    [Fact]
    public void PowerShellExeFor_never_returns_windows_powershell_for_bomless_utf8_cjk()
        => WithTempScript(Encoding.UTF8.GetBytes("# 中文注释\r\nWrite-Host hi\r\n"), p =>
        {
            var exe = LaunchTarget.PowerShellExeFor(p);
            Assert.NotEqual(LaunchTarget.PowerShellExe, exe);
            if (exe != null) Assert.EndsWith(LaunchTarget.PwshExe, exe, StringComparison.OrdinalIgnoreCase);
        });
}
