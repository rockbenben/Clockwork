using System.IO;
using Xunit;

// **吞了别人的消息，就不能再拿系统状态当自己的判据。**
//
// 手势路径把每一条 WM_RBUTTONDOWN 都吞掉（回调 return 1）。被吞掉的消息不再进入系统的
// 输入处理，键态表里那次按下**从未发生**——于是 GetAsyncKeyState(VK_RBUTTON) 从头到尾答「没按」。
//
// 这曾经是一个查了很久的真实故障：移动分支里有一句「手还按着吗？没按就整笔作废」的兜底，
// 判据正是那张键态表。结果每一笔手势都在**第一次移动**时被自己作废，一个采样点都攒不下，
// 屏幕上连轨迹都不会出现——而钩子装得好好的、日志一切正常、右键菜单也照常弹，
// 外表没有任何一处不对。探针实测：吞掉按下之后的 486 次移动里，答「按着」0 次。
//
// 那句兜底本身是需要的（抬手那一刻弹 UAC 安全桌面，低级钩子收不到那条抬起）。
// 它现在改用 _beatRDown——我们自己收到按下时盖的戳，不依赖任何被我们改动过的系统状态。
//
// 这条测试只盯一件事：那张键态表别再回到手势路径里。注释拦不住下一次，测试能。
public class MouseHookSwallowedStateTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
    }

    // 只看代码，不看注释——讲清这个坑的那段注释里必然要提到那两个名字，
    // 而这条测试要禁的是「再去调它」，不是「再提起它」。
    private static string CodeOnly(string src)
    {
        var sb = new System.Text.StringBuilder(src.Length);
        foreach (var line in src.Split('\n'))
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            sb.Append(i < 0 ? line : line[..i]).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public void Gesture_path_never_asks_the_system_whether_the_swallowed_button_is_down()
    {
        var code = CodeOnly(File.ReadAllText(Path.Combine(RepoRoot(), "app", "Native", "MouseHook.cs")));
        Assert.DoesNotContain("RightButtonDown()", code);
        Assert.DoesNotContain("GetAsyncKeyState", code);
    }

    // 兜底本身不能连带被删掉：没有它，丢失的抬起会让笔迹跟着没按键的光标一直跑。
    [Fact]
    public void A_lost_button_up_still_has_a_way_out()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Native", "MouseHook.cs"));
        Assert.Contains("GestureStaleMs", src);
        Assert.Contains("_beatRDown", src);
    }
}
