using System.Diagnostics;
using System.IO;
using System.Linq;
using Clockwork.Native;

namespace Clockwork.Engine;

// 占用某个端口的一个进程。
public sealed class PortOwner
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = "";

    // 命令行与当前工作目录，都来自同一次 PEB 读取（见 Native.ProcessInfo）。
    public string CommandLine { get; set; } = "";
    public string WorkingDir { get; set; } = "";

    // 名字取不到（受保护进程）时退回 PID：确认框里总得留个能指认它的东西。
    public string Display => ProcessName.Length > 0 ? $"{ProcessName} ({Pid})" : $"PID {Pid}";

    // 「这个端口到底是谁」。光看进程名，两个 node 分不出来；
    // 而「029-ai-job-search-cn · serve.py」和「035-shiye · cdp-proxy.mjs」一眼就能分。
    public string Source => SourceLabel(CommandLine, WorkingDir);

    // 工作目录不具身份的那些：从这些目录起的进程，cwd 说明不了任何事，得退回看命令行路径。
    private static readonly HashSet<string> AnonymousDirs = new(StringComparer.OrdinalIgnoreCase)
    { "windows", "system32", "syswow64", "program files", "program files (x86)", "temp", "" };

    // 通用容器目录，本身不构成身份：路径切到它们就再往上找一层。
    private static readonly HashSet<string> GenericDirs = new(StringComparer.OrdinalIgnoreCase)
    { "scripts", "script", "bin", "dist", "build", "src", "lib", "out", "app", "server", "node_modules", ".bin" };

    // 身份 = 「项目 · 入口」。
    //   项目：优先取工作目录的最后一段 —— 实测这是最可靠的一刀，因为 dev server 的 cwd
    //         就是项目根（tools\serve.py 这种相对路径，除了 cwd 没有别的办法知道是哪个项目）。
    //         cwd 落在 C:\Windows 这类无身份目录时，退回按命令行路径推断。
    //   入口：命令行里第一个非开关参数的文件名。
    // 两者都没有就返回空串，界面上这一列留白，不编造。
    public static string SourceLabel(string cmdLine, string workingDir)
    {
        string entry = "";
        foreach (var a in SplitArgs(cmdLine).Skip(1))
        {
            if (a.Length == 0 || a[0] == '-' || a[0] == '/') continue;
            entry = LastMeaningful(a);
            break;
        }
        // 没有参数（编译型二进制）：入口就是 exe 自己
        if (entry.Length == 0)
        {
            var exe = SplitArgs(cmdLine).FirstOrDefault() ?? "";
            entry = LastMeaningful(exe);
        }

        string project = Project(workingDir);
        if (project.Length == 0) project = ProjectFromPath(cmdLine);

        if (project.Length > 0 && entry.Length > 0) return $"{project} · {entry}";
        return project.Length > 0 ? project : entry;
    }

    // 工作目录的最后一段，除非它是 C:\Windows 这类不具身份的目录。
    private static string Project(string workingDir)
    {
        var parts = Segments(workingDir);
        if (parts.Count == 0) return "";
        var last = parts[^1];
        // 盘符根（"D:"）也不算身份
        return AnonymousDirs.Contains(last) || last.EndsWith(':') ? "" : last;
    }

    // 退路：cwd 不可用时从命令行路径里找项目名。node_modules 前一段是最常见的一刀
    //   …\GitHub\tools\web-tools-by-ai\node_modules\next\…\start-server.js → web-tools-by-ai
    // 否则取入口文件往上第一个非通用目录（…\.claude\skills\web-access\scripts\x.mjs → web-access）。
    private static string ProjectFromPath(string cmdLine)
    {
        foreach (var a in SplitArgs(cmdLine).Skip(1))
        {
            if (a.Length == 0 || a[0] == '-' || a[0] == '/') continue;
            var parts = Segments(a);
            int nm = parts.FindIndex(p => p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
            if (nm > 0) return parts[nm - 1];
            // 去掉文件名，再往上跳过通用容器目录
            for (int i = parts.Count - 2; i >= 0; i--)
                if (!GenericDirs.Contains(parts[i]) && !parts[i].EndsWith(':')) return parts[i];
            return "";
        }
        return "";
    }

    private static string LastMeaningful(string path)
    {
        var parts = Segments(path);
        for (int i = parts.Count - 1; i >= 0; i--)
            if (parts[i] != "..") return parts[i];
        return "";
    }

    private static List<string> Segments(string path)
        => (path ?? "").Split('\\', '/').Where(p => p.Length > 0 && p != ".").ToList();

    // 最小可用的命令行切分：只认双引号包裹与空格分隔。不处理 Windows 那套反斜杠转义引号的
    // 规则 —— 这里只为了认出「第一个参数是什么」，不是要重建 argv。
    public static List<string> SplitArgs(string cmd)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        foreach (var ch in cmd ?? "")
        {
            if (ch == '"') { q = !q; continue; }
            if (!q && char.IsWhiteSpace(ch)) { if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); } continue; }
            cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }
}

