using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Clockwork.Core;

namespace Clockwork.Engine;

/// <summary>一次检查的结果。</summary>
public enum UpdateStatus
{
    /// <summary>没有更新的版本——没有可做的事。</summary>
    UpToDate,
    /// <summary>新版已经下载、核对、解压；<see cref="UpdateOutcome.ExePath"/> 是它在哪。</summary>
    Ready,
    /// <summary>下载到的文件跟发布页公布的校验值对不上，已经丢弃，一个字节都没解压。</summary>
    VerifyFailed,
    /// <summary>检查本身没成：没网、超时、被限流，或者这次发布里没有我们要的文件。</summary>
    Failed,
}

/// <summary>检查更新的结果。</summary>
/// <param name="Status">发生了什么。</param>
/// <param name="Latest">查到的版本号（不带前导 v）。</param>
/// <param name="Current">正在跑的版本号。</param>
/// <param name="ExePath">解压出来的新程序，<paramref name="Status"/> 为
/// <see cref="UpdateStatus.Ready"/> 时有值。</param>
/// <param name="Url">发布页地址。</param>
/// <param name="Error">失败的原因。</param>
public sealed record UpdateOutcome(UpdateStatus Status, string Latest, string Current,
                                   string? ExePath, string? Url, string? Error);

/// <summary>
/// 检查更新：拉 GitHub Releases 的最新 tag，与当前版本比对；如果是更新的，就把它下载下来、
/// 核对发布页附的校验值，再解压到一个用户找得到的地方。
///
/// <para><b>它绝不替换正在跑的那个程序。</b> 发出来的是单文件自包含 exe，而 Windows 锁着运行中的
/// exe：原地覆写会撞共享冲突，还会留下半个装坏的安装。所以新版本解压到它自己的文件夹里，
/// 然后告诉用户它在哪——换不换是用户的事，得先把旧的那个关掉。</para>
/// </summary>
public static class UpdateChecker
{
    public const string RepoUrl = "https://github.com/rockbenben/Clockwork";
    public const string ReleasesUrl = "https://github.com/rockbenben/Clockwork/releases";
    private const string ApiUrl = "https://api.github.com/repos/rockbenben/Clockwork/releases/latest";
    private const string ChecksumsAsset = "SHA256SUMS.txt";

    /// <summary>新版本解压到哪儿：%LOCALAPPDATA%\Clockwork\Updates\&lt;版本&gt;\。
    /// 每用户一份、且不在程序自己的目录里，所以程序被挪走或换掉都还在，
    /// 也不需要用户知道 exe 装在哪儿就能找到它。</summary>
    public static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clockwork", "Updates");

