using System.Diagnostics;
using System.IO;
using System.Threading;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.Services;

// 错误日志、toast 卡片与「最近通知」留痕：全应用的「记账 + 说给人听」统一出口。
// 自 App 拆出（纯结构重构，线程模型一字未改）：入队侧只做内存操作，文件 I/O 全在
// Clockwork.ErrorLog 那条后台写盘线程上——这条纪律由 OffUiThreadTests 按源码文本钉住，
// 改这段之前先读那边的注释。配置目录由宿主以回调传入：_config 的归属仍在 App。
internal sealed class AppDiagnostics
{
    private readonly Func<string> _cfgDir;
    public AppDiagnostics(Func<string> cfgDir) => _cfgDir = cfgDir;

    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    // 品牌化非模态通知（右下角 toast，替代系统托盘气泡）。自动切到 UI 线程；整体兜底绝不抛。
    // 后台线程(动作组/单步)调用时 Dispatcher.Invoke 遇正在关闭的调度器会抛(TaskCanceled/InvalidOperation)，
    // 必须一并吞掉——否则会从 OnStepError 逃出、掀掉动作组剩余步骤(收工/睡前组的锁屏/关机就不执行了)。
    // 分级默认时长：运行回执看过就算；警示是「你需要知道」的（配置写盘失败、热键被占、动作组步骤异常），
    // 用同一个 5 秒等于错过就没了。durationMs<0=按级别取默认，0=常驻到点击，>0=显式毫秒。
    private const int InfoToastMs = 5000;
    private const int WarnToastMs = 12000;

