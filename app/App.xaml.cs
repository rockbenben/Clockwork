using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Native;
using Microsoft.Win32;

namespace Clockwork;

// 应用外壳：单实例 + AUMID + 崩溃兜底 + 配置加载 + --boot 分发 + 托盘 + 隐到托盘。
// Application 在 WPF/WinForms 间歧义，显式限定为 WPF。
public partial class App : System.Windows.Application
{
    private const string Aumid = "rockbenben.clockwork";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;   // 持引用防注册等待被回收；随进程退出
    private TrayIcon? _tray;
    private MainWindow? _main;
    private RootConfig _config = new();
    private string _cfgPath = "";
    private bool _configSuperseded;   // 导入已把新配置写盘：本实例内存里的 _config 从此作废，禁止回写（见 MarkConfigSuperseded）
    private string _statePath = "";   // clockwork.state.json：提醒耐久运行态
    private string _exeDir = "";
    private string _exePath = "";
    private int _launchRunning;   // 0/1 并发守卫
    private readonly RunGate _runGate = new();   // 启动序列/单步/动作组 共享的急停闸

    private readonly Dictionary<string, ReminderState> _reminderStates = new();
    private HashSet<string> _startupReminderIds = new();   // 启动那刻已存在的提醒 id：只有它们才允许「错过必补」（排除中途新建的）
    private DateTime _startTime;
    private int _uptimeAtLaunch;
    private bool _reminderTickBusy;   // 防重入：弹窗模态消息循环期间计时器再触发不叠窗
    private readonly Random _rng = new();
    private DispatcherTimer? _reminderTimer;

    /// <summary>接管等待的「分片」长度：每等这么久醒一次，看看是该继续等还是该退（见 ClaimSingleInstance
    /// 的循环）。它同时是「双击 exe 而老实例活得好好的」这条常态路径的总耗时——第一片等完、查到同伴进程
    /// 还活着就立即退。原来是单次 1200 毫秒的死等，每次双击都白转 1.2 秒的忙碌光标。</summary>
    private static readonly TimeSpan HandoffTakeoverWait = TimeSpan.FromMilliseconds(250);

    /// <summary>接管尝试的总上限。两类进程会等到这么久：被 <see cref="RelaunchForLanguage"/> /
    /// <see cref="RelaunchElevated"/> 派出来顶替老实例的（接管是它唯一的目的），以及双击后发现同伴进程
    /// 已消失的（没人会替它显示窗口，退出等于一个实例都不剩）。接管失败的后果不对称——等长一点的代价
    /// 只是一个看不见的进程多活几秒，而放弃得太早的代价是托盘应用凭空消失、用户只会以为程序崩了。</summary>
    private static readonly TimeSpan RelaunchTakeoverWait = TimeSpan.FromSeconds(3);

    // 启动只做编排，每一步的细节和理由都在各自的方法里。
    //
    // 顺序是承重的，不是书写习惯，有四处硬约束：
    //   · 提权子任务必须最先判——它不建窗口/托盘/计时器，也不该参与单实例；
    //   · 单实例判定要早于任何进程级副作用，否则一个马上要退出的多余进程会先去改共享状态；
    //   · AUMID 声明必须早于第一个 UI 对象（微软的要求），否则任务栏分组认不出是同一个应用；
    //   · UI 文化与 RTL 元数据必须早于建第一个窗口，XAML 的本地化在加载时就解析完了。
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (RunAutostartSubcommand(e.Args)) return;   // 一次性提权子任务：办完即退

        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // 关窗=隐到托盘；退出仅经托盘
        DispatcherUnhandledException += (s, ex) => { ShowCrash(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) => ShowCrash(ex.ExceptionObject as Exception);
        // 第三个入口：没 await 的 Task 吞掉的异常，在 GC 回收该 Task 时才冒出来（终结器线程、非致命）。
        // 只记日志不弹窗——冒出来的时机和出错的时机隔着一次 GC，弹窗没有上下文可指；SetObserved 防止
        // 宿主策略把它升级成进程退出。
        TaskScheduler.UnobservedTaskException += (s, ex) => { ex.SetObserved(); LogError(ex.Exception); };

        // 开发期自查（--smoke / --shots，见 DevChecks.cs）：在单实例之前——托盘里正在用的实例照常工作。
        if (e.Args.Contains("--smoke") || e.Args.Contains("--shots")) { RunDevCheck(e.Args); return; }

        if (!ClaimSingleInstance(e.Args)) return;     // 已有实例在跑：叫醒它，自己退出

        ResolveSelfPaths();
        DeclareAppUserModelId();
        LoadConfig();
        LoadReminderState();
        ApplyUiCulture();

        BuildShell();          // 主窗口 + 托盘
        // 配置读不出来的告警要等到这里才发得出去：LoadConfig 跑在托盘之前，那时没有任何可用的提示通道。
        if (_configUnreadable) WarnToast(Lf("Warn_ConfigUnreadable", _cfgPath + ".bad"));
        StartEngines();        // 提醒计时器 / 系统事件 / 全局热键 / 跨实例显示信号
        ShowInitialWindow(e.Args);

        // 放在最后、且不等它：注册表 + 图标解压是给「通知弹出时系统去读」用的，最快也要等到第一条提醒，
        // 而它此前堵在读配置和显示窗口之前。整体 try/catch 兜住——注册表被策略锁死之类的场景不该拖累启动，
        // 后果只是通知里没有应用图标。
        Task.Run(() => { try { RegisterAumidBranding(); } catch { } });
    }

    // 一次性提权子任务：由非提权主实例在 schtasks 拒绝时以管理员身份重开自己触发。
    // 仅执行自启注册/注销后立即退出——不建窗口/托盘/计时器，也不参与单实例，避免与运行中的主实例冲突。
    // 返回 true = 本次启动到此为止。
    private bool RunAutostartSubcommand(string[] args)
    {
        bool regTask = args.Contains("--register-autostart");
        if (!regTask && !args.Contains("--unregister-autostart")) return false;
        string res;
        try { res = regTask ? Autostart.Register(Environment.ProcessPath ?? "") : Autostart.Unregister(); }
        catch { res = "Error"; }
        Environment.ExitCode = res == "Ok" ? 0 : 2;   // 主实例据退出码刷新/报错
        Shutdown();
        return true;
    }

    // 单实例（best-effort）：已运行则置信号让旧实例显示窗口，自己退出。同步对象创建/打开失败
    // （另有提权实例持有同名命名对象、ACL 受限等）绝不因此崩溃——按「本实例照常运行」放行。
    // 返回 false = 本进程该退出。
    private bool ClaimSingleInstance(string[] args)
    {
        try
        {
            // Local\ = 每登录会话一实例（曾是 Global\ 整机一实例）：快速切换用户时，第二个会话双击 exe
            // 本该开出自己的实例——Global 下信号会去唤醒第一个会话里的窗口，第二个用户什么都看不见。
            // 托盘应用是每用户的（配置、自启注册表全在 HKCU），单实例的边界理应跟配置一致。
            // 同会话内的提权重开不受影响：管理员进程和普通进程共享同一个会话的 Local 命名空间。
            _mutex = new Mutex(true, @"Local\rockbenben.clockwork.mutex", out bool createdNew);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\rockbenben.clockwork.show");
            if (createdNew) return true;

            // 先发信号、再争互斥体。顺序反过来（原来的写法）会让每一次「已在托盘时双击 exe」都白等满
            // 这段超时才去叫醒老实例——而常态恰恰是老实例活得好好的，等待必然超时。
            // 先 Set 之后老实例并行去显示窗口，我们这边照旧等自己的接管窗口，两件事重叠。
            // 万一真抢到了互斥体（老实例确实在退出），这次多发的信号也无害：AutoReset 事件没人在等
            // 就自己落地；即便被本实例随后注册的等待接住，结果也只是把窗口显出来——而用户正是双击了 exe。
            _showEvent.Set();
            // 分片等 + 查同伴进程，两个方向都严格变好：
            //   · 双击 exe（常态）：第一片等完发现另一个 Clockwork 进程活着 → 它去显示窗口，我们立即退，
            //     总耗时仍是一片（250ms），不比原来慢；
            //   · 托盘退出后立刻双击 / 覆盖 exe 后立刻启动（竞态）：同伴进程已消失（或很快消失），
            //     此时退出等于一个实例都不剩——继续分片重试直到拿下互斥体，上限 3 秒。
            //     原来单次 250ms 就放弃，正是这个竞态的输法。
            // --show（切语言/提权重开派来的）从不因「同伴还活着」提前退：接管一个正在退出的实例
            // 是它唯一的目的，同伴活着恰是预期状态。
            bool relaunch = args.Contains("--show");
            var deadline = DateTime.UtcNow + RelaunchTakeoverWait;
            bool got = false;
            while (true)
            {
                try { got = _mutex.WaitOne(HandoffTakeoverWait); } catch (AbandonedMutexException) { got = true; }
                if (got || DateTime.UtcNow >= deadline) break;
                if (!relaunch && AnotherInstanceAlive()) break;
            }
            if (got) return true;   // 老实例确实在退出/已退出，本进程顶上
            Shutdown();
            return false;
        }
        catch { _mutex = null; _showEvent = null; return true; }
    }

