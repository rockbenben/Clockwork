using System.Collections.ObjectModel;
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

    public bool Enabled
    {
        get => Group.Enabled;
        set { if (Group.Enabled != value) { Group.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string Name => Group.Name;

    // 列表摘要：前 3 步的动作摘要串起来。用 StepSummary 而非 StepListSummary——后者会把「用途说明」
    // 当后缀拼进去，在这个窄列里太长。空组返回占位符：光一个数字 0 说不清"这个组什么都不会做"。
    public string Summary
    {
        get
        {
            if (Group.Steps.Count == 0) return Strings.Get("Group_Empty");
            var head = string.Join(" · ", Group.Steps.Take(3).Select(StepDisplay.StepSummary));
            return Group.Steps.Count > 3 ? head + " …" : head;
        }
    }

    // 组热键此前只在组编辑器里可见，多个组时根本说不出某个组合键属于谁。无热键时留空——
    // 一列占位符号比空白更吵。
    public string HotkeyLabel => Group.Hotkey ?? "";

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
