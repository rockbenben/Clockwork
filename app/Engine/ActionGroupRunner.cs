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
    // 单次顶层运行的取消闸（同 Budget，一次触发一份、经同一 deps 传给整条嵌套链）。动作组热键按第二次
    // 时置位——它取消的是「这一次运行」，不是全局急停：别的组和开机启动清单不该被一个组的热键带走。
    public RunCancel Cancel { get; init; } = new();
}

// 顺序执行动作组，整组按 group.Repeat 跑若干轮（轮间睡 RepeatDelayMs，可急停/可取消）。
// message 步骤弹确认闸门（否/关闭→中止整组）；其余步骤循环 repeat；步骤时间条件同顶层清单遵守。
// 按组 id 进程内互斥防重入（单进程用运行集即可）；重入（含环引用）返回 Skipped，是否上报由调用方决定。
// 「谁在跑」(_running) 与「按哪个键能取消谁」(_topRuns) 是两张表，别再合并回一张——理由见各自注释。
// 停止有两条来源，粒度不同：全局急停 StopSignal 停一切；deps.Cancel 只停这一次运行（动作组热键按第二次）。
// 循环里一律查 deps.Cancel.IsStopped（它已含全局急停），别再直接读 StopSignal——那会让单组取消失效。
public static class ActionGroupRunner
{
    // 「谁在跑」——纯防重入集，引用链上每个组 id 都登记（顶层与嵌套子组一视同仁）。
    // 同 id 再进即 Skipped，挡住双开 / 按键交错 / 环引用空转。
    private static readonly ConcurrentDictionary<string, byte> _running = new();

    // 「按哪个键能取消哪一次运行」——只登记顶层运行，键是发起它的那个组 id。
    // 必须与 _running 分开：两件事共用一份表时，链上任一子组的 id 都指向顶层的取消闸，
    // 于是用户按子组热键（本意「跑一下这个子组」）会把毫不相干的父组整轮掐掉——父组的锁屏/息屏
    // 尾步全不执行，气泡还只报子组名，根本指不到被杀的那一个。键相同、含义不同，就得是两张表。
    private static readonly ConcurrentDictionary<string, RunCancel> _topRuns = new();

    // 顶层运行的登记/注销，由派发方（App.RunGroupAsync）在后台任务里成对调用。
    // 返回 false = 该 id 已有顶层运行占位（并发重复触发），调用方不必注销。
    public static bool EnterTopLevel(string groupId, RunCancel cancel) => _topRuns.TryAdd(groupId ?? "", cancel);

    // 按「键+值」删除，绝不误删别人后来登记的同 id 运行。
    public static void ExitTopLevel(string groupId, RunCancel cancel)
        => _topRuns.TryRemove(new KeyValuePair<string, RunCancel>(groupId ?? "", cancel));

    // 开机清单的内联展开也要占同一个运行集（LaunchSequence 用）：文档承诺「同一组同时只会跑一份」，
    // 而那条路径以前完全没登记，开机期间按该组热键会再开一份并发副本。
    public static bool TryEnterRunning(string groupId) => _running.TryAdd(groupId ?? "", 0);
    public static void ExitRunning(string groupId) => _running.TryRemove(groupId ?? "", out _);

    // 某组此刻是否正在跑（任何来源：顶层、嵌套子组、开机清单展开）。热键据此把「在跑但这个键取消不了」
    // 与「压根没在跑」分开——前者不能谎报「已启动」，也不该再开一份；后者才该启动。
    public static bool IsRunning(string groupId) => _running.ContainsKey(groupId ?? "");

    // 请求取消某组当前这次顶层运行。返回 true=确实有一份在跑、已置位；false=没有可取消的顶层运行
    //（没在跑，或在跑的那份是别人的嵌套子步骤 / 开机清单展开——调用方要能分辨，见 IsRunning）。
    // 只置位不等待：调用方是 UI 线程（热键钩子），执行线程会在下一个动作边界自查退出。
    public static bool RequestCancel(string groupId)
    {
        if (!_topRuns.TryGetValue(groupId ?? "", out var cancel)) return false;
        cancel.Request();
        return true;
    }

    // 全局急停时把「停」推给每个在途运行的闸。RunCancel 的可中断延时只等自己那一个事件（理由见该处注释），
    // 所以急停必须主动推过来，否则一个睡在 30 分钟轮间延迟里的组要等这一觉睡完才发现急停。
    // 只需遍历顶层：嵌套子组与顶层共用同一个闸，推给顶层即全链生效。
    // 本次推送之后才开跑的运行拿不到推送，但它们的 IsStopped 会读到仍然置位的全局信号，照样停。
    public static void CancelAll()
    {
        foreach (var cancel in _topRuns.Values) cancel.Request();
    }

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
                    if (stopped || deps.Cancel.IsStopped || deps.Budget.Exhausted) { stopped = true; break; }
                    if (!step.Enabled) continue;
                    if (!StepCondition.IsSatisfied(step, hour, iso, now.Minute)) continue;   // 组内步骤同样遵守时间条件（分钟级）

                    if (step.Kind == "message")
                    {
                        if (!deps.Budget.TryConsume()) { stopped = true; break; }
                        if (step.Speak) deps.Speak(step.Message);
                        var res = deps.ShowMessage(step);
                        // 弹窗是模态、要等用户点掉才返回，其间取消/急停完全可能已经按下。此时那个答案作废：
                        // 用户按取消的意思是「这组别做了」，不是「把 onYes 那一串做完再停」。查在 RunOnYes 之前。
                        if (deps.Cancel.IsStopped) { stopped = true; break; }
                        if (res == MsgResult.Yes) deps.RunOnYes(step);
                        else if (res == MsgResult.No) { stopped = true; aborted = true; break; }   // 否/关闭 → 中止整组剩余步骤（含后续轮次），并让上层一并收手
                        if (step.DelayMs > 0 && !deps.Cancel.InterruptibleSleep(step.DelayMs)) stopped = true;
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
                            if (deps.Cancel.IsStopped || deps.Budget.Exhausted) stopped = true;
                            else if (step.DelayMs > 0 && !deps.Cancel.InterruptibleSleep(step.DelayMs)) stopped = true;
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
                            if (deps.Cancel.IsStopped) stopped = true;
                            else if (step.DelayMs > 0 && !deps.Cancel.InterruptibleSleep(step.DelayMs)) stopped = true;
                        }
                    }
                }
                if (!stopped && round < rounds && group.RepeatDelayMs > 0 && !deps.Cancel.InterruptibleSleep(group.RepeatDelayMs)) stopped = true;
            }
            return aborted ? GroupRunResult.Aborted : GroupRunResult.Completed;
        }
        finally { _running.TryRemove(gid, out _); }
    }
}
