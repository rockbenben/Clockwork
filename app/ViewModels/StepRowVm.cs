using Clockwork.Core;

namespace Clockwork.ViewModels;

// 启动清单/动作组里一行步骤的显示模型（启用 / 类型 / 摘要 / 延时）。
public sealed class StepRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;

    public StepRowVm(LaunchStep step, Action onChanged)
    {
        Step = step;
        _onChanged = onChanged;
    }

    public LaunchStep Step { get; }

    public bool Enabled
    {
        get => Step.Enabled;
        set { if (Step.Enabled != value) { Step.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool CanEdit => true;   // 启动清单项都可编辑（系统启动项页才有只读项）
    public string KindLabel => StepDisplay.StepKindLabel(Step.Kind);
    public string Summary => StepDisplay.StepListSummary(Step);
    public string DelayText => Step.DelayMs <= 0 ? "" : (Step.DelayMs % 1000 == 0 ? $"{Step.DelayMs / 1000}s" : $"{Step.DelayMs}ms");

    // 编辑后刷新显示（步骤字段被就地改动，通知 UI 重读）。
    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(DelayText));
    }
}
