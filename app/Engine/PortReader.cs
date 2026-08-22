using System.Diagnostics;
using System.Linq;
using Clockwork.Native;

namespace Clockwork.Engine;

// 端口页的一行：某个进程正在监听的一个端口。
public sealed class PortEntry
{
    public int Port { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = "";
    // 该 (进程, 端口) 的全部绑定地址，以 ", " 连接（"127.0.0.1, ::1"）。
    public string Address { get; set; } = "";
}

// 枚举监听端口并配上进程名。对应「系统启动项」页的 SystemStartupReader：
// 只读系统状态 + 一个破坏性动作（那边是删除自启项，这边是结束进程）。
public static class PortReader
{
    // 默认视图挡掉的系统噪音源。svchost 是关键的一个：它的 RPC 动态端口（49664 起）
    // 一开机就有十来个，全都 ≥1024，光靠端口号门槛拦不住。
    // ponytail: 进程名黑名单挡掉约 95% 的噪音；不够干净就改成判断 exe 是否在 C:\Windows 下。
    private static readonly HashSet<string> SystemOwners = new(StringComparer.OrdinalIgnoreCase)
    { "svchost", "System", "Idle", "lsass", "services", "wininit", "spoolsv", "dasHost", "wslservice", "vmms" };

    public static List<PortEntry> GetEntries()
    {
        var entries = Merge(TcpTable.Listeners());
        var names = ProcessNames();
        foreach (var e in entries) e.ProcessName = names.GetValueOrDefault(e.Pid, "");
        return entries;
    }

    // 同一个 (PID, 端口) 在 IPv4/IPv6 两张表里各出现一次是常态（Node 绑 localhost 会同时监听
    // 127.0.0.1 和 ::1），不合并的话同一个服务在列表里出现两行。
    // 同端口不同 PID 不合并 —— 那是真的两个进程（worker / SO_REUSEADDR），各自要能单独结束。
    public static List<PortEntry> Merge(IEnumerable<(string Address, int Port, int Pid)> rows)
        => rows.GroupBy(r => (r.Pid, r.Port))
               .Select(g => new PortEntry
               {
                   Port = g.Key.Port,
                   Pid = g.Key.Pid,
                   Address = string.Join(", ", g.Select(r => r.Address)
                                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                                .OrderBy(a => a, StringComparer.Ordinal)),
               })
               .OrderBy(e => e.Port).ThenBy(e => e.Pid)
               .ToList();

    // 「只看 dev 服务」的白名单。黑名单堆不出干净的列表——它只能挡系统服务，
    // 而实际噪音是 QQ / 微信 / 罗技驱动这类桌面软件，每台机器装的不一样，堆到最后
    // 是在维护别人机器上的软件清单。反过来列「什么东西会开 dev server」则是个有限集合。
    //
    // 已知代价：Go / Rust 编译出来的二进制叫什么名字都有（myapi.exe、__debug_bin12345.exe），
    // 白名单必然漏掉。这正是中间那一档（IsUserService）要留着的理由：漏掉的在那里找，
    // 不必一路退到「全部」里跟 svchost 一起翻。
    // ponytail: 进程名白名单；要连 Go/Rust 二进制一并认出来，得改成读 exe 路径
    // （落在你项目目录 / build 输出下的算 dev），代价是提权进程读不到路径。
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

    // 中间一档：只挡系统服务。<1024 是系统保留端口，PID 0/4 是内核占位。
    public static bool IsUserService(PortEntry e)
        => e.Port >= 1024 && e.Pid > 4 && !SystemOwners.Contains(e.ProcessName);

    // 白名单一档。有意不卡 1024 门槛：本地 nginx / caddy 起在 80 上是常事，
    // 白名单本身已经够窄，再叠一道端口门槛只会把真的 dev server 挡在外面。
    public static bool IsDevService(PortEntry e)
        => e.Pid > 4 && DevRuntimes.Contains(e.ProcessName);

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

    // 结束占用某端口的进程。entireProcessTree：dev server 常自己 spawn 子进程
    // （esbuild service、concurrently 的兄弟进程），只杀监听的那个会把它们留成孤儿。
    // 返回 "" = 已达成目标；否则是给用户看的失败原因。
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
