using System.Collections.ObjectModel;
using System.Linq;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.ViewModels;

// 动作组页一行（启用/名称/摘要/热键）。
public sealed class GroupRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;

    public GroupRowVm(ActionGroup group, Action onChanged)
    {
        Group = group;
        _onChanged = onChanged;
    }

    public ActionGroup Group { get; }

    public object Model => Group;

    public bool Enabled
    {
        get => Group.Enabled;
        set { if (Group.Enabled != value) { Group.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string Name => Group.Name;

    // 摘要口径在 StepDisplay.GroupSummary 一处维护：步骤编辑器选组时的内容预览用的是同一个，
    // 两处回答的是同一个问题「这个组里有什么」，各写一份迟早漂移（曾经就是两份）。
    public string Summary => StepDisplay.GroupSummary(Group);

    // 组热键此前只在组编辑器里可见，多个组时根本说不出某个组合键属于谁。无热键时留空——
    // 一列占位符号比空白更吵。
    public string HotkeyLabel => Group.Hotkey ?? "";

    // 读屏软件念的就是这一串（见 StepRowVm.ToString 的说明）。
    public override string ToString() => Name + " · " + Summary;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HotkeyLabel));
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
