namespace Clockwork.Core;

// 并发运行闸。启动序列 / 单步「运行这一步」/ 动作组 / 提醒静默组 共享同一个全局急停信号(StopSignal)。
// 若每个运行开跑前都无条件 Clear，一个运行的 Clear 会把「另一路正在跑、且用户刚按下急停」的信号
// 悄悄抹掉，导致急停失效。改为计数：只有「第一路」运行进入时清空急停；已有运行在跑时不再清空，
// 于是一次急停会一直生效到所有在途运行都结束，最后才由下一路全新运行重新清空。
public sealed class RunGate
{
    // 计数与「0→1 时清空急停」必须是一个原子步骤，光靠 Interlocked 不够：那样第二路运行可能在
    // 第一路执行 Clear() 之前就拿到 Increment=2 返回、立刻开始跑，第一步就撞上尚未清掉的旧急停
    // （急停置位在所有运行结束后是一直留着的，只等下一路 0→1 来清），于是整组零步退出且返回
    // Completed——调用方分辨不出，用户看到的是「到点了，那个组什么都没做，也没有任何提示」。
    // 触发面很窄但真实：同一 tick 里两条挂了静默动作组的提醒会背靠背派两个后台任务。
    private readonly object _gate = new();
    private int _active;

    // 「有没有东西在跑」发生变化。主窗口的急停按钮据此显示/隐藏——它只在真有东西在跑时出现，
    // 于是「出现」本身就是状态、「消失」就是停到了。Begin/End 来自后台线程，订阅方自己切回 UI 线程。
    public event Action? ActiveChanged;

    // 进入一路运行。首个并发运行(0→1)才清空急停信号；期间再进入的运行不清空，尊重在途急停。
    public void Begin()
    {
        lock (_gate) { if (++_active == 1) StopSignal.Clear(); }
        ActiveChanged?.Invoke();   // 广播在锁外：订阅方要切 UI 线程，持锁调用等着死锁
    }

    // 退出一路运行。
    public void End()
    {
        lock (_gate) { _active--; }
        ActiveChanged?.Invoke();
    }

    public int Active { get { lock (_gate) return _active; } }
}
