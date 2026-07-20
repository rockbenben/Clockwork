using System.Collections.ObjectModel;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.ViewModels;

// 动作组页一行（启用/名称/步骤数）。
public sealed class GroupRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;

    public GroupRowVm(ActionGroup group, Action onChanged)
    {
        Group = group;
        _onChanged = onChanged;
    }

    public ActionGroup Group { get; }

    public bool Enabled
    {
        get => Group.Enabled;
        set { if (Group.Enabled != value) { Group.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string Name => Group.Name;
    public string StepCount => Group.Steps.Count.ToString();

    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StepCount));
    }
}

// 动作组页 ViewModel（增删改移即存盘）。公共增删改移在 ListVm。
// 组 id 保留不换：SilentGroupId / OnYes 按组 id 引用，编辑换 id 会让引用失效（故不重写 OnReplacing）。
public sealed class GroupListVm : ListVm<ActionGroup, GroupRowVm>
{
    public GroupListVm(RootConfig config, Action save)
        : base(config, config.ActionGroups, g => new GroupRowVm(g, save), save) { }

    // 复制出的组换新 id + 名称加「副本」后缀；热键不复制，避免重复注册冲突。
    protected override void OnDuplicating(ActionGroup clone)
    {
        clone.Id = Guid.NewGuid().ToString();
        clone.Name += Strings.Get("Dup_Suffix");
        clone.Hotkey = "";
    }

    public ActionGroup? SelectedGroup => Selected;
}
