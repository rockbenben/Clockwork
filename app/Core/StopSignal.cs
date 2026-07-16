using System.Threading;

namespace Clockwork.Core;

// 全局「停止所有动作」信号（急停）。单进程多线程用 ManualResetEventSlim（Set/Reset/等待）；
// 启动序列/动作组/单步/提醒各线程共享此单例。
public static class StopSignal
{
    private static readonly ManualResetEventSlim _evt = new(false);

    public static void Request() => _evt.Set();
    public static void Clear() => _evt.Reset();
    public static bool IsRequested => _evt.IsSet;

    // 可中断延时：等 ms 毫秒；期间置位立即返回 false（被停），睡满返回 true。
    // ms<=0：仅查当前是否已停。ms>int.MaxValue 夹到上限（Wait 上限 ~24.8 天）。
    public static bool InterruptibleSleep(long ms)
    {
        if (ms <= 0) return !IsRequested;
        if (ms > int.MaxValue) ms = int.MaxValue;
        return !_evt.Wait((int)ms);   // Wait 返回 true=被 Set → 被打断 → 返回 false
    }
}
