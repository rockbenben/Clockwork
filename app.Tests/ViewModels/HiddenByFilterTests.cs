using Clockwork.Engine;
using Clockwork.ViewModels;
using Xunit;

// **空列表上有两句话要说，而它们互斥**：「这台机器本来就没有」与「被你的筛选藏起来了」。
// 端口页先有了那一行（Ports_Empty），系统启动项页这轮补上同一句（System_Empty）。
//
// 判据本身住在 ViewModel 里，因为只有在这儿它才测得动：任何一台具体的机器一次只能给出
// 三种状态里的一种——没扫过、扫到零项、筛到零项——而截图矩阵跑在哪台机器上，
// 就只能拍到那一种。实测这台开发机扫到 0 个自启项，于是「筛到零行」那一帧在这里根本拍不出来。
// 把判断写成属性、把三种状态在测试里各造一次，比给 harness 加一条到不了的状态更实在。
public class HiddenByFilterTests
{
    private static SystemStartupItem Item(string name, bool canToggle) => new()
    {
        Name = name,
        Command = @"C:\" + name + ".exe",
        Type = canToggle ? "Registry" : "Policy",
        CanToggle = canToggle,
        Enabled = canToggle,
    };

    private static SystemStartupVm SystemVm(params SystemStartupItem[] items)
    {
        var vm = new SystemStartupVm((_, _) => "Ok", _ => { });
        vm.SetItems(items.ToList());
        return vm;
    }

    [Fact]
    public void System_before_the_scan_lands_says_nothing()
        => Assert.False(new SystemStartupVm((_, _) => "Ok", _ => { }).FilterHidesEverything);

    [Fact]
    public void System_with_nothing_on_the_machine_stays_silent()
        => Assert.False(SystemVm().FilterHidesEverything);

    [Fact]
    public void System_says_it_when_the_search_matches_nothing()
    {
        var vm = SystemVm(Item("Steam", true), Item("Discord", true));
        Assert.False(vm.FilterHidesEverything);       // 有行，不该说话
        vm.Search = "zzz-no-such-item";
        Assert.True(vm.FilterHidesEverything);
    }

    [Fact]
    public void System_says_it_when_the_readonly_switch_hides_every_row()
    {
        // 另一种成因，也是这一行该出现的时刻：项都在，只是默认档把只读项全藏了。
        var vm = SystemVm(Item("Policy X", false), Item("Policy Y", false));
        Assert.True(vm.FilterHidesEverything);
        vm.ShowReadOnly = true;
        Assert.False(vm.FilterHidesEverything);       // 露出来了，那句话就该闭嘴
    }

    private static PortEntry E(int port) => new()
    {
        Port = port,
        Address = "127.0.0.1",
        Owners = new List<PortOwner> { new() { Pid = 100 + port, ProcessName = "node" } },
    };

    private static PortsVm PortsVmWith(params int[] ports)
    {
        var vm = new PortsVm();
        vm.SetItems(ports.Select(E).ToList());
        return vm;
    }

    [Fact]
    public void Ports_before_the_first_read_says_nothing()
        => Assert.False(new PortsVm().FilterHidesEverything);

    [Fact]
    public void Ports_with_nothing_listening_stays_silent()
        => Assert.False(PortsVmWith().FilterHidesEverything);

    [Fact]
    public void Ports_says_it_when_the_filter_hides_every_listening_port()
    {
        var vm = PortsVmWith(3000, 8080);
        Assert.False(vm.FilterHidesEverything);
        vm.Search = "zzz-no-such-port";
        Assert.True(vm.FilterHidesEverything);
        vm.Search = "";
        Assert.False(vm.FilterHidesEverything);       // 清掉过滤词，那句话跟着收回
    }

    [Fact]
    public void The_hint_counts_the_hidden_denominator_not_the_empty_numerator()
    {
        // 那句话要报的是「机器上到底有多少」——用户拿它判断自己是不是被筛掉了东西。
        var vm = SystemVm(Item("Steam", true), Item("Discord", true), Item("Policy X", false));
        vm.Search = "zzz";
        Assert.Equal(0, vm.Rows.Count);
        Assert.Equal(3, vm.TotalCount);
    }

    // ── 形状钉（与 PortsEmptyStateTests 同一家法）──
    // 这一行在截图矩阵里拍不到：判据依赖「扫到了东西又全被筛掉」，而这台开发机扫到 0 项，
    // 那帧永远凑不出来。行为上面已经各造一次，这里再钉住界面两端别把元素或文案接错。

    private static string Src(params string[] parts)
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var all = new List<string> { d!.FullName, "app" };
        all.AddRange(parts);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(all.ToArray()));
    }

    [Fact]
    public void The_system_page_wires_the_same_shape_as_ports()
    {
        var xaml = Src("MainWindow.xaml");
        Assert.Contains("x:Name=\"SysEmptyHint\"", xaml);
        var cs = Src("MainWindow.xaml.cs");
        Assert.Contains("_system.FilterHidesEverything", cs);
        Assert.Contains("Lf(\"System_Empty\", _system.TotalCount)", cs);
    }

    [Fact]
    public void The_scan_state_keeps_the_headers_and_holds_the_count_back()
    {
        // 扫描期间整张表连表头一起收起，是这一轮改掉的那件事；计数在分母还没数完时报
        // 「0 / 0 项」也是。两句都钉在源码形状上：它们各自只在「正在扫」那一帧才看得见。
        var cs = Src("MainWindow.xaml.cs");
        Assert.DoesNotContain("GridSystem.Visibility = Visibility.Collapsed", cs);
        Assert.Contains("SysCount.Text = \"\";", cs);
    }
}
