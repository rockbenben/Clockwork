using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Clockwork.Engine;

// 检查更新：拉 GitHub Releases 最新 tag，与当前版本比对。用户点击触发（非后台轮询）。
public sealed record UpdateInfo(bool HasNewer, string Latest, string Current, string? Url, string? Error);

public static class UpdateChecker
{
    public const string RepoUrl = "https://github.com/rockbenben/Clockwork";
    public const string ReleasesUrl = "https://github.com/rockbenben/Clockwork/releases";
    private const string ApiUrl = "https://api.github.com/repos/rockbenben/Clockwork/releases/latest";

    // 单个共享实例：每次 new HttpClient 会另起 handler/socket、反复检查会在 TIME_WAIT 堆积。头信息固定、一次设好即可。
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Clockwork-update-check");   // GitHub API 要求带 UA
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static async Task<UpdateInfo> CheckAsync(string currentVersion)
    {
        try
        {
            var json = await Http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string url = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? ReleasesUrl) : ReleasesUrl;
            string latest = NormalizeVersion(tag);
            bool newer = CompareVersions(latest, NormalizeVersion(currentVersion)) > 0;
            return new UpdateInfo(newer, string.IsNullOrEmpty(latest) ? tag : latest, currentVersion, url, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 仓库还没发过 Release：视作「没有比当前更新的版本」，提示已是最新（而非丢一个生硬的 404）。
            return new UpdateInfo(false, currentVersion, currentVersion, null, null);
        }
        catch (Exception ex)
        {
            // 无网络/超时/限流等：如实回错，UI 提示「检查失败」。
            return new UpdateInfo(false, "", currentVersion, null, ex.Message);
        }
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
