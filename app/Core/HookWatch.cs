namespace Clockwork.Core;

// 「钩子该不该摘了重装」的裁决。纯函数，不碰 Win32——与 GestureGate.ShouldWatch 同一套纪律：
// 这句话是承重的（它决定中键面板与右键手势何时静默失灵），而它原先只活在 GestureCoordinator.HealMouseHookIfDead
// 里，那儿碰 Win32、也读着实例字段，断言很难对着它写。

public static class HookWatch
{
    /// <summary>此刻该不该摘钩重装。</summary>
    ///
    /// 判据的关键是**在同一个采样区间里对齐两格信号**，而不是拿两扇各自计时的窗去比：
    /// 调用方每 500ms 采一次光标位置，区间内位置变了（<paramref name="cursorMovedInSample"/>）
    /// 就说明物理上确实发生过鼠标移动；一个活着的 WH_MOUSE_LL 对每次移动必有回调，
    /// 于是它的心跳时间戳必然落在这同一个区间内（silentSinceMs ≤ observedSampleMs）。
    /// 心跳比区间还老，只剩一种解释：移动发生了、回调却没来，钩子已经被 Windows 摘掉。
    /// 反过来说，区间内没动鼠标就**没有信息**——没输入本来就没事件，再老的心跳也不能动手。
    ///
    /// 旧版用的是两扇错位的窗（「光标 2000ms 内动过」对「心跳停了 1500ms」），用户移动后
    /// 停手 1.5~2 秒必落进重叠带，被当成钩子死了白摘白装（实测 clockwork.error.log
    /// 一天 518 行 reinstall、silentFor 512/519 贴着阈值成簇）。同区间对齐让这个误报带
    /// 在数学上不存在：停手的区间不检测，真死亡后的第一个移动区间立刻检出。
    ///
    /// 每条守卫都有断言盯着（改掉任何一条都要有测试变红）：
    /// <param name="cursorMovedInSample">本采样区间内光标位置是否变过。用户停着不动时
    ///   没有回调是**正常的**（没有输入就没有事件），那时重装是白摘白装、还把日志刷满。</param>
    /// <param name="silentSinceMs">回调最后一次被叫到距今多久。从未被叫到过时调用方自己先挡
    ///   掉（那种情况重装一百次也一样，见 GestureCoordinator 里 !EverBeat 那一段的长注释）。</param>
    /// <param name="observedSampleMs">本采样区间的**实测**长度（本次与上一次采样的墙钟差），
    ///   不是名义上的 500ms。系统卡顿时区间自己变长，新鲜度的界跟着变长，不会把卡顿误判成死亡。</param>
    /// <param name="msSinceLastHeal">距离上一次自愈摘钩过了多久。一个「装上几秒就被摘掉」的
    ///   钩子可能几秒一次地循环摘装，而每次摘装之间有个输入空窗。</param>
    /// <param name="beatSkewMs">心跳落在区间内之外允许的余量，盖计时器与时钟粒度；
    ///   不需要盖卡顿（卡顿由 <paramref name="observedSampleMs"/> 自适应）。</param>
    /// <param name="minGapMs">两次自愈之间最短间隔。</param>
    public static bool ShouldHeal(bool cursorMovedInSample, long silentSinceMs, long observedSampleMs,
                                  long msSinceLastHeal, long beatSkewMs, long minGapMs)
        => cursorMovedInSample
           && silentSinceMs > observedSampleMs + beatSkewMs
           && msSinceLastHeal >= minGapMs;

    /// <summary>装完又哑之后，下一次自愈该等多久。</summary>
    //
    // 为什么固定下限不够（原来只有 `minGapMs` 一条 3 秒的闸）：3 秒正好是「链被堵住」与
    // 「钩子被系统摘了」两种病共同的节拍，于是前者会被当成后者治——2026-10-06 06:16:40 起
    // 17 秒内重装 5 次，每次留下一条卡在链里的泵线程（`hook-join-background took 30000ms` 四条），
    // 而每一次摘装之间都有一段输入空窗，用户看到的正是「右键多点几下就卡、有些右键不出现」。
    //
    // 退避的读法：**重装真的把输入接回来了，调用方就把间隔复位**（见 GestureCoordinator 里
    // 心跳新鲜那一格），所以一次性的真故障仍然 3 秒内治好；只有「装了又哑、哑了又装」那种
    // 循环才会长。封顶是为了别让一次长达几小时的拥堵把自愈推到几分钟开外——那时它和原来的
    // 3 秒一样治不了病，但至少不再制造新的空窗。
    public static long NextHealGap(long currentMs, long floorMs, long capMs)
        => Math.Min(capMs, currentMs < floorMs ? floorMs : currentMs * 2);
}
