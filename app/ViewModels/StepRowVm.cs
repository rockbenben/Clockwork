using System.Windows;
using Clockwork.Core;

namespace Clockwork.ViewModels;

// 启动清单/动作组里一行步骤的显示模型（启用 / 类型 / 摘要 / 延时）。
public sealed class StepRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;

    public StepRowVm(LaunchStep step, Action onChanged, int depth = 0)
    {
        Step = step;
        _onChanged = onChanged;
        Depth = depth;
    }

    public LaunchStep Step { get; }

    /// <summary>这一行在第几层（顶层 = 0）。块里的孩子 &gt; 0。</summary>
    // 层级是**平铺表 + ParentId** 这套模型的全部呈现：DataGrid 仍然是平的一串，
    // 靠这一列的缩进让人看出块与子步骤的归属。见 StepTree 的注释（为什么不用嵌套树）。
    public int Depth { get; }

    /// <summary>给类型标签那一格用的左缩进。</summary>
    // 直接给 Thickness 而不是让 XAML 拿 Depth 换算：换算要一个 IValueConverter，
    // 而换算规则只有这一条（每层缩进固定像素），写进 VM 比写进转换器更好读、也少一个文件。
    public Thickness IndentMargin => new(Depth * 16, 0, 0, 0);

    public object Model => Step;

    public bool Enabled
    {
        get => Step.Enabled;
        set { if (Step.Enabled != value) { Step.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool CanEdit => true;   // 启动清单项都可编辑（系统启动项页才有只读项）
    public string KindLabel => StepDisplay.StepKindLabel(Step.Kind);
    public string Summary => StepDisplay.StepListSummary(Step);
    public string DelayText => Step.DelayMs <= 0 ? "" : (Step.DelayMs % 1000 == 0 ? $"{Step.DelayMs / 1000}s" : $"{Step.DelayMs}ms");

    // DataGrid 行在无障碍树里的名字取自本对象的 ToString()。不重写的话，读屏软件把每一行都念成
    // 「Clockwork.ViewModels.StepRowVm」——整张表对读屏用户等于一片空白。四个列表页各自重写一次。
    public override string ToString() => KindLabel + " " + Summary;

    // 编辑后刷新显示（步骤字段被就地改动，通知 UI 重读）。
    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(DelayText));
    }
}