// 端口页的一行：一个正在被监听的端口，连同占用它的全部进程。
//
// 单位是端口而不是进程：一个端口能被多个进程同时绑住。Windows 的 SO_REUSEADDR 允许
// 后来者直接绑到一个已被占用的地址上（Linux 上这个选项基本只影响 TIME_WAIT，语义完全不同），
// 而哪一个真正收新连接微软文档写的是 indeterminate——实测过：连续三次连接全部落到
// **先**启动的那个进程，按启动时间猜“后绑的赢”会猜反。
// Python 的 HTTPServer 默认开着这个选项，所以同一个脚本跑两遍不报错，早那个静静变成收不到连接的僵尸。
// 按进程分行会把「29029 上有两个东西在打架」呈现成两条各自正常的行，
// 而人想知道的、想操作的都是「这个端口」。
public sealed class PortEntry
{
    public int Port { get; set; }
    public List<PortOwner> Owners { get; set; } = new();
    // 该端口的全部绑定地址，以 ", " 连接（"127.0.0.1, ::1"）。
    public string Address { get; set; } = "";
}

// 枚举监听端口并配上进程名。对应「系统启动项」页的 SystemStartupReader：
// 只读系统状态 + 一个破坏性动作（那边是删除自启项，这边是结束占用进程）。
public static class PortReader
{
    // 中间档（IsUserService）挡掉的系统噪音源。svchost 是关键的一个：它的 RPC 动态端口（49664 起）
    // 一开机就有十来个，全都 ≥1024，光靠端口号门槛拦不住。
    // ponytail: 进程名黑名单挡掉约 95% 的噪音；不够干净就改成判断 exe 是否在 C:\Windows 下。
    private static readonly HashSet<string> SystemOwners = new(StringComparer.OrdinalIgnoreCase)
    { "svchost", "System", "Idle", "lsass", "services", "wininit", "spoolsv", "dasHost", "wslservice", "vmms" };

    // 「只看项目服务」的**补充**判据（主判据是工作目录里的项目标记，见 IsDevService）。
    // 它存在的理由只有一个：提权 / 受保护进程读不到 cwd（实测 42 个监听端口里有一半读不到），
    // 只靠主判据会把那种情况下的 dev server 一并藏掉。
    // 它不区分「你的 dev server」和「工具链常驻的服务」，也不区分项目内外——
    // 有意不去猜：命令行已经显示在「来源」列里，看一眼就知道是谁，
    // 比一条会出错、会把你自己的服务藏起来的规则可靠。
    private static readonly HashSet<string> DevRuntimes = new(StringComparer.OrdinalIgnoreCase)
    {
        // JS/TS：Vite / Next / webpack-dev-server / Nest / Storybook 全在 node 名下
        "node", "bun", "deno",
        // Python：uvicorn / gunicorn / Flask / Django / mkdocs 都活在 python.exe 里，不单独出名
        "python", "pythonw", "py",
        "dotnet", "iisexpress", "func",
        // JVM：Spring Boot / Gradle 守护进程 / Elasticsearch 都是这个名字
        "java", "javaw",
        "ruby", "php",
        // 本地 web / 静态站
        "nginx", "httpd", "caddy", "hugo", "air",
        // 本地起的数据库
        "postgres", "mysqld", "mongod", "redis-server", "sqlservr",
        // Docker 映射出来的端口，属主不是容器而是这几个中转进程
        "com.docker.backend", "wslrelay", "wslhost", "vpnkit",
        // 隧道 / 本地大模型
        "ngrok", "cloudflared", "ollama",
    };

    // 进程名 + 命令行 + 工作目录一次取齐。同步就够：实测整轮 3ms
    //（读 PEB 每个进程约 0.1ms；曾用 WMI 做同一件事要 413ms，只好放后台，现在不必了）。
    // 同一个 PID 可能占多个端口，按 PID 去重后只读一次。
    public static List<PortEntry> GetEntries()
    {
        var entries = Merge(TcpTable.Listeners());
        var names = ProcessNames();
        var info = new Dictionary<int, (string Cmd, string Cwd)>();
        foreach (var e in entries)
            foreach (var o in e.Owners)
            {
                o.ProcessName = names.GetValueOrDefault(o.Pid, "");
                if (!info.TryGetValue(o.Pid, out var v)) info[o.Pid] = v = ProcessInfo.Read(o.Pid);
                o.CommandLine = v.Cmd;
                o.WorkingDir = v.Cwd;
            }
        return entries;
    }

    // 按端口归并。同一个端口下的多来源会合并到一行：
    //   · IPv4 / IPv6 两张表各出现一次（Node 绑 localhost 会同时监听 127.0.0.1 和 ::1）
    //   · 同端口多进程（SO_REUSEADDR，见 PortEntry 的说明）
    //   · 按网卡逐个监听的多地址行（NetBIOS 的 139 就是四个）
    public static List<PortEntry> Merge(IEnumerable<(string Address, int Port, int Pid)> rows)
        => rows.GroupBy(r => r.Port)
               .Select(g => new PortEntry
               {
                   Port = g.Key,
                   Owners = g.Select(r => r.Pid).Distinct().OrderBy(p => p)
                             .Select(p => new PortOwner { Pid = p }).ToList(),
                   Address = string.Join(", ", g.Select(r => r.Address)
                                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                                .OrderBy(a => a, StringComparer.Ordinal)),
               })
               .OrderBy(e => e.Port)
               .ToList();

