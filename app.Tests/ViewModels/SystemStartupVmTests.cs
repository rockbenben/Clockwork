using Clockwork.Engine;
using Clockwork.ViewModels;
using Xunit;

public class SystemStartupVmTests
{
    private static List<SystemStartupItem> Items() => new()
    {
        new SystemStartupItem { Name = "Steam", Command = @"C:\steam.exe", Type = "Registry", CanToggle = true, Enabled = true },
        new SystemStartupItem { Name = "OneDrive", Command = @"C:\onedrive.exe", Type = "StartupFolder", CanToggle = true, Enabled = true },
        new SystemStartupItem { Name = "Policy X", Command = @"C:\pol.exe", Type = "Registry", CanToggle = false, ReadOnlyNote = "策略" },
    };

    [Fact]
    public void Filter_hides_readonly_by_default()
    {
        var r = SystemStartupVm.Filter(Items(), "", false);
        Assert.Equal(2, r.Count);
        Assert.DoesNotContain(r, i => i.Name == "Policy X");
    }

    [Fact]
    public void Filter_shows_readonly_when_toggled()
        => Assert.Equal(3, SystemStartupVm.Filter(Items(), "", true).Count);

    [Fact]
    public void Filter_by_search_name()
    {
        var r = SystemStartupVm.Filter(Items(), "steam", false);
        Assert.Single(r);
        Assert.Equal("Steam", r[0].Name);
    }

    [Fact]
    public void Filter_by_search_command()
    {
        var r = SystemStartupVm.Filter(Items(), "onedrive.exe", false);
        Assert.Single(r);
        Assert.Equal("OneDrive", r[0].Name);
    }

    [Fact]
    public void SetItems_populates_rows_filtered()
    {
        var vm = new SystemStartupVm((i, e) => "Ok", _ => { });
        vm.SetItems(Items());
        Assert.Equal(2, vm.Rows.Count);       // 只读项默认隐藏
    }

    [Fact]
    public void Remove_drops_item_from_rows()
    {
        var items = Items();
        var vm = new SystemStartupVm((i, e) => "Ok", _ => { });
        vm.SetItems(items);
        vm.Remove(items[0]);
        Assert.Single(vm.Rows);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "Steam");
    }

    [Fact]
    public void Toggle_success_updates_item()
    {
        var item = new SystemStartupItem { Name = "x", Enabled = true, CanToggle = true };
        string? reported = null;
        var row = new SystemStartupRowVm(item, (i, e) => "Ok", m => reported = m);
        row.Enabled = false;
        Assert.False(item.Enabled);
        Assert.Null(reported);
    }

    [Fact]
    public void Toggle_needsadmin_reverts_and_reports()
    {
        var item = new SystemStartupItem { Name = "x", Enabled = true, CanToggle = true };
        string? reported = null;
        var row = new SystemStartupRowVm(item, (i, e) => "NeedsAdmin", m => reported = m);
        row.Enabled = false;
        Assert.True(item.Enabled);   // 未变
        Assert.NotNull(reported);
    }
}