    // log=false：不写「最近通知」（预览等试看场景——留痕会让托盘历史出现和真投递无法区分的幻影条目，
    // 反复预览还会把 8 格环形缓冲里的真条目全部挤掉）。
    public void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                          int durationMs = -1, string? key = null, bool log = true)
    {
        int dur = durationMs >= 0 ? durationMs : (level == Views.ToastLevel.Warn ? WarnToastMs : InfoToastMs);
        // 留痕与弹卡片一起做（都在 UI 线程）：_notifications 不是线程安全的，后台线程调本方法时不能就地写。
        void Post()
        {
            if (log) _notifications.Add(new NotificationEntry(DateTime.Now, title, message, level == Views.ToastLevel.Warn, key, dur));
            Views.NotificationToast.Show(title, message, level, dur, key);
        }
        try
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp == null) return;
            if (disp.CheckAccess()) Post();
            else disp.Invoke(Post);
        }
        catch { }
    }

    // 警告气泡的便捷入口（RunOnYes 等回调用）。ShowToast 自身已全 try/catch 守护、可跨线程调。
    public void WarnToast(string msg) => ShowToast("Clockwork", msg, Views.ToastLevel.Warn);

    // 托盘「查看上次启动日志」：按系统关联打开 clockwork.run.log；还没跑过启动清单则提示。
    public void OpenRunLog()
    {
        var path = Path.Combine(_cfgDir(), "clockwork.run.log");
        if (!File.Exists(path)) { ShowToast("Clockwork", Strings.Get("Tray_NoLog"), Views.ToastLevel.Info); return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { WarnToast(ex.Message); }
    }

    // 托盘「最近通知」：回看被点掉 / 被挤掉 / 已自动消失的卡片（会话级，不落盘）。
    private readonly NotificationLog _notifications = new();

    public IReadOnlyList<NotificationEntry> RecentNotifications => _notifications.Recent;

    // 弹窗路径触发时留痕（卡片路径由 ShowToast 顺手记）：与 ShowToast 的 Post() 同口径，
    // 只在 UI 线程调——_notifications 不是线程安全的。
    public void AddNotificationTrace(NotificationEntry n) => _notifications.Add(n);

    // 从托盘重放一条：忠实还原——原时长（120s 的长文卡不会被放成 5 秒一闪、常驻仍常驻）、原时刻
    // （眉标显示它当初几点发生，不是重放的现在）、原合并键（同键卡片还在屏时就地更新而非叠双份，
    //  且 countMerge:false——重放不是一次新触发，不涨 ×N、不改在屏卡片的时刻戳）。
    // 不再记一笔留痕（否则回看动作本身会把缓冲刷乱）。
    public void ReplayNotification(NotificationEntry n)
    {
        try
        {
            Views.NotificationToast.Show(n.Title, n.Message,
                n.Warn ? Views.ToastLevel.Warn : Views.ToastLevel.Info,
                n.DurationMs, n.Key, at: n.At, countMerge: false);
        }
        catch { }
    }

    // 错误日志路径与写入格式的唯一出处。best-effort：写不进去不影响主流程。
    // 任何线程可调（UnobservedTaskException 在终结器线程上进来）。
    private string ErrorLogPath => Path.Combine(_cfgDir(), "clockwork.error.log");

    // 日志的上限。超过就砍掉前一半，只留最近的。
    //
    // 这个文件从前是**无上限**的——一句 AppendAllText，写到天荒地老。单看每次只多一两行不像问题，
    // 可它是这个程序唯一的事后线索（今天定位鼠标钩子那次就是靠它），于是不能靠「少记点」来控制体积，
    // 只能给它一个头。128KB 约合两千多行，够回溯很久，而且永远不会变成用户目录里那种没人敢删的大文件。
    //
    // 砍前一半而不是清空：清空会在最需要历史的那一刻（刚出过一次故障）把历史一起丢掉。
    // 按行首对齐再截，别把一行从中间劈开——半行时间戳比没有更难读。
    private const int ErrorLogMaxBytes = 128 * 1024;

    // 时间戳固定按公历、走不变文化。`$"{DateTime.Now:yyyy-MM-dd}"` 用的是 CurrentCulture，
    // 而本程序只设 CurrentUICulture（见 I18n/Strings.cs），CurrentCulture 留给系统区域设置——
    // 于是区域设成泰国的机器上这一行写成 2569-09-02（佛历），设成沙特的写成 1448-03-20（回历），
    // 年月日全变。排障要按时间和事件查看器、别的日志对齐，日志里的时间就不能随机器换历法。
    private static string Stamp()
        => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    // 连续重复的行只记第一条，判据见 Core/LogDedup.cs（那里也解释了为什么要收敛）。
    private readonly LogDedup _dedup = new();

    // **入队侧的内存锁。** 只护两样东西：_dedup 的账本、以及往 _logQueue 里放一行。
    // 从前这把锁护的是「先截断再追加」那一整段文件操作，于是**后台线程在锁里等磁盘 I/O 时，
    // UI 线程下一次记日志就得跟着一起等**——而 UI 线程被卡过 5 秒就是幽灵化
    //（见 Core.UiStallWatch）。现在文件 I/O 全在专属写盘线程上，这把锁里再也没有 I/O。
    private readonly object _logLock = new();

    // 待落盘的行（**已经带好时间戳与换行**——时间戳必须在事件发生那一刻取，
    // 不能等写盘时再取，否则「排队等了 200ms」会写成「200ms 后才发生」）。
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logQueue = new();
    private readonly SemaphoreSlim _logWork = new(0);
    private Thread? _logWriter;
    private readonly object _logStartLock = new();

    // **只护「碰文件」那一段。** 有了它才有「单写者」：写盘线程与同步排空（崩溃 / 退出）
    // 不会同时去截断同一个文件。
    //
    // 锁序固定为 `_logFileLock` → `_logLock`（截断之后要清 _dedup 的账本），**绝无反向**：
    // 入队侧只拿 _logLock、永不碰 _logFileLock。加新代码时守住这个方向就不会有环。
    private readonly object _logFileLock = new();

    // 日志上限与截断判据见 ErrorLogMaxBytes。超过就砍前一半，只留最近的。
    //
    // 同步排空的两处超时。**它们存在的唯一理由是「退出/崩溃绝不能被一份写不动的日志吊住」**：
    // 文件若落在已经断掉的网络盘上，写盘线程会卡在一次系统调用里很久，而这时
    // OnExit 正等着拿 _logFileLock——没有超时就是「点了退出，程序赖着不走」。
    // 取值只影响「卡住时等多久放弃」，不影响正常路径（正常路径里这把锁几乎总是空的）。
    private const int LogFlushTimeoutMs = 300;      // 退出：宁可少写最后几行，也不能让退出挂住
    private const int LogCrashTimeoutMs = 2000;     // 崩溃：异常堆栈是这份文件最值钱的一段，多等一会儿

    // internal：Views 层的无主对话框抢前台失败（DialogForeground）也要在这份日志里留证据。
    // （App 上的 internal 门面转发供 Views 层调用，见 App.xaml.cs。）
    internal void AppendErrorLog(string line)
    {
        // 任何线程可调（UnobservedTaskException 从终结器线程进来）。
        //
        // **这一段只做内存操作**：去重判据 + 入队。文件 I/O 全在 LogWriterLoop 那条线程上。
        // 于是「后台线程正在写盘」再也不会让 UI 线程的这一次调用等——那正是它从同步改成入队的理由。
        //
        // 代价要说清（取舍，不是疏漏）：行落盘比从前晚一点点（正常是亚毫秒级，
        // 写盘线程一被唤醒就排空），所以**硬杀进程（TerminateProcess / 断电）会带走队列里那几行**。
        // 接受它，因为换来的是「UI 线程永不因日志而阻塞」；两条真正要紧的路各有同步兜底：
        // 崩溃走 LogError（自己排空再写），正常退出走 OnExit → FlushErrorLog。
        try
        {
            EnsureLogWriter();
            lock (_logLock)
            {
                var write = _dedup.Next(line);
                if (write == null) return;
                _logQueue.Enqueue($"[{Stamp()}] {write}\r\n");
            }
            _logWork.Release();   // 出锁再放行：写盘线程醒来时这一行已经在队列里了
        }
        catch { }   // best-effort：写不进去不影响主流程
    }

    private void EnsureLogWriter()
    {
        if (_logWriter != null) return;
        lock (_logStartLock)
        {
            if (_logWriter != null) return;
            _logWriter = new Thread(LogWriterLoop) { IsBackground = true, Name = "Clockwork.ErrorLog" };
            _logWriter.Start();
        }
    }

    // 写盘线程的一生：等信号 → 把当时攒下的都排空。
    // 一次信号排空一批，所以连着记十行只会唤醒一两次，而不是十次。
    private void LogWriterLoop()
    {
        while (true)
        {
            try { _logWork.Wait(); } catch { return; }   // 信号量被拆（退出竞态）：收摊
            TryDrainErrorLog(int.MaxValue);
        }
    }

    // 把队列里攒下的行写出去。**调用方必须已持有 _logFileLock。**
    // 拆出「已持锁」的版本是为了让 LogError 能在同一段临界区里先排空、再写自己的堆栈——
    // 分成两次拿锁的话，中间会插进写盘线程，时间线就倒了。
    private void DrainQueueLocked(string path)
    {
        var sb = new System.Text.StringBuilder();
        while (_logQueue.TryDequeue(out var line)) sb.Append(line);
        if (sb.Length == 0) return;
        var fi = new FileInfo(path);
        if (fi.Exists && fi.Length > ErrorLogMaxBytes)
        {
            var all = File.ReadAllText(path);
            int cut = all.IndexOf('\n', all.Length / 2);
            File.WriteAllText(path, cut < 0 ? "" : all[(cut + 1)..]);
            // 前一半被丢掉了，_dedup 记着的「上一行」可能已经不在文件里——
            // 那时再写「同上一行」就是指着一行读者看不到的话。清账，让下一条原样写出去。
            // 这里要拿 _logLock（账本归入队侧所有），锁里只有内存操作，不会把入队侧拖住。
            lock (_logLock) { _dedup.Reset(); }
        }
        File.AppendAllText(path, sb.ToString());
    }

    /// <summary>同步把队列排空。给崩溃（堆栈必须在进程死之前落盘）和退出（进程一走队列就没了）用。</summary>
    //
    // 带超时：拿不到锁就放弃，绝不无界等。理由见 LogFlushTimeoutMs 那段。
    private bool TryDrainErrorLog(int timeoutMs)
    {
        if (!Monitor.TryEnter(_logFileLock, timeoutMs)) return false;
        try { DrainQueueLocked(ErrorLogPath); return true; }
        catch { return false; }   // best-effort
        finally { Monitor.Exit(_logFileLock); }
    }

    // OnExit 用的同步收尾：把队列里最后那几行补上。
    // 不调 EnsureLogWriter：这条路自己就能排空，没必要为了收尾再起一条线程。
    public void FlushErrorLog() => TryDrainErrorLog(LogFlushTimeoutMs);

    // 崩溃日志额外空一行分隔（异常带多行堆栈，挤在一起没法读），故不走 AppendErrorLog。返回路径供崩溃框指路。
    // 不参与「连续重复只记一条」：每个堆栈各不相同，而崩溃从来不是刷屏的那一类。
    // 但共用同一个文件，也共用同一把文件锁——两处写的是同一份东西。
    internal string LogError(Exception? ex)
    {
        var path = ErrorLogPath;
        // 带超时拿文件锁：崩溃路径绝不能被一份写不动的日志吊住（理由见 LogFlushTimeoutMs）。
        // 拿不到也照写——那时已经在「文件系统不正常」的极端情形里了，追加一段小文本
        // 仍然是让堆栈留在磁盘上的唯一机会，而少写它的代价比交错几行大得多。
        bool locked = Monitor.TryEnter(_logFileLock, LogCrashTimeoutMs);
        try
        {
            // **先排空队列再写自己。** 队列里是崩溃之前刚发生的事，而这份文件的读法是
            // 「从上往下就是时间线」；不先排空的话，堆栈会插在它们前面，时间线当场倒过来——
            // 而倒过来的日志比没有更难读（会把人引向错误的方向）。
            try { DrainQueueLocked(path); } catch { }
            // **写之前先清账。** 这一段堆栈插在 _dedup 的账本之外，它并不知道中间隔了东西。
            // 不清的话：先记了 L，崩一次，同样的 L 再来时被判成「与上一行相同」，
            // 日志里就成了「一整段堆栈 + 一句『同上一行，且一直在发生』」——那句标记指着一行
            // 跟它毫无关系的话，而 L 本身一次都没写进去。判据本来就是「隔了别的事件之后
            // 再出现的同一条是新事件」（见 Core/LogDedup.cs）。
            lock (_logLock) { _dedup.Reset(); }
            try { File.AppendAllText(path, $"[{Stamp()}] {ex}\r\n\r\n"); } catch { }
        }
        catch { }
        finally { if (locked) Monitor.Exit(_logFileLock); }
        return path;
    }

    public void ShowCrash(Exception? ex)
    {
        var logPath = LogError(ex);
        // 崩溃兜底：先试品牌对话框；若它自身(依赖主题/资源)也失败，退回最稳的原生 MessageBox。
        var body = Lf("Crash_Body", ex?.Message ?? "", logPath);
        var title = Strings.Get("Crash_Title");
        try { Views.BrandDialog.Warn(null, title, body); }
        catch
        {
            try { System.Windows.MessageBox.Show(body, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); } catch { }
        }
    }
}
