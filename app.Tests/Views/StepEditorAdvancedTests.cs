using System;
using System.IO;
using Clockwork.Views;
using Xunit;

public class StepEditorAdvancedTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Common_step_types_are_offered_at_the_top_of_the_add_menu()
        => Assert.Equal(new[] { "app", "url" }, StepMenu.QuickKinds);

    [Fact]
    public void Advanced_fields_are_collapsed_by_default_and_keep_existing_values_visible()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "app", "Views", "StepEditorWindow.xaml"));
        Assert.Contains("x:Name=\"MoreExp\"", xaml);
        Assert.Contains("x:Name=\"PanAppAdvanced\"", xaml);

        var code = File.ReadAllText(Path.Combine(Root(), "app", "Views", "StepEditorWindow.xaml.cs"));
        Assert.Contains("MoreExp.IsExpanded =", code);
        Assert.Contains("Vis(PanAppAdvanced, kind == \"app\")", code);
    }
}
