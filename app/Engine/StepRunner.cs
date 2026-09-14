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
        if (r.HasWarning) return new StepMark("⚠ " + r.Warning, 1, 0);
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
    public static StepMark RunStepMark(LaunchStep s, Func<string, bool>? confirmDestructive, IReadOnlyList<string> selfPaths,
                                       RunVars? vars = null)
    {
        try { return MarkOf(InvokeStepAction(s, confirmDestructive, selfPaths, null, vars)); }
        catch (Exception ex) { return new StepMark("⚠ " + Strings.Lf("Mark_Exception", ex.Message), 1, 0); }
    }

    // 活：跑单步 repeat 次（单步「运行」/循环动作的测试路径）。
    public static StepMark RunStepMarkRepeat(LaunchStep s, Func<string, bool>? confirmDestructive, IReadOnlyList<string> selfPaths,
                                             RunVars? vars = null)
        => AggregateRepeat(StepHelpers.StepRepeat(s), _ => RunStepMark(s, confirmDestructive, selfPaths, vars), s.DelayMs);

    // 活：单步派发。
    // cancel：本次动作组运行的取消闸；null=没有 per-run 闸（开机清单 / 单步「运行这一步」），只认全局急停。
    // 必须一路传到 WindowManager：等窗口、置前重试、置前延时都可能挂住好几秒，只查全局急停的话，
    // 用户按热键取消动作组之后，这些步骤照样会把窗口拽到前台并把按键打进去。
    //
    // vars：这一次运行的变量表（见 Core.RunVars）。null = 这条路径上没有变量，占位符只认 {clipboard}。
    // 产出值的步骤往里写，送文字出去的步骤从里读——这一层是**唯一**同时看得见两边的地方。
    public static ActionResult InvokeStepAction(LaunchStep s, Func<string, bool>? confirmDestructive, IReadOnlyList<string> selfPaths,
                                                RunCancel? cancel = null, RunVars? vars = null)
    {
        switch (s.Kind)
        {
            case "app": return RunLaunchItem(s, selfPaths);
            case "keys": return KeyInput.SendKeyCombo(s.Combo);
            // 鼠标步骤只是「发送按键」的一个预设入口：转成伪键走同一条注入路（含 UIPI 被拒的同款告警）。
            // 别在这里另起一套 SendWheel/SendMouseButton 调用——两处各写一份，将来改注入行为必漏一处。
            // 点几次/滚几格用步骤自带的「重复次数」，与手写伪键的行为完全一致。
            case "mouse": return KeyInput.SendKeyCombo(StepDisplay.MouseActionCombo(s.Action));
            case "volume":
                switch (s.Action)
                {
                    case "mute": AudioController.Mute(true); return ActionResult.Empty;
                    case "unmute": AudioController.Mute(false); return ActionResult.Empty;
                    // 设为音量=想听到声音：系统若静音，只改百分比等于没调 → 先解静音再设。
                    case "set": AudioController.Mute(false); AudioController.SetVolumePercent(s.Level); return ActionResult.Empty;
                    case "micMute": AudioController.MuteMic(true); return ActionResult.Empty;
                    case "micUnmute": AudioController.MuteMic(false); return ActionResult.Empty;
                    default: return ActionResult.Warn("Warn_UnknownVolume", s.Action);
                }
            case "window":
                {
                    var r = WindowManager.WindowAction(s.Process, s.Action, s.SendKey, s.WaitForWindowSeconds, s.PostWindowDelaySeconds, cancel);
                    if (s.Action == "sendkey")
                    {
                        // 键注入前台后无法证实接收 → 成功记「~ 未校验」；没发出去才告警；急停/取消静默。
                        if (r == WindowOutcome.Ok) return ActionResult.Unver();
                        if (r == WindowOutcome.Cancelled) return ActionResult.Empty;
                        return ActionResult.Warn("Warn_SendKeyFail", s.Process);
                    }
                    return r switch
                    {
                        WindowOutcome.Ok or WindowOutcome.Cancelled => ActionResult.Empty,
                        // 「当前窗口」没找到 = 前台是 Clockwork 自己（在主窗口里点「运行这一步」试跑
                        // 就是这种情况）或压根没有前台，与「那个程序没开」是两回事，也套不了 close 的幂等——
                        // 什么都没关掉，只是没有目标。
                        // 「恢复活动窗口」找不到目标与「当前窗口」找不到目标是同一件事，同一句话：
                        // 触发那一刻前台是 Clockwork 自己，或者压根没有前台窗口。
                        WindowOutcome.NoWindow when StepDisplay.IsCurrentWindow(s) || s.Action == "restore"
                            => ActionResult.Warn("Warn_NoForegroundWindow"),
                        // close 幂等：目标态就是「不在运行」，没开=已达成，记 ✓ 不告警。
                        WindowOutcome.NoWindow => s.Action == "close" ? ActionResult.Empty
                            : ActionResult.Warn("Warn_WindowNotFound", s.Process, s.Action),
                        // 「回到刚才那个窗口」没有目标进程，套不进「找到了『{0}』的窗口，但…」那句话——
                        // 拼出来是「找到了「」的窗口」，一个空引号。它自己一句。
                        WindowOutcome.Failed when s.Action == "restore"
                            => ActionResult.Warn("Warn_RestoreFailed"),
                        // 窗口在、动作没生效：与「找不到窗口」是两回事，措辞必须分开——
                        // 前者该去查进程为什么没起来，后者该去看是不是前台锁定/提权窗口/弹框挡住了。
                        WindowOutcome.Failed => ActionResult.Warn("Warn_WindowActionFailed", StepDisplay.WindowTarget(s), s.Action),
                        // 动作合法、只是这个目标不开放它（「当前窗口」+ 置前/发键）。这个组合在编辑器里
                        // 点得出来，所以它必须有自己的话——落到下面那句会变成「不认识的窗口动作：activate」，
                        // 指着一个完全合法的动作说不认识，而问题在目标那一侧。
                        WindowOutcome.NotForCurrentWindow
                            => ActionResult.Warn("Warn_NotForCurrentWindow", s.Action),
                        _ => ActionResult.Warn("Warn_UnknownWindowAction", s.Action),
                    };
                }
            // 文本参数一律先替换：设置剪贴板文本、搜索地址模板都在这个字段上。
            // 搜索模板里那个 {0} 查不到同名变量，会原样留着交给 SystemCommands 自己替换——两套占位互不打架。
            case "system":
                // **无人值守的路径上不弹确认框，如实跳过。** confirmDestructive 为 null 就是
                // 「这条执行路径上没有可以问的人」——与下面 prompt/choice 那一支同一个道理，
                // 也同一种交代法。改之前开机清单里放一条「注销 / 重启 / 关机」，登录几秒后
                // 就会弹出一个置顶模态把整条清单卡住等人来点，而急停键只在步骤之间被检查、解不开它。
                // 静默当作「用户点了取消」也不行：那会在 run.log 里记一个 ✓，而事情压根没做。
                // 拦的是 RequiresHuman 而不是 IsDestructive：两者的差别就是清空回收站。
                // 把它也拦下来曾经把一件 2.7.1 起就能用的自动化改成了永不跑（见 RequiresHuman 的注释）。
                if (confirmDestructive == null && SystemCommands.RequiresHuman(s.Command))
                    return ActionResult.Warn("Warn_DestructiveNoUi");
                // 占位回调是**恒真**。走到这儿而 confirmDestructive 为 null，意思是「没人可问，
                // 但这条命令允许无人值守跑」——那就该跑。恒假会让它静默什么都不做，
                // 而 run.log 记下一个 ✓：那正是上面那段注释反对的「假装做了」。
                // 不问自动化的命令本就不会调它，恒真对它们无影响。
                SystemCommands.Invoke(s.Command, confirmDestructive ?? (_ => true), Fill(s.Text, false, vars), s.Level);
                return ActionResult.Empty;
            case "text": return WindowManager.SendText(Fill(s.Text, urlEncode: false, vars), s.Process, cancel);
            // 打开网址：从「运行程序」拆出来的一档。执行上仍是 ShellExecute（同一条路），
            // 拆出来的收益在模型侧——编辑器只剩一个地址框，摘要显示网址本身，
            // 而且**只有送出去的文字支持占位替换**：在 app 的可执行路径里做替换是注入面。
            case "url":
            {
                var url = Fill(s.Target, urlEncode: true, vars).Trim();
                if (url.Length == 0) return ActionResult.Warn("Warn_LaunchFail", "", new Strings.Ref("Sum_Unset"));
                // 自指要拦，与 app 那一支同一条（那边是 IsSelfTarget + Warn_SelfSkip）。
                // 这一档尤其容易撞上：「运行程序」改成「打开网址」时目标会跟着搬过来
                // （见 StepEditorWindow.KindCombo_Changed），而目标很可能就是 Clockwork 自己。
                // 不拦的话 ShellExecute 会再开一个自己——单实例那边会把它引到「显示主窗口」，
                // 于是一条动作组步骤变成了「莫名弹出主窗口」，而日志记的是 ✓。
                if (selfPaths != null && selfPaths.Count > 0 && LaunchTarget.IsSelfTarget(url, selfPaths))
                    return ActionResult.Warn("Warn_SelfSkip", s.Label);
                try { Start(url); return ActionResult.Empty; }
                catch (Exception ex) { return ActionResult.Warn("Warn_LaunchFail", StepHelpers.Ellipsis(url), ex.Message); }
            }
            // 打开文件 / 文件夹：从「运行程序」拆出的一档，与「打开网址」完全对称——执行上仍是
            // 同一条 ShellExecute 路（Start），拆出来的收益在模型侧：编辑器只剩一个路径框，
            // 摘要写的是路径本身。文档交给关联程序、文件夹交给资源管理器、盘符/UNC/shell: 位置也都走它。
            // 与 app 同一道口径：**不做占位替换**。在本地路径里做替换是注入面（见 url 支上那句注释）。
            case "path":
            {
                var path = LaunchTarget.NormalizeTarget(s.Target);
                if (path.Length == 0) return ActionResult.Warn("Warn_LaunchFail", "", new Strings.Ref("Sum_Unset"));
                // 自指同拦：app / url 两支都有这一道。对着 Clockwork.exe 自己建一条「打开文件」
                // 不会开第二份（单实例会把它引到弹出主窗口），但那与这一步承诺的事完全无关。
                if (selfPaths != null && selfPaths.Count > 0 && LaunchTarget.IsSelfTarget(path, selfPaths))
                    return ActionResult.Warn("Warn_SelfSkip", s.Label);
                try { Start(path); return ActionResult.Empty; }
                catch (Exception ex) { return ActionResult.Warn("Warn_LaunchFail", StepHelpers.Ellipsis(path), ex.Message); }
            }
            // 取选中的文字：机器整个在 SystemCommands（版本号判定、终端键序、注入被拒的判读），
            // 与「搜索选中的文字」共用同一份——那台机器身上攒着好几轮实测，复制一份出去必然漂。
            // 顺带写进变量：配了「输出到变量」就把取到的文字也放一份进去，后面的步骤可以按名字引用。
            // 留空则只进剪贴板（与加变量之前逐字一致），{clipboard} 那条老路照走。
            case "copySelection":
            {
                // 用它的返回值，不要再读一次剪贴板。
                //
                // CopySelectionToClipboard 已经拿 GetClipboardSequenceNumber + WaitClipboardChange
                // 确认过「剪贴板真的变了」才返回，那份文字是**已经被证实新鲜的**。
                // 丢掉它再读一遍，把序列号检查本要关掉的竞态又打开了：两次读之间只要有
                // 剪贴板管理器 / 云同步 / 密码管理器写一下，用户变量里就是别人的内容，
                // 而 run.log 里记的是一个干净的 ✓。附带好处：少一趟 STA 往返（争用时重试路径最多 5×1.5s）。
                var got = SystemCommands.CopySelectionToClipboard();
                if (!string.IsNullOrWhiteSpace(s.OutputVar) && vars != null) vars.Set(s.OutputVar, got);
                return ActionResult.Empty;
            }
            // 等剪贴板变化：给「复制了再继续」这类串联用。超时不算失败——
            // 等到了就往下走，没等到也往下走，只是如实说一声；把它做成硬失败会让整组停在这里。
            case "waitClipboard":
            {
                int secs = StepHelpers.ClampWaitSeconds(s.Level);
                return SystemCommands.WaitClipboardChange(Win32.GetClipboardSequenceNumber(), secs * 1000, cancel)
                    ? ActionResult.Empty
                    : ActionResult.Warn("Warn_WaitClipboard", secs);
            }
            // 问句步骤走不到这里就该被接走了（交互路径各自拦下：App.RunStepAsync 与 ActionGroupRunner）。
            // 落到这儿说明这条执行路径上没有可以发问的界面——开机清单就是这种：登录时弹一个
            // 拦住整条清单的输入框，是个没人想要的结果。如实说一声，别记成 ✓。
            case "prompt" or "choice": return ActionResult.Warn("Warn_AskNoUi");
            case "delay": return ActionResult.Empty;   // 纯延时：动作由步尾统一 delayMs 完成
            case "message": return ActionResult.Empty;  // 消息在启动/非交互路径静默跳过（交互「运行这一步」由 App.RunStepAsync 弹窗）；不报未知类型
            default: return ActionResult.Warn("Warn_UnknownKind", s.Kind);
        }
    }

    // 占位替换。两道闸都是为了**别做无谓的事**：
    //   没有占位符 → 原样返回，连正则都不跑（绝大多数步骤属于这一类）；
    //   有占位符但没用到 {clipboard} → 不去碰剪贴板（读它要跳一次 STA 线程，而引变量根本用不着）。
    private static string Fill(string? text, bool urlEncode, RunVars? vars = null)
    {
        if (!StepPlaceholder.Has(text)) return text ?? "";
        string clip = "";
        if (StepPlaceholder.UsesClipboard(text)) { try { clip = SystemCommands.ClipboardText(); } catch { } }
        return StepPlaceholder.Apply(text, clip, urlEncode, vars);
    }

    // 网址交给 ShellExecute：默认浏览器 / 协议处理器由系统挑，与 app 那条路同一个机制。
    private static void Start(string url)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });

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
            return ActionResult.Warn("Warn_SelfSkip", item.Label);

        // 已运行则激活窗口、不重复启动。
        if (ShouldActivateInsteadOfLaunch(item))
        {
            var pn = !string.IsNullOrEmpty(item.ActivateProcess) ? item.ActivateProcess : LaunchTarget.TargetProcessName(tgt);
            pn = StepHelpers.ToProcessName(pn);   // 统一进程名规范化（剥目录+.exe），与其余调用点一致
            if (!string.IsNullOrEmpty(pn) && WindowManager.Handles(pn).Length > 0)
            {
                // 返回值必须消费：手势 / 热键 / 定时触发都没有前台豁免，SetForeground 被前台锁
                // 降级（任务栏闪一下）时窗口根本没到前面。静默记成 ✓，用户看到的就是「按了没反应」
                // ——与窗口动作那路（本文件 window 分支的 Warn_WindowActionFailed）同一个口径。
                // {1} 与那路一样传裸操作名 "activate"。
                return WindowManager.SetForeground(pn)
                    ? ActionResult.Empty
                    : ActionResult.Warn("Warn_WindowActionFailed", StepDisplay.WindowTarget(item), "activate");
            }
        }

        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };   // 走 shell：可开 URL/URI(ms-settings:)/文档
            if (LaunchTarget.IsPowerShellScript(tgt))
            {
                // .ps1 直接用 PowerShell 跑（否则文件关联进编辑器）。
                // 两道前置检查存在的理由都一样：这两种失败下 powershell 会在解析脚本之前就退出，
                // 于是只留下一个退出码（找不到文件 -196608 / 解码失败 1），而黑窗一闪即逝、错误没人看得到。
                // 光靠下面那条「0.5 秒内退出」的告警，用户只会拿到一串数字，照着查不出任何东西。
                if (!File.Exists(tgt))
                    return ActionResult.Warn("Warn_LaunchFail", item.Label, new Strings.Ref("Err_ScriptMissing"));
                var exe = LaunchTarget.PowerShellExeFor(tgt);
                if (exe == null)
                    return ActionResult.Warn("Warn_LaunchFail", item.Label, new Strings.Ref("Err_ScriptNeedsPwsh"));
                psi.FileName = exe;
                psi.Arguments = LaunchTarget.PowerShellFileArgs(tgt, item.Args);
            }
            else
            {
                psi.FileName = tgt;
                if (!string.IsNullOrEmpty(item.Args)) psi.Arguments = item.Args;
            }

            // 工作目录：留空时默认目标所在目录（仅当目标是完整路径且该目录存在）。
            // 同样过 NormalizeTarget——它和「目标」是同一个编辑器里的两个路径框、同一个浏览按钮，
            // 粘贴来源也一样（资源管理器带引号的路径、%USERPROFILE%）。只规范化其中一个，
            // 就会出现「目标能开、工作目录悄悄没生效」这种没人会联想到编码/引号的故障。
            var workDir = LaunchTarget.NormalizeTarget(item.WorkDir);
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
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

            using var proc = Process.Start(psi);
            // Start 不抛错只代表进程被拉起。秒退且退出码非 0=多半启动失败；拿不到进程对象(ShellExecute 开文档/URL)则跳过、保持 ✓。
            if (proc != null)
            {
                try
                {
                    if (proc.WaitForExit(500) && proc.ExitCode != 0)
                        return ActionResult.Warn("Warn_QuickExit", item.Label, proc.ExitCode);
                }
                catch { }
            }
            return ActionResult.Empty;
        }
        catch (Exception ex)
        {
            return ActionResult.Warn("Warn_LaunchFail", item.Label, ex.Message);
        }
    }
}
