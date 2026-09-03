using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Clockwork.Engine;

// 系统命令派发。破坏性命令（注销/重启/关机/清空回收站）经注入的 confirmDestructive 回调门控。
public static class SystemCommands
{
    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    // 锁屏。原先是 rundll32.exe user32.dll,LockWorkStation——而 rundll32 干的就是「加载 user32、调这个导出」，
    // 中间那层进程纯属多余，更要紧的是 rundll32 是典型 LOLBin，被 AppLocker / ASR 规则 / 不少企业杀软直接拦截，
    // 那种机器上这条命令永远不生效且毫无反馈。直接调则连失败都能如实报（返回 BOOL）。
    // 注意它与 rundll32 版一样是「发起后立即返回」，锁屏本身是异步完成的——依赖它的步骤该留的延时照留。
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    /// <summary>把剪贴板里拿到的那段文字压成一句能进地址栏的查询词。</summary>
    //
    // 选中的往往是**一段**而不是一个词：换行和连续空白原样进 URL 会变成一串 %0A%20%20，
    // 搜出来的东西和你选的那句话对不上。压成单行单空格，就是你念出来的那句。
    //
    // 200 字封顶。选中一整篇文档是常有的事（Ctrl+A 之后顺手画了个手势），不截断的话
    // 拼出来的地址长到浏览器直接报错——用户看到的是一个错误页，而不是「我选多了」。
    public static string SearchQuery(string? raw)
    {
        var q = string.Join(" ", (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return q.Length > 200 ? q[..200] : q;
    }

    // 终端里 Ctrl+C 不是复制，是**中断当前命令**。
    //
    // 「获取选中的文字」全靠替用户按一次 Ctrl+C，而在 cmd / PowerShell / Windows Terminal 这类窗口上，
    // 那一下会把正在跑的东西打断——用户只是想搜个词，结果 npm install 挂了。这不是「取不到文本」，
    // 是**造成了破坏**，而且事后完全看不出是谁干的。
    // 这些终端都认 Ctrl+Insert 作复制（DOS 时代传下来的老组合），改发它即可两头都对。
    //
    // 这份名单是照着 Quicker 抄的——它的「组合动作模块选项」里就有一项
    // 「使用 Ctrl+Insert 作为复制快捷键的进程」，理由逐字相同。区别是它让用户自己维护名单，
    // 这里先写死一份常见的：要真有人用冷门终端撞上了，再把它做成设置也不迟。
    //
    // **有意不收 Code / devenv 这类内嵌终端的宿主**：它们的前台进程名是编辑器本身，
    // 而在编辑器里 Ctrl+C 就是复制。按进程名分不开「焦点在编辑区还是在终端面板」，
    // 猜错的代价是编辑器里复制不了——比在终端里少一次中断更常见。
    private static readonly string[] CtrlCInterruptsProcesses =
    {
        "cmd", "powershell", "pwsh", "conhost", "openconsole", "windowsterminal",
        "mintty", "conemu", "conemu64", "wezterm-gui", "alacritty", "putty", "kitty",
    };

    /// <summary>在这个进程的窗口上，「复制」该按哪几下——**按顺序试，第一下成了就不试第二下**。</summary>
    //
    // 要两个而不是一个：「复制」在不同宿主上绑着不同的键，押中一个才有结果、押不中就只能报
    // 「没有选中文字」——而那句话是错的，选中的东西一直在那儿。实测 Windows Terminal 的
    // 默认表把 copy 同时绑在 ctrl+shift+c / ctrl+insert / enter 上，传统控制台只认 ctrl+insert，
    // 普通程序只认 ctrl+c。
    //
    // **顺序按伤害排，不按概率排**：终端那一档先发两个绝对安全的复制键，
    // ctrl+c 排在**最后**——在传统控制台里它是中断，会把正在跑的命令打断。
    //
    // 但它必须在列表里，这是实测逼出来的：Windows Terminal 的**默认表里 ctrl+c 压根没绑 copy**
    // （只有 ctrl+shift+c / ctrl+insert / enter），所以很多人自己把 ctrl+c 改成了复制——
    // 而那种配置下，用户的 keybindings 会盖掉默认那几个，前两个键一个都不生效。
    // 实测就撞上了这一种：两个安全键都发了，剪贴板纹丝不动，而用户说「我这用的是 ctrl+c」。
    //
    // 风险与收益的账：前两个键在**默认配置**下必定命中，所以 ctrl+c 根本轮不到；
    // 只有「两个安全键都不认」的机器才会走到它，而那种机器上它多半正是被绑成复制的那一个。
    // 代价是「终端里什么都没选中还画了这条手势」时会中断一次——那一下本来也取不到文字，
    // 用户已经做错了一件事；而在此之前，这个功能对那类配置是**完全不可用**的。
    public static string[] CopyKeysFor(string? foregroundProcess)
    {
        var name = StepHelpers.ToProcessName(foregroundProcess ?? "");
        foreach (var p in CtrlCInterruptsProcesses)
            if (string.Equals(name, p, StringComparison.OrdinalIgnoreCase))
                return new[] { "Ctrl+Insert", "Ctrl+Shift+C", "Ctrl+C" };
        return new[] { "Ctrl+C", "Ctrl+Insert" };
    }

    /// <summary>先试哪一下。（CopyKeysFor 的第一个）</summary>
    public static string CopyComboFor(string? foregroundProcess) => CopyKeysFor(foregroundProcess)[0];

    /// <summary>替用户按一次「复制」，把选中的文字取回来。取不到就抛（消息里带着诊断线索）。</summary>
    //
    // 「选中的文字」没有 API 可问，全世界的做法都一样：模拟一次复制、从剪贴板取。
    // 代价是剪贴板被覆盖——不做保存/还原是有意的：剪贴板有多种格式（图片、文件列表、富文本），
    // 只还原纯文本等于悄悄把用户复制的图片换成一串字，比覆盖更坏。
    //
    // 靠**剪贴板版本号**判断这次复制成没成，而不是先清空再看有没有东西。
    // 清空那种做法有个要命的失败态：没选中东西时复制是空操作，于是清空白清了——
    // 用户刚复制的一张图、一批文件、一段准备粘贴的话，就这么没了，还找不回来。
    // 版本号只读不写：没变 = 什么都没复制到，原来的剪贴板一根毫毛没动。
    // 它同时解决了另一半问题——不看版本号就直接读，读到的会是上一次复制的内容，
    // 手势会安安静静地去搜一小时前的那段话。
    //
    // **一处机器，三处用**：搜索选中的文字、「获取选中的文字」步骤、以及将来任何要拿选区的地方。
    // 这台机器身上攒着好几轮实测（终端键序、注入被拒的判读、等待时长），复制一份出去必然漂。
    public static string CopySelectionToClipboard()
    {
        uint seq = Win32.GetClipboardSequenceNumber();
        // 进程名与键名记下来是为了**报错时能说清**：我们唯一知道的事实是「剪贴板没变」，
        // 而那至少有四种成因——没选中 / 那个程序不响应这个复制键 / 它压根不让复制 /
        // 焦点不在你以为的那个窗口上。光说「没有选中文字」是在四选一里押一个，
        // 押错时用户会一直去检查一件本来就没问题的事。
        var proc = Win32.ForegroundProcessName();
        var keys = CopyKeysFor(proc);
        bool copied = false;
        for (int k = 0; k < keys.Length && !copied; k++)
        {
            // **这一下的结果不能扔。** SendKeyCombo 在 SendInput 被系统拒收时返回
            // Warn_KeyRejected（「前台是提权窗口/安全桌面时会这样」）——那句话就是答案本身：
            // 目标窗口以管理员身份运行而本进程没有，UIPI 会把注入的按键整个丢掉。
            // 扔掉它再去报「没有选中文字」，等于把「我按不到那个窗口」说成「你没选中东西」。
            var sent = KeyInput.SendKeyCombo(keys[k]);
            if (sent.Warning is { Length: > 0 } w) throw new InvalidOperationException(w);
            // 轮询而不是睡死一个数：快的程序 30ms 就放好了，慢的几百毫秒才到。
            // 第一次给足 1 秒（Quicker 的故障排除文档专门列了「复杂网页 / PDF 阅读器响应慢」
            // 这一条，并为此把等待做成可调参数）；退一步那次只给 0.5 秒——走到那儿时
            // 第一个键已经明确没用，再等满一秒只是让报错来得更晚。
            copied = WaitClipboardChange(seq, k == 0 ? 1000 : 500);
        }
        if (!copied) throw new InvalidOperationException(NoSelection(proc, string.Join(" / ", keys)));
        return ClipboardText();
    }

    /// <summary>剪贴板里此刻的文字（拿不到就是空串）。</summary>
    //
    // 单独抽出来是因为「要跳 STA 线程」这件事必须只有一处知道：WinForms 的 Clipboard 只能在
    // STA 上调，而所有执行路径（开机序列 / 单步 / 动作组）都跑在 MTA 线程池上，直接调必抛。
    // 【曾经它只有一行 `return ClipboardText();`】——抽这个方法出来的那一轮忘了写函数体，
    // 于是每一个用到 {clipboard} 的步骤都会栈溢出。栈溢出是**抓不住的**：进程当场没，
    // 错误日志里一个字都不会留，用户看到的只是「点了没反应，程序不见了」。
    // 单元测试也罩不到它：StepPlaceholder.Apply 是把剪贴板内容**当参数收**的（那正是为了可测），
    // 于是让它可测的那道缝，恰好也把这个函数挡在了测试之外。现在由 --selftest 的端到端那条盯着。
    public static string ClipboardText()
    {
        // 取不到就是空串（调用方一律按「剪贴板里没有文字」处理）；打不开则由 ClipboardRetry 重试。
        string got = "";
        try
        {
            ClipboardRetry(() =>
            {
                got = WinForms.Clipboard.GetText();
                // **空串有两种来历，必须分开。** GetText 在别的程序正写着剪贴板时会返回空串
                // 而不抛异常——那时上面那道重试（只认异常）根本不会触发，空串被当成正常结果收下，
                // 于是 {clipboard} 偶尔换成空、「搜索选中的文字」偶尔搜了个空：不报错，只是偶尔不对。
                // ContainsText 说「里面确实有文字」时，这个空串就是没读着，重试一次；
                // 它说没有文字时，空串是对的，直接收下（否则每次剪贴板真空都要白等 4 轮）。
                if (got.Length == 0 && WinForms.Clipboard.ContainsText())
                    throw new InvalidOperationException("clipboard read came back empty");
            });
        }
        catch { }
        return got;
    }

    /// <summary>等剪贴板版本号变掉，最多等 <paramref name="timeoutMs"/> 毫秒。变了返回 true。</summary>
    //
    // 20ms 一跳而不是睡死一个数：快的程序 30ms 就放好了，等满上限只会让每一次都变慢。
    //
    // **每一跳都要问一次「还让我等吗」。** ActionGroupRunner 只在**步骤之间**采样 deps.Cancel，
    // 所以这个循环自己不查的话，急停键、托盘「停止」、组编辑器那颗停止按钮，都得等它睡满
    // 才轮得到——最长 60 秒里整组卡着不动，而用户已经按过停止了。
    // 隔壁送文字那一支一直是把 cancel 传到底的（`WindowManager.SendText(..., cancel)`），
    // 这一支借了同一个字段位、却漏了同一件事。
    public static bool WaitClipboardChange(uint since, int timeoutMs, RunCancel? cancel = null)
    {
        for (int i = 0; i * 20 < timeoutMs && Win32.GetClipboardSequenceNumber() == since; i++)
        {
            if (RunCancel.Stopped(cancel)) break;   // 停了就别再等：返回值按「没等到」走，如实报一句
            Thread.Sleep(20);
        }
        return Win32.GetClipboardSequenceNumber() != since;
    }

    // 「取不到选中的文字」——把已知的事实附在后面，别让这句话独自去猜成因。
    // 括号里两样都是标识符（进程名、键名），不进文案表：翻译它们没有意义，
    // 而它们恰恰是唯一能一眼定位的东西——比如看到「Code · Ctrl+C」就知道是内嵌终端那一档，
    // 看到「WindowsTerminal · Ctrl+Insert」就知道该去查那个终端认不认这个复制键。
    private static string NoSelection(string? proc, string copyKey)
        => Strings.Get("Err_NoSelection")
           + (string.IsNullOrWhiteSpace(proc) ? $"（{copyKey}）" : $"（{proc} · {copyKey}）");

    private static void Start(string file, string? args = null, bool useShell = false)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = useShell };
        if (args != null) psi.Arguments = args;
        Process.Start(psi);
    }

