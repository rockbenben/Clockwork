using System;
using System.IO;
using Clockwork.Core;
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

    // 这几个用例钉的是「什么算配过高级项」这个判据本身。放在这里而不是构造真窗口：
    // WPF 窗口要 STA + 资源字典，而且在单测进程里 Show 出来没有意义；判据是纯数据，直接测。
    [Fact]
    public void A_basic_app_step_keeps_the_advanced_section_collapsed()
        => Assert.False(StepEditorWindow.AdvancedConfigured(
            new LaunchStep { Kind = "app", Target = "notepad.exe" }, ""));

    [Fact]
    public void Turning_off_activate_if_running_counts_as_a_configured_advanced_field()
        => Assert.True(StepEditorWindow.AdvancedConfigured(
            new LaunchStep { Kind = "app", ActivateIfRunning = false }, ""));

    [Fact]
    public void A_zero_delay_counts_as_a_configured_advanced_field()
        => Assert.True(StepEditorWindow.AdvancedConfigured(
            new LaunchStep { Kind = "app", DelayMs = 0 }, ""));

    [Fact]
    public void A_real_advanced_value_still_expands_the_section()
        => Assert.True(StepEditorWindow.AdvancedConfigured(
            new LaunchStep { Kind = "app", Args = "--safe-mode" }, ""));
}
