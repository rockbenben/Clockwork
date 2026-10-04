using System.IO;
using Xunit;

// 端口页「这一档被筛空了」的叠加提示——形状钉死的三条：
// 一、界面里必须有这个提示元素（PortsEmptyHint），而且默认 Collapsed：它只在空态出现，
//    常态列表上不能有一行幽灵文案和表头抢位。
// 二、代码里出现条件必须是「行数为零 **且** 总数不为零」——总数为 0 时空白就是事实本身，
//    那句「{0} 个端口在监听」在那个状态下说的是反话。
// 三、文案走 Ports_Empty 带总数的形式，截图矩阵里有专门拍这条路的变体。
//
// 与 EmptyPanelCtaTests 同一家法：这条 UI 路只在「筛空」的那一刻存在，
// 真机截图矩阵大多拍不到它，靠一次合成变体 + 源码形状各钉一半。
public class PortsEmptyStateTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var all = new System.Collections.Generic.List<string> { d!.FullName, "app" };
        all.AddRange(parts);
        return File.ReadAllText(Path.Combine(all.ToArray()));
    }

    [Fact]
    public void The_hint_lives_in_the_list_panel_and_starts_collapsed()
    {
        var xaml = Src("MainWindow.xaml");
        Assert.Contains("x:Name=\"PortsEmptyHint\"", xaml);
        Assert.Contains("Visibility=\"Collapsed\"", xaml);
    }

    [Fact]
    public void The_hint_shows_only_when_something_is_hidden()
    {
        // 这一条原本钉的是 MainWindow.xaml.cs 里那句字面表达式。判据后来搬进了 PortsVm
        // （系统启动项页要共用同一条判断），只钉位置就成了假证人：表达式还在，只是换了地方。
        // 所以两头各钉一刀——VM 里那两个条件还在，视图确实问的是这个属性。
        var vm = Src("ViewModels", "PortsVm.cs");
        Assert.Contains("Rows.Count == 0 && _all.Count > 0", vm);
        var cs = Src("MainWindow.xaml.cs");
        Assert.Contains("ports.FilterHidesEverything", cs);
        Assert.Contains("Lf(\"Ports_Empty\", ports.TotalCount)", cs);
    }

    [Fact]
    public void The_shots_matrix_has_a_variant_that_empties_the_view()
    {
        var dev = Src("DevChecks.cs");
        Assert.Contains("MainWindowPortsHidden", dev);
        // 清空视图的手段的必须是「搜一个不存在的词」——它让任意一轮真机扫描落回后仍保持零行，
        // 变体才不会随机器端口状态闪断。
        Assert.Contains("PSearch.Text", dev);
    }
}
