using System.IO;
using System.Text;
using Clockwork.Core;

namespace Clockwork.Engine;

public sealed record LaunchSummary(int Total, int Fail, int Unverified, bool Stopped);
public sealed record LaunchRunResult(LaunchSummary Summary, IReadOnlyList<string> LogLines, string? BootNote);

// 启动序列编排：就绪门控 + 开机延时 + 建计划 + group 展开 + 循环 + 急停 + 三态日志。
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
        var plan = LaunchPlan.Build(config, hour, isoDay);
        var lines = new List<string>();
        int fail = 0, unver = 0, total = 0;

        foreach (var step in plan)
        {
            if (!stopped && StopSignal.IsRequested) stopped = true;
            if (stopped) break;
            int rep = StepHelpers.StepRepeat(step);

            if (step.Kind == "group")
            {
                var g = ActionGroupResolver.Resolve(config.ActionGroups, step.GroupId);
                if (g == null) { lines.Add($"[{Ts(now)}] {StepDisplay.StepSummary(step)}  ⚠ 找不到动作组"); fail++; total++; }
                else if (!g.Enabled) { lines.Add($"[{Ts(now)}] {StepDisplay.StepSummary(step)}  · 动作组「{g.Name}」已禁用，跳过"); }
                else
                {
                    for (int gi = 1; gi <= rep && !stopped; gi++)
                    {
                        var hdr = rep > 1 ? $"运行动作组：{g.Name}（第 {gi}/{rep} 次）" : $"运行动作组：{g.Name}";
                        lines.Add($"[{Ts(now)}] {hdr}");
                        foreach (var sub in g.Steps)
                        {
                            if (!stopped && StopSignal.IsRequested) stopped = true;
                            if (stopped) break;
                            if (!sub.Enabled) continue;
                            if (!StepCondition.IsSatisfied(sub, hour, isoDay)) continue;   // 组内步骤同样遵守时间条件
                            if (sub.Kind == "message") continue;                            // 启动展开跳过 message（启动静默，不弹确认）
                            if (sub.Kind == "group") { lines.Add($"[{Ts(now)}]     {StepDisplay.StepSummary(sub)}  · 开机时不展开嵌套动作组，已跳过"); continue; }
                            int subRep = StepHelpers.StepRepeat(sub);
                            for (int si = 1; si <= subRep && !stopped; si++)
                            {
                                var rr = stepMark(sub);
                                var subSfx = subRep > 1 ? $"（第 {si}/{subRep} 次）" : "";
                                lines.Add($"[{Ts(now)}]     {StepDisplay.StepSummary(sub)}{subSfx}  {rr.Mark}");
                                fail += rr.Fail; unver += rr.Unver; total++;
                                if (StopSignal.IsRequested) stopped = true;
                                else if (sub.DelayMs > 0 && !StopSignal.InterruptibleSleep(sub.DelayMs)) stopped = true;
                            }
                        }
                        if (!stopped && gi < rep && step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                    }
                }
            }
            else
            {
                for (int i = 1; i <= rep && !stopped; i++)
                {
                    var rr = stepMark(step);
                    var sfx = rep > 1 ? $"（第 {i}/{rep} 次）" : "";
                    lines.Add($"[{Ts(now)}] {StepDisplay.StepSummary(step)}{sfx}  {rr.Mark}");
                    fail += rr.Fail; unver += rr.Unver; total++;
                    if (StopSignal.IsRequested) stopped = true;
                    else if (i < rep && step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
                }
            }
            if (!stopped && step.DelayMs > 0 && !StopSignal.InterruptibleSleep(step.DelayMs)) stopped = true;
        }

        if (stopped) lines.Add($"[{Ts(now)}] ⏹ 已手动停止，后续步骤未执行");
        return new LaunchRunResult(new LaunchSummary(total, fail, unver, stopped), lines, bootNote);
    }

    private static string Ts(Func<DateTime> now) => now().ToString("HH:mm:ss");
    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "；" + b;

    // 写启动日志文件（活）。
    public static void WriteLog(string path, LaunchRunResult r, DateTime when)
    {
        var s = r.Summary;
        var bootHdr = string.IsNullOrEmpty(r.BootNote) ? "" : r.BootNote + "\r\n";
        var stopHdr = s.Stopped ? "⏹ 本次运行被手动停止（急停键 / 托盘「停止」）\r\n" : "";
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
