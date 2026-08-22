using Clockwork.Engine;
using Clockwork.ViewModels;
using Xunit;

public class PortsVmTests
{
    private static PortEntry E(int port, string address, params (int Pid, string Name)[] owners) => new()
    {
        Port = port,
        Address = address,
        Owners = owners.Select(o => new PortOwner { Pid = o.Pid, ProcessName = o.Name }).ToList(),
    };

    private static List<PortEntry> Entries() => new()
    {
        E(3000, "127.0.0.1, ::1", (100, "node")),
        E(8080, "0.0.0.0", (200, "java")),
        E(5432, "127.0.0.1", (300, "postgres")),
        // dev 运行时但端口 <1024：白名单该收，「隐藏系统服务」那档因端口门槛收不到。
        E(80, "0.0.0.0", (400, "nginx")),
        // 普通桌面软件：不是系统服务，也不是 dev 运行时 —— 中间那档能看到，白名单看不到。
        E(4001, "127.0.0.1", (500, "QQ")),
        // Go/Rust 编译出来的任意名字：白名单必然漏掉，这是它已知的代价。
        E(7100, "0.0.0.0", (600, "myapi")),
        E(49670, "0.0.0.0", (700, "svchost")),
        E(445, "0.0.0.0", (4, "System")),
    };

    private static int[] Ports(IEnumerable<PortEntry> e) => e.Select(x => x.Port).OrderBy(p => p).ToArray();

    // —— Merge：单位是端口 ——

    [Fact]
    public void Merge_folds_dual_stack_rows_of_one_process()
    {
        var r = PortReader.Merge(new[] { ("127.0.0.1", 3000, 100), ("::1", 3000, 100) });
        Assert.Single(r);
        Assert.Equal("127.0.0.1, ::1", r[0].Address);
        Assert.Equal(3000, r[0].Port);
        Assert.Equal(new[] { 100 }, r[0].Owners.Select(o => o.Pid));
    }

    // Windows 的 SO_REUSEADDR 允许第二个进程绑到已占用的地址上（后绑的接管新连接）。
    // 按端口分行才看得出「29029 上有两个东西在打架」，按进程分行则是两条各自正常的行。
    [Fact]
    public void Merge_keeps_every_pid_holding_one_port_in_a_single_row()
    {
        var r = PortReader.Merge(new[] { ("127.0.0.1", 29029, 8588), ("127.0.0.1", 29029, 9800) });
        Assert.Single(r);
        Assert.Equal(new[] { 8588, 9800 }, r[0].Owners.Select(o => o.Pid));
    }

    [Fact]
    public void Merge_dedupes_identical_addresses_and_pids()
    {
        var r = PortReader.Merge(new[] { ("0.0.0.0", 3000, 100), ("0.0.0.0", 3000, 100) });
        Assert.Single(r);
        Assert.Equal("0.0.0.0", r[0].Address);
        Assert.Single(r[0].Owners);
    }

    [Fact]
    public void Merge_sorts_by_port()
    {
        var r = PortReader.Merge(new[] { ("0.0.0.0", 8080, 1), ("0.0.0.0", 3000, 2) });
        Assert.Equal(new[] { 3000, 8080 }, r.Select(e => e.Port));
    }

    // —— 三档视图：只看 dev 服务 / 隐藏系统服务（默认兜底）/ 全部 ——

    [Fact]
    public void DevOnly_keeps_dev_runtimes_only()
        => Assert.Equal(new[] { 80, 3000, 5432, 8080 }, Ports(PortsVm.Filter(Entries(), "", devOnly: true, showAll: false)));

    [Fact]
    public void HideSystem_keeps_desktop_apps_and_unknown_binaries()
        => Assert.Equal(new[] { 3000, 4001, 5432, 7100, 8080 }, Ports(PortsVm.Filter(Entries(), "", devOnly: false, showAll: false)));

