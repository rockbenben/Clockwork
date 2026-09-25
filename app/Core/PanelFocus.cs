namespace Clockwork.Core;

/// <summary>面板焦点看门狗每一拍该做什么。</summary>
public enum PanelWatchAction
{
    /// <summary>这一拍无事可做。</summary>
    None,
    /// <summary>还没拿到过前台、额度没用完：再抢一次。</summary>
    Retry,
    /// <summary>拿到过前台又丢了：失焦即关（正常面板的唯一自动关闭路）。</summary>
    Dismiss,
    /// <summary>从未激活、补抢额度用尽：不再抢，面板留在屏幕上（可见可点）。</summary>
    GiveUp,
}

/// <summary>面板已存在时，这次「唤出」对它该做什么。</summary>
public enum PanelToggleAction
{
    /// <summary>正常开着的面板：热键/中键再按一次收起来。</summary>
    Dismiss,
    /// <summary>从未激活的僵尸：救活它，而不是替用户关掉一块他以为没出现的面板。</summary>
    Revive,
}

// 中键长按唤出面板的焦点决策。纯函数，不碰 Win32——与 HookWatch.ShouldHeal 同一套纪律：
// 这句话是承重的（它决定僵尸面板是复活还是被误关、看门狗补抢几次），而它原先只活在
// QuickPanelWindow 的计时器闭包和 PanelCoordinator.TogglePanel 的一个 if 里，碰着实例字段，断言没法对它写。
//
// 背景病：中键长按这条路没有「刚响应用户输入」的前台锁豁免（那次按下被钩子吞了，系统不算
// Clockwork 响应过输入），SetForegroundWindow 会被降级、AttachThreadInput 兜底也看前台脸色。
// 旧看门狗只在第一个 250ms 补抢一次，同样没有豁免，自然一样被拒——于是面板挂成一块
// 永远收不到键盘的浮窗，还占着 PanelCoordinator._panel，把下一次「唤出」变成「关闭」，长按时灵时不灵。
public static class PanelFocus
{
    /// <summary>250ms 一拍、最多补抢的拍数（8 拍 ≈ 2 秒）。</summary>
    // 钉成常量并让测试盯着：改这个窗口 = 改「面板对着用户正在干的活抢多少次前台」，
    // 必须是下决心的事，不是顺手改的数字。
    public const int MaxRetries = 8;

    /// <summary>看门狗每一拍的裁决。tick 从 0 计（第一拍 tick==0），由调用方随每拍递增。</summary>
    //
    // 分支顺序是承重的：
    //   1. 这一拍就在前台 → 什么也不做（调用方记 hadFocus）。
    //   2. 曾经在前台、现在不在 → 失焦即关。
    //   3. 从未激活、额度没用完 → 再抢一次。
    //   4. 额度用尽的那一拍 → GiveUp（调用方记一行日志，恰好记一次）。
    //   5. 之后 → None：GiveUp 不等于「永远挂着」——调用方从这里转入僵尸观察拍
    //      （ZombieShouldDismiss），三盏灯齐亮才关；这一支仍报 None 是因为「关不关」还要问
    //      前台归属与光标位置，那些信号不归这份纯计时裁决持有。
    // everActivated 为 false 而 hadFocus 为 true 的组合在数学上不可能（拿到过前台必然
    // 激活过），但 Dismiss 仍排在 Retry 之前钉死：万一上游字段被改坏，宁可误关也不能
    // 对着一块收不到键盘的浮窗无限补抢。
    public static PanelWatchAction WatchTick(bool hadFocus, bool nowFocused,
                                             bool everActivated, int tick)
        => nowFocused ? PanelWatchAction.None
           : hadFocus ? PanelWatchAction.Dismiss
           : !everActivated && tick < MaxRetries ? PanelWatchAction.Retry
           : !everActivated && tick == MaxRetries ? PanelWatchAction.GiveUp
           : PanelWatchAction.None;

    /// <summary>GiveUp 后观察拍的停留门槛：光标离开面板矩形后需连续这么多拍（250ms×8≈2 秒）没回来。</summary>
    // 2 秒是「手离开面板去干别的」与「手在面板附近找格子」的分界：后者总会在一两拍内
    // 把光标带回来（格子就在眼前），前者会持续。拍数与补抢额度同量级，不是随手挑的。
    public const int ZombieDwellTicks = 8;

    /// <summary>「没有哪一拍见过真前台」的面板（从未激活的僵尸、detach 后被回滚的半激活）观察拍裁决：三盏灯**同时**亮才关。</summary>
    //
    //   1. foregroundIsOtherProcess —— 前台确实是别的程序的窗口。自家主窗/toast/提醒弹窗都不算：
    //      用户可能正对着这套自家 UI，「前台短暂不是面板」≠「用户走了」。
    //   2. !cursorInsidePanel —— 光标在面板矩形之外。**回来一下即清零计数**，所以「瞄着点、
    //      手悬在格子上方」永不会被切；切的是「人明确走开」这一种姿势。
    //   3. consecutiveTicksOutside ≥ ZombieDwellTicks —— 离开要持续约 2 秒，单拍抖动不算数。
    //
    // 成立即正确的原因：这块面板没有任何一拍真正持有过前台（调用方门是 hadFocus，不是 everActivated——
    // 半激活的 Activated 是发过的，别拿它当证据），键盘实际落在前台那个程序上；
    // 剩下的唯一用法是鼠标点格子——光标持续留在面板之外，用法就没有了；
    // 此时「留着」才是问题（「失焦不关」的原始投诉，2026-09-26 部署日志坐实半激活一类同样如此），
    // 关掉是对的。Revive（DecideToggle 那条路）照旧先于一切：再按一次唤出仍是救活。
    public static bool ZombieShouldDismiss(bool foregroundIsOtherProcess, bool cursorInsidePanel, int consecutiveTicksOutside)
        => foregroundIsOtherProcess && !cursorInsidePanel && consecutiveTicksOutside >= ZombieDwellTicks;

    /// <summary>面板已存在时，这次「唤出」该关掉它还是救活它。</summary>
    //
    // 从未激活过 = 僵尸：上次呼出根本没抢到前台，用户看到的这次长按其实按在
    // 一块他以为没出现的面板上。这时 Dismiss 会把「召唤」变成「关闭」——正是
    // 「长按了没反应」的观感来源——救活，而不是关掉。
    // 激活过 = 正常开着的面板，toggle 收起的语义一字不动。
    public static PanelToggleAction DecideToggle(bool everActivated)
        => everActivated ? PanelToggleAction.Dismiss : PanelToggleAction.Revive;
}
