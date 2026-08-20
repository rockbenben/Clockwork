using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.Engine;

// 提醒/消息步骤的副作用：语音播报 + 「点是后」动作。
public static class ReminderActions
{
    // 语音播报走一个专属 STA 后台线程：SpVoice 在该线程创建并只在该线程使用，避免"一处线程建、
    // 另一处线程用"的跨单元 COM 调用(旧实现把静态 SpVoice 在 UI 线程建、又从动作组后台线程调，
    // 会随机变慢或失败)。Speak 只入队立即返回；worker 逐条同步播报，天然串行不叠音。
    private static readonly BlockingCollection<(string Text, int LeadInMs)> _speakQueue = new();
    private static Thread? _speakThread;
    private static readonly object _speakLock = new();
    private static volatile bool _speakUnavailable;   // SAPI 建不出来：停用，后续 Speak 直接丢弃不入队

    // 提示音的前导时长。系统「星号」音约半秒，取 500ms 让它基本落完再开口。
    public const int SoundLeadInMs = 500;

    // 到点提示音。用系统「星号（信息）」音而不是自带 wav：不占体积、跟随用户的系统声音方案，
    // 静音方案下自然不响（那正是用户的表态）。Play() 是异步的，不会拖住调用它的 UI 线程。
    // 失败仅吞：没有声卡 / 远程桌面会话下响不出来，不该因此把提醒本身搅黄。
    public static void Ding()
    {
        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }

    // leadInMs：开口前先静候这么久。给「提示音 + 朗读」都开的提醒用——两者若同时开始，
    // 「叮」会盖在第一个字上，而提示音存在的意义正是先让人抬头、随后的朗读才有人在听。
    // SystemSounds.Play() 是异步的、没有播完回调，所以只能按经验留一段前导（提示音本身约半秒）。
    // 等待放在这条专属播报线程上：它本来就是串行读稿的，睡在这儿谁也挡不着；放到 UI 线程上
    // 睡半秒则会卡住提醒 tick，紧随其后的模态弹窗也跟着晚开。
    // 只在真放了提示音时才传非 0——只朗读不出声的提醒不该白等这半秒。
    public static void Speak(string text, int leadInMs = 0)
    {
        if (string.IsNullOrEmpty(text) || _speakUnavailable) return;
        EnsureSpeakWorker();
        try { _speakQueue.Add((text, leadInMs)); } catch { }
    }

    private static void EnsureSpeakWorker()
    {
        if (_speakThread != null) return;
        lock (_speakLock)
        {
            if (_speakThread != null) return;
            var th = new Thread(SpeakLoop) { IsBackground = true, Name = "Clockwork.Speech" };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            _speakThread = th;
        }
    }

    private static void SpeakLoop()
    {
        dynamic? voice;
        try
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice");
            voice = t == null ? null : Activator.CreateInstance(t);
        }
        catch { voice = null; }
        if (voice == null)
        {
            // SAPI 不可用：停用 + 封口队列(此后 Add 抛→被 Speak 的 try 吞掉，杜绝与本清空竞态残留一条)，再排空已入队的。
            _speakUnavailable = true;
            _speakQueue.CompleteAdding();
            while (_speakQueue.TryTake(out _)) { }
            return;
        }
        foreach (var (text, leadInMs) in _speakQueue.GetConsumingEnumerable())
        {
            if (leadInMs > 0) Thread.Sleep(leadInMs);   // 给前面那声提示音让出时间，见 Speak 的注释
            try { voice.Speak(text, 0); }   // 0 = SVSFDefault：同步，本线程逐条读完再取下一条
            catch { }
        }
    }

    // 「点是后」：运行程序/打开文件（run，兼容旧 sound）、开网页（url）、运行动作组（group）。
    // 失败经 warn 报一条托盘警示，不弹崩溃框——点了「是」却什么都没发生是最糟的反馈。
    // warn：组引用悬空（被删/被禁用）时回调一条已本地化的提示——用户点了「是」却什么都没发生，不该零反馈。
    public static void RunOnYes(OnYes? onYes, IReadOnlyList<ActionGroup> groups, Action<ActionGroup> runGroup, Action<string>? warn = null)
    {
        if (onYes == null) return;
        try
        {
            var type = onYes.Type == "sound" ? "run" : onYes.Type;
            switch (type)
            {
                case "run":
                    // 规范化与解释器选择都走 LaunchTarget 那一份：同一个路径在「启动程序」步骤和
                    // 提醒的「点是后」必须表现一致，两处各写一版迟早只修好一处。
                    // pwsh 缺席时退回 powershell.exe 照常尝试；真起不来由方法末尾的 catch 报进 warn。
                    var run = LaunchTarget.NormalizeTarget(onYes.Target);
                    if (LaunchTarget.IsPowerShellScript(run))
                        Process.Start(new ProcessStartInfo { FileName = LaunchTarget.PowerShellExeFor(run) ?? LaunchTarget.PowerShellExe, Arguments = LaunchTarget.PowerShellFileArgs(run), UseShellExecute = true });
                    else
                        Process.Start(new ProcessStartInfo { FileName = run, UseShellExecute = true });
                    break;
                case "url":
                    // 和上面的 run 分支读的是同一个 Target 字段、同一个输入框，规范化口径也得一样，
                    // 否则「粘进来末尾带个空格」在两个分支里表现不同。
                    Process.Start(new ProcessStartInfo { FileName = LaunchTarget.NormalizeTarget(onYes.Target), UseShellExecute = true });
                    break;
                case "group":
                    if (string.IsNullOrWhiteSpace(onYes.Target)) break;   // 从未选过组（下拉留在「（无）」）：不算悬空引用，不误报「组被删」
                    var g = ActionGroupResolver.Resolve(groups, onYes.Target);
                    if (g != null && g.Enabled) runGroup(g);
                    else warn?.Invoke(Strings.Get(g == null ? "Warn_OnYesGroupMissing" : "Warn_OnYesGroupDisabled"));
                    break;
            }
        }
        catch (Exception ex)
        {
            // 报出去，不吞。这是提醒里最「用户刚刚亲手点过」的一步——点了「是」却什么都没发生
            // （目标被移走、路径改了、脚本删了），无声就是最糟的反馈：用户会以为自己没点上。
            // 组缺失/禁用两条路本来就在用这个 warn 通道，异常没理由是唯一的例外。
            // 仍然不弹崩溃框（那正是原本吞异常想避免的），只出一条托盘警示。
            warn?.Invoke(Strings.Lf("Warn_OnYesFailed", ex.Message));
        }
    }
}
