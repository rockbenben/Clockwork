using System;
using System.IO;
using Clockwork.Views;
using Xunit;

public class TrailPlacementTests
{
    [Fact]
    public void Same_monitor_and_scale_reuses_the_coverage()
        => Assert.True(GestureTrailWindow.SameCoverage(0, 0, 3840, 2160, 1.5, 0, 0, 3840, 2160, 1.5));

    [Fact]
    public void A_different_monitor_or_scale_recomputes_the_coverage()
    {
        Assert.False(GestureTrailWindow.SameCoverage(0, 0, 3840, 2160, 1.5, -2048, 0, 2048, 1152, 1.25));
        Assert.False(GestureTrailWindow.SameCoverage(0, 0, 3840, 2160, 1.5, 0, 0, 3840, 2160, 1.25));
    }

    [Fact]
    public void First_pointer_sample_does_not_create_or_position_the_layered_window()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "GestureTrailWindow.cs"));
        // SetWindowPos remains the visible-window fallback after Show; the first sample
        // must not create the HWND or position a hidden full-screen window.
        var cover = Between(code, "private bool CoverMonitorOf", "private void Reposition");
        Assert.DoesNotContain("EnsureHandle", cover);
        Assert.DoesNotContain("Reposition()", cover);
        Assert.DoesNotContain("SetWindowPos", cover);
        Assert.Contains("Reposition();", code);
        // The visible fallback runs only for a new coverage or a physical mismatch, not on every Hide→Show.
        Assert.Contains("!_positionApplied", code);
    }

    // 上一笔的药丸还亮着时窗口仍然可见；此时在新监视器起笔，CoverMonitorOf 只改了
    // WPF 的 Left/Top 并把 _positionApplied 置 false，Point 若照旧直接 return，
    // 物理像素兜底就永远不跑。这里钉住 Point 在 IsVisible 分支里补这一刀。
    // （真正的 HWND 位置仍要在真实手势/双屏上验；源码守卫只防分支被删。）
    [Fact]
    public void A_visible_window_applies_a_pending_physical_position_before_skipping_show()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "GestureTrailWindow.cs"));
        var point = Between(code, "public void Point(", "public void Finish");
        Assert.Contains("if (IsVisible)", point);
        Assert.Contains("Reposition();", point);
        Assert.Contains("_positionApplied = true;", point);
    }

    [Fact]
    public void A_window_at_the_expected_physical_rect_counts_as_positioned()
        => Assert.True(GestureTrailWindow.CoverageMatches(0, 0, 3840, 2160, 0, 0, 3840, 2160));

    [Fact]
    public void A_shifted_or_resized_window_needs_repositioning()
    {
        Assert.False(GestureTrailWindow.CoverageMatches(1, 0, 3841, 2160, 0, 0, 3840, 2160));
        Assert.False(GestureTrailWindow.CoverageMatches(0, 0, 3839, 2160, 0, 0, 3840, 2160));
    }

    // 位置缓存不能只信“第一次写过”。每次 Show 后廉价读一下 HWND 的真实矩形；
    // 不一致（混合 DPI 下 WPF 可能按自己的 DIP 记账挪了窗）才重写。
    [Fact]
    public void The_show_path_rechecks_the_physical_rect_before_trusting_the_cache()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "GestureTrailWindow.cs"));
        var show = Between(code, "private void ShowGated()", "protected override void OnClosed");
        Assert.Contains("PositionMatches()", show);
        Assert.Contains("CoverageMatches", code);
    }

    private static string Between(string text, string start, string end)
    {
        int a = text.IndexOf(start, StringComparison.Ordinal);
        int b = text.IndexOf(end, a, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a);
        return text.Substring(a, b - a);
    }

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }
}