    // 单个共享实例：每次 new HttpClient 会另起 handler/socket、反复检查会在 TIME_WAIT 堆积。头信息固定、一次设好即可。
    // 元数据与校验值文件都很小，zip 不小——分成两个，免得慢网把小的请求也拖成长等。
    private static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(12));
    private static readonly HttpClient Downloads = CreateClient(TimeSpan.FromMinutes(10));

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Clockwork-update-check");   // GitHub API 要求带 UA
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    /// <summary>
    /// 查有没有新版，有就把新版下载、核对、解压。**不抛异常**：每一种失败都变成
    /// <see cref="UpdateStatus.Failed"/>——连不上网是常事，不是让程序崩掉的理由。
    /// </summary>
    public static async Task<UpdateOutcome> RunAsync(string currentVersion, CancellationToken ct = default)
    {
        string url = ReleasesUrl;
        string tempDir = "";
        try
        {
            var json = await Http.GetStringAsync(ApiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 草稿与预发布不是给正在跑正式版的人准备的。「latest」这个端点本来就不含草稿，
            // 这里仍写出来：我们要找的是「更新的正式版」，没做完的不算。
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return new(UpdateStatus.UpToDate, currentVersion, currentVersion, null, url, null);
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                return new(UpdateStatus.UpToDate, currentVersion, currentVersion, null, url, null);

            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            url = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? ReleasesUrl) : ReleasesUrl;
            string latest = NormalizeVersion(tag);
            if (CompareVersions(latest, NormalizeVersion(currentVersion)) <= 0)
                return new(UpdateStatus.UpToDate, latest, currentVersion, null, url, null);

            var assets = ReadAssets(root);
            string? zipName = UpdateCheck.SelectSelfContainedAsset(assets.Select(a => a.Name));
            string? zipUrl = zipName is null ? null : assets.FirstOrDefault(a => a.Name == zipName).Url;
            string? sumsUrl = assets.FirstOrDefault(a => a.Name == ChecksumsAsset).Url;
            if (zipName is null || zipUrl is null)
                return new(UpdateStatus.Failed, latest, currentVersion, null, url, "这次发布里没有自包含的 win-x64 压缩包");
            if (sumsUrl is null)
                return new(UpdateStatus.Failed, latest, currentVersion, null, url, "这次发布里没有 " + ChecksumsAsset);

            string destDir = Path.Combine(UpdatesRoot, latest);

            // 之前已经下过了：那个目录只在校验值对得上之后才写，所以里面的程序是验过的。
            // 再下一遍等于每次启动都重下几十兆。
            string? already = FindExecutable(destDir);
            if (already is not null)
                return new(UpdateStatus.Ready, latest, currentVersion, already, url, null);

            tempDir = Path.Combine(Path.GetTempPath(), "Clockwork-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, zipName);
            await DownloadFileAsync(zipUrl, zipPath, ct);

            string sums = await ReadTextAsync(sumsUrl, ct);
            var checksums = UpdateCheck.ParseChecksums(sums);
            if (!checksums.TryGetValue(zipName, out var expected))
                return new(UpdateStatus.Failed, latest, currentVersion, null, url, ChecksumsAsset + " 里没有 " + zipName);
            if (!UpdateCheck.MatchesChecksum(expected, UpdateCheck.ComputeSha256Hex(zipPath)))
                return new(UpdateStatus.VerifyFailed, latest, currentVersion, null, url, "下载到的文件与 " + expected + " 不符");

            Directory.CreateDirectory(destDir);
            ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
            string? exe = FindExecutable(destDir);
            if (exe is null)
                return new(UpdateStatus.Failed, latest, currentVersion, null, url, "下载到的压缩包里没有可执行文件");
            return new(UpdateStatus.Ready, latest, currentVersion, exe, url, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 仓库还没发过 Release：视作「没有比当前更新的版本」，提示已是最新（而非丢一个生硬的 404）。
            return new(UpdateStatus.UpToDate, currentVersion, currentVersion, null, url, null);
        }
        catch (Exception ex)
        {
            // 无网络/超时/限流等：如实回错，UI 提示「检查失败」。
            return new(UpdateStatus.Failed, "", currentVersion, null, url, ex.Message);
        }
        finally
        {
            // 下载的是临时副本：已经解压完了，%TEMP% 里给每个下过的版本都留一份几十兆不值得。
            if (tempDir.Length > 0)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static List<(string Name, string Url)> ReadAssets(JsonElement root)
    {
        var assets = new List<(string, string)>();
        if (!root.TryGetProperty("assets", out var arr) || arr.ValueKind != JsonValueKind.Array) return assets;
        foreach (var a in arr.EnumerateArray())
        {
            string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string url = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() ?? "" : "";
            if (name.Length > 0 && url.Length > 0) assets.Add((name, url));
        }
        return assets;
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken ct)
    {
        using var resp = await Downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await src.CopyToAsync(dst, ct);
    }

    private static async Task<string> ReadTextAsync(string url, CancellationToken ct)
    {
        using var resp = await Downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>解压出来的新程序；没有就 null。</summary>
    private static string? FindExecutable(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    // 去掉前导 v/V。
    public static string NormalizeVersion(string? v)
    {
        v = (v ?? "").Trim();
        if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v.Substring(1);
        return v;
    }

    // 语义化比较：a>b 返回 >0、a<b 返回 <0、相等 0。按 . - + 分段，每段取前导数字，缺段按 0。
    public static int CompareVersions(string? a, string? b)
    {
        var pa = ParseParts(a); var pb = ParseParts(b);
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] ParseParts(string? v)
        => (v ?? "").Split('.', '-', '+')
            .Select(s => int.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();
}
