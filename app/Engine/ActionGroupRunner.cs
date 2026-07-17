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
}

// 顺序执行动作组。message 步骤弹确认闸门（否/关闭→中止整组）；其余步骤循环 repeat；步骤时间条件同顶层清单遵守。
// 按组 id 进程内互斥防重入（单进程用运行集即可）。
public static class ActionGroupRunner
{
    private static readonly ConcurrentDictionary<string, byte> _running = new();

    public static void RunGroup(ActionGroup group, GroupDeps deps)
    {
        var gid = group.Id ?? "";
        if (!_running.TryAdd(gid, 0)) return;   // 已在跑：忽略本次触发（避免双开/按键交错）
        try
        {
            var now = DateTime.Now;   // 取一次，小时/分钟同源，避免跨分钟边界不一致
            var (hour, iso) = StepCondition.ResolveSentinels(deps.Hour, deps.IsoDay, now);
            bool stopped = false;
            foreach (var step in group.Steps)
            {
                if (stopped || StopSignal.IsRequested) break;
                if (!step.Enabled) continue;
                if (!StepCondition.IsSatisfied(step, hour, iso, now.Minute)) continue;   // 组内步骤同样遵守时间条件（分钟级）

                if (step.Kind == "message")
                {
                    if (step.Speak) deps.Speak(step.Message);
                    var res = deps.ShowMessage(step);
                    if (res == MsgResult.Yes) deps.RunOnYes(step);
                    else if (res == MsgResult.No) break;   // 否/关闭 → 中止整组剩余步骤
                    if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                }
                else if (step.Kind == "group")
                {
                    // 组内嵌套动作组：跑引用的组（否则会落到 InvokeStepAction 的未知类型分支、静默不执行）。
                    int rep = StepHelpers.StepRepeat(step);
                    for (int i = 1; i <= rep && !stopped; i++)
                    {
                        try { deps.RunGroupStep(step); }
                        catch (Exception ex) { deps.OnStepError(step, ex); }
                        if (StopSignal.IsRequested) stopped = true;
                        else if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                }
                else
                {
                    int rep = StepHelpers.StepRepeat(step);
                    for (int i = 1; i <= rep && !stopped; i++)
                    {
                        // 单步异常必须就地兜住：否则一步抛异常会中断整组剩余步骤——收工/睡前组里
                        // 若前面某步失败，锁屏/关显示器就不再执行，屏幕开着且无任何提示。每步失败记一笔、整组继续。
                        try { deps.RunStep(step); }
                        catch (Exception ex) { deps.OnStepError(step, ex); }
                        if (StopSignal.IsRequested) stopped = true;
                        else if (step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                }
            }
        }
        finally { _running.TryRemove(gid, out _); }
    }
}
