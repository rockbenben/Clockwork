using System;
using System.IO;
using System.Linq;
using Xunit;

// 站点映射表只有一个使用者：siteMap 步骤。入口必须留在那个步骤里，
// 不要再回到设置页——设置页是全局旋钮，一张只服务一类动作的表放在那里既难找又抢版面。
public class SiteMapPlacementTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string App(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    [Fact]
    public void Site_map_is_opened_from_the_step_that_uses_it()
    {
        var xaml = App("Views", "StepEditorWindow.xaml");
        Assert.Contains("x:Name=\"SiteMapEditBtn\"", xaml);
        Assert.Contains("Click=\"EditSiteMap_Click\"", xaml);
        var code = App("Views", "StepEditorWindow.xaml.cs");
        Assert.Contains("App.Instance?.EditSiteMap(this)", code);
        Assert.Contains("internal void EditSiteMap(Window owner)", App("App.xaml.cs"));
    }

    [Fact]
    public void Settings_no_longer_offers_the_site_map_section()
    {
        var xaml = App("MainWindow.xaml");
        Assert.DoesNotContain("SiteMapBtn", xaml);
        Assert.DoesNotContain("SiteMap_Click", xaml);
        Assert.DoesNotContain("SiteMap_Click", App("MainWindow.xaml.cs"));
    }

    [Fact]
    public void The_step_hint_does_not_point_back_at_settings()
    {
        foreach (var name in new[] { "Strings.resx", "Strings.en.resx" })
        {
            var text = App("Resources", name);
            int at = text.IndexOf("name=\"Ed_SiteMapUrlHint\"", StringComparison.Ordinal);
            Assert.True(at >= 0, $"{name} 找不到 Ed_SiteMapUrlHint");
            int end = text.IndexOf("</data>", at, StringComparison.Ordinal);
            var block = text.Substring(at, end - at);
            Assert.DoesNotContain("设置", block);
            Assert.DoesNotContain("settings", block.ToLowerInvariant());
        }
    }

    // 运行时报「域名不在站点表里」时，用户手上是那条步骤；让他去改 JSON 等于把他送回旧路。
    [Fact]
    public void The_missing_site_warning_points_at_the_step_not_the_json()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "app", "Resources"), "Strings*.resx"))
        {
            var text = File.ReadAllText(file);
            int at = text.IndexOf("name=\"Warn_SiteMapNoMatch\"", StringComparison.Ordinal);
            Assert.True(at >= 0, $"{Path.GetFileName(file)} 找不到 Warn_SiteMapNoMatch");
            int end = text.IndexOf("</data>", at, StringComparison.Ordinal);
            var block = text.Substring(at, end - at);
            Assert.DoesNotContain("clockwork.settings.json", block);
        }
    }
}
