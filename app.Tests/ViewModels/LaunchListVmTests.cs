using Clockwork.Core;
using Clockwork.ViewModels;
using Xunit;

public class LaunchListVmTests
{
    private static (LaunchListVm vm, RootConfig cfg, int[] saves) Make(params string[] labels)
    {
        var cfg = new RootConfig { LaunchSteps = labels.Select(l => new LaunchStep { Kind = "app", Label = l }).ToList() };
        int[] saves = { 0 };
        var vm = new LaunchListVm(cfg, () => saves[0]++);
        return (vm, cfg, saves);
    }

    [Fact]
    public void Loads_rows_from_config()
    {
        var (vm, _, _) = Make("a", "b");
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("a", vm.Rows[0].Summary);
    }

    [Fact]
    public void Add_appends_when_no_selection()
    {
        var (vm, cfg, saves) = Make("a");
        vm.SelectedIndex = -1;
        var pos = vm.Add(new LaunchStep { Kind = "app", Label = "new" });
        Assert.Equal(1, pos);
        Assert.Equal(2, cfg.LaunchSteps.Count);
        Assert.Equal("new", cfg.LaunchSteps[1].Label);
        Assert.Equal(1, saves[0]);
    }

    [Fact]
    public void Add_inserts_after_selection()
    {
        var (vm, cfg, _) = Make("a", "b", "c");
        vm.SelectedIndex = 0;
        vm.Add(new LaunchStep { Kind = "app", Label = "x" });
        Assert.Equal(new[] { "a", "x", "b", "c" }, cfg.LaunchSteps.Select(s => s.Label).ToArray());
    }

    [Fact]
    public void Delete_removes_and_syncs_config()
    {
        var (vm, cfg, saves) = Make("a", "b", "c");
        vm.SelectedIndex = 1;
        vm.DeleteSelected();
        Assert.Equal(new[] { "a", "c" }, cfg.LaunchSteps.Select(s => s.Label).ToArray());
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(1, saves[0]);
    }

    [Fact]
    public void MoveUp_reorders_config_and_rows()
    {
        var (vm, cfg, _) = Make("a", "b", "c");
        vm.SelectedIndex = 2;
        vm.MoveUp();
        Assert.Equal(new[] { "a", "c", "b" }, cfg.LaunchSteps.Select(s => s.Label).ToArray());
        Assert.Equal("c", vm.Rows[1].Summary);
        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void MoveDown_at_end_is_noop()
    {
        var (vm, cfg, saves) = Make("a", "b");
        vm.SelectedIndex = 1;
        vm.MoveDown();
        Assert.Equal(new[] { "a", "b" }, cfg.LaunchSteps.Select(s => s.Label).ToArray());
        Assert.Equal(0, saves[0]);
    }

    [Fact]
    public void Enabled_toggle_updates_step_and_saves()
    {
        var (vm, cfg, saves) = Make("a");
        vm.Rows[0].Enabled = false;
        Assert.False(cfg.LaunchSteps[0].Enabled);
        Assert.Equal(1, saves[0]);
    }
}
