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
    private static readonly BlockingCollection<string> _speakQueue = new();
    private static Thread? _speakThread;
    private static readonly object _speakLock = new();
    private static volatile bool _speakUnavailable;   // SAPI 建不出来：停用，后续 Speak 直接丢弃不入队

    // 到点提示音。用系统「星号（信息）」音而不是自带 wav：不占体积、跟随用户的系统声音方案，
    // 静音方案下自然不响（那正是用户的表态）。Play() 是异步的，不会拖住调用它的 UI 线程。
    // 失败仅吞：没有声卡 / 远程桌面会话下响不出来，不该因此把提醒本身搅黄。
    public static void Ding()
    {
        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }

    public static void Speak(string text)
    {
        if (string.IsNullOrEmpty(text) || _speakUnavailable) return;
        EnsureSpeakWorker();
        try { _speakQueue.Add(text); } catch { }
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
        foreach (var text in _speakQueue.GetConsumingEnumerable())
        {
            try { voice.Speak(text, 0); }   // 0 = SVSFDefault：同步，本线程逐条读完再取下一条
            catch { }
        }
    }

    // 「点是后」：运行程序/打开文件（run，兼容旧 sound）、开网页（url）、运行动作组（group）。失败仅吞（不弹崩溃框）。
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
                    // 这里没有告警通道（整个方法吞异常），pwsh 缺席时退回 powershell.exe 照常尝试。
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
        catch { }
    }
}
