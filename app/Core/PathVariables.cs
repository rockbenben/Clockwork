using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 常用系统路径与环境变量辅助类：
// 1. 绝对路径与可移植 %VAR% 的自动折叠（ToPortable）；
// 2. 实时路径解析与变量未识别探测（Resolve）；
// 3. 常用文件夹与系统变量清单（供 UI 菜单与快速创建使用）。
public static class PathVariables
{
    public sealed record PathItem(string Name, string Expression, string Resolved);

    private static readonly Regex UnresolvedRegex = new(@"%[a-zA-Z0-9_()]+%", RegexOptions.Compiled);

    // 折叠优先级必须由细到粗：先匹配 AppData / LocalAppData，再匹配 UserProfile；
    // 否则 C:\Users\<谁>\AppData\... 会被抢先折成 %USERPROFILE%\AppData\...，失去 %APPDATA% 的独立性。
    private static readonly (string Token, Func<string> GetFolder)[] Mappings = new[]
    {
        ("%APPDATA%", (Func<string>)(() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))),
        ("%LOCALAPPDATA%", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ("%PROGRAMDATA%", () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
        ("%ProgramFiles(x86)%", () => Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? ""),
        ("%PROGRAMFILES%", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
        ("%USERPROFILE%", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        ("%WINDIR%", () => Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
    };

    /// <summary>
    /// 把绝对路径转为标准 %VAR% 环境变量表示；若不属于任何已知目录则原样返回。
    /// 例如：C:\Users\Admin\Desktop\test.exe → %USERPROFILE%\Desktop\test.exe
    /// </summary>
    public static string ToPortable(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return p;

        // 若已包含变量记号，无需重复折叠
        if (p.StartsWith('%')) return p;

        foreach (var (token, getFolder) in Mappings)
        {
            var folder = getFolder();
            if (string.IsNullOrEmpty(folder)) continue;

            if (p.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return token;

            if (p.Length > folder.Length
                && string.Compare(p, 0, folder, 0, folder.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (p[folder.Length] == '\\' || p[folder.Length] == '/'))
            {
                return token + '\\' + p.Substring(folder.Length + 1);
            }
        }

        return p;
    }

    /// <summary>
    /// 展开环境变量（简易重载）。
    /// </summary>
    public static string Resolve(string? path) => Resolve(path, out _);

    /// <summary>
    /// 展开环境变量，并检测是否包含未识别的环境变量（如拼错的 %NOTEXIST%）。
    /// </summary>
    public static string Resolve(string? path, out bool hasUnresolved)
    {
        var p = path ?? "";
        hasUnresolved = false;
        if (string.IsNullOrWhiteSpace(p)) return "";

        var expanded = Environment.ExpandEnvironmentVariables(p);
        if (UnresolvedRegex.IsMatch(expanded))
        {
            hasUnresolved = true;
        }
        return expanded;
    }

    /// <summary>
    /// 常用文件夹清单（桌面、下载、文档、用户启动项、公共启动项）
    /// </summary>
    public static IReadOnlyList<PathItem> GetCommonFolders(bool isZh = true)
    {
        return new[]
        {
            new PathItem(isZh ? "桌面" : "Desktop", @"%USERPROFILE%\Desktop", Resolve(@"%USERPROFILE%\Desktop", out _)),
            new PathItem(isZh ? "下载" : "Downloads", @"%USERPROFILE%\Downloads", Resolve(@"%USERPROFILE%\Downloads", out _)),
            new PathItem(isZh ? "文档" : "Documents", @"%USERPROFILE%\Documents", Resolve(@"%USERPROFILE%\Documents", out _)),
            new PathItem(isZh ? "开机启动文件夹 (当前用户)" : "Startup folder (current user)", @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup", Resolve(@"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup", out _)),
            new PathItem(isZh ? "公共启动文件夹 (所有用户)" : "Startup folder (all users)", @"%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup", Resolve(@"%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup", out _)),
        };
    }

    /// <summary>
    /// 核心系统环境变量清单
    /// </summary>
    public static IReadOnlyList<PathItem> GetSystemVariables(bool isZh = true)
    {
        return new[]
        {
            new PathItem(isZh ? "用户主目录" : "User profile", "%USERPROFILE%", Resolve("%USERPROFILE%", out _)),
            new PathItem(isZh ? "应用漫游配置 (Roaming)" : "Roaming AppData", "%APPDATA%", Resolve("%APPDATA%", out _)),
            new PathItem(isZh ? "应用本地配置 (Local)" : "Local AppData", "%LOCALAPPDATA%", Resolve("%LOCALAPPDATA%", out _)),
            new PathItem(isZh ? "公共数据 (ProgramData)" : "All users data", "%PROGRAMDATA%", Resolve("%PROGRAMDATA%", out _)),
            new PathItem(isZh ? "程序目录 (Program Files)" : "Program Files", "%PROGRAMFILES%", Resolve("%PROGRAMFILES%", out _)),
            new PathItem(isZh ? "Windows 目录" : "Windows folder", "%WINDIR%", Resolve("%WINDIR%", out _)),
            new PathItem(isZh ? "临时文件夹" : "Temp folder", "%TEMP%", Resolve("%TEMP%", out _)),
        };
    }
}
