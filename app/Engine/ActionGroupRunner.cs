using System.Collections.Concurrent;
using Clockwork.Core;

namespace Clockwork.Engine;

public enum MsgResult { Yes, No, Ok }

// 一次 RunGroup 的结局。原来只有 bool（false=重入），但「被否中止」必须与「跑完了」分开：
// 父组的引用步骤要按 Repeat 跑 N 轮，若看不出子组是被否中止的，就会把同一个确认框重弹 N 次。
// 与 MsgResult 同放（两条执行路径 ActionGroupRunner / LaunchSequence 都在 Clockwork.Engine 下）。
public enum GroupRunResult
{
    Completed,   // 跑到底（含被急停/预算截停——那两者已各有自己的反馈通道）
    Aborted,     // message 步骤答「否/关闭」：本组剩余步骤与后续轮次全停，并向上传染
    Skipped,     // 组 id 已在 _running 里（环引用或同组已在跑）：这次没跑，是否上报由调用方决定
}

// 动作组执行的依赖 seam（活交互经此注入，便于测编排流程）。
public sealed class GroupDeps
{
    public int Hour { get; init; } = -1;                                   // <0 → 取当前
    public int IsoDay { get; init; }                                       // <=0 → 取当前
    public Action<LaunchStep> RunStep { get; init; } = _ => { };           // 非 message 步骤执行（生产=InvokeStepAction 丢结果）
    public Func<LaunchStep, MsgResult> ShowMessage { get; init; } = _ => MsgResult.Ok;  // message 步骤弹窗
    public Action<LaunchStep> RunOnYes { get; init; } = _ => { };          // message 点是→onYes
    public Action<string> Speak { get; init; } = _ => { };                 // message 播报
    // 组内嵌套「group」步骤：跑引用的组。返回结局而非 void——中止/跳过都要能让上层的引用轮次收手。
    public Func<LaunchStep, GroupRunResult> RunGroupStep { get; init; } = _ => GroupRunResult.Completed;
    public Action<LaunchStep, Exception> OnStepError { get; init; } = (_, _) => { };    // 某步抛异常：记录后继续（不中止整组）
    // 某步被跳过但没有异常（如嵌套「动作组」引用的目标缺失/已禁用/重入）：reason=原因说明（供日志与提示直接展示），
    // benign=是否「良性」——true 表示这是正常配置状态（如目标组本就被人为禁用），不该被当成故障；false 表示
    // 值得关注（坏配置/环引用空转）。与 OnStepError 分开是因为三种情况都不是异常，套用「异常：」措辞是在
    // 给用户一个不存在的故障去查。
    public Action<LaunchStep, string, bool> OnStepSkipped { get; init; } = (_, _, _) => { };
    public RunBudget Budget { get; init; } = new();                        // 单次顶层运行共享的步数预算（嵌套引用经同一 deps 传递）
}

// 顺序执行动作组，整组按 group.Repeat 跑若干轮（轮间睡 RepeatDelayMs，可急停）。
// message 步骤弹确认闸门（否/关闭→中止整组）；其余步骤循环 repeat；步骤时间条件同顶层清单遵守。
// 按组 id 进程内互斥防重入（单进程用运行集即可）；重入（含环引用）返回 Skipped，是否上报由调用方决定。
public static class ActionGroupRunner
{
    private static readonly ConcurrentDictionary<string, byte> _running = new();

