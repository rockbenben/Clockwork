using System.IO;
using Clockwork.Core;
using Xunit;

// 更新检查里不联网的那部分：挑资产、读校验值、算摘要。
//
// 联网那部分在 Engine.UpdateChecker，这三样都不该靠真机跑到 GitHub 才验得了。
public class UpdateCheckTests
{
    [Fact]
    public void The_self_contained_zip_is_picked_and_not_the_framework_dependent_one()
    {
        // 一次发布带两份 zip：依赖运行时的那份要机器上先装 .NET 10，挑错了用户拿到的 exe 起不来。
        string[] assets =
        [
            "Clockwork-v3.2.0-win-x64-needs-dotnet10.zip",
            "Clockwork-v3.2.0-win-x64.zip",
            "Clockwork.exe",
            "SHA256SUMS.txt",
        ];
        Assert.Equal("Clockwork-v3.2.0-win-x64.zip", UpdateCheck.SelectSelfContainedAsset(assets));
    }

    [Fact]
    public void No_self_contained_asset_means_no_pick()
    {
        string[] onlyFrameworkDependent =
        [
            "Clockwork-v3.2.0-win-x64-needs-dotnet10.zip",
            "Clockwork.exe",
            "SHA256SUMS.txt",
        ];
        Assert.Null(UpdateCheck.SelectSelfContainedAsset(onlyFrameworkDependent));
        Assert.Null(UpdateCheck.SelectSelfContainedAsset([]));
    }

    [Fact]
    public void Checksums_are_parsed_by_file_name()
    {
        // sha256sum 写出来的样子：摘要、空白、文件名；文件名可能带一个前导的 *（二进制模式）。
        string text =
            "a388a698d2fbf5b303bb3bd39a46d95311e3731d34f7790c06460581ae9923b9  Clockwork-v3.2.0-win-x64-needs-dotnet10.zip\r\n"
          + "cb0216f8f5ccb03681c4cf3ddaa39bf8ece16319c9f860cf7b0d5c277297e0ce *Clockwork-v3.2.0-win-x64.zip\r\n"
          + "8cade4ee4ed3de5b0197d11b8d2f2e57ada0304376f1a381451d0d9e7c3b5154  Clockwork.exe\r\n";
        var sums = UpdateCheck.ParseChecksums(text);
        Assert.Equal(3, sums.Count);
        Assert.Equal("cb0216f8f5ccb03681c4cf3ddaa39bf8ece16319c9f860cf7b0d5c277297e0ce",
                     sums["Clockwork-v3.2.0-win-x64.zip"]);
        Assert.Equal("8cade4ee4ed3de5b0197d11b8d2f2e57ada0304376f1a381451d0d9e7c3b5154",
                     sums["Clockwork.exe"]);
    }

    [Fact]
    public void Lines_that_are_not_a_digest_are_skipped()
    {
        // 一次发布还带 README 和许可证，那些行不是我们要的。
        string text =
            "# SHA256SUMS.txt\r\n"
          + "not-a-digest  Clockwork.exe\r\n"
          + "8cade4ee4ed3de5b0197d11b8d2f2e57ada0304376f1a381451d0d9e7c3b5154  Clockwork.exe\r\n";
        var sums = UpdateCheck.ParseChecksums(text);
        Assert.Single(sums);
        Assert.True(sums.ContainsKey("Clockwork.exe"));
    }

    [Fact]
    public void The_digest_of_a_file_is_computed_and_compared()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, "clockwork"u8.ToArray());
        try
        {
            string digest = UpdateCheck.ComputeSha256Hex(path);
            Assert.Equal(64, digest.Length);
            Assert.True(UpdateCheck.MatchesChecksum(digest.ToUpperInvariant(), digest));   // 大小写不带信息
            string altered = (digest[0] == 'a' ? "b" : "a") + digest[1..];
            Assert.False(UpdateCheck.MatchesChecksum(altered, digest));
            Assert.False(UpdateCheck.MatchesChecksum(null, digest));
            Assert.False(UpdateCheck.MatchesChecksum(digest, ""));
        }
        finally { File.Delete(path); }
    }
}
