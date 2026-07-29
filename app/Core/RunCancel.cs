using System.Threading;

namespace Clockwork.Core;

// 单次顶层运行的取消闸（生命周期同 RunBudget：一次触发一份，经 GroupDeps 传给整条嵌套引用链）。
//
// 与全局急停 StopSignal 的分工必须分清，否则一定会退化成又一个全局开关：
//   StopSignal —— 总闸。停「所有在跑的东西」：启动清单、单步、全部动作组。三个入口（急停热键 / 托盘 /
//                 主窗按钮）都走它，是保命通道。
//   RunCancel  —— 只停「这一次运行」。动作组热键按第二次时用：用户要收回的是他刚用这个键启动的那一组，
//                 顺手把开机启动清单和别的组一起干掉，是他没要求过的破坏。
//
// 两者是「或」的关系：本闸被取消、或总闸被拉下，本次运行都得停（IsStopped）。而 IsRequested 只答
// 「这一份被单独取消了吗」——两个来源要分得开，否则急停之后所有 token 看起来都像被用户逐个取消过。
public sealed class RunCancel
{
    private readonly ManualResetEventSlim _evt = new(false);

    public void Request() => _evt.Set();

    // 这一份运行是否被单独取消（不含全局急停）。
    public bool IsRequested => _evt.IsSet;

    // 本次运行是否该停：单独取消 或 全局急停。执行循环里的判断一律用它。
    public bool IsStopped => _evt.IsSet || StopSignal.IsRequested;

    // 可中断延时：等 ms 毫秒；被停则立即返回 false，睡满返回 true。
    // ms<=0：仅查当前状态。语义与 StopSignal.InterruptibleSleep 保持一致，调用点可直接替换。
    //
    // 只等自己这一个事件，不去 WaitAny 全局急停的内核句柄。急停要能立刻叫醒长睡眠（组的 RepeatDelayMs
    // 允许几十分钟），但那件事由 App.RequestStop 主动「推」给每个在途闸（ActionGroupRunner.CancelAll）——
    // 而不是每个闸自己去「拉」。理由是 ManualResetEventSlim 同时持有托管状态位和惰性内核事件，Set 先改位
    // 再置内核、Reset 先复位内核再清位，两者不原子：只要有人等内核句柄、别人读 IsSet，Request/Clear 交错
    // 就能留下「句柄恒置位、IsSet 为假」的永久分歧——此后每个带延时的组都在第一步静默截断还报 Completed，
    // 且看上去没有任何东西被停过。推模型下全局信号只有 IsRequested 一个读法，这类分歧不可能发生。
    public bool InterruptibleSleep(long ms)
    {
        if (IsStopped) return false;         // 进门先查：本闸已取消，或睡下之前全局急停就已置位
        if (ms <= 0) return true;
        if (ms > int.MaxValue) ms = int.MaxValue;
        return !_evt.Wait((int)ms);          // Wait 返回 true=被取消 → 被打断 → 返回 false
    }

    // —— 给「既被动作组、也被启动清单调用」的执行路径用的统一分发 ——
    // 那些代码（StepRunner / WindowManager 的等窗口、重试、置前延时）拿到的可能是某次运行的闸，
    // 也可能什么都没有（开机清单没有 per-run 闸）。分发只此一处，免得各调用点各写一遍 ?: 然后漂移。

    // 「该停了吗」：有闸就问闸（它已含全局急停），没闸就只认全局急停。
    public static bool Stopped(RunCancel? cancel) => cancel?.IsStopped ?? StopSignal.IsRequested;

    // 可中断延时的同款分发。两条路径的返回值语义一致：睡满 true、被停 false。
    public static bool Sleep(RunCancel? cancel, long ms)
        => cancel != null ? cancel.InterruptibleSleep(ms) : StopSignal.InterruptibleSleep(ms);
}