    // 中间一档：只挡系统服务。<1024 是系统保留端口，PID 0/4 是内核占位。
    // 判据落在「任一占用者」上而不是「全部占用者」：一个端口只要有一个正经进程占着就该看得见，
    // 不能因为旁边还挂着个内核占位就整行消失。
    public static bool IsUserService(PortEntry e)
        => e.Port >= 1024 && e.Owners.Any(o => o.Pid > 4 && !SystemOwners.Contains(o.ProcessName));

    // 项目标记：沿工作目录往上找到其中任一个，就说明这个进程是从你在写的代码里跑起来的。
    private static readonly string[] ProjectMarkers =
        { ".git", "package.json", "pyproject.toml", "go.mod", "Cargo.toml", "requirements.txt", "pom.xml", "build.gradle", "composer.json", "Gemfile" };

    // 同一个目录反复查没意义（一个进程占八个端口就要查八次）。目录中途新增/删除 .git
    // 不会被看到，但那件事在一次运行里几乎不发生，重启即可。
    private static readonly Dictionary<string, bool> ProjectDirCache = new(StringComparer.OrdinalIgnoreCase);

    // 工作目录（或其上方几层）里有项目标记吗。往上最多找 6 层：
    // 够覆盖 monorepo 的 packages/foo/ 这种，又不至于一路爬到盘符根把整个 D: 当成项目。
    public static bool LooksLikeProject(string workingDir)
    {
        if (string.IsNullOrEmpty(workingDir)) return false;
        if (ProjectDirCache.TryGetValue(workingDir, out var hit)) return hit;
        bool found = false;
        var d = workingDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(d) && !found; i++)
        {
            foreach (var m in ProjectMarkers)
            {
                var probe = Path.Combine(d, m);
                try { if (Directory.Exists(probe) || File.Exists(probe)) { found = true; break; } }
                catch { }   // 路径非法 / 无权限：当作没找到
            }
            try { d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch { break; }
        }
        return ProjectDirCache[workingDir] = found;
    }

    // 白名单一档，两个判据取并集：
    //   工作目录里有项目标记 —— 主判据。零配置，不看进程名，所以 Go/Rust 编译出的
    //     任意命名二进制也能认出来——那正是纯白名单已知的盲区。实测在本机 42 个监听端口上
    //     零误报零漏报：QQ / 微信 / Discord / 罗技 / NVIDIA 全部落选，三个项目服务全中。
    //   进程名在白名单里 —— 补充。提权进程读不到 cwd（实测 42 个里有一半读不到），
    //     只靠主判据会把那种情况下的 dev server 一并藏掉。
    // 有意不卡 1024 门槛：本地 nginx / caddy 起在 80 上是常事。
    public static bool IsDevService(PortEntry e)
        => e.Owners.Any(o => o.Pid > 4 && (LooksLikeProject(o.WorkingDir) || DevRuntimes.Contains(o.ProcessName)));

    // 一次快照拿全部 PID→进程名，而不是逐行 GetProcessById：后者每行开一个句柄，
    // 几十行下来要么忘了 Dispose 漏句柄，要么写一圈 using 把代码撑开。
    private static Dictionary<int, string> ProcessNames()
    {
        var d = new Dictionary<int, string>();
        foreach (var p in Process.GetProcesses())
        {
            try { d[p.Id] = p.ProcessName; }
            catch { }            // 受保护进程读不到名字：界面回落显示 PID
            finally { p.Dispose(); }
        }
        return d;
    }

    // 释放端口：结束占用它的每一个进程。返回 "" = 端口已腾出；否则是给用户看的失败原因
    // （多个失败以分号连接——一次动作可能牵涉多个进程，只报第一个会让人以为其余都成了）。
    public static string FreePort(PortEntry entry)
    {
        var errors = new List<string>();
        foreach (var o in entry.Owners.Where(o => o.Pid > 4))
        {
            var err = Kill(o.Pid);
            if (err != "") errors.Add($"{o.Display}: {err}");
        }
        return string.Join("; ", errors);
    }

    // entireProcessTree：dev server 常自己 spawn 子进程（esbuild service、concurrently 的
    // 兄弟进程），只杀监听的那个会把它们留成孤儿。
    public static string Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(2000);
            return "";
        }
        // 进程在「扫描快照」与「点下菜单」之间已经退出（终端里刚 Ctrl+C 掉）——
        // 目标状态已达成，报错反而莫名其妙。刷新后那一行自会消失。
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        // 主要是提权进程的拒绝访问，以及 entireProcessTree 部分子进程杀不掉时的 AggregateException。
        catch (Exception ex) { return ex.Message; }
    }
}
