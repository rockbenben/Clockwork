namespace Clockwork.Core;

// 单次顶层运行（一次开机运行 / 一次组触发）的执行步数预算。
// 环引用由编辑器 DFS（FindCycle）+ 运行期重入集挡住，这里兜的是「引用 ×999 套 ×999」的有限爆炸——
// 急停能救人，但静默的失控运行本身就是缺陷。耗尽时回调一次（App 接 toast），此后 TryConsume 恒 false。
public sealed class RunBudget
{
    public const int MaxRunSteps = 5000;
    private readonly Action? _onExhausted;
    private int _left = MaxRunSteps;
    public bool Exhausted { get; private set; }

    public RunBudget(Action? onExhausted = null) => _onExhausted = onExhausted;

    public bool TryConsume()
    {
        if (_left > 0) { _left--; return true; }
        if (!Exhausted) { Exhausted = true; _onExhausted?.Invoke(); }
        return false;
    }
}
