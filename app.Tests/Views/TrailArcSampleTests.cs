using System;
using System.IO;
using System.Linq;
using Clockwork.Views;
using Xunit;

// 预览里的那段弧：**形状只有一处**。
//
// 设置页的预览与样式选择器里的缩略图画的是同一种形状（一条正弦弧），
// 只是装进不同的框里（留白与起伏幅度不同）。
// 抄两份的话，两条弧迟早会走样，而预览的全部价值就是「所见即所得」——
// 抄一份的预览比没有预览更坏。
//
// 所以：形状只有 GestureTrailRenderer.SampleArc 一处，取景由调用方给。
public class TrailArcSampleTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Views"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // 两处预览都必须走那一个采样器。
    // 让它变红的改动：谁再抄一份弧线出来。
    [Fact]
    public void Both_previews_sample_the_same_arc()
    {
        var preview = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Views", "GestureTrailPreview.cs"));
        var settings = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Views", "GestureSettingsWindow.xaml.cs"));
        Assert.Contains("GestureTrailRenderer.SampleArc(", preview);
        Assert.Contains("GestureTrailRenderer.SampleArc(", settings);
    }

    // 弧线本身：从左到右，两端一样高，中间鼓起来。
    // 收笔那头正是亮头所在——形状变了，淡出的效果就看不出来（见 GestureSettingsWindow.Sample）。
    // **不钉采样段数**：段数是实现细节，改它不该让这条变红（形状才是契约）。
    [Fact]
    public void The_arc_runs_left_to_right_and_comes_back_to_the_same_height()
    {
        var pts = GestureTrailRenderer.SampleArc(100, 60, 8, 0.74, 0.42).ToList();
        Assert.True(pts.Count > 2, "弧线至少要有起笔、若干中间点与收笔");
        Assert.Equal(8, pts[0].X, 3);             // 起笔在左，留白 8
        Assert.Equal(92, pts[^1].X, 3);           // 收笔在右
        Assert.Equal(pts[0].Y, pts[^1].Y, 3);     // 两端一样高
        Assert.True(pts.Min(p => p.Y) < pts[0].Y); // 中段鼓起来（y 更小 = 更高）
    }
}
