using System.IO;
using System.Linq;
using Xunit;

// 手势笔迹窗是**分层窗**（AllowsTransparency=true），于是它继承一条 WPF 分层窗的老账：
//
//   **Hide() 不擦位图。** 藏起来的窗再显出来时，DWM 贴的是它上次合成的那张位图——不是「空」。
//   收笔时窗被藏了，而那张位图里正好是刚画完的那条线；下一笔 Show() 一贴，上一笔原样回到屏上
//  （用户报的原话：「画手势为什么还出现上一次的手势轨迹」）。
//
// 解法是把顺序倒过来：**先让清空后的画面合成出去，再藏**（HideSoon + _hideTimer）。这条守卫盯
// 四件在编译期和运行期都不会吭声的事——它们每一条都只是「改一行、看着还挺合理」：
//
//   1. Finish 有没有被改回裸 Hide()（那正是这个 bug 的写法）；
//   2. HideDelayMs 有没有被调成 0（0 等于没有推迟，bug 原样回来，而常量看着还在）；
//   3. 新的一笔有没有撤掉待藏的表（不撤，上一笔排的那一跳会在 120ms 后落到正在画的新笔迹上）；
//   4. 关窗有没有停表（CloseTrail 是 Finish() 紧接 Close()，不停就是往死窗上跳）。
public class TrailStaleBitmapTests
{
    private static string TrailSource()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(d.FullName, "app", "Views", "GestureTrailWindow.cs"));
    }

    // 取一个方法的方法体：从签名到它那一层缩进的收尾大括号。
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature);
        Assert.True(start >= 0, $"源码里找不到 {signature}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", start);
        Assert.True(end > start, $"{signature} 的方法体没找到收尾括号");
        return src.Substring(start, end - start);
    }

    // **这条是整份文件的重点。** Finish 里那一句一旦被改回 Hide()，用户报的那个 bug 就整个回来了，
    // 而且看起来更「直接」、更像正经写法——正因为如此，它是最可能被顺手改掉的一处。
    // 注释里写着为什么（见 HideSoon），但注释拦不住重构，断言才拦得住。
    [Fact]
    public void Finish_does_not_hide_the_window_directly()
    {
        var body = Body(TrailSource(), "public void Finish()");
        Assert.Contains("HideSoon()", body);
        Assert.DoesNotContain("Hide();", body);   // HideNote(); 不匹配这个字面量，不必排除
    }

    // 推迟时长是 0 的话，HideSoon 就成了 Hide 的别名：常量还在、名字还在、调用点也还在，
    // 只有那一帧空画面没被合成出去——也就是 bug 原样回来。0 是这里唯一「合法但错」的值。
    [Fact]
    public void The_delay_is_actually_a_delay()
    {
        var src = TrailSource();
        int i = src.IndexOf("private const int HideDelayMs =");
        Assert.True(i >= 0, "找不到 HideDelayMs：推迟藏窗的时长没了");
        var digits = new string(src.Substring(i).SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        Assert.True(digits.Length > 0, "HideDelayMs 的数值没解析出来");
        Assert.True(int.Parse(digits) > 0, "HideDelayMs 不能是 0：0 就是没有推迟，上一笔的位图会原样贴回来");
    }

    // 上一笔收笔时排的那一跳，必须被新的一笔撤掉。放在 Point 最前面是有意的（连摆位之前）：
    // 任何一个采样点都该算「正在画」，否则手快时头一两个点会被上一笔的待藏掐掉。
    [Fact]
    public void A_new_stroke_cancels_the_pending_hide()
    {
        Assert.Contains("_hideTimer.Stop()", Body(TrailSource(), "public void Point("));
    }

    // CloseTrail 的写法是 Finish() 紧接 Close()，中间没有任何等待——所以 120ms 后那一跳必然落在
    // 已经关掉的窗上。对已关窗口调 Hide() 抛 InvalidOperationException，而 CloseTrail 那两条路径
    //（关手势 / 装钩失败）上应用还活着，这一下会冒到 UI 线程上。同 NotificationToast.OnClosed 的口径。
    [Fact]
    public void Closing_the_window_stops_the_pending_hide()
    {
        Assert.Contains("_hideTimer.Stop()", Body(TrailSource(), "protected override void OnClosed"));
    }
}
