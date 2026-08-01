using System.IO;
using System.Text;
using Clockwork.Core;

namespace Clockwork.Engine;

public sealed record LaunchSummary(int Total, int Fail, int Unverified, bool Stopped, bool Truncated);
public sealed record LaunchRunResult(LaunchSummary Summary, IReadOnlyList<string> LogLines, string? BootNote);

// 启动序列编排：就绪门控 + 开机延时 + 建计划 + group 递归展开（沿途访问集挡环）+ 组级轮次 + 单步预算 + 循环 + 急停 + 三态日志。
// 单步执行经注入 stepMark（默认 StepRunner.RunStepMark），便于测展开/循环/急停/计数；boot 门控用真实 ReadyGate/StopSignal。
public static class LaunchSequence
{
    public static LaunchRunResult Run(RootConfig config, bool boot, int hour, int isoDay,
        Func<LaunchStep, StepMark> stepMark, Func<DateTime> now)
    {
        string? bootNote = null;
        bool stopped = false;

        if (boot && config.Settings != null)
        {
            // 可选就绪门控（默认关）。
            if (config.Settings.StartupWaitForReady)
            {
                var r = ReadyGate.WaitSystemReady();
                bootNote = $"就绪门控：等待 {r.WaitedMs / 1000.0:F1}s（Shell={r.Shell} 网络={r.Net}）{(!r.Ready ? "，超时仍未就绪，照常放行" : "")}";
            }
            // 诚实固定延时（主杠杆）：可被急停打断。手改配置可能写入越界值，消费侧 clamp 到 [0,600] 与设置页一致。
            int preDelay = StepHelpers.ClampStartupDelay(config.Settings.StartupDelaySeconds);
            if (preDelay > 0)
            {
                bootNote = Join(bootNote, $"开机延迟：{preDelay}s");
                if (!StopSignal.InterruptibleSleep(preDelay * 1000L)) stopped = true;
            }
        }

        var nowDt = now();
        (hour, isoDay) = StepCondition.ResolveSentinels(hour, isoDay, nowDt);
        var plan = LaunchPlan.Build(config, hour, isoDay, nowDt.Minute);
        var lines = new List<string>();
        int fail = 0, unver = 0, total = 0;
        // 执行步数预算：环由沿途访问集挡住，这里兜「×999 套 ×999」的有限爆炸（与 ActionGroupRunner 同一常量）。
        int budgetLeft = RunBudget.MaxRunSteps;
        bool budgetOut = false;

        bool Consume()
        {
            if (budgetLeft > 0) { budgetLeft--; return true; }
            if (!budgetOut) { budgetOut = true; fail++; lines.Add($"[{Ts(now)}] ⚠ 单次运行已达 {RunBudget.MaxRunSteps} 步上限，剩余步骤未执行"); }
            return false;
        }

        // 跑一个普通步骤 rep 次（顶层与组内共用；pad=日志缩进）。
        void RunPlain(LaunchStep s, string pad)
        {
            int rep = StepHelpers.StepRepeat(s);
            for (int i = 1; i <= rep && !stopped; i++)
            {
                if (!Consume()) { stopped = true; break; }
                var rr = stepMark(s);
                var sfx = rep > 1 ? $"（第 {i}/{rep} 次）" : "";
                lines.Add($"[{Ts(now)}] {pad}{StepDisplay.StepSummary(s)}{sfx}  {rr.Mark}");
                fail += rr.Fail; unver += rr.Unver; total++;
                if (StopSignal.IsRequested) stopped = true;
                else if (i < rep && s.DelayMs > 0 && !StopSignal.InterruptibleSleep(s.DelayMs)) stopped = true;
            }
        }

        // 递归展开动作组：整组 g.Repeat 轮（轮间睡 RepeatDelayMs）；组内 group 步骤继续下钻。
        // pathIds=当前引用链上的组 id：同链再现即环（手改 json 造出，编辑器 DFS 拦不到的），告警跳过；
        // 同级引用同一组两次不在同链上，照常各跑。深度由访问集自然封顶（每组每链至多一次）。
        // 返回 false = 这一次没跑（该组已被别的来源占着）。调用方据此立刻收掉剩余的引用轮次：
        // 结论在整个循环里不会变，再迭代只是重复记日志 + 空烧预算，而预算是与整张清单共享的
        // 5000 步保险丝——Repeat=999 的引用能烧掉它五分之一，把清单后面真正要跑的步骤挤没。
        // （同 ActionGroupRunner 收到 Skipped 就 break 的理由。）
        bool RunGroupInline(ActionGroup g, int depth, HashSet<string> pathIds)
        {
            string pad = new string(' ', 4 * depth);
            // 占住与 ActionGroupRunner 共用的运行集。文档承诺「同一组同时只会跑一份」，而开机清单这条
            // 路径以前完全没登记：开机跑清单期间按该组热键，会再开一份并发副本（关窗口/发按键/锁屏各来两轮）。
            // 登记后那次按键被识别成「在跑但取消不了」，给提示而不重复执行。
            if (!ActionGroupRunner.TryEnterRunning(g.Id))
            {
                // 记一笔失败：这一步没执行，清单没做到它承诺的事，用户该在回执里看到「N 步失败/警告」。
                lines.Add($"[{Ts(now)}] {pad}动作组「{g.Name}」已在运行（由别处触发），本次跳过");
                fail++; total++;
                return false;
            }
            try { RunGroupRounds(g, depth, pathIds, pad); }
            finally { ActionGroupRunner.ExitRunning(g.Id); }
            return true;
        }

        void RunGroupRounds(ActionGroup g, int depth, HashSet<string> pathIds, string pad)
        {
            int rounds = StepHelpers.ClampRepeat(g.Repeat);
            for (int round = 1; round <= rounds && !stopped; round++)
            {
                foreach (var sub in g.Steps)
                {
                    if (!stopped && StopSignal.IsRequested) stopped = true;
                    if (stopped) break;
                    if (!sub.Enabled) continue;
                    if (!StepCondition.IsSatisfied(sub, hour, isoDay, nowDt.Minute)) continue;   // 组内步骤同样遵守时间条件
                    // 启动路径只跳过模态 message（开机时没人在座，弹是/否框会把整条清单挂住）。
                    // 卡片形态不拦路，照常执行——「早晨例程已就绪」这类提示正是它的主场。
                    if (sub.Kind == "message" && StepHelpers.MessageFormOf(sub) != MessageForm.Card) continue;
                    if (sub.Kind == "group")
                    {
                        var ng = ActionGroupResolver.Resolve(config.ActionGroups, sub.GroupId);
                        if (ng == null) { lines.Add($"[{Ts(now)}] {pad}{StepDisplay.StepSummary(sub)}  ⚠ 找不到动作组"); fail++; total++; continue; }
                        if (!ng.Enabled) { lines.Add($"[{Ts(now)}] {pad}{StepDisplay.StepSummary(sub)}  · 动作组「{ng.Name}」已禁用，跳过"); continue; }
                        if (pathIds.Contains(ng.Id)) { lines.Add($"[{Ts(now)}] {pad}{StepDisplay.StepSummary(sub)}  ⚠ 环引用，已跳过"); fail++; total++; continue; }
                        int rep = StepHelpers.StepRepeat(sub);
                        for (int i = 1; i <= rep && !stopped; i++)
                        {
                            // 引用迭代自身也计一步（与 ActionGroupRunner 同步）：叶子组只含 group/message 步骤时
                            // 普通步骤一次都不计费，999^depth 的展开能绕过整个预算——而开机路径上用户还没有窗口可按急停。
                            if (!Consume()) { stopped = true; break; }
                            var hdr = rep > 1 ? $"运行动作组：{ng.Name}（第 {i}/{rep} 次）" : $"运行动作组：{ng.Name}";
                            lines.Add($"[{Ts(now)}] {pad}{hdr}");
                            pathIds.Add(ng.Id);
                            bool ranSub = RunGroupInline(ng, depth + 1, pathIds);
                            pathIds.Remove(ng.Id);
                            if (!ranSub) break;   // 被别处占着：结论不会变，收掉剩余轮次（见 RunGroupInline）
                            if (StopSignal.IsRequested) stopped = true;
                            else if (i < rep && sub.DelayMs > 0 && !StopSignal.InterruptibleSleep(sub.DelayMs)) stopped = true;
                        }
                        if (!stopped && sub.DelayMs > 0 && !StopSignal.InterruptibleSleep(sub.DelayMs)) stopped = true;
                        continue;
                    }
                    RunPlain(sub, pad + "    ");
                    if (!stopped && sub.DelayMs > 0 && !StopSignal.InterruptibleSleep(sub.DelayMs)) stopped = true;
                }
                if (!stopped && round < rounds && g.RepeatDelayMs > 0 && !StopSignal.InterruptibleSleep(g.RepeatDelayMs)) stopped = true;
            }
        }

        foreach (var step in plan)
        {
            if (!stopped && StopSignal.IsRequested) stopped = true;
            if (stopped) break;

            if (step.Kind == "group")
            {
                var g = ActionGroupResolver.Resolve(config.ActionGroups, step.GroupId);
                if (g == null) { lines.Add($"[{Ts(now)}] {StepDisplay.StepSummary(step)}  ⚠ 找不到动作组"); fail++; total++; }
                else if (!g.Enabled) { lines.Add($"[{Ts(now)}] {StepDisplay.StepSummary(step)}  · 动作组「{g.Name}」已禁用，跳过"); }
                else
                {
                    int rep = StepHelpers.StepRepeat(step);
                    for (int gi = 1; gi <= rep && !stopped; gi++)
                    {
                        if (!Consume()) { stopped = true; break; }   // 顶层引用迭代同样计费，理由同组内
                        var hdr = rep > 1 ? $"运行动作组：{g.Name}（第 {gi}/{rep} 次）" : $"运行动作组：{g.Name}";
                        lines.Add($"[{Ts(now)}] {hdr}");
                        if (!RunGroupInline(g, 1, new HashSet<string> { g.Id })) break;   // 同上：收掉剩余轮次
                        if (!stopped && gi < rep && step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                }
            }
            else RunPlain(step, "");
            if (!stopped && step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
        }

        if (stopped && !budgetOut) lines.Add($"[{Ts(now)}] ⏹ 已手动停止，后续步骤未执行");
        return new LaunchRunResult(new LaunchSummary(total, fail, unver, stopped, budgetOut), lines, bootNote);
    }

    private static string Ts(Func<DateTime> now) => now().ToString("HH:mm:ss");
    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "；" + b;

    // 写启动日志文件（活）。
    public static void WriteLog(string path, LaunchRunResult r, DateTime when)
    {
        var s = r.Summary;
        var bootHdr = string.IsNullOrEmpty(r.BootNote) ? "" : r.BootNote + "\r\n";
        // 截停与手动急停是两回事：截停时 Stopped 也为 true（循环因此提前退出），但真相是撞了步数上限，
        // 不是用户按了急停/托盘「停止」——两者都占位时优先说更具体、更真实的截停（复用正文预算行的措辞）。
        var stopHdr = s.Truncated ? $"⏹ 本次运行已达 {RunBudget.MaxRunSteps} 步上限，剩余步骤未执行\r\n"
            : s.Stopped ? "⏹ 本次运行被手动停止（急停键 / 托盘「停止」）\r\n" : "";
        var sb = new StringBuilder();
        sb.Append("Clockwork · 上次启动清单运行日志\r\n");
        sb.Append($"时间：{when:yyyy-MM-dd HH:mm:ss}\r\n");
        sb.Append(bootHdr).Append(stopHdr);
        sb.Append($"共 {s.Total} 步：{s.Fail} 步失败/警告、{s.Unverified} 步已发送但无法校验、其余成功\r\n");
        sb.Append("（~ 表示按键/热键类动作已注入，但目标是否响应无法确认）\r\n");
        sb.Append(new string('=', 40)).Append("\r\n");
        sb.Append(string.Join("\r\n", r.LogLines));
        try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); } catch { }
    }
}