    [Fact]
    public void ShowAll_keeps_everything_and_overrides_devOnly()
    {
        Assert.Equal(8, PortsVm.Filter(Entries(), "", devOnly: false, showAll: true).Count);
        Assert.Equal(8, PortsVm.Filter(Entries(), "", devOnly: true, showAll: true).Count);
    }

    [Fact]
    public void DevOnly_does_not_apply_the_1024_floor()
    {
        Assert.Contains(PortsVm.Filter(Entries(), "", devOnly: true, showAll: false), e => e.Port == 80);
        Assert.DoesNotContain(PortsVm.Filter(Entries(), "", devOnly: false, showAll: false), e => e.Port == 80);
    }

    // 判据是「任一占用者」：一个端口只要有一个正经进程占着就该看得见，
    // 不能因为旁边还挂着个内核占位或系统服务就整行消失。
    [Fact]
    public void A_single_qualifying_owner_is_enough_to_show_the_row()
    {
        var mixed = new List<PortEntry> { E(3000, "0.0.0.0", (4, "System"), (900, "node")) };
        Assert.Single(PortsVm.Filter(mixed, "", devOnly: true, showAll: false));
        Assert.Single(PortsVm.Filter(mixed, "", devOnly: false, showAll: false));
    }

    [Fact]
    public void Kernel_only_rows_never_survive_either_filter()
    {
        var kernel = new List<PortEntry> { E(445, "0.0.0.0", (4, "System")) };
        Assert.Empty(PortsVm.Filter(kernel, "", devOnly: true, showAll: false));
        Assert.Empty(PortsVm.Filter(kernel, "", devOnly: false, showAll: false));
    }

    // —— 搜索 ——

    [Fact]
    public void Filter_by_port_number()
    {
        var r = PortsVm.Filter(Entries(), "3000", devOnly: false, showAll: false);
        Assert.Single(r);
        Assert.Equal(3000, r[0].Port);
    }

    [Fact]
    public void Filter_by_process_name_matches_any_owner()
    {
        var mixed = new List<PortEntry> { E(3000, "0.0.0.0", (100, "python"), (101, "node")) };
        Assert.Single(PortsVm.Filter(mixed, "NODE", devOnly: false, showAll: false));
        Assert.Single(PortsVm.Filter(mixed, "python", devOnly: false, showAll: false));
    }

    // 搜索是在当前那一档之上再筛，不是绕过它：勾着「只看 dev 服务」时搜 QQ 搜不出来。
    [Fact]
    public void Filter_search_does_not_bypass_the_current_view()
    {
        Assert.Empty(PortsVm.Filter(Entries(), "svchost", devOnly: false, showAll: false));
        Assert.Empty(PortsVm.Filter(Entries(), "QQ", devOnly: true, showAll: false));
        Assert.Single(PortsVm.Filter(Entries(), "QQ", devOnly: false, showAll: false));
    }

    // —— 行视图 ——

    [Fact]
    public void Row_collapses_repeated_process_names()
        => Assert.Equal("python ×2", PortRowVm.ProcessLabel(E(29029, "127.0.0.1", (8588, "python"), (9800, "python"))));

    [Fact]
    public void Row_lists_distinct_process_names_side_by_side()
        => Assert.Equal("python, node", PortRowVm.ProcessLabel(E(3000, "0.0.0.0", (1, "python"), (2, "node"))));

    [Fact]
    public void Row_falls_back_to_pid_when_process_name_unknown()
        => Assert.Equal("PID 900", PortRowVm.ProcessLabel(E(3000, "0.0.0.0", (900, ""))));

    [Fact]
    public void Row_lists_every_pid()
        => Assert.Equal("8588, 9800", new PortRowVm(E(29029, "127.0.0.1", (8588, "python"), (9800, "python"))).PidText);

