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
        // The visible fallback runs only for a new coverage, not on every Hide→Show.
        Assert.Contains("if (!_positionApplied)", code);
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