    // text/level：只有少数命令带参数（setClipboard 用 text、brightness 用 level），
    // 从 LaunchStep 的既有字段直接借过来，不为两条命令给模型再加两个字段。
    /// <summary>这条命令要不要先问一句。**与下面 switch 里带 confirmDestructive 的那几支必须同步。**</summary>
    //
    // 摆在紧邻 Invoke 的地方而不是另起一个文件：它存在的唯一意义是让调用方能在**派发之前**
    // 知道「这一步会弹确认」，而那个判断和 switch 里的实际门控必须是同一份名单。
    // 加新的破坏性命令时两处一起改（StepRunner 那条守卫会因此对新命令自动生效）。
    public static bool IsDestructive(string? command)
        => command is "signOut" or "restart" or "shutdown" or "emptyRecycleBin";

    /// <summary>没有人可问时就**不能跑**的那几条（比 IsDestructive 窄）。</summary>
    ///
    /// 两个判据答的是两个不同的问题，当时合成一个是错的：
    ///   IsDestructive   = 「有人在时该不该先问一句」——四条都该。
    ///   RequiresHuman   = 「没人在时能不能照跑」——只有把会话掲掉的那三条不行。
    ///
    /// 关机 / 重启 / 注销在开机清单里跑到一半把会话掲掉，不可能是那一刻想要的。
    /// 而清空回收站不属于这一类：它是用户主动配进清单的一件日常自动化，
    /// **没人看着恰恰是它该照跑的时候**。把它也归进「没人就不跑」曾经造成一次回归：
    /// 2.7.1 上它在登录时照跑（那个 case 压根不查回调），改完之后变成永不跑，
    /// 没有迁移、没有告知，只在 run.log 里留一个 ⚠——用户看到的是「升级后它不干活了」。
    /// 那道确认防的是面板上误点一下，不是防用户自己安排的自动化。
    public static bool RequiresHuman(string? command)
        => command is "signOut" or "restart" or "shutdown";

