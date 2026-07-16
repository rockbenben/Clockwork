using System.Threading;

namespace Clockwork.Core;

// 并发运行闸。启动序列 / 单步「运行这一步」/ 动作组 / 提醒静默组 共享同一个全局急停信号(StopSignal)。
// 若每个运行开跑前都无条件 Clear，一个运行的 Clear 会把「另一路正在跑、且用户刚按下急停」的信号
// 悄悄抹掉，导致急停失效。改为计数：只有「第一路」运行进入时清空急停；已有运行在跑时不再清空，
// 于是一次急停会一直生效到所有在途运行都结束，最后才由下一路全新运行重新清空。
public sealed class RunGate
{
    private int _active;

    // 进入一路运行。首个并发运行(0→1)才清空急停信号；期间再进入的运行不清空，尊重在途急停。
    public void Begin()
    {
        if (Interlocked.Increment(ref _active) == 1) StopSignal.Clear();
    }

    // 退出一路运行。
    public void End() => Interlocked.Decrement(ref _active);

    public int Active => Volatile.Read(ref _active);
}
