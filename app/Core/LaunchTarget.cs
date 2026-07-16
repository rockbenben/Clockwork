using System.IO;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 启动目标解析纯 helpers（命令行拆分 / 进程名推导 / 备用路径 / 自指判断）。
public static class LaunchTarget
{
    public sealed record CommandLine(string Target, string Arguments);

    // 拆 Run 键命令行 → Target/Arguments。首字符引号则取引号内为 Target；否则第一个空白前为 Target。
    public static CommandLine ParseCommandLine(string commandLine)
    {
        var s = commandLine ?? "";
        if (string.IsNullOrWhiteSpace(s)) return new CommandLine("", "");
        s = s.Trim();
        string target, rest;
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return new CommandLine(s.Trim('"'), "");
            target = s.Substring(1, end - 1);
            rest = s.Substring(end + 1).Trim();
        }
        else
        {
            int idx = s.IndexOfAny(new[] { ' ', '\t' });
            if (idx < 0) { target = s; rest = ""; }
            else { target = s.Substring(0, idx); rest = s.Substring(idx + 1).Trim(); }
        }
        return new CommandLine(target, rest);
    }

    // 目标 → 进程名（不含扩展名），供「已运行则激活窗口」判断。网址/文档/脚本/快捷方式（进程名与目标名不一致）返回 ''。
    public static string TargetProcessName(string target)
    {
        var t = target ?? "";
        if (string.IsNullOrWhiteSpace(t)) return "";
        if (Regex.IsMatch(t, @"^\s*[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)) return "";   // 网址
        string leaf;
        try { leaf = Path.GetFileName(t); } catch { leaf = t; }
        string ext;
        try { ext = Path.GetExtension(leaf); } catch { ext = ""; }
        if (ext == "" || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(leaf);
        return "";   // .ps1/.bat/.lnk/文档 等：进程名无法可靠推导，交给手填
    }

    // 备用路径解析：目标是完整路径且不存在时，返回 altTargets(每行一条) 里第一个存在的候选；都不存在则返回原目标。
    // 目标非完整路径(裸程序名/网址/文档关联)时原样返回。
    // 「存在」= 文件或目录（对齐旧 PS 版 Test-Path 语义）：打开文件夹的步骤（双机 D:\Work / E:\Work）目录候选也要能匹配。
    public static string ResolveLaunchTarget(string target, string altTargets)
    {
        var t = target ?? "";
        bool rooted;
        try { rooted = Path.IsPathRooted(t); } catch { rooted = false; }
        if (!rooted) return t;                       // 裸程序名/网址/文档：不动
        if (PathExists(t)) return t;                 // 主路径存在：用它
        foreach (var line in (altTargets ?? "").Split('\n'))
        {
            var c = line.Trim();
            if (c != "" && PathExists(c)) return c;  // 第一个存在的备用路径
        }
        return t;                                    // 都不存在：返回原目标（照常尝试/报错）
    }

    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    public const string PowerShellExe = "powershell.exe";

    // 目标是否 PowerShell 脚本(.ps1)——须经 powershell.exe 运行；直接 ShellExecute 会按文件关联进编辑器而非执行。
    public static bool IsPowerShellScript(string? target)
        => !string.IsNullOrEmpty(target) && Regex.IsMatch(target, @"\.ps1$", RegexOptions.IgnoreCase);

    // 构造 powershell.exe 运行 .ps1 的参数串：-NoProfile -ExecutionPolicy Bypass -File "路径" [附加参数]。
    public static string PowerShellFileArgs(string target, string? extraArgs = null)
        => $"-NoProfile -ExecutionPolicy Bypass -File \"{target}\"" + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);

    // 目标路径是否就是 Clockwork 自身（防开机自启动循环）。规范化后大小写不敏感比较。
    public static bool IsSelfTarget(string target, IEnumerable<string> selfPaths)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string tf;
        try { tf = Path.GetFullPath(target); } catch { return false; }
        foreach (var sp in selfPaths)
        {
            if (string.IsNullOrWhiteSpace(sp)) continue;
            string sf;
            try { sf = Path.GetFullPath(sp); } catch { continue; }
            if (string.Equals(tf, sf, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