    public static void Invoke(string command, Func<string, bool> confirmDestructive, string text = "", int level = 0)
    {
        switch (command)
        {
            case "showDesktop":
            {
                // 原生 Shell COM（等价 Win+D，不注入按键、结果可信）；COM 不可用或失败都退回模拟按键。
                // ShellApp() 在 ProgID 未注册时返回 null——?. 会短路成「什么都不做」且不抛，故不能只靠 catch 兜底。
                bool toggled = false;
                try { var sh = ShellApp(); if (sh != null) { sh.ToggleDesktop(); toggled = true; } } catch { }
                if (!toggled) KeyInput.SendKeyCombo("Win+D");
                break;
            }
            case "lockScreen":
                if (!LockWorkStation()) throw Fail("lockScreen", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
                break;
            case "taskManager": Start("taskmgr.exe"); break;
            // 播放声音。留空 = 系统的「星号」提示音（跟随用户的声音方案，不自带音频文件，
            // 与提醒那边同一个来源，见 Reminder.Sound 上的说明）；填了路径就放那个 .wav。
            //
            // 只认 .wav：SoundPlayer 就只会放 wav，喂 mp3 会抛「不是有效的波形文件」——
            // 与其把那句系统异常原样抛给用户，不如在这里说清楚它要的是什么。
            // 想放 mp3 的用「运行程序」指向那个文件，系统的默认播放器会接手。
            //
            case "playSound":
            {
                var wav = (text ?? "").Trim().Trim('"');
                if (wav.Length == 0) { System.Media.SystemSounds.Asterisk.Play(); break; }
                if (!wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Strings.Lf("Err_SoundNotWav", StepHelpers.Ellipsis(wav)));
                if (!System.IO.File.Exists(wav))
                    throw new InvalidOperationException(Strings.Lf("Err_SoundMissing", StepHelpers.Ellipsis(wav)));
                // SND_ASYNC：一段几秒的提示音不该把整条动作卡在这里，后面还有步骤要跑。
                // 要「等它放完再往下」用一条延时步骤。
                if (!Win32.PlaySound(wav, IntPtr.Zero, Win32.SND_FILENAME | Win32.SND_ASYNC | Win32.SND_NODEFAULT))
                    throw new InvalidOperationException(Strings.Lf("Err_SoundNotWav", StepHelpers.Ellipsis(wav)));
                break;
            }
            case "searchSelection":
            {
                var q = SearchQuery(CopySelectionToClipboard());
                // 剪贴板确实变了，只是取出来不是文字（选中的是图 / 文件）。开一个空搜索页
                // 会让人以为「手势没生效」，而真正的原因在这儿。
                if (q.Length == 0) throw new InvalidOperationException(NoSelection(Win32.ForegroundProcessName(), "—"));
                var tpl = string.IsNullOrWhiteSpace(text) ? StepDisplay.DefaultSearchUrl : text.Trim();
                Start(tpl.Replace("{0}", Uri.EscapeDataString(q)), useShell: true);
                break;
            }
            case "clearClipboard":
                // WinForms Clipboard 要求 STA 线程，而所有执行路径（开机序列/单步/动作组）都在 MTA 线程池上——
                // 直接调必抛 ThreadStateException，故挪到专用 STA 线程同步执行。
                try { ClipboardRetry(() => WinForms.Clipboard.Clear()); }
                // 退路只试一次，理由见 ClipboardRetry 的说明——两条 5 轮阶梯叠起来会让「清一下剪贴板」
                // 最坏占掉十几秒，而那期间整组是停着的。
                catch { try { ClipboardRetry(() => WinForms.Clipboard.SetText(" "), attempts: 1); } catch (Exception ex) { throw new InvalidOperationException(Strings.Lf("Err_ClearClipboard", ex.Message)); } }
                break;
            case "monitorOff":
                // HWND_BROADCAST(0xFFFF) WM_SYSCOMMAND(0x0112) SC_MONITORPOWER(0xF170) 2=关。
                Win32.PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2);
                break;
            // 与下面的 sleep 用同一个 API，只差 PowerState——原先这条起 shutdown.exe /h，
            // 在禁用了休眠的机器上错误只打进一个看不见的控制台，这一步照记 ✓（与 sleep 曾经的坑同源）。
            // 休眠没有 rundll32 兜底：那条命令固定的参数组合在休眠可用时才休眠，而这里失败恰恰意味着休眠不可用，
            // 退过去只会变成「睡眠」——把用户要的「存盘断电」悄悄换成另一件事，不如如实报错。
            case "hibernate":
                if (!SetSuspend(WinForms.PowerState.Hibernate)) throw Fail("hibernate", Strings.Get("Err_PowerStateUnavailable"));
                break;
            case "signOut": if (confirmDestructive(Strings.Get("Sys_signOut"))) Start("shutdown.exe", "/l"); break;
            case "restart": if (confirmDestructive(Strings.Get("Sys_restart"))) Start("shutdown.exe", "/r /t 0"); break;
            case "shutdown": if (confirmDestructive(Strings.Get("Sys_shutdown"))) Start("shutdown.exe", "/s /t 0"); break;
            case "emptyRecycleBin":
                // 先数条目：查询成功且为空→静默跳过（本就无事，避免「已空」误报）；非空→清。
                // 但查询失败（某些盘符/权限下返回非零 HRESULT）时不能假装成功——照旧尝试清（对空桶清也是无害 no-op）。
                //
                // **要确认**，与注销/重启/关机同一个回调、同一套措辞。那三条只是打断你，
                // 这一条是把文件真的删掉，而且传的是 SHERB_NOCONFIRMATION——系统自己那道确认也被关了，
                // 于是不接这一道就完全没有回头路。它还是出厂面板上的一格，误点一下代价不对等。
                //
                // 确认排在**查过、确实有东西**之后：桶本来就空的时候这一路本就静默跳过，
                // 那时弹一句「确定清空吗」问的是一件不会发生的事。这样这道确认恰好只在真会丢东西时出现。
                try
                {
                    var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                    bool queriedEmpty = SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems <= 0;
                    if (!queriedEmpty && confirmDestructive(Strings.Get("Sys_emptyRecycleBin")))
                        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                }
                catch (Exception ex) { throw new InvalidOperationException(Strings.Lf("Err_EmptyRecycleBin", ex.Message)); }
                break;
            case "openSettings": Start("ms-settings:", useShell: true); break;
            case "screenshot":
                // 原生截图协议（Win10 1809+/Win11），不注入按键；协议缺失才退回 Win+Shift+S。
                try { Start("ms-screenclip:", useShell: true); } catch { KeyInput.SendKeyCombo("Win+Shift+S"); }
                break;
            // 失败才退回 rundll32：它无法传类型化参数，在开启休眠的机器上会误休眠，所以只当兜底、不当主路。
            case "sleep":
                if (!SetSuspend(WinForms.PowerState.Suspend)) Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                break;
            // 显示器模式：系统自带的 DisplaySwitch.exe，和按 Win+P 选一项完全等价，不注入按键。
            case "displayInternal": Start("DisplaySwitch.exe", "/internal"); break;
            case "displayClone": Start("DisplaySwitch.exe", "/clone"); break;
            case "displayExtend": Start("DisplaySwitch.exe", "/extend"); break;
            case "displayExternal": Start("DisplaySwitch.exe", "/external"); break;
            case "setClipboard": SetClipboard(text ?? ""); break;
            case "notificationsOff": SetNotifications(false); break;
            case "notificationsOn": SetNotifications(true); break;
            case "brightness": SetBrightness(level); break;
            default: throw new InvalidOperationException(Strings.Lf("Err_UnknownSysCmd", command));
        }
    }

