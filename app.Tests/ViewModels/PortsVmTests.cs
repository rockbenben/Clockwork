using Clockwork.Engine;
using Clockwork.ViewModels;
using Xunit;

public class PortsVmTests
{
    private static List<PortEntry> Entries() => new()
    {
        new PortEntry { Port = 3000, Pid = 100, ProcessName = "node", Address = "127.0.0.1, ::1" },
        new PortEntry { Port = 8080, Pid = 200, ProcessName = "java", Address = "0.0.0.0" },
        new PortEntry { Port = 5432, Pid = 300, ProcessName = "postgres", Address = "127.0.0.1" },
        // dev 运行时但端口 <1024：白名单该收，「隐藏系统服务」那档因端口门槛收不到。
        new PortEntry { Port = 80, Pid = 400, ProcessName = "nginx", Address = "0.0.0.0" },
        // 普通桌面软件：不是系统服务，也不是 dev 运行时 —— 中间那档能看到，白名单看不到。
        new PortEntry { Port = 4001, Pid = 500, ProcessName = "QQ", Address = "127.0.0.1" },
        // Go/Rust 编译出来的任意名字：白名单必然漏掉，这是它已知的代价。
        new PortEntry { Port = 7100, Pid = 600, ProcessName = "myapi", Address = "0.0.0.0" },
        new PortEntry { Port = 49670, Pid = 700, ProcessName = "svchost", Address = "0.0.0.0" },
        new PortEntry { Port = 445, Pid = 4, ProcessName = "System", Address = "0.0.0.0" },
    };

    private static int[] Ports(IEnumerable<PortEntry> e) => e.Select(x => x.Port).OrderBy(p => p).ToArray();

    // —— Merge：双栈去重是这个页面的正确性核心，漏了同一个服务会出现两行 ——

    [Fact]
    public void Merge_folds_dual_stack_rows_of_one_process()
    {
        var r = PortReader.Merge(new[] { ("127.0.0.1", 3000, 100), ("::1", 3000, 100) });
        Assert.Single(r);
        Assert.Equal("127.0.0.1, ::1", r[0].Address);
        Assert.Equal(3000, r[0].Port);
        Assert.Equal(100, r[0].Pid);
    }

    [Fact]
    public void Merge_keeps_same_port_owned_by_different_pids()
        => Assert.Equal(2, PortReader.Merge(new[] { ("0.0.0.0", 3000, 100), ("0.0.0.0", 3000, 101) }).Count);

    [Fact]
    public void Merge_dedupes_identical_addresses()
    {
        var r = PortReader.Merge(new[] { ("0.0.0.0", 3000, 100), ("0.0.0.0", 3000, 100) });
        Assert.Single(r);
        Assert.Equal("0.0.0.0", r[0].Address);
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

    // 桌面软件（QQ）和任意命名的 Go 二进制（myapi）在这一档能看到 —— 白名单漏掉的东西
    // 不必一路退到「全部」里跟 svchost 一起翻。
    [Fact]
    public void HideSystem_keeps_desktop_apps_and_unknown_binaries()
        => Assert.Equal(new[] { 3000, 4001, 5432, 7100, 8080 }, Ports(PortsVm.Filter(Entries(), "", devOnly: false, showAll: false)));

    [Fact]
    public void ShowAll_keeps_everything_and_overrides_devOnly()
    {
        Assert.Equal(8, PortsVm.Filter(Entries(), "", devOnly: false, showAll: true).Count);
        Assert.Equal(8, PortsVm.Filter(Entries(), "", devOnly: true, showAll: true).Count);
    }

    // dev 白名单不设 1024 门槛：本地 nginx / caddy 起在 80 上是常事，卡端口号会把它们挡掉。
    [Fact]
    public void DevOnly_does_not_apply_the_1024_floor()
    {
        Assert.Contains(PortsVm.Filter(Entries(), "", devOnly: true, showAll: false), e => e.Port == 80);
        Assert.DoesNotContain(PortsVm.Filter(Entries(), "", devOnly: false, showAll: false), e => e.Port == 80);
    }

    [Fact]
    public void Kernel_pids_never_survive_either_filter()
    {
        Assert.DoesNotContain(PortsVm.Filter(Entries(), "", devOnly: true, showAll: false), e => e.Pid <= 4);
        Assert.DoesNotContain(PortsVm.Filter(Entries(), "", devOnly: false, showAll: false), e => e.Pid <= 4);
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
    public void Filter_by_process_name()
    {
        var r = PortsVm.Filter(Entries(), "NODE", devOnly: false, showAll: false);
        Assert.Single(r);
        Assert.Equal("node", r[0].ProcessName);
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
    public void Row_falls_back_to_pid_when_process_name_unknown()
        => Assert.Equal("PID 900", new PortRowVm(new PortEntry { Port = 3000, Pid = 900 }).ProcessName);

    [Fact]
    public void Row_url_points_at_localhost()
        => Assert.Equal("http://localhost:5173", new PortRowVm(new PortEntry { Port = 5173, Pid = 9 }).Url);

    // PID 0/4 是内核占位，杀必失败 → 菜单项要能灰掉（与系统启动项页 CanEdit 同一门控）
    [Fact]
    public void Row_cannot_kill_kernel_pids()
    {
        Assert.False(new PortRowVm(new PortEntry { Pid = 4 }).CanKill);
        Assert.True(new PortRowVm(new PortEntry { Pid = 5 }).CanKill);
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
