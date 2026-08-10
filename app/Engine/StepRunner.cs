using System.Diagnostics;
using System.IO;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;

namespace Clockwork.Engine;

public sealed record StepMark(string Mark, int Fail, int Unver);

// 单步执行与三态标记（✓/⚠/~）。活派发调 Native/Engine；标记归纳逻辑（MarkOf/AggregateRepeat）可测。
public static class StepRunner
{
    // ActionResult → 三态标记。
    public static StepMark MarkOf(ActionResult r)
    {
        if (r.Warning != null) return new StepMark("⚠ " + r.Warning, 1, 0);
        if (r.Unverified) return new StepMark(Strings.Get("Mark_Unverified"), 0, 1);
        return new StepMark("✓", 0, 0);
    }

    // 跑 rep 次并归纳：Mark 取首个非 ✓；Fail/Unver 累加；每次之间急停可中断（末次不等）。
    public static StepMark AggregateRepeat(int rep, Func<int, StepMark> runOne, int delayMs)
    {
        string mark = "✓";
        int fail = 0, unver = 0;
        for (int i = 1; i <= rep; i++)
        {
            var rr = runOne(i);
            fail += rr.Fail; unver += rr.Unver;
            if (mark == "✓" && rr.Mark != "✓") mark = rr.Mark;
            if (i < rep)
            {
                if (StopSignal.IsRequested) break;
                if (delayMs > 0 && !StopSignal.InterruptibleSleep(delayMs)) break;
            }
        }
        return new StepMark(mark, fail, unver);
    }

    // 活：跑单步并归纳标记，捕获异常。
    public static StepMark RunStepMark(LaunchStep s, Func<string, bool> confirmDestructive, IReadOnlyList<string> selfPaths)
    {
        try { return MarkOf(InvokeStepAction(s, confirmDestructive, selfPaths)); }
        catch (Exception ex) { return new StepMark("⚠ " + Strings.Lf("Mark_Exception", ex.Message), 1, 0); }
    }

    // 活：跑单步 repeat 次（单步「运行」/循环动作的测试路径）。
    public static StepMark RunStepMarkRepeat(LaunchStep s, Func<string, bool> confirmDestructive, IReadOnlyList<string> selfPaths)
        => AggregateRepeat(StepHelpers.StepRepeat(s), _ => RunStepMark(s, confirmDestructive, selfPaths), s.DelayMs);

    // 活：单步派发。
    // cancel：本次动作组运行的取消闸；null=没有 per-run 闸（开机清单 / 单步「运行这一步」），只认全局急停。
    // 必须一路传到 WindowManager：等窗口、置前重试、置前延时都可能挂住好几秒，只查全局急停的话，
    // 用户按热键取消动作组之后，这些步骤照样会把窗口拽到前台并把按键打进去。
    public static ActionResult InvokeStepAction(LaunchStep s, Func<string, bool> confirmDestructive, IReadOnlyList<string> selfPaths, RunCancel? cancel = null)
    {
        switch (s.Kind)
        {
            case "app": return RunLaunchItem(s, selfPaths);
            case "keys": return KeyInput.SendKeyCombo(s.Combo);
            case "volume":
                switch (s.Action)
                {
                    case "mute": AudioController.Mute(true); return ActionResult.Empty;
                    case "unmute": AudioController.Mute(false); return ActionResult.Empty;
                    // 设为音量=想听到声音：系统若静音，只改百分比等于没调 → 先解静音再设。
                    case "set": AudioController.Mute(false); AudioController.SetVolumePercent(s.Level); return ActionResult.Empty;
                    case "micMute": AudioController.MuteMic(true); return ActionResult.Empty;
                    case "micUnmute": AudioController.MuteMic(false); return ActionResult.Empty;
                    default: return ActionResult.Warn(Strings.Lf("Warn_UnknownVolume", s.Action));
                }
            case "window":
                {
                    int n = WindowManager.WindowAction(s.Process, s.Action, s.SendKey, s.WaitForWindowSeconds, s.PostWindowDelaySeconds, cancel);
                    if (s.Action == "sendkey")
                    {
                        // 键注入前台后无法证实接收 → 成功记「~ 未校验」；n=0（窗口没出现/抢不到前台）才告警；急停/取消打断也返 0，静默。
                        if (n > 0) return ActionResult.Unver();
                        if (!RunCancel.Stopped(cancel)) return ActionResult.Warn(Strings.Lf("Warn_SendKeyFail", s.Process));
                        return ActionResult.Empty;
                    }
                    // close 幂等：目标态就是「不在运行」，没开=已达成，记 ✓ 不告警。其余动作 0 仍告警；急停/取消返 0 静默。
                    if (n <= 0 && s.Action != "close" && !RunCancel.Stopped(cancel))
                        return ActionResult.Warn(Strings.Lf("Warn_WindowNotFound", s.Process, s.Action));
                    return ActionResult.Empty;
                }
            case "system": SystemCommands.Invoke(s.Command, confirmDestructive, s.Text, s.Level); return ActionResult.Empty;
            case "text": return WindowManager.SendText(s.Text, s.Process, cancel);
            case "delay": return ActionResult.Empty;   // 纯延时：动作由步尾统一 delayMs 完成
            case "message": return ActionResult.Empty;  // 消息在启动/非交互路径静默跳过（交互「运行这一步」由 App.RunStepAsync 弹窗）；不报未知类型
            default: return ActionResult.Warn(Strings.Lf("Warn_UnknownKind", s.Kind));
        }
    }

