using System.Linq;
using Clockwork.Core;
using Clockwork.ViewModels;
using Xunit;

// Models 是 config 里那份 List 本身，Rows 是平行的另一份，删除按**下标**同删。
// 所以只要有谁绕过 VM 去动那份 List（面板管理器拖页眉重排、加动作页追加），
// 两边就此错位——之后在界面上按「删除」删掉的是另一个组，而且没有任何提示。
public class ListVmResyncTests
{
    private static (RootConfig Cfg, GroupListVm Vm) Make(params string[] names)
    {
        var c = new RootConfig();
        c.ActionGroups.Clear();
        foreach (var n in names) c.ActionGroups.Add(new ActionGroup { Id = n, Name = n });
        return (c, new GroupListVm(c, () => { }));
    }

    [Fact]
    public void Reordering_the_models_behind_the_vm_desyncs_until_resync()
    {
        var (cfg, vm) = Make("a", "b", "c");
        // 面板管理器干的就是这件事：直接在 config 的清单里挪位置。
        var moved = cfg.ActionGroups[0];
        cfg.ActionGroups.RemoveAt(0);
        cfg.ActionGroups.Insert(2, moved);

        vm.Resync();
        Assert.Equal(cfg.ActionGroups.Select(g => g.Name), vm.Rows.Select(r => r.Name));
    }

    [Fact]
    public void Appending_behind_the_vm_is_picked_up()
    {
        var (cfg, vm) = Make("a", "b");
        cfg.ActionGroups.Add(new ActionGroup { Id = "c", Name = "c" });
        vm.Resync();
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("c", vm.Rows[2].Name);
    }

    // 这一条是整个 bug 的落点：重排之后删除必须删掉**你选中的那个**。
    [Fact]
    public void After_a_reorder_delete_still_removes_the_selected_group()
    {
        var (cfg, vm) = Make("a", "b", "c");
        var moved = cfg.ActionGroups[0];
        cfg.ActionGroups.RemoveAt(0);
        cfg.ActionGroups.Insert(2, moved);   // 现在是 b, c, a
        vm.Resync();

        vm.SelectedIndex = vm.Rows.ToList().FindIndex(r => r.Name == "c");
        vm.DeleteSelected();

        Assert.DoesNotContain(cfg.ActionGroups, g => g.Name == "c");
        Assert.Equal(new[] { "b", "a" }, cfg.ActionGroups.Select(g => g.Name));
        Assert.Equal(cfg.ActionGroups.Select(g => g.Name), vm.Rows.Select(r => r.Name));
    }

    // 选中项按引用找回来：重排之后同一个下标已经是别的东西了。
    [Fact]
    public void Resync_keeps_the_selection_on_the_same_group()
    {
        var (cfg, vm) = Make("a", "b", "c");
        vm.SelectedIndex = 0;                // 选中 a
        var moved = cfg.ActionGroups[0];
        cfg.ActionGroups.RemoveAt(0);
        cfg.ActionGroups.Insert(2, moved);   // a 挪到了末尾
        vm.Resync();
        Assert.Equal("a", vm.Rows[vm.SelectedIndex].Name);
    }
}
