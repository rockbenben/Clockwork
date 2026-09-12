using System.IO;
using System.Linq;
using System.Text;
using Clockwork.Native;
using Xunit;

// App 执行别名重解析数据的解析。字节布局没有官方文档（Win11 24H2 实测），
// 这里用手工拼的缓冲区把布局钉死：一旦解析器被「扫到 dataLen 结束」之类的改法带偏，
// 测试就会红。
public class AppExecutionAliasTests
{
    // 按实测布局拼一个 APPEXECLINK 缓冲区：
    //   偏移 0：DWORD tag（ParseStrings 不看，置 0）
    //   偏移 4：WORD  ReparseDataLength（Data 区从偏移 8 起算的字节数）
    //   偏移 8：DWORD 串数 N
    //   偏移 12：N 个 NUL 结尾 UTF-16LE 串
    private static byte[] BuildBuffer(string[] parts, int dataLen, int trailingJunk = 0)
    {
        var body = new MemoryStream();
        var b = new byte[4];
        BitConverter.TryWriteBytes(b, parts.Length);
        body.Write(b, 0, 4);
        foreach (var s in parts)
        {
            var chars = Encoding.Unicode.GetBytes(s + '\0');
            body.Write(chars, 0, chars.Length);
        }
        var data = body.ToArray();                       // 偏移 8 起的 Data 区
        var buf = new byte[8 + data.Length + trailingJunk];
        BitConverter.TryWriteBytes(buf.AsSpan(4, 2), (ushort)dataLen);
        Buffer.BlockCopy(data, 0, buf, 8, data.Length);
        return buf;
    }

    [Fact]
    public void Parses_the_three_empirical_strings_in_order()
    {
        var parts = new[]
        {
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App",
            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24_x64__8wekyb3d8bbwe\wt.exe",
        };
        var data = 4 + parts.Sum(p => (p.Length + 1) * 2);
        var buf = BuildBuffer(parts, data);

        var got = AppExecutionAlias.ParseStrings(buf, data);

        Assert.Equal(parts, got);
    }

    // dataLen 之内、串数之外的尾巴（实测缓冲区尾部的版本字段见过杂字符 "0"）绝不能被当成第四条串——
    // 解析必须按偏移 8 的串数收口，而不是扫到 dataLen 结束。
    [Fact]
    public void Trailing_junk_inside_dataLen_is_not_a_fourth_string()
    {
        var parts = new[] { "family", "family!App", @"C:\app\real.exe" };
        var stringsBytes = 4 + parts.Sum(p => (p.Length + 1) * 2);
        // 尾巴 "0\0" 在 Data 区内，ReparseDataLength 也把它算进去——与实测缓冲区一致。
        var buf = BuildBuffer(parts, stringsBytes + 4, trailingJunk: 4);
        Encoding.Unicode.GetBytes("0\0").CopyTo(buf, 8 + stringsBytes);

        var got = AppExecutionAlias.ParseStrings(buf, stringsBytes + 4);

        Assert.Equal(3, got.Count);
        Assert.DoesNotContain("0", got);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(17)]   // 上限 16：脏数据不该被照单全收
    public void An_implausible_count_yields_nothing(int count)
    {
        var buf = new byte[64];
        BitConverter.TryWriteBytes(buf.AsSpan(8), count);
        BitConverter.TryWriteBytes(buf.AsSpan(4), (ushort)56);
        Assert.Empty(AppExecutionAlias.ParseStrings(buf, 56));
    }

    [Fact]
    public void A_truncated_final_string_returns_the_completed_ones()
    {
        // 声称 3 条，但第 3 条在终止符之前就被 dataLen 截断：前两条照给。
        // Data 区 18 字节 = 串数(4) + "a\0"(4) + "b\0"(4) + 3 个宽字符 'A'（无 NUL 收尾，6）。
        const int data = 4 + 4 + 4 + 6;
        var buf = new byte[8 + data];
        BitConverter.TryWriteBytes(buf.AsSpan(4), (ushort)data);
        BitConverter.TryWriteBytes(buf.AsSpan(8), 3);
        Encoding.Unicode.GetBytes("a\0b\0").CopyTo(buf, 12);
        buf.AsSpan(20, 6).Fill(0x41);

        var got = AppExecutionAlias.ParseStrings(buf, data);

        Assert.Equal(new[] { "a", "b" }, got);
    }

    [Fact]
    public void Null_or_tiny_buffers_yield_nothing()
    {
        Assert.Empty(AppExecutionAlias.ParseStrings(null!, 0));
        Assert.Empty(AppExecutionAlias.ParseStrings(new byte[11], 3));
    }

    // 真机冒烟：本机装了 Terminal 时，WindowsApps 下的 wt.exe 别名必须解析回包里的真 exe。
    // 没装（别名不存在 / 不是重解析点）则跳过——这是环境能力测试，不伪造前提。
    [Fact]
    public void Real_wt_alias_resolves_to_a_terminal_exe_when_present()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (!File.Exists(alias)) return;

        var real = AppExecutionAlias.TryResolveRealExe(alias);
        Assert.NotNull(real);
        Assert.EndsWith("wt.exe", real, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(real), "解析出的真 exe 应该在磁盘上：" + real);
        // 解析出来的必须是包里的真文件，不是把别名路径原样吐回来。
        Assert.NotEqual(Path.GetFullPath(alias), Path.GetFullPath(real));
        Assert.Contains("Microsoft.WindowsTerminal", real, StringComparison.OrdinalIgnoreCase);
    }
}
