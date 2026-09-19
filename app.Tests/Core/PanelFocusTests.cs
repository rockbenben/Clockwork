using Clockwork.Core;
using Xunit;

// 面板焦点看门狗与 toggle 分派的两句话。抽成纯函数就是为了这个：分支被挪错、额度被删掉、
// GiveUp 从「恰好一次」变成「每拍一次」，都得有测试变红——否则症状是「面板挂成一块收不到
// 键盘的浮窗」或「长按一次落空一次」，在生产上只会被当成「这功能时好时坏」。
//
// 记法约定：WatchTick 的 tick 从 0 计（第一拍 tick==0），MaxRetries 拍是最后一拍补抢，
// tick == MaxRetries 那一拍是 GiveUp——所以「8 拍补抢」实际占用 9 拍（最后一拍只记账）。
public class PanelFocusTests
{
    // 这一拍就在前台：什么也不做。无论其他输入是什么。
    [Fact]
    public void Focused_tick_returns_None()
        => Assert.Equal(PanelWatchAction.None, PanelFocus.WatchTick(hadFocus: true, nowFocused: true, everActivated: true, tick: 0));

    // 拿到过前台又丢了：失焦即关。这条必须排在补抢之前——曾激活的面板永远不该被
    // 强抢前台（那是在跟用户手上的活抢焦点）。
    [Fact]
    public void Lost_focus_after_having_it_dismisses()
        => Assert.Equal(PanelWatchAction.Dismiss, PanelFocus.WatchTick(hadFocus: true, nowFocused: false, everActivated: false, tick: 0));

    // 从未激活、额度没用完：每拍都补抢。首拍与最后一拍（tick == MaxRetries-1）都要放行。
    [Fact]
    public void Never_activated_and_within_budget_retries()
    {
        Assert.Equal(PanelWatchAction.Retry, PanelFocus.WatchTick(hadFocus: false, nowFocused: false, everActivated: false, tick: 0));
        Assert.Equal(PanelWatchAction.Retry, PanelFocus.WatchTick(hadFocus: false, nowFocused: false, everActivated: false, tick: PanelFocus.MaxRetries - 1));
    }

    // 额度用尽的那一拍记 GiveUp，之后归于沉默——「放弃」这条日志必须恰好一行，
    // 不能每 250ms 刷一行把 error.log 灌满。
    [Fact]
    public void The_tick_after_the_budget_gives_up_exactly_once()
    {
        Assert.Equal(PanelWatchAction.GiveUp, PanelFocus.WatchTick(hadFocus: false, nowFocused: false, everActivated: false, tick: PanelFocus.MaxRetries));
        Assert.Equal(PanelWatchAction.None, PanelFocus.WatchTick(hadFocus: false, nowFocused: false, everActivated: false, tick: PanelFocus.MaxRetries + 1));
    }

    // 激活过但此刻不在前台、也没探到过焦点（上游字段坏掉时的假组合）：
    // 不许补抢。everActivated 的意义就是把干预封顶——宁可无动作，不可抢个没完。
    [Fact]
    public void Activated_panel_that_never_had_focus_is_not_retried()
        => Assert.Equal(PanelWatchAction.None, PanelFocus.WatchTick(hadFocus: false, nowFocused: false, everActivated: true, tick: 0));

    // toggle 分派：从未激活 = 僵尸 → 救活；激活过 → 照旧收起。
    // 把「唤出」落在「关掉一块用户以为没出现的面板」上，正是「长按了没反应」的来源。
    [Fact]
    public void DecideToggle_zombie_revives_activated_dismisses()
    {
        Assert.Equal(PanelToggleAction.Revive, PanelFocus.DecideToggle(everActivated: false));
        Assert.Equal(PanelToggleAction.Dismiss, PanelFocus.DecideToggle(everActivated: true));
    }

    // 补抢窗口钉在 8 拍 ≈ 2 秒：这是「面板对着用户正在干的活抢多少次前台」的决定，
    // 改它必须是下决心的事，不是顺手改的数字。
    [Fact]
    public void Retry_budget_is_pinned()
        => Assert.Equal(8, PanelFocus.MaxRetries);
}