    // 确认框要逐个点名——释放一个端口可能带走不止一个进程。
    [Fact]
    public void Row_names_every_owner_for_the_confirmation()
        => Assert.Equal("python (8588)、python (9800)",
                        new PortRowVm(E(29029, "127.0.0.1", (8588, "python"), (9800, "python"))).OwnersText);

    [Fact]
    public void Row_url_points_at_localhost()
        => Assert.Equal("http://localhost:5173", new PortRowVm(E(5173, "127.0.0.1", (9, "node"))).Url);

    // —— 来源：「项目 · 入口」——

    // 工作目录就是项目根，这是最可靠的一刀：tools\serve.py 是相对路径，
    // 除了 cwd 没有任何办法知道它属于哪个项目。
    [Fact]
    public void Source_takes_the_project_from_the_working_directory()
        => Assert.Equal("029-ai-job-search-cn · serve.py", PortOwner.SourceLabel(
            @"C:\Python314\python.exe tools\serve.py",
            @"D:\GitHub\Projects\365\029-ai-job-search-cn\"));

    [Fact]
    public void Source_skips_switches_when_picking_the_entry()
        => Assert.Equal("035-shiye · cdp-proxy.mjs", PortOwner.SourceLabel(
            @"C:\node.exe --enable-source-maps C:\Users\X\.claude\skills\web-access\scripts\cdp-proxy.mjs",
            @"D:\GitHub\Projects\365\035-shiye"));

    // cwd 落在 C:\Windows 这类目录时说明不了任何事，退回按命令行路径推断
    [Fact]
    public void Source_ignores_anonymous_working_directories()
        => Assert.Equal("etlp-mpv-py-embed-win64 · embyToLocalPlayer.py", PortOwner.SourceLabel(
            @"python.exe D:\tools\etlp-mpv-py-embed-win64\embyToLocalPlayer.py", @"C:\WINDOWS\"));

    // 退路一：node_modules 前一段是项目名
    [Fact]
    public void Source_falls_back_to_the_segment_before_node_modules()
        => Assert.Equal("web-tools-by-ai · start-server.js", PortOwner.SourceLabel(
            @"C:\node.exe D:\GitHub\tools\web-tools-by-ai\node_modules\next\dist\server\lib\start-server.js", ""));

    // 退路二：跳过 scripts / bin / dist 这类通用容器目录，往上找真正的身份
    [Fact]
    public void Source_skips_generic_container_directories()
        => Assert.Equal("web-access · cdp-proxy.mjs", PortOwner.SourceLabel(
            @"C:\node.exe C:\Users\X\.claude\skills\web-access\scripts\cdp-proxy.mjs", ""));

    // .bin\..\next 这种回溯路径末段是 ".."，得退一格取真正的名字
    [Fact]
    public void Source_handles_dot_dot_in_bin_shims()
        => Assert.Equal("web-tools-by-ai · next", PortOwner.SourceLabel(
            @"""C:\node.exe"" ""D:\GitHub\tools\web-tools-by-ai\node_modules\.bin\..\next\dist\bin\next"" build", ""));

    // 编译型二进制没有参数：入口就是 exe 自己，项目取自 cwd
    [Fact]
    public void Source_uses_the_exe_itself_when_there_are_no_args()
        => Assert.Equal("api · myapi.exe", PortOwner.SourceLabel(@"D:\Projects\api\myapi.exe", @"D:\Projects\api"));

    [Fact]
    public void Source_is_empty_when_nothing_is_readable()
        => Assert.Equal("", PortOwner.SourceLabel("", ""));

    // —— 同一进程占多个端口 ——
    // 释放端口杀的是进程，所以兄弟端口会一并没。不说清楚就是把破坏半径藏起来。

    [Fact]
    public void Rows_know_the_other_ports_their_process_holds()
    {
        var vm = new PortsVm { ShowAll = true };
        vm.SetItems(new List<PortEntry>
        {
            E(3000, "0.0.0.0", (40784, "node")),
            E(10290, "127.0.0.1", (40784, "node")),
            E(10291, "127.0.0.1", (40784, "node")),
            E(5432, "127.0.0.1", (300, "postgres")),
        });
        var r3000 = vm.Rows.Single(r => r.PortText == "3000");
        Assert.Equal(new[] { 10290, 10291 }, r3000.SiblingPorts);
        Assert.Equal("3000 +2", r3000.PortLabel);
        Assert.Equal("10290, 10291", r3000.SiblingPortsText);
        // 独占一个端口的行不该多出任何标记
        Assert.Equal("5432", vm.Rows.Single(r => r.PortText == "5432").PortLabel);
    }

    // 兄弟端口必须从**未过滤**的全集算：被当前档位挡掉的端口看不见，但释放时照样会没，
    // 按可见行算会把破坏半径报小。
    [Fact]
    public void Sibling_ports_come_from_the_unfiltered_set()
    {
        var vm = new PortsVm { DevOnly = false, ShowAll = false };
        vm.SetItems(new List<PortEntry>
        {
            E(3000, "0.0.0.0", (500, "node")),
            E(80, "0.0.0.0", (500, "node")),   // <1024，当前档位看不见
        });
        var visible = Assert.Single(vm.Rows);
        Assert.Equal("3000", visible.PortText);
        Assert.Equal(new[] { 80 }, visible.SiblingPorts);   // 但它仍在破坏半径里
    }

    // —— 自动刷新：数据没变就不能碰 UI ——
    // 每 5 秒重建一次列表会丢选中行、丢滚动位置、还闪。签名相同就整轮空转。

    [Fact]
    public void Signature_is_stable_for_identical_scans()
        => Assert.Equal(PortsVm.Signature(Entries()), PortsVm.Signature(Entries()));

    [Fact]
    public void Signature_changes_when_a_port_appears()
    {
        var more = Entries();
        more.Add(E(5173, "127.0.0.1", (999, "node")));
        Assert.NotEqual(PortsVm.Signature(Entries()), PortsVm.Signature(more));
    }

    [Fact]
    public void Signature_changes_when_an_owner_changes()
    {
        var other = Entries();
        other[0] = E(3000, "127.0.0.1, ::1", (101, "node"));   // 同端口，换了 PID
        Assert.NotEqual(PortsVm.Signature(Entries()), PortsVm.Signature(other));
    }

    // 行对象在无变化的那一轮必须原样保留 —— 重建就是丢选中行
    [Fact]
    public void SetItems_leaves_rows_untouched_when_nothing_changed()
    {
        var vm = new PortsVm();
        vm.SetItems(Entries());
        var before = vm.Rows.ToList();
        vm.SetItems(Entries());
        Assert.Equal(before, vm.Rows);   // 同一批实例，不是等值的新对象
    }

    [Fact]
    public void SetItems_rebuilds_when_something_changed()
    {
        var vm = new PortsVm();
        vm.SetItems(Entries());
        var before = vm.Rows.ToList();
        var more = Entries();
        more.Add(E(5173, "127.0.0.1", (999, "node")));
        vm.SetItems(more);
        Assert.NotEqual(before, vm.Rows);
    }

    [Fact]
    public void Row_cannot_free_a_kernel_only_port()
    {
        Assert.False(new PortRowVm(E(445, "0.0.0.0", (4, "System"))).CanKill);
        Assert.True(new PortRowVm(E(445, "0.0.0.0", (4, "System"), (500, "nginx"))).CanKill);
    }

    [Fact]
    public void SetItems_populates_rows_for_the_current_view()
    {
        var vm = new PortsVm { DevOnly = true };
        vm.SetItems(Entries());
        Assert.Equal(4, vm.Rows.Count);
        vm.DevOnly = false;
        Assert.Equal(5, vm.Rows.Count);
        vm.ShowAll = true;
        Assert.Equal(8, vm.Rows.Count);
        vm.Search = "8080";
        Assert.Single(vm.Rows);
    }
}