    // 是否走「已在运行则激活」的捷径而不真的启动。带「参数」时不走——参数的意思是「做这件具体的事」
    // （msedge.exe + 一个网址、notepad.exe + 一个文件）；只把已有窗口带到前台等于把这件事悄悄吞掉，
    // 而且还会记成 ✓ 成功。本选项默认开启，不留这道口子的话每个带参数的新步骤都会中招。
    public static bool ShouldActivateInsteadOfLaunch(LaunchStep s)
        => s.ActivateIfRunning && string.IsNullOrEmpty(s.Args);

    // 活：启动 app 步骤。
    public static ActionResult RunLaunchItem(LaunchStep item, IReadOnlyList<string> selfPaths)
    {
        // 备用路径：主路径不存在时用备用里第一个存在的。
        var tgt = LaunchTarget.ResolveLaunchTarget(item.Target, item.AltTargets);
        if (selfPaths != null && selfPaths.Count > 0 && LaunchTarget.IsSelfTarget(tgt, selfPaths))
            return ActionResult.Warn(Strings.Lf("Warn_SelfSkip", item.Label));

        // 已运行则激活窗口、不重复启动。
        if (ShouldActivateInsteadOfLaunch(item))
        {
            var pn = !string.IsNullOrEmpty(item.ActivateProcess) ? item.ActivateProcess : LaunchTarget.TargetProcessName(tgt);
            pn = StepHelpers.ToProcessName(pn);   // 统一进程名规范化（剥目录+.exe），与其余调用点一致
            if (!string.IsNullOrEmpty(pn) && WindowManager.Handles(pn).Length > 0)
            {
                WindowManager.SetForeground(pn);
                return ActionResult.Empty;
            }
        }

        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };   // 走 shell：可开 URL/URI(ms-settings:)/文档
            if (LaunchTarget.IsPowerShellScript(tgt))
            {
                // .ps1 直接用 PowerShell 跑（否则文件关联进编辑器）。
                psi.FileName = LaunchTarget.PowerShellExe;
                psi.Arguments = LaunchTarget.PowerShellFileArgs(tgt, item.Args);
            }
            else
            {
                psi.FileName = tgt;
                if (!string.IsNullOrEmpty(item.Args)) psi.Arguments = item.Args;
            }

            // 工作目录：留空时默认目标所在目录（仅当目标是完整路径且该目录存在）。
            if (!string.IsNullOrEmpty(item.WorkDir)) psi.WorkingDirectory = item.WorkDir;
            else if (!string.IsNullOrEmpty(tgt))
            {
                string td = "";
                try { if (Path.IsPathRooted(tgt)) td = Path.GetDirectoryName(tgt) ?? ""; } catch { td = ""; }
                if (td != "" && Directory.Exists(td)) psi.WorkingDirectory = td;
            }

            psi.WindowStyle = item.WindowStyle switch
            {
                "minimized" => ProcessWindowStyle.Minimized,
                "maximized" => ProcessWindowStyle.Maximized,
                "hidden" => ProcessWindowStyle.Hidden,
                _ => ProcessWindowStyle.Normal,
            };
            if (item.Elevated) psi.Verb = "runas";

            var proc = Process.Start(psi);
            // Start 不抛错只代表进程被拉起。秒退且退出码非 0=多半启动失败；拿不到进程对象(ShellExecute 开文档/URL)则跳过、保持 ✓。
            if (proc != null)
            {
                try
                {
                    if (proc.WaitForExit(500) && proc.ExitCode != 0)
                        return ActionResult.Warn(Strings.Lf("Warn_QuickExit", item.Label, proc.ExitCode));
                }
                catch { }
            }
            return ActionResult.Empty;
        }
        catch (Exception ex)
        {
            return ActionResult.Warn(Strings.Lf("Warn_LaunchFail", item.Label, ex.Message));
        }
    }
}