    public static GroupRunResult RunGroup(ActionGroup group, GroupDeps deps)
    {
        var gid = group.Id ?? "";
        if (!_running.TryAdd(gid, 0)) return GroupRunResult.Skipped;   // 已在跑：忽略本次触发（避免双开/按键交错/环引用空转）
        try
        {
            var now = DateTime.Now;   // 取一次，小时/分钟同源，避免跨分钟边界不一致
            var (hour, iso) = StepCondition.ResolveSentinels(deps.Hour, deps.IsoDay, now);
            bool stopped = false;
            // 「被否中止」与 stopped 分开记：stopped 也被急停/预算/睡眠打断占用，混在一起就分不出
            // 该向上传染的那一种。用户在子组里答过一次「否」，祖先各层的剩余轮次都必须一起收。
            bool aborted = false;
            int rounds = StepHelpers.ClampRepeat(group.Repeat);
            for (int round = 1; round <= rounds && !stopped; round++)
            {
                foreach (var step in group.Steps)
                {
                    if (stopped || StopSignal.IsRequested || deps.Budget.Exhausted) { stopped = true; break; }
                    if (!step.Enabled) continue;
                    if (!StepCondition.IsSatisfied(step, hour, iso, now.Minute)) continue;   // 组内步骤同样遵守时间条件（分钟级）

                    if (step.Kind == "message")
                    {
                        if (!deps.Budget.TryConsume()) { stopped = true; break; }
                        if (step.Speak) deps.Speak(step.Message);
                        var res = deps.ShowMessage(step);
                        if (res == MsgResult.Yes) deps.RunOnYes(step);
                        else if (res == MsgResult.No) { stopped = true; aborted = true; break; }   // 否/关闭 → 中止整组剩余步骤（含后续轮次），并让上层一并收手
                        if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                    else if (step.Kind == "group")
                    {
                        // 组内嵌套动作组：跑引用的组。嵌套调用共享同一 deps → 同一份预算；
                        // 环重入（以及目标缺失/已禁用等其他「没跑」的结局）由 RunGroupStep 包装层经 OnStepSkipped
                        // 上报——这些都不是异常，OnStepError 专属 RunStep 真正抛出的异常。
                        // 每次引用迭代自身也占一步预算：否则叶子组只含 group 步骤时，999^depth 的展开
                        // 一步都不计费，5000 步保险丝对纯引用链完全失效（文档承诺的「至多 5000 步」要真成立）。
                        int rep = StepHelpers.StepRepeat(step);
                        for (int i = 1; i <= rep && !stopped; i++)
                        {
                            if (!deps.Budget.TryConsume()) { stopped = true; break; }
                            var sub = GroupRunResult.Completed;
                            try { sub = deps.RunGroupStep(step); }
                            catch (Exception ex) { deps.OnStepError(step, ex); }
                            // 子组被「否」中止：本轮剩余步骤与后续轮次全停，并把中止继续往上传
                            // ——否则 rep 次迭代会把同一个确认框重弹 rep 次（循环子序列的推荐做法正好走这条路）。
                            if (sub == GroupRunResult.Aborted) { stopped = true; aborted = true; break; }
                            // 重入注定持续整个循环（同一 id 在同一调用栈上）：再迭代只会重复上报 + 空睡，
                            // Repeat=999 时就是 999 条日志/气泡和 ~99 秒的零工作睡眠。
                            if (sub == GroupRunResult.Skipped) break;
                            if (StopSignal.IsRequested || deps.Budget.Exhausted) stopped = true;
                            else if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                        }
                    }
                    else
                    {
                        int rep = StepHelpers.StepRepeat(step);
                        for (int i = 1; i <= rep && !stopped; i++)
                        {
                            if (!deps.Budget.TryConsume()) { stopped = true; break; }
                            // 单步异常必须就地兜住：否则一步抛异常会中断整组剩余步骤——收工/睡前组里
                            // 若前面某步失败，锁屏/关显示器就不再执行，屏幕开着且无任何提示。每步失败记一笔、整组继续。
                            try { deps.RunStep(step); }
                            catch (Exception ex) { deps.OnStepError(step, ex); }
                            if (StopSignal.IsRequested) stopped = true;
                            else if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                        }
                    }
                }
                if (!stopped && round < rounds && group.RepeatDelayMs > 0 && !StopSignal.InterruptibleSleep(group.RepeatDelayMs)) stopped = true;
            }
            return aborted ? GroupRunResult.Aborted : GroupRunResult.Completed;
        }
        finally { _running.TryRemove(gid, out _); }
    }
}
