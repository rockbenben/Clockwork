using System.Collections.Concurrent;
using Clockwork.Core;

namespace Clockwork.Engine;

public enum MsgResult { Yes, No, Ok }

// 动作组执行的依赖 seam（活交互经此注入，便于测编排流程）。
public sealed class GroupDeps
{
    public int Hour { get; init; } = -1;                                   // <0 → 取当前
    public int IsoDay { get; init; }                                       // <=0 → 取当前
    public Action<LaunchStep> RunStep { get; init; } = _ => { };           // 非 message 步骤执行（生产=InvokeStepAction 丢结果）
    public Func<LaunchStep, MsgResult> ShowMessage { get; init; } = _ => MsgResult.Ok;  // message 步骤弹窗
    public Action<LaunchStep> RunOnYes { get; init; } = _ => { };          // message 点是→onYes
    public Action<string> Speak { get; init; } = _ => { };                 // message 播报
    public Action<LaunchStep> RunGroupStep { get; init; } = _ => { };       // 组内嵌套「group」步骤：跑引用的组
    public Action<LaunchStep, Exception> OnStepError { get; init; } = (_, _) => { };    // 某步抛异常：记录后继续（不中止整组）
    public RunBudget Budget { get; init; } = new();                        // 单次顶层运行共享的步数预算（嵌套引用经同一 deps 传递）
}

// 顺序执行动作组，整组按 group.Repeat 跑若干轮（轮间睡 RepeatDelayMs，可急停）。
// message 步骤弹确认闸门（否/关闭→中止整组）；其余步骤循环 repeat；步骤时间条件同顶层清单遵守。
// 按组 id 进程内互斥防重入（单进程用运行集即可）；重入（含环引用）返回 false，是否上报由调用方决定。
public static class ActionGroupRunner
{
    private static readonly ConcurrentDictionary<string, byte> _running = new();

    public static bool RunGroup(ActionGroup group, GroupDeps deps)
    {
        var gid = group.Id ?? "";
        if (!_running.TryAdd(gid, 0)) return false;   // 已在跑：忽略本次触发（避免双开/按键交错/环引用空转）
        try
        {
            var now = DateTime.Now;   // 取一次，小时/分钟同源，避免跨分钟边界不一致
            var (hour, iso) = StepCondition.ResolveSentinels(deps.Hour, deps.IsoDay, now);
            bool stopped = false;
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
                        else if (res == MsgResult.No) { stopped = true; break; }   // 否/关闭 → 中止整组剩余步骤（含后续轮次）
                        if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                    else if (step.Kind == "group")
                    {
                        // 组内嵌套动作组：跑引用的组。嵌套调用共享同一 deps → 同一份预算；
                        // 自身不占预算（其内部每步会占），环重入由 RunGroupStep 包装层经 OnStepError 上报。
                        int rep = StepHelpers.StepRepeat(step);
                        for (int i = 1; i <= rep && !stopped; i++)
                        {
                            try { deps.RunGroupStep(step); }
                            catch (Exception ex) { deps.OnStepError(step, ex); }
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
            return true;
        }
        finally { _running.TryRemove(gid, out _); }
    }
}
