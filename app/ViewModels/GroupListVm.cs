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
            // 注释只是分段标签、不是动作：算进摘要会挤掉一个真正的动作位，还让省略号提前出现。
            // 判空也按「动作数」而非「步骤数」——一个只剩注释的组确实什么都不做，而本列回答的正是
            //「这个组会做什么」，此时「（空）」是准确回答，也省掉一个分支。注释文字在编辑器里照样看得到。
            var acts = Group.Steps.Where(s => s.Kind != "comment").ToList();
            if (acts.Count == 0) return Strings.Get("Group_Empty");
            var head = string.Join(" · ", acts.Take(3).Select(StepDisplay.StepSummary));
            return acts.Count > 3 ? head + " …" : head;
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
