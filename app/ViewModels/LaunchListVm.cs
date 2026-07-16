using System.Collections.ObjectModel;
using Clockwork.Core;

namespace Clockwork.ViewModels;

// 启动清单页 ViewModel：ObservableCollection 与 config.LaunchSteps 保持同步；增删改移即存盘。
// 公共增删改在 ListVm；这里只加启动清单独有的上/下移。
public sealed class LaunchListVm : ListVm<LaunchStep, StepRowVm>
{
    public LaunchListVm(RootConfig config, Action save)
        : base(config, config.LaunchSteps, s => new StepRowVm(s, save), save) { }

    public void MoveUp() => Move(-1);
    public void MoveDown() => Move(1);

    private void Move(int dir)
    {
        int i = SelectedIndex, j = i + dir;
        if (i < 0 || i >= Rows.Count || j < 0 || j >= Rows.Count) return;
        (Models[i], Models[j]) = (Models[j], Models[i]);
        Rows.Move(i, j);
        SelectedIndex = j;
        Save();
    }

    public LaunchStep? SelectedStep => Selected;

    // 接管系统启动项时用：目标已在清单里则不重复加（去重按 Target）。返回该步骤(新增或既有)的索引，供调用方定位选中。
    public int AddIfNew(LaunchStep step)
    {
        for (int i = 0; i < Models.Count; i++)
            if (!string.IsNullOrEmpty(step.Target) && string.Equals(Models[i].Target, step.Target, StringComparison.OrdinalIgnoreCase))
            {
                // 已有同名步骤：接管的意图就是让它开机启动，若既有步骤被禁用则重新启用
                // （否则系统项已禁、清单项也没勾，两边都不启动，还谎报接管成功）。
                if (!Models[i].Enabled) { Models[i].Enabled = true; Rows[i].Refresh(); Save(); }
                return i;
            }
        return Add(step);
    }
}
