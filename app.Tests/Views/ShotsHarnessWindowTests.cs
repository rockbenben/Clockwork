using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住「--shots 造出来的窗必须关掉」，因为这一类缺陷结构上逃得过截图矩阵。**
//
// 事实经过（2026-10-17）：用户报屏幕上留了一条蓝色 L 形轨迹不消失。抓下来一看，那不是手势笔迹，
// 而是 `--shots` 矩阵里 GestureTrail 那一档留下的覆盖窗：DevChecks 用 (300,200)…(540,440) 五个
// 探针点画了一条 L，而 GestureTrailWindow.Point() 在第 2 个点就 ShowGated —— 一扇满屏、置顶、
// 点不着的分层窗。
//
// 漏在哪：RunShots 为「按窗口自身宽度补一档」调了一次 `make().Width`，量完就把这扇窗丢了。
// 同一批 make() 的正式那一份有 Close()，只有这一次没有，于是每种语言在屏幕上留一扇。
//
// **为什么矩阵抓不到它、为什么要有这条断言：** 那扇窗铺满整块屏，不在 RenderTargetBitmap 拍的
// 范围内，PNG 里一切正常，`--shots` 照样报 OK。这与 SizeToContentTests 钉的「harness 替窗口封了
// 高度、图里永远正常」是同一类——图是绿的，真机上有一扇窗赖着不走。人眼 review 是唯一防线，
// 而人眼会忘，所以改成构建期检查。
public class ShotsHarnessWindowTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Code()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "app", "DevChecks.cs"));
        // 注释先剥掉再断言：本项目的注释全是 `//` 逐行写的，而解释性注释里往往会原样引用被断言的
        // 那段代码——不剥的话，把代码删掉、只留注释，断言照样是绿的（同 HookSuspectsTests）。
        return string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    // 造出来的窗不能只为了取一个属性就被丢掉：`make().` 一出现就说明有个窗口被构造成了即抛物。
    // 正式那一份都写成 `Park(w = make())`（赋给 w，后面有 Close），只有量宽度那次曾经是裸的。
    [Fact]
    public void The_shots_matrix_never_discards_a_window_it_built()
        => Assert.DoesNotContain("make().", Code());

    // 上面那条只保证「不丢」，这一条保证「是关掉了」而不是换了个写法继续丢：
    // 量宽度那次必须紧挨着一次 Close()，否则用户屏幕上还是会留一扇满屏覆盖窗。
    [Fact]
    public void The_width_probe_window_is_closed_right_after_it_is_measured()
    {
        var code = Code();
        int at = code.IndexOf("double native", StringComparison.Ordinal);
        Assert.True(at >= 0, "RunShots 里找不到量窗口原生宽度那一段");
        // 取构造前后的一小段：这几句本来就在同一个循环体里，隔不出别的意思。
        var block = code.Substring(Math.Max(0, at - 200), 400);
        Assert.Contains("make()", block);
        Assert.Contains("Close()", block);
    }
}