    // 是否还有别的 Clockwork 进程在跑（按进程名，排除自己；只看本会话——互斥体是 Local\ 的，
    // 别的用户会话里的实例与本会话互不相干，把它算成同伴会在竞态分支里错误地提前放弃接管）。
    // 查不了（权限受限等）按「活着」处理：宁可这次双击白按一下，也别冒双实例或全灭的险。
    private static bool AnotherInstanceAlive()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var peers = Process.GetProcessesByName(self.ProcessName);
            bool alive = false;
            foreach (var p in peers)
            {
                try { alive |= p.Id != self.Id && p.SessionId == self.SessionId; } catch { alive = true; }
                p.Dispose();
            }
            return alive;
        }
        catch { return true; }
    }

    private void ResolveSelfPaths()
    {
        _exePath = Environment.ProcessPath ?? "";
        _exeDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
    }

    // AUMID 拆成两半：声明必须留在启动路径上（微软的要求是「在进程创建任何 UI 对象之前」，
    // 晚了任务栏分组/固定就认不出是同一个应用），而它只是一次 P/Invoke，没有 I/O。
    // 品牌信息的落盘（注册表 + 把内嵌图标解到 %LOCALAPPDATA%）挪到后台去（见 RegisterAumidBranding）——
    // 那是通知弹出时才用得上的东西，却堵在读配置和显示窗口前面，是启动路径上白付的一笔文件与注册表 I/O。
    private void DeclareAppUserModelId()
    {
        try { Native.Shell.SetCurrentProcessExplicitAppUserModelID(Aumid); } catch { }
    }

    private void LoadConfig()
    {
        _cfgPath = ConfigPath.Resolve(_exeDir);
        EnsureConfigFile();
        _config = ConfigStore.Read(_cfgPath, out var normalized, out _configUnreadable);
        // 读不出来（漏个逗号 / 上次断电留下半截文件）：先把原文件留一份 .bad 再往下走。
        // 光靠下面「不写回」还不够——用户看到界面空空，多半会当成配置丢了、照着重建一遍，
        // 那一次保存才是真正把原文件盖掉的时刻。备份是给那一刻留的后路。
        if (_configUnreadable)
        {
            try { File.Copy(_cfgPath, _cfgPath + ".bad", overwrite: true); } catch { }
        }
        // 规范化界面语言到「必是受支持的一门」：空→跟随系统；不在 18 项列表里的有效文化→映射最接近（pt-BR→pt）；
        // 无效→跟随系统。既尊重示例配置指定的语言，又保证送进 MainWindow 下拉的语言必能匹配——
        // 否则「非空但不在列表」会被下拉初始化当不匹配、强存 zh-CN 并重启，弄丢用户/系统语言。变了就落盘。
        var normLang = Languages.Normalize(_config.Settings.Language);
        if (!string.Equals(normLang, _config.Settings.Language, StringComparison.Ordinal))
        { _config.Settings.Language = normLang; normalized = true; }
        // 读入时若发生了重启后有影响的规范化（剔 null / 补生或重发 id / 补语言），立即写回——
        // 尤其去重重发的提醒 id：不落盘则每次启动都换新 id，运行态接不上、被去重那条每次重启都重弹。
        // 解析失败时绝不写回。这里的 _config 是与用户内容毫无关系的默认配置，而 normalized 一定会被
        // 上面那段语言规范化翻成 true（默认配置的 Language 是空串，Normalize 必返回具体语言码）——
        // 于是「解析失败落回默认」这个本意是止损的分支，反而在启动那一刻就把坏配置抹平成默认配置，
        // 用户漏打一个逗号就永久失去全部设置。ConfigStore.Read 承诺的「不损坏」必须在这里兑现。
        if (normalized && !_configUnreadable) { try { ConfigStore.Write(_config, _cfgPath); } catch { } }
    }

    // 配置读不出来：本次以默认配置启动。true 时既不写回配置，也在托盘就绪后弹一条告警——
    // 不说的话用户只会看到一个空界面，无从知道原因，更不会想到原文件还在、修好就能恢复。
    private bool _configUnreadable;

    // 提醒运行态落盘路径 + 载入上次的耐久态（上次触发日期/稍后到点）。重启后不再重复弹当天已弹过的。
    private void LoadReminderState()
    {
        _statePath = Path.Combine(CfgDir, "clockwork.state.json");
        foreach (var kv in ReminderStateStore.Load(_statePath)) _reminderStates[kv.Key] = kv.Value;
        // 载入时顺手清掉过期(早于今天)的稍后，别让陈旧记录长期留在盘里（Decide 也有运行期兜底）。
        // 「错过必补」且启用的提醒例外：保留陈旧稍后交给 Decide 补发一次（跨天未应答不无声吞掉），
        // 与 Decide 的过期分支同一口径。禁用的照清——Decide 对禁用项直接 none，留着只会烂在盘里。
        // 事件型也照清（与 Decide 的事件守卫同口径）：残留的「错过必补」不该让昨晚的稍后在今早诈尸。
        bool cleaned = false;
        foreach (var kv in _reminderStates)
            if (kv.Value.SnoozeUntil is DateTime su && su.Date < DateTime.Now.Date)
            {
                var owner = _config.Reminders.FirstOrDefault(x => x.Id == kv.Key);
                if (owner is { Enabled: true, CatchUpIfMissed: true } && !ReminderEvent.IsEvent(owner.Trigger)) continue;
                kv.Value.SnoozeUntil = null; cleaned = true;
            }
        if (cleaned) ReminderStateStore.Save(_statePath, _reminderStates);
        _startupReminderIds = new HashSet<string>(_config.Reminders.Select(x => x.Id));
    }

    private void ApplyUiCulture()
    {
        Strings.ApplyCulture(_config.Settings.Language);   // 建任何窗口前设 UI 文化
        if (Strings.IsRightToLeft)                          // 阿拉伯语等：全窗口默认从右向左（须在建任何窗口前覆盖元数据）
            FrameworkElement.FlowDirectionProperty.OverrideMetadata(
                typeof(Window), new FrameworkPropertyMetadata(System.Windows.FlowDirection.RightToLeft));
    }

    private void BuildShell()
    {
        // 运行闸的变化搬到 UI 线程再广播：Begin/End 都在后台运行线程上调，订阅方（急停按钮）要动控件。
        _runGate.ActiveChanged += () => Dispatcher.BeginInvoke(() => RunStateChanged?.Invoke());
        _main = new MainWindow(_config, SaveConfig, MigrateReminderState);
        _tray = new TrayIcon(this);
    }

    private void StartEngines()
    {
        // 提醒计时器：记录启动时刻与开机分钟数（供「登录时」提醒门控），按 tickSeconds 轮询。
        _startTime = DateTime.Now;
        _uptimeAtLaunch = SystemInfo.UptimeMinutes();
        StartReminderTimer();
        WireSystemEvents();
        RegisterStopHotkey();

        // 跨实例「显示窗口」信号：事件驱动等待（原每秒轮询）。AutoReset 事件被 Set 才回调，常态零唤醒；
        // executeOnlyOnce:false = 每次信号都再等下一次。（单实例对象创建失败时 _showEvent 为 null，跳过。）
        if (_showEvent != null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private void ShowInitialWindow(string[] args)
    {
        bool forceShow = args.Contains("--show");   // 语言切换重启后：强制显示窗口，忽略「启动时最小化」
        if (args.Contains("--boot"))
        {
            _main!.ShowInTaskbar = false;   // 自启：不显窗、只入托盘
            var bt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            bt.Tick += (s, _) => { bt.Stop(); RunLaunchAsync(true); };
            bt.Start();
        }
        else if (_config.Settings.StartMinimized && !forceShow)
        {
            _main!.ShowInTaskbar = false;
        }
        else
        {
            _main!.Show();
        }

        // 这次不显示 ≠ 不会被显示。开机自启之后，当天第一次双击托盘要现场把整棵可视树建出来
        // （5 个 Tab、4 个 DataGrid、943 行 Theme.xaml 的模板），实测 141 毫秒；跑过一次之后只要 15 毫秒。
        // 那 141 毫秒以前是「白着」——看着像窗口已经开了；cloak 之后变成「空着」，反而更像卡住。
        // 所以别让它发生：趁没人看的时候把布局先跑一遍。
        // ApplicationIdle 是关键——等 UI 线程真的闲下来才做，绝不去和开机清单抢登录那几秒。
        if (!_main.IsVisible) Dispatcher.BeginInvoke(WarmMainWindow, DispatcherPriority.ApplicationIdle);
    }

    // 把窗口的布局先跑一遍，不显示它。贵的是模板展开与 DataGrid 行实例化，这两件都发生在
    // Measure/Arrange 里，Show() 只是在此之上再加一次合成——所以不 Show 也能把大头付掉。
    // 失败无所谓：最坏就是回到「第一次打开慢一点」，不该牵连任何别的东西。
    private void WarmMainWindow()
    {
        if (_main == null || _main.IsVisible) return;
        try
        {
            // 显式限定到 WPF：UseWindowsForms 的全局 using 让 Size/Point 与 System.Drawing 的同名类型撞车。
            var size = new System.Windows.Size(_main.Width, _main.Height);
            _main.Measure(size);
            _main.Arrange(new Rect(new System.Windows.Point(), size));
            _main.UpdateLayout();
            // 试过再往前付一步——用 RenderTargetBitmap 离屏渲一遍，想把光栅化也提前。实测无效
            //（83ms vs 79ms，噪声之内），只换来一次几 MB 的分配，故删掉。
            // 剩下那 ~65ms 不在内容上，而在窗口呈现本身（HwndTarget 建面、DWM 首次合成握手），
            // 不 Show 就付不掉。要再压只剩「cloak 着 Show 一次再 Hide」那条路，代价是登录时抢焦点
            // 和一次真实的显示循环——为 65ms 不值。
        }
        catch { }
    }

    public void ShowMain()
    {
        if (_main == null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.ShowInTaskbar = true;
        _main.Activate();
    }

    public void ExitApp()
    {
        _reminderTimer?.Stop();
        UnwireSystemEvents();
        if (_main != null) _main.AllowClose = true;
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        Shutdown();
    }

    // 所有退出路径（托盘退出/语言切换重启/提权重启）都过 Shutdown → 在此兜底：
    // 提醒状态的后台补写是 fire-and-forget，进程退出会带走未落盘的快照，退出前同步补写最后一份。
    protected override void OnExit(ExitEventArgs e)
    {
        ReminderStateStore.FlushPending();
        base.OnExit(e);
    }

    // 语言切换：重开自身（--show 强制显示窗口）后退出当前实例。新实例读到已保存的新语言，
    // 建窗前 ApplyCulture 即全量生效。单实例：本实例先释放互斥体/退出，新实例的等待(1200ms)随即接管。
    public void RelaunchForLanguage()
    {
        // 重开失败也必须退出：导入只改了磁盘文件、切换语言只改了 _config，都靠新实例重读生效。
        // 若留着旧实例不退，它内存里的旧 _config 会被之后任一次 SaveConfig 覆盖回磁盘——把刚导入的配置无声还原（数据丢失）。
        // 故失败时先弹「模态」提示手动重开（toast 会随进程退出看不到），再照常退出。
        try { Process.Start(new ProcessStartInfo { FileName = _exePath, Arguments = "--show", UseShellExecute = true }); }
        catch (Exception ex) { if (_main != null) Views.BrandDialog.Warn(_main, "Clockwork", Lf("Relaunch_Fail", ex.Message)); }
        ExitApp();
    }

    // 以管理员身份重开自身（系统启动项开关/接管遇 NeedsAdmin 时用；旧版 Show-NeedsAdminPrompt 的移植）。
    // 仅用户取消 UAC（Win32 1223）静默留在当前实例；其他启动失败（exe 被删/被策略拦）如实报警——
    // 用户刚点了「是，以管理员重开」，静默不动会让人以为提权坏了。新实例对单实例互斥有 1.2s 接管等待。
    public void RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _exePath, Arguments = "--show", Verb = "runas", UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return; }   // 取消 UAC：保持现状
        catch (Exception ex) { WarnToast(Lf("Autostart_Fail", ex.Message)); return; }
        ExitApp();
    }

    // 后台跑启动清单（手动重跑或 --boot）。并发守卫防连点交错。
    public void RunLaunchAsync(bool boot)
    {
        if (Interlocked.Exchange(ref _launchRunning, 1) == 1) return;   // 已有一次在跑，忽略
        var cfg = _config.SnapshotForRun();   // 快照列表：开机延迟期间 UI 增删步骤不会让后台枚举抛「集合已修改」
        var selfPaths = new[] { _exePath };
        var cfgDir = CfgDir;
        Task.Run(() =>
        {
            _runGate.Begin();   // 首个并发运行才清急停；不再无条件 Clear（避免抹掉在途急停）
            try
            {
                var result = LaunchSequence.Run(cfg, boot, -1, 0,
                    // 卡片形态的 message 在这里截下：InvokeStepAction 对 message 一律静默返回 ✓
                    // （模态形态在启动路径本就该静默），只有这一层知道该弹卡片。顶层与组展开共用本 lambda。
                    s => s.Kind == "message" && StepHelpers.MessageFormOf(s) == MessageForm.Card
                         ? ShowStepCard(s)
                         : StepRunner.RunStepMark(s, a => ConfirmDestructive(a), selfPaths),
                    () => DateTime.Now);
                LaunchSequence.WriteLog(Path.Combine(cfgDir, "clockwork.run.log"), result, DateTime.Now);
                if (!boot) Dispatcher.Invoke(() => NotifyRunResult(result));
            }
            // 没有 catch 时任何异常都让整个开机序列静默中止（无日志/无 toast/什么都没启动）——如实报出来。
            catch (Exception ex) { WarnToast(Lf("Warn_LaunchRunCrashed", ex.Message)); }
            finally { _runGate.End(); Interlocked.Exchange(ref _launchRunning, 0); }
        });
    }

    // —— 全局热键（急停 + 动作组） ——
    private const int HotkeyId = 0xB001;          // 急停
    private const int GroupHotkeyBase = 0xB100;   // 动作组热键 id 区间起点
    private const int GroupSlotMax = 0xBFF0;      // RegisterHotKey 应用侧 id 上限 0xBFFF，留余量
    private nint _hotkeyHwnd;                     // 主窗口句柄，注册/注销共用
    private bool _hotkeysSuspended;               // 捕捉期间为真：SaveConfig 不得重建热键（否则组会抢走正被改绑的急停组合）
    private readonly Dictionary<int, string> _groupHotkeyIds = new();   // 当前已注册的 id → 组 Id（WM_HOTKEY 查表）
    private readonly Dictionary<string, int> _groupIdSlots = new();     // 组 Id → 固定 id 槽位：重建不换号，队列里滞留的旧 WM_HOTKEY 不会错派到别的组
    private int _nextGroupSlot = GroupHotkeyBase;
    private HashSet<string> _hotkeyFails = new();                       // 上一轮注册失败的「组Id|键」：同一失败只 toast 一次，不随每次保存刷屏
    private string? _stopHotkeyFail;                                    // 急停键上次失败的组合：同一失败只 toast 一次（每次捕捉进出都会重注册）

    // 供 Views 层取 App 实例（挂起/恢复热键等），唯一出处——别再各处手写 Application.Current as App。
    public static App? Instance => System.Windows.Application.Current as App;

    private void RegisterStopHotkey()
    {
        try
        {
            _hotkeyHwnd = new WindowInteropHelper(_main!).EnsureHandle();   // 即便未显示也拿得到句柄
            HwndSource.FromHwnd(_hotkeyHwnd)?.AddHook(HotkeyHook);          // 钩子只挂一次
        }
        catch { return; }
        ResumeHotkeys();
    }

    // 按当前配置重注册全部热键（急停 + 各组）。捕捉结束/配置变更后调用，无需重启。
    // 急停先注册——组热键与急停撞车时输的是组（注册失败并 toast 点名），急停永远保命优先。
    public void ResumeHotkeys()
    {
        _hotkeysSuspended = false;
        RebindStopHotkey(_config.Settings.StopHotkey);
        RebindGroupHotkeys();
    }

    // 捕捉期间暂时注销全部热键：避免录键时按到已注册组合触发急停/跑组（e.Handled 拦不住 OS 级 WM_HOTKEY）。
    // 置起挂起标志：挂起期间任何 SaveConfig 都不得重建组热键——否则捕捉中途保存（改急停键正是此路径）
    // 会让某个组抢注用户刚指给急停的组合，恢复时急停注册失败、保命键从此哑掉。
    public void SuspendHotkeys()
    {
        _hotkeysSuspended = true;
        if (_hotkeyHwnd == 0) return;
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, HotkeyId); } catch { }
        UnregisterGroupHotkeys();
    }

    // 急停键：先注销旧的、再注册新的（空/无效=停用）。失败/无效 toast 按组合去重——
    // 每次进出捕捉框都会重注册，被占用时不该每次都弹同一条（组热键同款策略）。
    private void RebindStopHotkey(string? combo)
    {
        // 句柄已销毁（退出时主窗 Closed 触发的恢复）就跳过：否则在死句柄上注册失败、又弹「注册失败」气泡。
        if (_hotkeyHwnd == 0 || !HotKey.IsWindow(_hotkeyHwnd)) return;
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, HotkeyId); } catch { }
        if (string.IsNullOrWhiteSpace(combo)) { _stopHotkeyFail = null; return; }   // 空=禁用
        var p = KeyInput.ToHotkeyParams(combo);
        // 保留组合也在此拦：配置可手改/可带旧版遗留，只挡捕捉 UI 挡不住 JSON 里写进来的 Alt+F4。
        // 文案分开：解析不了 → 无法识别；解析得了但系统保留 → 明说保留，别让用户以为拼写错了反复试。
        if (p == null || HotkeyCapture.IsReserved(combo))
        {
            var msgKey = p == null ? "Hotkey_Unrecognized" : "Hotkey_Reserved";
            if (_stopHotkeyFail != combo) ShowToast("Clockwork", Lf(msgKey, combo), Views.ToastLevel.Warn);
            _stopHotkeyFail = combo;
            return;
        }
        bool ok = false;
        try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, HotkeyId, p.Modifiers | HotKey.MOD_NOREPEAT, p.Vk); } catch { }
        if (!ok)
        {
            if (_stopHotkeyFail != combo) ShowToast("Clockwork", Lf("Hotkey_RegisterFail", combo), Views.ToastLevel.Warn);
            _stopHotkeyFail = combo;
            return;
        }
        _stopHotkeyFail = null;
    }

    // 动作组热键：全量重建（先注销全部旧注册，再按当前配置注册启用组的非空热键）。
    // 注册失败（被其它程序占用 / 与急停或其它组重复）toast 点名组与键——但同一失败只报一次，
    // 不随之后每次无关的保存反复刷屏；失败消除（改键/解除占用）后再失败会重新提示。
    private void RebindGroupHotkeys()
    {
        if (_hotkeyHwnd == 0 || !HotKey.IsWindow(_hotkeyHwnd)) return;   // 句柄已销毁则跳过（同 RebindStopHotkey）
        UnregisterGroupHotkeys();
        // 清掉已删除组的槽位映射：字典有界（≤当前组数），回绕后它们的号可安全复用。
        var liveIds = new HashSet<string>(_config.ActionGroups.Select(x => x.Id));
        foreach (var dead in _groupIdSlots.Keys.Where(k => !liveIds.Contains(k)).ToList())
            _groupIdSlots.Remove(dead);
        var fails = new HashSet<string>();
        foreach (var g in _config.ActionGroups)
        {
            if (!g.Enabled || string.IsNullOrWhiteSpace(g.Hotkey)) continue;   // 禁用组不占键，把组合让给别人
            // 每组用固定 id 槽位（按组 Id 分配、正常不复用）：重建后 id 不换号，
            // 消息队列里滞留的旧 WM_HOTKEY 要么派给同一组、要么查表落空被忽略，绝不错派到别的组。
            // 槽位逼近 RegisterHotKey 的 id 上限（同进程建删数千个组）才回绕复用已删组让出的号；
            // 回绕时逐号跳过仍在映射中的槽位——绝不与现存组（含本轮已注册的）撞号。
            if (!_groupIdSlots.TryGetValue(g.Id, out int slot))
            {
                var inUse = new HashSet<int>(_groupIdSlots.Values);
                int probes = 0;
                do
                {
                    if (_nextGroupSlot >= GroupSlotMax) _nextGroupSlot = GroupHotkeyBase;
                    slot = _nextGroupSlot++;
                } while (inUse.Contains(slot) && ++probes < GroupSlotMax - GroupHotkeyBase);
                _groupIdSlots[g.Id] = slot;
            }
            var p = KeyInput.ToHotkeyParams(g.Hotkey);
            bool reserved = HotkeyCapture.IsReserved(g.Hotkey);   // 手改配置写进来的 Alt+F4 等：拒注册并明说保留
            bool ok = false;
            if (p != null && !reserved)
            {
                try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, slot, p.Modifiers | HotKey.MOD_NOREPEAT, p.Vk); } catch { }
            }
            if (!ok)
            {
                var key = g.Id + "|" + g.Hotkey;
                fails.Add(key);
                if (!_hotkeyFails.Contains(key))
                    WarnToast(reserved ? Lf("Hotkey_Reserved", g.Hotkey) : Lf("Hotkey_GroupRegisterFail", g.Name, g.Hotkey));
                continue;
            }
            _groupHotkeyIds[slot] = g.Id;
        }
        _hotkeyFails = fails;
    }

    private void UnregisterGroupHotkeys()
    {
        foreach (var id in _groupHotkeyIds.Keys)
            try { HotKey.UnregisterHotKey(_hotkeyHwnd, id); } catch { }
        _groupHotkeyIds.Clear();
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != HotKey.WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id == HotkeyId)
        {
            RequestStop();
            handled = true;
        }
        else if (_groupHotkeyIds.TryGetValue(id, out var gid))
        {
            // 按组 Id 现查（注册后组可能已被编辑），禁用兜底跳过——正常情况下禁用组根本不会注册。
            var g = _config.ActionGroups.FirstOrDefault(x => x.Id == gid);
            if (g is { Enabled: true }) ToggleGroupByHotkey(g);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // 动作组热键是开关：没在跑→跑；正在跑→取消这一次运行。「按了没反应」是这个键以前最大的毛病——
    // 组还在跑时再按一次，旧实现被运行集当成重入静默丢弃，屏幕上一点动静都没有，用户只能怀疑热键坏了。
    //
    // 取消的边界刻意收窄到「这一次运行」：不动全局急停，启动清单和别的组照跑。要停一切请用急停键
    // （设置页可改，托盘/主窗按钮同款）——一个组的热键不该有掀桌子的权力。
    //
    // 回执必须给，而且两个分支都要给、还要共用同一张卡（同一合并键）：
    //   · 只给取消发卡 → 启动分支静默，用户分不出「按了启动」和「这键没注册上」；
    //   · 两个分支各发各的卡 → 取消卡（原来是 Warn，默认挂 12 秒）会在屏上过期说谎：期间再按一次，
    //     组其实已经重新跑起来了，用户看到的还是「已请求取消」，于是判定 toggle 坏了。
    // 同键合并后，卡片就地更新成最后一次按键的结果，连按也只有一张。两条都用 Info：这是用户对自己
    // 的组主动做的开关，不是故障；Warn 的 12 秒时长正是过期卡片能骗人的原因。
    //
    // 竞态说明：RunGroupAsync 先派 Task、再由后台线程登记，中间有个空窗。这个窗口不是「几微秒」——
    // 每个在途运行都占着一个线程池线程阻塞，线程注入延迟在争用时可达约 1 秒，人手连按完全够得着。
    // 落进空窗的第二次按键会判成「没在跑」而再派一次，那一次随即被运行集判重入跳过（不会跑两遍），
    // 代价只是这一次按键白按。要根治得把登记提到派发线程上做两段式握手，收益不抵复杂度，暂留。
    private void ToggleGroupByHotkey(ActionGroup g)
    {
        var key = "grouptoggle:" + g.Id;
        if (ActionGroupRunner.RequestCancel(g.Id))
        {
            ShowToast("Clockwork", Lf("Toast_GroupCancelled", g.Name), Views.ToastLevel.Info, key: key);
            return;
        }
        // 在跑、但这一次运行不是热键管得着的那种（它是别的组的嵌套步骤，或开机清单内联展开的）：
        // 既不能取消，也绝不能再开一份并发副本。照单飞的老规矩不跑，但必须说清楚——
        // 这里要是直接调 RunGroupAsync，气泡会报「已启动」，而实际那一次随即被判重入丢弃，纯属骗人。
        if (ActionGroupRunner.IsRunning(g.Id))
        {
            ShowToast("Clockwork", Lf("Toast_GroupBusy", g.Name), Views.ToastLevel.Info, key: key);
            return;
        }
        RunGroupAsync(g);
        ShowToast("Clockwork", Lf("Toast_GroupStarted", g.Name), Views.ToastLevel.Info, key: key);
    }

    // —— 提醒计时器 ——
    private void StartReminderTimer()
    {
        int tick = _config.Settings.TickSeconds;
        if (tick < 5) tick = 30;
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(tick) };
        _reminderTimer.Tick += (s, e) => ReminderTick();
        _reminderTimer.Start();
    }

    private void ReminderTick()
    {
        // 弹窗 ShowDialog 是 UI 线程的嵌套消息循环，其间 DispatcherTimer 仍在走。无守卫会重入本方法、
        // 在已有模态弹窗上再叠一个。首个 tick 处理完（含所有到点提醒依次弹完）前，后续 tick 直接跳过。
        if (_reminderTickBusy) return;
        // 勿扰生效：本 tick 不触发任何东西（含静默组），到期自动恢复。但基线要照常刷——
        // 不刷的话，勿扰期间拔的电源会在勿扰结束后的第一个 tick 被当成「刚拔」补弹一次，
        // 与订阅类事件（锁屏/解锁/唤醒，勿扰期间直接不算）行为相反。勿扰的语义只有一条：期间发生的事件不算数。
        if (DndRemaining != null) { PollEnvironmentTriggers(DateTime.Now, fire: false); return; }
        _reminderTickBusy = true;
        try
        {
            var now = DateTime.Now;
            bool durableChanged = false;
            // 清理孤儿运行态：已删除/已改 id 的提醒不再留状态（防长驻累积，也防按 id 串状态）。
            if (_reminderStates.Count > 0)
            {
                var live = new HashSet<string>(_config.Reminders.Select(x => x.Id));
                foreach (var dead in _reminderStates.Keys.Where(k => !live.Contains(k)).ToList())
                { _reminderStates.Remove(dead); durableChanged = true; }
                // 两个「本轮已触发」集合同样按 id 记事，一并跟着清，别让删掉的提醒在集合里长住。
                _idleFired.RemoveWhere(k => !live.Contains(k));
                _lowBatteryFired.RemoveWhere(k => !live.Contains(k));
            }
            // 空闲 / 电源两类事件没有系统通知可订，只能轮询——正好搭这班车。
            durableChanged |= PollEnvironmentTriggers(now);
            foreach (var r in _config.Reminders.ToList())
            {
                if (!_reminderStates.TryGetValue(r.Id, out var st)) { st = new ReminderState(); _reminderStates[r.Id] = st; }
                string firedBefore = st.LastFiredDate;
                var d = ReminderEngine.Decide(r, now, _startTime, st, _uptimeAtLaunch, _startupReminderIds.Contains(r.Id));
                if (d.Action == "arm" && d.Base is DateTime b)
                {
                    // 到点后延迟：'arm' 交 ArmAt 算 pendingFireAt（与事件触发共用同一份口径）。
                    // 固定延时不设上限（用户可能有意配多天错峰）。
                    ArmAt(r, st, b);
                }
                else if (d.Action == "fire")
                {
                    // 仅"时间型首触发"(本次 Decide 刚把 LastFiredDate 置为今天)在弹模态前先落盘，防被杀/断电后次日重复弹。
                    // 稍后/重复型触发不预存——它们清掉的 SnoozeUntil/NextRepeatAt 若在弹窗时被杀，宁可从盘上旧值恢复重弹也别丢。
                    // durable：此处的意义就是「先写成盘再弹窗」，不能走失败转后台的快路径（后台没落地就被杀等于没存）。
                    if (st.LastFiredDate != firedBefore) ReminderStateStore.Save(_statePath, _reminderStates, durable: true);
                    FireAndAdvance(r, st);
                    durableChanged = true;   // 稍后/重复又改了状态 → 循环末再存一次
                }
            }
            // 模态弹窗挂着时攒下的会话/电源事件（FireReminderEvent 忙时排队）在这儿兜底补发——
            // 事件自己的出口通常先清掉队列，这里保证最晚一个 tick 内一定补上。
            durableChanged |= DrainPendingEvents();
            if (durableChanged) ReminderStateStore.Save(_statePath, _reminderStates);
        }
        finally { _reminderTickBusy = false; }
    }

    // 触发一条提醒并推进它的运行态。计时器与事件触发共用同一份——事件那条路要是不推进状态，
    // 「稍后」和「没确认就催」在事件型任务上会静默失效（SnoozeUntil 落了盘却没人来看）。
    private void FireAndAdvance(Reminder r, ReminderState st)
    {
        var (action, snooze) = FireReminder(r);
        // 排下一步（稍后/重复）必须用弹窗返回后的时刻，不能用触发前的 now——弹窗是模态的，
        // 自动关闭设得长（如 30 分钟）时 now 已陈旧半小时，「稍后 10 分钟」会算出一个已经过去的
        // SnoozeUntil，下个 tick 立即重弹、再挂 30 分钟，成了永久模态循环；重复催促同理会背靠背连弹。
        var after = DateTime.Now;
        if (snooze is int m) ReminderEngine.Snooze(st, after, m);
        else ReminderEngine.UpdateAfterFire(r, after, action, st);
        // 「仅一次」触发完成（催促/稍后链都结束）→ 自动取消勾选：条目保留（想再用改个日期重新勾上），
        // 用完即焚会让误设时间没得救。时机必须在链结束后——立刻停用会被 Decide 的 !Enabled 早退掐死在途链。
        if (ReminderEngine.ShouldDisableAfterOnce(r, st))
        {
            r.Enabled = false;
            SaveConfig();
            _main?.RefreshReminderRows();
        }
    }

    // —— 事件触发（空闲 / 锁屏 / 解锁 / 唤醒 / 插拔电源 / 低电量）——
    // 会话与电源事件由 Windows 广播，订阅即得；空闲与电量没有通知可订，搭提醒计时器轮询。
    private readonly HashSet<string> _idleFired = new();         // 本轮离开已触发过的提醒 id（人回来即清）
    private readonly HashSet<string> _lowBatteryFired = new();   // 本轮低电量已触发过的（充回阈值以上即清）
    private bool? _lastOnAc;                                     // 上次读到的电源状态；null=还没读过（首轮只记基线）
    private string _lastEvent = "";
    private DateTime _lastEventAt = DateTime.MinValue;

    private void WireSystemEvents()
    {
        // 订阅这两个是纯白拿：Windows 本来就在广播，不订就只能靠轮询猜锁屏和唤醒。
        // 回调在 SystemEvents 自己的线程上来，FireReminderEvent 内部会切回 UI 线程。
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch { }   // 无交互式桌面等边角场景下会抛；事件触发失效即可，不该拖垮启动
    }

    private void UnwireSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }
    }

    // 只认 SessionLock/SessionUnlock 这两个字面意义上的「锁屏 / 解锁」。
    // 切换用户、远程连断（Console*/Remote*）刻意不并进来：那是另一件事，混进来会让「解锁时」在没人解锁时也跑。
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) FireReminderEvent("lock");
        else if (e.Reason == SessionSwitchReason.SessionUnlock) FireReminderEvent("unlock");
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) FireReminderEvent("resume");
    }

    // 忙时（模态提醒窗挂着）到达的会话/电源事件排在这儿，忙段一结束就补发。
    // 时间型提醒天然有下个 tick 重试；事件没有——丢一次就永远丢了。「锁屏提醒的弹窗还挂着，
    // 解锁提醒就被吞掉」正是这个功能的招牌场景（解锁打卡）被自己人挡掉。
    private readonly List<string> _pendingEvents = new();

    // OS 事件入口：切到 UI 线程 → 勿扰闸 → 去抖 → 忙则排队，闲则触发并顺带清队。
    private void FireReminderEvent(string ev)
    {
        if (!Dispatcher.CheckAccess()) { try { Dispatcher.BeginInvoke(() => FireReminderEvent(ev)); } catch { } return; }
        if (DndRemaining != null) return;    // 勿扰期间事件不触发（与计时器一个待遇），也不排队——没发生就是没发生
        var now = DateTime.Now;
        // 去抖：同一次锁屏/唤醒 Windows 可能连发两三条（唤醒尤其明显，Resume 常与 StatusChange 挨着来）。
        // 3 秒内的同名事件当成同一次；不同事件互不影响（锁屏紧接着唤醒是真的发生了两件事）。
        // 去抖放在排队之前：连发的重复通知该在门口拦掉，而不是在队里叠三份。
        if (_lastEvent == ev && (now - _lastEventAt).TotalSeconds < 3) return;
        _lastEvent = ev; _lastEventAt = now;

        // 忙 = 有模态提醒窗在挂（它的嵌套消息循环里本回调照样进来）。排队等补发，别丢弃——
        // 队内按事件名去重，同名挤在队里补发一次就够。
        if (_reminderTickBusy) { if (!_pendingEvents.Contains(ev)) _pendingEvents.Add(ev); return; }

        _reminderTickBusy = true;
        try
        {
            bool changed = FireEventNow(ev, now);
            changed |= DrainPendingEvents();   // 触发期间又攒下的（弹窗挂着时来的事件）当场补掉
            if (changed) ReminderStateStore.Save(_statePath, _reminderStates);
        }
        finally { _reminderTickBusy = false; }
    }

    // 补发忙时攒下的事件。调用方必须已持有 _reminderTickBusy；返回是否动过运行态。
    // 勿扰开始后剩下的先留在队里（不触发也不丢），勿扰结束后的第一个出口再补——
    // 它们发生在勿扰之前，按「忙时只是延后」的口径处理，和勿扰期间发生的事件（直接不算）不同。
    private bool DrainPendingEvents()
    {
        bool changed = false;
        while (_pendingEvents.Count > 0 && DndRemaining == null)
        {
            var ev = _pendingEvents[0];
            _pendingEvents.RemoveAt(0);
            changed |= FireEventNow(ev, DateTime.Now);
        }
        return changed;
    }

    // 挑出该响应本事件的提醒，逐条触发并推进状态。返回是否动过运行态（调用方负责落盘）。
    // 重入闸与勿扰由调用方把关——轮询那条路已经在计时器的闸里了，不能在这儿再上一次。
    // 配了「触发延迟」的不当场响，而是武装 PendingFireAt 交给计时器引爆（Decide 的事件门开在 pending
    // 之后正是为此）——否则编辑器收下了延迟、运行时却当场弹，等于静默吞掉一项用户设置。
    private bool FireEventNow(string ev, DateTime now)
    {
        bool changed = false;
        foreach (var r in _config.Reminders.ToList())
        {
            if (!ReminderEvent.ShouldFire(r, ev, now)) continue;
            FireOrArm(r);
            changed = true;
        }
        return changed;
    }

    // 事件到点的统一出口：配了「触发延迟」的武装 PendingFireAt 交计时器引爆，其余当场响。
    // 订阅类（FireEventNow）与轮询类（空闲/低电量）都走这儿——只改其一的话，
    // 「空闲 10 分钟后再等 5 分钟」这类配置会在轮询路径被静默吞掉，和评审 #5 同一个坑再挖一遍。
    private void FireOrArm(Reminder r)
    {
        var st = StateOf(r);
        if (r.DelaySeconds > 0 || r.RandomDelaySeconds > 0) ArmAt(r, st, DateTime.Now);
        else FireAndAdvance(r, st);
    }

    // 到点后延迟：固定 + 随机（错峰），算出 PendingFireAt。计时器 arm 分支与事件触发共用——
    // 随机上界 +1 处防 int.MaxValue 溢出（否则 _rng.Next 抛异常）；long 累加避免 int 溢出。
    private void ArmAt(Reminder r, ReminderState st, DateTime baseTime)
    {
        int rd = r.RandomDelaySeconds;
        long rand = rd > 0 ? _rng.Next(0, rd == int.MaxValue ? rd : rd + 1) : 0;
        st.PendingFireAt = baseTime.AddSeconds((long)r.DelaySeconds + rand);
    }

    private ReminderState StateOf(Reminder r)
    {
        if (!_reminderStates.TryGetValue(r.Id, out var st)) { st = new ReminderState(); _reminderStates[r.Id] = st; }
        return st;
    }

    // 轮询空闲时长与电源状态。计时器默认 30 秒一轮，对这两件事绰绰有余——
    // 「空闲 10 分钟」晚半分钟触发没人在意，而为它单开一个高频计时器纯属浪费。
    // 由 ReminderTick 在重入闸内调用，返回是否动过运行态。
    // fire=false（勿扰期间）：只维护基线（电源在线态、空闲/低电量的「本轮已触发」复位），一律不触发——
    // 勿扰的语义是「期间发生的事件不算数」，基线不跟着走的话，勿扰里拔的电源会在勿扰结束后被误判成「刚拔」。
    private bool PollEnvironmentTriggers(DateTime now, bool fire = true)
    {
        bool changed = false;
        int idle = Native.IdleTime.Minutes();
        if (idle < 1) _idleFired.Clear();   // 人回来了：整体复位，下次离开重新算一轮
        var (onAc, pct) = Native.PowerStatus.Read();
        // 首轮只记基线不触发：否则程序一启动就会把「现在接着电源」当成一次刚插上。
        if (fire && _lastOnAc is bool was && was != onAc) changed |= FireEventNow(onAc ? "acPlugged" : "acUnplugged", now);
        _lastOnAc = onAc;

        foreach (var r in _config.Reminders.ToList())
        {
            if (r.Trigger == "idle")
            {
                if (!fire) continue;   // 勿扰中不触发也不置位：空闲跨过勿扰结束仍在持续的，结束后照常响
                if (!ReminderEvent.ShouldFire(r, "idle", now)) continue;
                if (!ReminderEvent.IdleDue(r, idle, _idleFired.Contains(r.Id))) continue;
                _idleFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
            else if (r.Trigger == "lowBattery")
            {
                // 复位判定不看 Enabled/星期/勿扰：电量涨回去了就是涨回去了，跟这条现在该不该响无关。
                if (ReminderEvent.LowBatteryReset(r, pct, onAc)) { _lowBatteryFired.Remove(r.Id); continue; }
                if (!fire) continue;
                if (!ReminderEvent.ShouldFire(r, "lowBattery", now)) continue;
                if (!ReminderEvent.LowBatteryDue(r, pct, onAc, _lowBatteryFired.Contains(r.Id))) continue;
                _lowBatteryFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
        }
        return changed;
    }

    // 触发一条提醒：静默组 / 语音 / 通知 / 弹窗（是-否-稍后）。返回 (result, snoozeMinutes)。
    // preview=编辑器「预览这条」：被动提醒 toast 固定几秒自动消失（预览是试看，不该常驻堆屏）。
    private (string Action, int? Snooze) FireReminder(Reminder r, bool preview = false)
    {
        if (!string.IsNullOrWhiteSpace(r.SilentGroupId))
        {
            var g = ActionGroupResolver.Resolve(_config.ActionGroups, r.SilentGroupId);
            if (g != null && g.Enabled) RunGroupAsync(g);
            // 引用的组被删/被禁用时不再静默装作成功——夜间例程停摆却零反馈是最难察觉的故障；警告但仍记已处理（不重弹刷屏）。
            else WarnToast(Lf(g == null ? "Warn_SilentGroupMissing" : "Warn_SilentGroupDisabled", StepHelpers.Ellipsis(r.Message)));
            // 静默组无确认交互，跑一次即完结——返回 "ok"：让 UpdateAfterFire 停掉催促（否则配了 repeatMinutes
            // 会每 N 分钟把整组（可能含静音/关应用/锁屏）重跑），同时排下一轮「循环运行」（intervalMinutes）——
            // 静默任务的周期轮询正是靠这个返回值成立，改动它会悄悄弄断循环。
            return ("ok", null);
        }
        if (r.Speak) ReminderActions.Speak(r.Message);
        bool confirm = r.OnYes != null && r.OnYes.Type != "none";
        // 无动作、非重复 → 走右下角提醒卡片（不置顶抢视线）。时长遵循配置的「自动关闭」（0=常驻到点击）。
        if (!confirm && r.RepeatMinutes <= 0)
        {
            int secs = ReminderEngine.PopupTimeoutSeconds(r);   // 已在源头封顶 24h，secs*1000 不会越界
            int dur = secs > 0 ? secs * 1000 : (preview ? 5000 : 0);   // 预览固定 5s 自动关；真触发 0=常驻
            // 真触发按提醒 id 合并（同一条反复触发只占一张卡、标 ×N，不堆满右下角）；预览不带合并键——
            // 带了会并入同一条提醒还没人读的常驻卡片，并把它改写成 5 秒自动关，等于替用户把未读提醒销掉。
            // 预览也不留痕（log:false）：试看不该在托盘「最近通知」里冒充一次真投递。
            ShowToast(Strings.Get("Tray_ReminderTitle"), r.Message, Views.ToastLevel.Info, dur,
                key: preview ? null : ReminderToastKey(r), log: !preview);
            return ("ok", null);
        }
        // 弹窗路径。弹窗是模态的，其嵌套消息循环期间 _reminderTickBusy 挡住所有其他提醒——
        // 所以弹窗一律有超时（用户没设就兜底 60s），引擎不能没有下车点。
        // 超时（无人应答）的去向由「是否配了重复催促」决定：
        //   配了 → ""（超时未确认，交 UpdateAfterFire 按用户设的节奏续催，受 repeatUntil/MaxRepeats 约束）；
        //   没配 → 自动「稍后 10 分钟」——这类提醒没有任何续催机制，超时记成已处理或未确认都等于静默丢弃。
        // 「未应答」因此落在引擎的持久状态（SnoozeUntil 落盘，重启也不丢，删除提醒后由孤儿清理回收），
        // 而不是落在某个 UI 构件上——卡片会被挤掉/误点/比配置活得久，投递保证不能跟着 UI 的生死走。
        int psecs = ReminderEngine.PopupTimeoutSeconds(r);
        int timeoutSecs = psecs > 0 ? psecs : ReminderEngine.UnattendedPopupSeconds;
        int? autoSnooze = r.RepeatMinutes > 0 ? null : ReminderEngine.UnattendedSnoozeMinutes;
        var (act, snooze) = Views.ReminderPopupWindow.Show(_main, r.Message, confirm, timeoutSecs, autoSnooze);
        if (act == "yes") ReminderActions.RunOnYes(r.OnYes, _config.ActionGroups, g => RunGroupAsync(g), WarnToast);
        if (act == "snooze") return ("", snooze);
        return (act, null);
    }

    private static string ReminderToastKey(Reminder r) => "reminder:" + r.Id;

    // 「预览这条」：立即触发一次。预览不改任何运行状态——FireReminder 的返回值（含超时自动稍后）整个丢弃。
    // 与 tick 共用重入守卫：预览的模态弹窗期间 tick 不再往上叠新提醒窗；反向 tick 正忙时预览静默忽略
    // （用户面前已经有一个提醒模态窗了）。
    public void PreviewReminder(Reminder r)
    {
        if (_reminderTickBusy) return;
        _reminderTickBusy = true;
        try { FireReminder(r, preview: true); }
        finally { _reminderTickBusy = false; }
    }

    // 配置所在目录（state/run.log/error.log 都落在配置旁）：一处定义，5 个落点共用。
    private string CfgDir => Path.GetDirectoryName(_cfgPath) ?? _exeDir;

    // 警告气泡的便捷入口（RunOnYes 等回调用）。ShowToast 自身已全 try/catch 守护、可跨线程调。
    private void WarnToast(string msg) => ShowToast("Clockwork", msg, Views.ToastLevel.Warn);

    // 急停唯一出口：全局热键、托盘菜单、主窗口按钮三个入口都走这里，免得日后长出第四种写法。
    // 置位后由各运行线程在动作边界自查退出，长等待（启动延迟/等窗口）被 InterruptibleSleep 立刻打断。
    // 气泡是必须的：按下去当场没有任何反应，用户会以为按钮是坏的、然后接着乱按。
    public void RequestStop()
    {
        StopSignal.Request();
        // 再把「停」推给每个在途动作组的取消闸：它们的可中断延时只等自己那一个事件，不去等全局信号的
        // 内核句柄（那会引入两份状态、可能永久分歧）。不推的话，睡在轮间延迟里的组要睡满才发现急停。
        ActionGroupRunner.CancelAll();
        ShowToast("Clockwork", Strings.Get("Hotkey_Stopped"), Views.ToastLevel.Warn);
        // 不在这儿动急停按钮：它只由「有没有东西在跑」决定，而那个变化由运行闸(RunGate)统一广播。
        // 按下急停到真正停下之间最多几百毫秒，中间态没有观察价值，回执由上面这条气泡负责。
    }

    // —— 运行状态（主窗口急停按钮据此显示/隐藏）——
    // 三条运行路径（启动清单 / 单步 / 动作组，提醒的静默组走动作组）都过同一个闸，故这是唯一可信来源。
    public bool IsRunning => _runGate.Active > 0;

    public event Action? RunStateChanged;

    // —— 勿扰（暂停提醒）——旧版同款：会话级、不落盘；生效期间提醒 tick 整体跳过（含静默组），
    // 到期自动恢复；期间错过的提醒按宽限/错过必补的正常规则处理。
    private DateTime? _dndUntil;

    public TimeSpan? DndRemaining
    {
        get
        {
            if (_dndUntil is DateTime du)
            {
                var left = du - DateTime.Now;
                if (left > TimeSpan.Zero) return left;
                _dndUntil = null;   // 过期即清，菜单/判定两边都干净
            }
            return null;
        }
    }

    public void PauseReminders(int hours)
    {
        _dndUntil = DateTime.Now.AddHours(hours);
        ShowToast("Clockwork", Lf("Toast_DndOn", hours), Views.ToastLevel.Info);
    }

    public void ResumeReminders()
    {
        _dndUntil = null;
        ShowToast("Clockwork", Strings.Get("Toast_DndOff"), Views.ToastLevel.Info);
    }

    // 托盘菜单重建用：当前动作组列表（「运行：某组」项）。
    public IReadOnlyList<ActionGroup> Groups => _config.ActionGroups;

    // 配置文件路径（导入/导出用）。
    public string ConfigFilePath => _cfgPath;

    // 托盘「查看上次启动日志」：按系统关联打开 clockwork.run.log；还没跑过启动清单则提示。
    public void OpenRunLog()
    {
        var path = Path.Combine(CfgDir, "clockwork.run.log");
        if (!File.Exists(path)) { ShowToast("Clockwork", Strings.Get("Tray_NoLog"), Views.ToastLevel.Info); return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { WarnToast(ex.Message); }
    }

    // 单步「运行这一步」：后台跑（含循环 repeat），完成弹托盘气泡回执。
    private int _stepRunning;

    public void RunStepAsync(LaunchStep step, Window? owner = null)
    {
        // 消息步骤：在 UI 线程弹窗（是/否闸门 + 可选朗读/onYes），不走后台执行——否则会被当作未知类型告警。
        if (step.Kind == "message")
        {
            if (step.Speak) ReminderActions.Speak(step.Message);
            // owner 必须继续往下传：On-Yes 组若不传，闸门弹在编辑器前面、组本身抛到主窗口后面，
            // z-order 又变回这整个功能要修的那个老毛病（编辑器看着关掉了，其实主窗后面藏着一个提问框）。
            if (ShowGroupMessage(step, owner) == MsgResult.Yes)
                ReminderActions.RunOnYes(step.OnYes, _config.ActionGroups, g => RunGroupAsync(g, owner), WarnToast);
            return;
        }
        // 单飞守卫（旧版同款）：气泡回执要几秒才出，急着连点「运行」会把同一步跑两遍——上一次没完就忽略。
        if (Interlocked.Exchange(ref _stepRunning, 1) == 1) return;
        var selfPaths = new[] { _exePath };
        Task.Run(() =>
        {
            _runGate.Begin();
            try
            {
                var mark = StepRunner.RunStepMarkRepeat(step, a => ConfirmDestructive(a, owner), selfPaths);
                ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + mark.Mark, mark.Fail > 0 ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
            }
            finally { _runGate.End(); Interlocked.Exchange(ref _stepRunning, 0); }
        });
    }

    // 「运行整组」/提醒静默组/onYes 组/组编辑器试跑：后台跑动作组。返回本次运行的取消闸——
    // 组编辑器据此把「试跑」按钮就地变「停止」，并在关窗时收掉这次运行（不留孤儿）。
    // onDone：跑完（或被取消/异常）后在 UI 线程回调一次，用于把按钮翻回去。
    //
    // 竞态窗口：本方法把 Task 派下去就返回，EnterTopLevel 的登记要等后台线程真正跑到那一行才发生——
    // 派发与登记之间有个空窗，不是「几微秒」那种可以忽略的窗口（ToggleGroupByHotkey 的连按去重正是
    // 撞在这个窗口上，那边有更完整的说明）。落进空窗期间查「是否已顶层在跑」，看到的还是登记前的状态。
    public RunCancel RunGroupAsync(ActionGroup group, Window? owner = null, Action? onDone = null)
    {
        // 快照与 deps 都在调用线程（UI）上先建好，不把活对象带进后台：
        //   · SnapshotForRun 复制步骤列表——后台 foreach 组步骤时，UI 线程若在改同一个组（删除守卫的
        //     联动清理会就地改其他组的 Steps），背景枚举撞见活列表被改写会抛「集合已修改」。这条注释
        //     曾经只是给「万一手快」的历史兜底；组编辑器上线试跑（本方法的 owner/onDone 参数正是为它加的）
        //     之后，它是第一个能在运行进行中还继续编辑同一份步骤列表的调用方——背景枚举撞见活列表被
        //     UI 线程改写从「理论风险」变成「必然发生」，这份注释因此从历史说明升级成不可删的强制约束。
        //   · BuildGroupDeps 同理在 UI 线程取 _config.ActionGroups 的快照，道理一样（见其内部注释）。
        var snap = group.SnapshotForRun();
        var deps = BuildGroupDeps(owner);
        Task.Run(() =>
        {
            // _runGate.Begin() 放进 try：它（含广播 ActiveChanged 的订阅方）与 EnterTopLevel 理论上都
            // 可能抛。若抛在 try 外，finally 不会跑，_runGate.End() 和 onDone 都会被跳过——编辑器的
            // 「试跑」按钮永远卡在「停止」，主窗口的急停按钮也永远显示着。挪进来后无论哪一步出岔子，
            // 配对的收尾都保证会跑。owned 先置 false：EnterTopLevel 没跑到或抛出时，finally 不会误调
            // ExitTopLevel 去清一个从未登记过的顶层运行。
            bool owned = false;
            try
            {
                _runGate.Begin();
                // 只有这一层登记为「顶层运行」——即热键/试跑停止按钮能取消的那一次。嵌套子组与它共用
                // 同一个闸但不登记，否则子组的热键/引用会把整条链（含毫不相干的父组）一起掐掉。
                owned = ActionGroupRunner.EnterTopLevel(snap.Id, deps.Cancel);
                // 顶层入口：结局有意丢弃。Aborted 是用户自己在确认框上点的「否」（他已经知道了，再弹一张
                // 卡是噪音）；Skipped 是同组已在跑的单飞去重——热键连按/循环任务撞上上一轮时本就该静默忽略。
                // 嵌套引用那层不同：那里的 Skipped 意味着配置有环，必须发声（见 RunGroupStep）。
                _ = ActionGroupRunner.RunGroup(snap, deps);
            }
            catch (Exception ex) { WarnToast(Lf("Mark_Exception", ex.Message)); }
            finally
            {
                if (owned) ActionGroupRunner.ExitTopLevel(snap.Id, deps.Cancel);
                _runGate.End();
                // 回调整体兜住：调度器正在关闭时 Invoke 会抛，逃出去会变成 Task 里的未观察异常。
                if (onDone != null) { try { Dispatcher.Invoke(onDone); } catch { } }
            }
        });
        return deps.Cancel;
    }

    // owner：本次运行的弹窗归属窗口（null=主窗口）。嵌套子组与 onYes 组共用同一份 deps，
    // 故 owner 自动传遍整条引用链，不必在每一层再传一次。
    private GroupDeps BuildGroupDeps(Window? owner = null)
    {
        var selfPaths = new[] { _exePath };
        var groups = _config.ActionGroups.ToList();   // 组列表快照（UI 线程取）：后台 Resolve 不再枚举 UI 正在增删的活列表
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            // 把本次运行的取消闸一路传进步骤执行层：等窗口 / 置前重试 / 置前延时都可能挂几秒到几十秒，
            // 只查全局急停的话，用户取消了动作组，这些步骤过一会儿照样把窗口拽到前台、把按键打进去。
            RunStep = s => StepRunner.InvokeStepAction(s, a => ConfirmDestructive(a, owner), selfPaths, deps.Cancel),
            ShowMessage = s => ShowGroupMessage(s, owner),
            // onYes 组的结局也丢弃：ReminderActions.RunOnYes 是 Action<ActionGroup> 契约（提醒弹窗路径共用），
            // 且语义上 onYes 是「是」分支的副作用出口——它内部的确认框属于那条子流程，把它的中止回灌成父组中止，
            // 会变成「点了是反而整组停了」，比现状更难解释。
            RunOnYes = s => ReminderActions.RunOnYes(s.OnYes, groups, g => { _ = ActionGroupRunner.RunGroup(g.SnapshotForRun(), deps); }, WarnToast),
            Speak = ReminderActions.Speak,
            OnStepError = (s, ex) => LogGroupStepError(s, ex),
            OnStepSkipped = (s, reason, benign) => LogGroupStepSkipped(s, reason, benign),
            Budget = new RunBudget(() => WarnToast(Strings.Get("Warn_RunBudget"))),
            // 组内嵌套「动作组」步骤：跑引用组的快照（防运行中被编辑/清理）。三种「这次没跑」的结局——
            // 目标缺失、目标已禁用、重入（环引用/已在运行）——都必须发声：同一份坏配置在启动清单里有
            // 「⚠ 找不到动作组」可查，热键/计划任务这条（正是无人值守跑的那条）以前却什么都不说。
            // 解析与分类（措辞、良性判定）在 ActionGroupResolver.ResolveForRun/Reentrant 里（WPF 之外，可单测）；
            // 这里只管接结果转发给 OnStepSkipped——纯接线。三种都返回 Skipped：目标不存在/被禁/在跑，
            // 下一次迭代结论完全相同，让上层引用轮次立刻收手（否则 Repeat=999 就是 999 条重复告警）。
            RunGroupStep = s =>
            {
                var target = ActionGroupResolver.ResolveForRun(groups, s.GroupId);
                if (target.Skip != null) { deps.OnStepSkipped(s, target.Skip.Reason, target.Skip.Benign); return GroupRunResult.Skipped; }
                var res = ActionGroupRunner.RunGroup(target.Group!.SnapshotForRun(), deps);
                if (res == GroupRunResult.Skipped)
                {
                    var reentrant = ActionGroupResolver.Reentrant();
                    deps.OnStepSkipped(s, reentrant.Reason, reentrant.Benign);
                }
                return res;
            },
        };
        return deps;
    }

    // 动作组内某步抛异常：记一笔到错误日志并弹一次托盘气泡，随后整组继续（不静默中止）。
    // 仅用于 RunStep 真正抛出的异常——「这次没跑」但没有异常的情况（嵌套组引用缺失/禁用/重入）走 LogGroupStepSkipped。
    private void LogGroupStepError(LaunchStep step, Exception ex)
    {
        var logPath = Path.Combine(CfgDir, "clockwork.error.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 动作组步骤失败（已跳过、整组继续）: {StepDisplay.StepSummary(step)} — {ex.Message}\r\n"); } catch { }
        // 带合并键（按步骤摘要分桶）：同一步在多轮/多次引用里反复失败时叠加计数而不是堆一摞 12 秒的卡片
        // （整组 Repeat 仍会让同一步失败很多次）；不同步骤的失败仍各占一张，不会互相盖掉。
        ShowToast("Clockwork", Lf("Mark_Exception", StepDisplay.StepSummary(step)), Views.ToastLevel.Warn,
                  key: "groupstep:" + StepDisplay.StepSummary(step));
    }

    // 动作组内某步被跳过（没有异常，如嵌套组引用的目标缺失/已禁用/重入）：记一笔到错误日志（措辞用「已跳过」
    // 而非「失败」）并弹一次托盘气泡，气泡文案带上具体原因——不能只报步骤摘要，否则用户只能打开日志文件才知道
    // 为什么。已禁用是正常配置状态（与 LaunchSequence 对同一条件的判断口径一致），用 Info；其余用 Warn。
    private void LogGroupStepSkipped(LaunchStep step, string reason, bool benign)
    {
        var logPath = Path.Combine(CfgDir, "clockwork.error.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 动作组步骤已跳过（整组继续）: {StepDisplay.StepSummary(step)} — {reason}\r\n"); } catch { }
        // 合并键与 LogGroupStepError 分桶（groupskip: vs groupstep:）：同一步骤上一次是真异常这一次只是良性
        // 跳过（或反过来），两张卡不该互相盖掉——用户需要分别看到「确实失败过」与「最近一次只是被跳过」。
        ShowToast("Clockwork", Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), reason),
                  benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn,
                  key: "groupskip:" + StepDisplay.StepSummary(step));
    }

    // 卡片形态 message 的投递（启动清单路径专用）。返回 ✓——卡片弹出即算完成，没有可失败的部分。
    // 这条路径必须自己播报：ActionGroupRunner 的 message 分支会先调 deps.Speak，而启动清单
    // （LaunchSequence 的顶层与组展开）不经过那个分支，只调注入的 stepMark。
    private StepMark ShowStepCard(LaunchStep s)
    {
        if (s.Speak) ReminderActions.Speak(s.Message);
        ShowToast("Clockwork", s.Message, Views.ToastLevel.Info,
                  s.PopupSeconds > 0 ? s.PopupSeconds * 1000 : 0, key: StepCardKey(s));
        return new StepMark("✓", 0, 0);
    }

    // 卡片合并键：同一句话在整组重复轮次里合成一张 ×N，而不是叠一摞。
    private static string StepCardKey(LaunchStep s) => "stepmsg:" + (s.Message ?? "");

    // 动作组 message 步骤的呈现。三种形态见 StepHelpers.MessageFormOf。
    // 卡片：弹完立即返回 Ok（不拦路），调用方（ActionGroupRunner）照常扣预算、查取消、跑下一步。
    // 播报不在这里做——ActionGroupRunner 的 message 分支和 RunStepAsync 都已在调用前处理。
    private MsgResult ShowGroupMessage(LaunchStep step, Window? owner = null)
    {
        var form = StepHelpers.MessageFormOf(step);
        if (form == MessageForm.Card)
        {
            ShowToast("Clockwork", step.Message, Views.ToastLevel.Info,
                      step.PopupSeconds > 0 ? step.PopupSeconds * 1000 : 0, key: StepCardKey(step));
            return MsgResult.Ok;
        }
        return Dispatcher.Invoke(() =>
        {
            var win = owner ?? _main;
            if (form == MessageForm.Confirm)
                return Views.BrandDialog.Confirm(win, Strings.Get("Confirm_Title"), step.Message) ? MsgResult.Yes : MsgResult.No;
            Views.BrandDialog.Info(win, "Clockwork", step.Message);
            return MsgResult.Ok;
        });
    }

    private void NotifyRunResult(LaunchRunResult r)
    {
        var s = r.Summary;
        // 截停要先判：撞步数上限时 Stopped 也是 true（循环因此提前退出），但告诉用户「已手动停止」是在说
        // 一件他没做过的事——真相只躺在他得手动打开的日志里。与 WriteLog 的 stopHdr 用同一优先级。
        if (s.Truncated) ShowToast("Clockwork", Strings.Get("Warn_RunBudget"), Views.ToastLevel.Warn);
        else if (s.Stopped) ShowToast("Clockwork", Lf("Tray_LaunchStopped", s.Total), Views.ToastLevel.Warn);
        else if (s.Fail > 0) ShowToast("Clockwork", Lf("Tray_LaunchWarn", s.Total, s.Fail), Views.ToastLevel.Warn);
    }

    // 品牌化非模态通知（右下角 toast，替代系统托盘气泡）。自动切到 UI 线程；整体兜底绝不抛。
    // 后台线程(动作组/单步)调用时 Dispatcher.Invoke 遇正在关闭的调度器会抛(TaskCanceled/InvalidOperation)，
    // 必须一并吞掉——否则会从 OnStepError 逃出、掀掉动作组剩余步骤(收工/睡前组的锁屏/关机就不执行了)。
    // 分级默认时长：运行回执看过就算；警示是「你需要知道」的（配置写盘失败、热键被占、动作组步骤异常），
    // 用同一个 5 秒等于错过就没了。durationMs<0=按级别取默认，0=常驻到点击，>0=显式毫秒。
    private const int InfoToastMs = 5000;
    private const int WarnToastMs = 12000;

    // log=false：不写「最近通知」（预览等试看场景——留痕会让托盘历史出现和真投递无法区分的幻影条目，
    // 反复预览还会把 8 格环形缓冲里的真条目全部挤掉）。
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
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
            if (Dispatcher.CheckAccess()) Post();
            else Dispatcher.Invoke(Post);
        }
        catch { }
    }

    // 托盘「最近通知」：回看被点掉 / 被挤掉 / 已自动消失的卡片（会话级，不落盘）。
    private readonly NotificationLog _notifications = new();

    public IReadOnlyList<NotificationEntry> RecentNotifications => _notifications.Recent;

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

    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    // 破坏性系统命令（重启/关机/注销）的确认框。owner 决定它弹在谁前面——组编辑器试跑时必须是编辑器，
    // 否则确认框藏在模态编辑器后面，用户看着「卡住了」而实际是有个框在等他。
    private bool ConfirmDestructive(string action, Window? owner = null)
        => Dispatcher.Invoke(() => Views.BrandDialog.Confirm(
            owner ?? _main, Strings.Get("Confirm_Title"), Lf("Confirm_Destructive", action), Views.ToastLevel.Warn));

    // 配置存盘（原子写）。ViewModel 增删改移时回调。持续写失败（OneDrive/杀软锁死超过重试）不再静默吞——
    // 界面看着已保存、重启全回退是静默数据丢失，至少弹个警告让用户知道改动只在内存里。
    public void SaveConfig()
    {
        if (_configSuperseded) return;   // 内存里的 _config 已作废，任何回写都是「无声还原」——见 MarkConfigSuperseded
        try { ConfigStore.Write(_config, _cfgPath); }
        // 写盘失败=界面看着已保存、重启全回退的静默数据丢失。这条不给它自动消失：常驻到用户点掉。
        // 同键合并：连续几次保存失败只留一张（标 ×N），不至于把屏幕糊满。
        catch (Exception ex) { ShowToast("Clockwork", Lf("Warn_SaveConfigFail", ex.Message), Views.ToastLevel.Warn, 0, key: "saveconfig"); }
        // 组增删改/启停/改键都走此保存——热键跟着当前配置即时重建。
        // 捕捉挂起期间跳过（改急停键的保存正发生在挂起中）：此刻重建会让组抢注急停的新组合；
        // 捕捉一定以 ResumeHotkeys 收尾，那里会按「急停先、组后」的次序统一重建。
        if (!_hotkeysSuspended) RebindGroupHotkeys();
    }

    // 导入配置：新配置已原子写入磁盘，本实例内存里的 _config 就此作废——它靠重开新实例重读生效。
    // 从此禁止任何回写。否则「写盘 → 弹『已导入』确认框 → 重开自身」中间那段模态期间，提醒计时器照常在走
    //（DispatcherTimer 在嵌套消息循环里不会停，这正是 _reminderTickBusy 存在的原因），一条「仅一次」提醒
    // 触发完毕会自动取消勾选并调 SaveConfig，把旧 _config 覆盖回刚导入的文件——用户点完确定重启，
    // 导入无声还原。RelaunchForLanguage 失败时的模态提示同理，一并被这道闸挡住。
    public void MarkConfigSuperseded() => _configSuperseded = true;

    // 编辑提醒会换新 id（借此重置「今天已弹」态），但两项在途的耐久投递不该丢：
    //   SnoozeUntil    —— 用户明确要求的一次推迟；
    //   NextIntervalAt —— 「循环运行」的下一轮。它与 SnoozeUntil 同属落盘状态（见 ReminderState），
    //                     漏迁的后果是：改一下文案，跑了一上午的「每 30 分钟」当场停到明天——
    //                     换新 id 后 LastFiredDate 也清空了，当天首发窗口早已过去，Decide 一路返回 none。
    // 只在新配置仍配了循环时迁 NextIntervalAt：在编辑里把循环关掉的，不该再多跑一轮（静默组会整组重跑）。
    // 不迁 LastFiredDate——「编辑即可当天重弹」正是换 id 的本意。迁完即耐久落盘，防编辑后崩溃丢状态。
    public void MigrateReminderState(string oldId, Reminder updated)
    {
        var newId = updated.Id;
        if (string.IsNullOrEmpty(oldId) || oldId == newId) return;
        // 「启动时就存在」资格随编辑迁移：否则编辑过的提醒 existedAtStartup=false，「错过必补」当天失效。
        if (_startupReminderIds.Remove(oldId)) _startupReminderIds.Add(newId);
        if (!_reminderStates.TryGetValue(oldId, out var old)) return;
        var carryInterval = updated.IntervalMinutes >= 1 ? old.NextIntervalAt : null;
        if (old.SnoozeUntil != null || carryInterval != null)
        {
            if (!_reminderStates.TryGetValue(newId, out var st)) { st = new ReminderState(); _reminderStates[newId] = st; }
            st.SnoozeUntil = old.SnoozeUntil;
            st.NextIntervalAt = carryInterval;
        }
        // PendingFireAt 有意不迁：它按旧时间算出，编辑就是要按新配置重新判定。
        _reminderStates.Remove(oldId);   // 旧 id 已不被任何提醒引用，成孤儿；显式移除并落盘
        ReminderStateStore.Save(_statePath, _reminderStates);
    }

    // AUMID 的品牌信息落盘（注册表项 + 图标文件）。只在通知弹出时才被系统读到，与启动无关，
    // 故整个搬到后台线程、由 OnStartup 末尾 fire-and-forget 调用（见那里的注释）。
    // 进程内的 AUMID 声明不在这儿——它必须在建 UI 之前同步做完，留在 OnStartup 里。
    private void RegisterAumidBranding()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
            key?.SetValue("DisplayName", "Clockwork");
            // 通知在操作中心的品牌图标。不能指向 exe 旁的 assets\logo.ico——单文件发布那里没有；
            // 把内嵌图标解压到 %LOCALAPPDATA%\Clockwork\logo.ico 再注册，toast 分组头才带应用图标。
            var ico = ExtractBrandIcon();
            if (ico != null) key?.SetValue("IconUri", ico);
        }
        catch { }
    }

    // 把内嵌 logo.ico 解压到 LocalAppData 的稳定路径并返回；已存在(非空)则直接复用，不重复写。
    private static string? ExtractBrandIcon()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clockwork");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "logo.ico");
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            var res = System.Windows.Application.GetResourceStream(new Uri("logo.ico", UriKind.Relative));
            if (res == null) return null;
            using var fs = File.Create(path);
            res.Stream.CopyTo(fs);
            return path;
        }
        catch { return null; }
    }

    private void EnsureConfigFile()
    {
        if (File.Exists(_cfgPath)) return;
        var example = Path.Combine(_exeDir, "clockwork.settings.example.json");
        try
        {
            if (File.Exists(example)) File.Copy(example, _cfgPath, false);
            else ConfigStore.Write(RootConfig.Default(), _cfgPath);
        }
        catch { }
    }

    // 追加错误日志，返回日志路径。任何线程可调（UnobservedTaskException 在终结器线程上进来）。
    private string LogError(Exception? ex)
    {
        var logPath = Path.Combine(CfgDir, "clockwork.error.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n"); } catch { }
        return logPath;
    }

    private void ShowCrash(Exception? ex)
    {
        var logPath = LogError(ex);
        // 崩溃兜底：先试品牌对话框；若它自身(依赖主题/资源)也失败，退回最稳的原生 MessageBox。
        var body = Lf("Crash_Body", ex?.Message ?? "", logPath);
        var title = Strings.Get("Crash_Title");
        try { Views.BrandDialog.Warn(null, title, body); }
        catch
        {
            try { System.Windows.MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
        }
    }
}