    // 往剪贴板放一段固定文本（常用地址 / 话术 / 模板）。空文本=清空，与 clearClipboard 同效——
    // 把空串写进剪贴板会留下一个「空但存在」的项，不如直接清掉。
    // Clipboard 与 clearClipboard 一样要 STA 线程（所有执行路径都在线程池的 MTA 上），走同一个 RunSta。
    private static void SetClipboard(string text)
    {
        try
        {
            ClipboardRetry(() =>
            {
                if (text.Length == 0) { WinForms.Clipboard.Clear(); return; }
                WinForms.Clipboard.SetText(text);
                // **复核写进去了没有。** 不抛不等于落定：别的程序（剪贴板历史 / 云同步 / 密码管理器）
                // 可能正好在这一瞬间抢走剪贴板，于是这一步「成功」了而内容根本不在——
                // 下一步的 {clipboard} 取到空串，不报错，只是偶尔不对，最难查的那一种。
                // 读回一次即可判定；不一致就抛出去交给 ClipboardRetry 再来一遍。
                // 只复核非空那一档：清空的语义是「别留着我的东西」，被别人抢先写入不算失败。
                if (WinForms.Clipboard.GetText() != text)
                    throw new InvalidOperationException("clipboard write did not stick");
            });
        }
        catch (Exception ex) { throw Fail("setClipboard", ex.Message); }
    }

