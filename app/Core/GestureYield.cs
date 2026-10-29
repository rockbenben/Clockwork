namespace Clockwork.Core;

// 手势让路的裁决与计时。判据是纯函数、计时器是纯状态机：钩子链堵住时，
// 我们唯一还能替用户做的选择是「先别抢右键」。这只影响右键手势，
// 中键唤面板不在范围内（面板是核心功能，孤立的中键抬起也比右键廉价得多）。
public enum GestureYieldChange
{
    None,
    Entered,
    Renewed,
    Exited,
    ForcedExit,
}

public static class GestureYieldRules
{
    public const long DefaultDurationMs = 4000;
    public const long MaxDurationMs = 30_000;
    public const int HealDeferredThreshold = 2;

    /// <summary>
    /// 此刻该不该进入/续期让路。
    /// </summary>
    /// <param name="stranded">还有泵线程卡在链上（HookWatch 的 stranded 计数）。</param>
    /// <param name="healDeferred">自愈被连续推迟过几次。连续两次说明链堵不是一瞬。</param>
    /// <param name="callbackWindowMaxMs">本采样窗口内回调自身的峰值（读后清零，见 MouseHook.TakeCongestionPeaks）。</param>
    /// <param name="replayWindowMaxMs">本采样窗口内补发注入的峰值。</param>
    /// <param name="thresholdMs">系统的 LowLevelHooksTimeout；回调达到它就已经越过摘钩预算。</param>
    public static bool ShouldTrigger(bool stranded, int healDeferred, int callbackWindowMaxMs,
                                     int replayWindowMaxMs, int thresholdMs)
        => stranded
           || healDeferred >= HealDeferredThreshold
           || (thresholdMs > 0 && (callbackWindowMaxMs >= thresholdMs || replayWindowMaxMs >= thresholdMs));
}

/// <summary>
/// 「让路中」的计时器：进入默认 4 秒，判据仍成立就续期，但从**首次进入**起最多 30 秒。
/// 超过 30 秒强制解除——宁可重新抢一次右键，也不让降级状态永久挂着没人发现。
/// </summary>
public sealed class GestureYield
{
    public bool Active { get; private set; }
    public long SinceMs { get; private set; }
    public long UntilMs { get; private set; }

    /// <summary>
    /// 「在判据消失之前不要再自动让路」——用户点过「立即恢复手势」，或者 30 秒上限解除过。
    ///
    /// **它属于这里，不属于协调器**：换钩子、自愈重装这些钩子生命周期的事件
    /// 不该把用户那句话作废掉（用户要的是「链还堵着，但别再让路」），
    /// 而这件事的判据本来就只在这里。
    /// </summary>
    public bool Suppressed { get; private set; }

    /// <summary>压住下一次自动进入，直到判据自己消失。</summary>
    public void Suppress() => Suppressed = true;

    public GestureYieldChange Tick(long nowMs, bool trigger)
    {
        if (Active)
        {
            if (nowMs - SinceMs >= GestureYieldRules.MaxDurationMs)
            {
                Active = false;
                // **30 秒强制解除是一次真正的恢复，不是下一拍又进**：判据消失之前不再自动让路。
                Suppressed = true;
                return GestureYieldChange.ForcedExit;
            }
            if (trigger)
            {
                UntilMs = Math.Min(SinceMs + GestureYieldRules.MaxDurationMs,
                                   nowMs + GestureYieldRules.DefaultDurationMs);
                return GestureYieldChange.Renewed;
            }
            if (nowMs >= UntilMs)
            {
                Active = false;
                return GestureYieldChange.Exited;
            }
            return GestureYieldChange.None;
        }

        // **判据消失，压制就作废**：用户那句「先别再让路」只压到判据真的没了为止。
        if (!trigger)
        {
            Suppressed = false;
            return GestureYieldChange.None;
        }
        // **压着就不进**：用户点过「立即恢复手势」，或 30 秒上限解除过。
        // 这一档必须在「换钩子 / 自愈重装」之后照样成立——
        // 那些事件与用户的意思无关，却会让让路重新开始，用户按的那个按钮就等于没按过。
        if (Suppressed) return GestureYieldChange.None;
        Active = true;
        SinceMs = nowMs;
        UntilMs = nowMs + GestureYieldRules.DefaultDurationMs;
        return GestureYieldChange.Entered;
    }

    /// <summary>立即解除（用户点「立即恢复手势」/钩子被拆）。返回这次是否真的从让路中退出。</summary>
    public bool End()
    {
        if (!Active) return false;
        Active = false;
        return true;
    }
}
