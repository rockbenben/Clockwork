using System.IO;
using System.Security.Cryptography;

namespace Clockwork.Core;

/// <summary>
/// 更新检查里那些不联网的部分：挑哪个文件、读发布页给的校验值、算文件的摘要。
///
/// 放 Core 是按本仓库的规矩（见 CONTRIBUTING）：纯逻辑、无 Win32、无 UI，最便宜的是单测。
/// 联网那部分在 <c>Engine.UpdateChecker</c>，它把 Release 的原始 JSON 递进来。
/// </summary>
public static class UpdateCheck
{
    /// <summary>
    /// 一次发布里挑出**自包含**的那份 zip，没有就返回 null。
    /// <para>一次发布带两个 zip：自包含的（.NET 运行时在里面，拿到就能跑）和依赖运行时的
    /// （机器上得先装 .NET 10）。只有前者能当作「下载下来双击就能用」交给用户，所以依赖运行时的
    /// 那份永远不挑——挑错了，用户拿到的 exe 会因为缺运行时而起不来。</para>
    /// </summary>
    public static string? SelectSelfContainedAsset(IEnumerable<string> assetNames)
    {
        foreach (var name in assetNames)
        {
            if (!name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("-needs-dotnet10", StringComparison.OrdinalIgnoreCase)) continue;
            return name;
        }
        return null;
    }

    /// <summary>
    /// 发布页那份 SHA256SUMS.txt，按文件名索引出摘要。
    /// <para>格式就是 <c>sha256sum</c> 写的那样：摘要、空白、文件名——文件名可能带一个前导的
    /// <c>*</c>（二进制模式）。不像摘要的行直接跳过而不是猜：一次发布还带 README 和许可证，
    /// 那些行不是我们要的。</para>
    /// </summary>
    public static Dictionary<string, string> ParseChecksums(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int sep = line.IndexOfAny(new[] { ' ', '\t' });
            if (sep <= 0) continue;
            var digest = line[..sep].Trim();
            var name = line[(sep + 1)..].Trim();
            if (name.StartsWith('*')) name = name[1..];
            if (name.Length == 0 || digest.Length != 64) continue;
            if (!digest.All(Uri.IsHexDigit)) continue;
            map[name] = digest.ToLowerInvariant();
        }
        return map;
    }

    /// <summary>文件的 SHA-256，小写十六进制。</summary>
    public static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 下载到的那个文件，摘要是不是发布页公布的那一个。大小写不敏感：十六进制摘要两种大小写都有人印，
    /// 而字母的大小写不带任何信息。
    /// </summary>
    public static bool MatchesChecksum(string? expected, string? actual)
        => !string.IsNullOrWhiteSpace(expected)
           && !string.IsNullOrWhiteSpace(actual)
           && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
}