    // 通知总开关。注册表有两处，两处都写：
    //   PushNotifications\ToastEnabled                      —— 设置里「从应用和其他发送者获取通知」，决定 toast 弹不弹；
    //   Notifications\Settings\NOC_GLOBAL_SETTING_TOASTS_ENABLED —— 通知中心那一层的全局开关。
    // 只写其一在部分 Windows 版本上不生效（而且哪一处生效随版本变），两处都写才稳；两处都是当前用户的键，
    // 不需要管理员。立即对之后弹出的通知生效，已在屏上的那条不会被收回。
    //
    // 这不是 Win11 的「专注 / 勿扰」——那套没有公开可写的开关（WNF 状态，未文档化）。此处关的是通知本身，
    // 效果更彻底：勿扰只是攒起来，这个是根本不弹。别忘了配一条 notificationsOn 收尾，否则会一直静着。
    private static void SetNotifications(bool on)
    {
        int v = on ? 1 : 0;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
                k?.SetValue("ToastEnabled", v, RegistryValueKind.DWord);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                k?.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", v, RegistryValueKind.DWord);
        }
        catch (Exception ex) { throw Fail(on ? "notificationsOn" : "notificationsOff", ex.Message); }
    }

    // 屏幕亮度。唯一的路是 WMI 的 WmiMonitorBrightnessMethods，而 System.Management 在 .NET 上是个要单独装的包——
    // 为一个滑杆让自包含发布多背一个依赖不划算，改由系统自带的 powershell.exe 现调。
    // ponytail: 起一次 powershell ≈ 0.5–1 秒；真嫌慢再换 System.Management——
    //           但先知道价码：实测那个包让单文件 exe 涨约 832KB（+40%），且是全仓唯一的外部依赖
    //           （同一笔账在 Native/ProcessInfo.cs 里算过一次，那边最终没买）。
    // 必须走 Invoke-CimMethod：第一版写的是 (Get-CimInstance ...).WmiSetBrightness(1,p)，
    // 而 CimInstance 根本不携带 WMI 方法，那一版在所有机器上（含支持的笔记本）都必然失败（评审 #1 实跑证实）。
    // 两段都挂 -ErrorAction Stop：Get-CimInstance 的「不支持」是非终止错误，不升级的话
    // 管道空转、退出码 0——台式机上会把「什么都没调」谎报成功。
    // 只对「由系统驱动的显示器」有效：笔记本内屏、部分一体机。外接显示器走 DDC/CI，这条路够不着——
    // 那种机器上会如实报错，不假装成功。
    private static void SetBrightness(int percent)
    {
        int p = Math.Clamp(percent, 0, 100);
        try
        {
            string cmd = "$i = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop; " +
                         "$i | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{Timeout=1;Brightness=" + p + "} -ErrorAction Stop | Out-Null";
            var psi = new ProcessStartInfo
            {
                FileName = LaunchTarget.PowerShellExe,
                Arguments = "-NoProfile -NonInteractive -Command \"" + cmd + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("powershell");
            // 等一小会儿只为拿到退出码：不支持的显示器上 WMI 类是空的，PowerShell 会以非 0 退出，
            // 那正是要如实报给用户的那条。超时不算失败（慢机器上别把成功报成失败），进程留着自己跑完。
            if (proc.WaitForExit(4000) && proc.ExitCode != 0) throw new InvalidOperationException("exit " + proc.ExitCode);
        }
        catch (Exception ex) { throw Fail("brightness", ex.Message); }
    }

    // 命令失败的统一措辞：命令名用本地化标签（用户在下拉里看到的那个），后面接系统给的原文。
    // 睡眠/休眠共用。必须接住返回值：待机或休眠被驱动/组策略禁用时（powercfg /a 会显示不可用），
    // SetSuspendState 是**返回 false 而不抛异常**的。原先只 try/catch，等于假定失败会抛——
    // 于是回退分支永远不跑、电脑整夜醒着，而这一步照记 ✓。
    // 成功时本调用要到机器唤醒之后才返回，那时返回的是 true，不会重复触发兜底。
    private static bool SetSuspend(WinForms.PowerState state)
    {
        try { return WinForms.Application.SetSuspendState(state, false, false); }
        catch { return false; }
    }

    private static InvalidOperationException Fail(string command, string detail)
        => new(Strings.Lf("Err_SysCommand", Strings.Get("Sys_" + command), detail));

    private static dynamic? ShellApp()
    {
        var t = Type.GetTypeFromProgID("Shell.Application");
        return t == null ? null : Activator.CreateInstance(t);
    }

    // 在专用 STA 线程上同步跑 action（剪贴板等 OLE 依赖 STA）；异常原栈重抛给调用方。
    // 剪贴板的读 / 写 / 清空共用这一条：**打不开就短暂重试**。
    //
    // 剪贴板是全系统共享的，别的程序开着它的那一瞬间谁都打不开——这是常态，不是故障。
    // 实测：紧跟在一轮开满窗口之后写剪贴板，必现一次「所请求的剪贴板操作失败」。
    // 不重试的那一处就会偶尔失败：写会弹一句莫名其妙的报错，读会静默地退回空串
    //（于是「搜索选中的文字」偶尔搜了个空、{clipboard} 偶尔换成空）。三处都走这里，规则只有一条。
    //
    // 开得了的时候第一次就开了，所以正常路径一次都不会多睡；最坏也只多 4×20ms。
    //
    // attempts 可调是为了**别让两条阶梯叠起来**：clearClipboard 在清空失败后还会退一步去写一个空格，
    // 两次都跑满 5 轮的话，那一步最坏要占掉 2×5×(RunSta 上限 + 20ms) ≈ 15 秒，
    // 而它在用户眼里只是「清一下剪贴板」。退路只试一次：走到那儿时主路已经明确失败了，
    // 再把同一件事重试五遍只是让报错来得更晚。
    private static void ClipboardRetry(Action action, int attempts = 5)
    {
        for (int i = 0; ; i++)
        {
            try { RunSta(action); return; }
            catch when (i < attempts - 1) { Thread.Sleep(20); }
        }
    }

    // 这里跑的全是剪贴板操作，而剪贴板是 Windows 上最容易被别的进程挂住的东西之一：
    // OpenClipboard 拿不到锁时会一直等（剪贴板历史 / 云同步 / 密码管理器正持着它）。
    // 于是**必须给 Join 一个上限**：不给的话这条线程挂住 = 整条启动清单或动作组永远停在这一步，
    // 而急停键只在步骤之间被检查，解不开它。
    //
    // 1.5 秒：剪贴板正常是微秒级，等到 1.5 秒基本可以断定对方不会放手了。
    // 外层 ClipboardRetry 会重试 5 次，所以最坏情况约 7.5 秒——有界，且步骤会如实报错，
    // 而不是无声地停在那里。超时的那条线程是 IsBackground，随进程退出，不必也不该去 Abort 它。
    private const int StaJoinMs = 1500;

    private static void RunSta(Action action)
    {
        Exception? err = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        // 内部诊断串，与本文件里「clipboard write did not stick」同一档：会经 Mark_Exception 露给用户，
        // 但它描述的是一个机制层面的故障，翻成 18 种语言没有意义。
        if (!t.Join(StaJoinMs)) throw new TimeoutException("clipboard is held by another process");
        if (err != null) ExceptionDispatchInfo.Capture(err).Throw();
    }
}
