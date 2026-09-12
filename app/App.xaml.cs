using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
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
    private EventWaitHandle? _runEvent;        // --run-group 的跨实例投递信号（配套请求文件见 RunRequest）
    private RegisteredWaitHandle? _showWait;   // 持引用防注册等待被回收；随进程退出
    private RegisteredWaitHandle? _runWait;    // 同上，监听 _runEvent
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

        // 开发期自查（--smoke / --shots / --selftest，见 DevChecks.cs）：在单实例之前——托盘里正在用的实例照常工作。
        if (e.Args.Contains("--smoke") || e.Args.Contains("--shots") || e.Args.Contains("--selftest")
            || e.Args.Contains("--screenshots") || e.Args.Contains("--hookprobe")) { RunDevCheck(e.Args); return; }

        if (!ClaimSingleInstance(e.Args)) return;     // 已有实例在跑：叫醒它，自己退出

        ResolveSelfPaths();
        DeclareAppUserModelId();
        LoadConfig();
        LoadReminderState();
        ApplyUiCulture();
        ApplyTheme(_config.Settings.Theme);

        BuildShell();          // 主窗口 + 托盘
        // 面板页迁移的账同理——它是一次**结构性**改写（动作列表少几个、托盘少几行），
        // 静默完成的话用户只会以为程序把配置弄坏了。逐条列出来，让他知道东西搬到哪儿去了。
        if (Core.ConfigStore.LastMigrationLog.Count > 0)
            ShowToast(Strings.Get("Mig_Title"),
                      string.Join(Environment.NewLine, Core.ConfigStore.LastMigrationLog), Views.ToastLevel.Info);
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

    // 命令行解析在 Core.RunRequest.ParseGroupArg（纯逻辑、可测），这里只留调用。

    // 置信号的兜底包装：命名事件在受限令牌 / ACL 受限下可能为 null 或抛。
    private static bool Set(EventWaitHandle? h)
    {
        try { return h != null && h.Set(); } catch { return false; }
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
            _runEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RunRequest.EventName);
            if (createdNew) return true;

            // --run-group：老实例还在，把请求转交给它，自己不显示窗口也不接管。
            // 必须在下面那句 _showEvent.Set() 之前分流——外部触发一个动作组不该顺带把设置窗口弹出来，
            // 那是「计划任务半夜跑一个静默组」这条用法上最刺眼的副作用。
            if (RunRequest.ParseGroupArg(args) is string want)
            {
                // 写不进去就明说：静默退出会让调用方（计划任务 / 快捷方式）以为跑过了。
                // 退出码给非 0，脚本里 if errorlevel 判得到。
                Environment.ExitCode = RunRequest.Write(want) && Set(_runEvent) ? 0 : 2;
                Shutdown();
                return false;
            }

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
        // 陈旧的「今天不再」同理清掉。它是耐久内容（Save 的跳空判断认它），不扫的话一月里跳过一次的提醒
        // 会在 clockwork.state.json 里留一行到天荒地老，且每次耐久写盘都跟着重新序列化一遍。
        foreach (var kv in _reminderStates)
            if (kv.Value.SkippedDate.Length > 0 && string.CompareOrdinal(kv.Value.SkippedDate, ReminderEngine.DateKey(DateTime.Now)) < 0)
            { kv.Value.SkippedDate = ""; cleaned = true; }
        if (cleaned) ReminderStateStore.Save(_statePath, _reminderStates);
        // 过期的「快速提醒」清干净。它靠触发后自删收尾，可机器要是整天关着、错过了那个日期，
        // once + 已过的 onceDate 就再也触发不了，于是永远删不掉——一条谁都不认识的死行留在定时任务里。
        // 必须赶在建 MainWindow（列表 VM 由 _config.Reminders 建行）之前做，否则又得走 VM 删。
        _config.Reminders.RemoveAll(x => x.Temporary && string.CompareOrdinal(x.OnceDate, ReminderEngine.DateKey(DateTime.Now)) < 0);
        // 有意不在这里 SaveConfig：它的失败路径要弹 toast=建窗口，而本方法跑在 ApplyUiCulture 之前，
        // 那里的 RTL 元数据覆盖硬性要求「建任何窗口之前」，一旦被抢跑就抛异常、整个启动流程断在半路
        // （ShutdownMode.OnExplicitShutdown 下进程还活着，却没有托盘也没有窗口）。
        // 从内存里摘掉就够了：列表 VM 稍后按 _config 建行，看不到这些死行；盘上那几行等下一次
        // 任何配置改动顺手带走，带不走也只是下次启动再清一遍，无害。
        _startupReminderIds = new HashSet<string>(_config.Reminders.Select(x => x.Id));
    }

    // 换主题＝把整个资源栈重搭一遍（调色板 + Theme.xaml），而不是只替换调色板那一份。
    //
    // **只替换调色板试过，不成立**，实测记在这儿免得再走一遍：
    // 画笔的 Color 是 DynamicResource 指过去的，理论上换掉字典就该全体更新。
    // 第一次切（深→浅）确实全对；再切回来（浅→深）就散了——表面还是浅色、文字已经变回深色主题的纸白，
    // 混成一屏读不出字的东西。也就是说一部分画笔更新了、一部分没有，WPF 在这条路上不保证一致。
    // 与其去猜是模板缓存还是 Freezable 冻结，不如换一条有确定性的：整份重新解析，
    // 拿到的是一批全新的画笔，颜色必然齐整。
    //
    // 代价：**已经开着的窗口不会跟着变**——它们用 StaticResource 抓住的是旧的画笔实例。
    // 所以用户在设置里换主题时走重启（与换语言同一条路，见 Theme_Changed）；
    // 而截图 harness 是切完再建窗口，正好落在「新窗口」这一侧，不受影响。
    internal static void ApplyTheme(string? theme)
    {
        bool dark = Themes.IsDark(theme, Native.SystemTheme.PrefersLight());
        var dicts = Current.Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new ResourceDictionary { Source = new Uri(dark ? "Palette.Dark.xaml" : "Palette.Light.xaml", UriKind.Relative) });
        dicts.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        Native.DarkWindow.Dark = dark;
        Native.DarkWindow.RefreshTitleBars();
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
        _main = new MainWindow(_config, SaveConfig, MigrateReminderState,
                               r => PeekState(r)?.SkippedDate == ReminderEngine.DateKey(DateTime.Now));
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
        ApplyMouseHook();   // 默认关，所以常态下这里什么都不做

        // 跨实例「显示窗口」信号：事件驱动等待（原每秒轮询）。AutoReset 事件被 Set 才回调，常态零唤醒；
        // executeOnlyOnce:false = 每次信号都再等下一次。（单实例对象创建失败时 _showEvent 为 null，跳过。）
        if (_showEvent != null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, executeOnlyOnce: false);
        // --run-group 的投递信号：形状与上面那条完全一样，只是醒来后去取请求文件而不是显示窗口。
        if (_runEvent != null)
            _runWait = ThreadPool.RegisterWaitForSingleObject(_runEvent,
                (_, _) => Dispatcher.BeginInvoke(DrainRunRequest), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // 取走并执行一次外部投递的「跑这个组」请求。
    // while 而不是 if：AutoReset 事件在两次 Set 挨得极近时只会唤醒一次（第二次 Set 落在同一个已置位
    // 状态上），留在文件里的那条请求就再没人来取了。循环取到空为止，这条竞态就不存在。
    // 实际上请求文件只存一条（后写覆盖先写），循环更多是防「取的时候又来一条」。
    private void DrainRunRequest()
    {
        // 循环要**以内容变化为界**，不能只看「还取得到」。Take() 是「读出来再删掉」，
        // 而它在删不掉的时候仍然把内容交出来（只读属性、被杀软/同步盘锁住、拒绝删除的 ACL——
        // 实测读得到而删会抛 Win32 错误 5）。那时 while 就成了死循环：UI 线程再也回不来
        //（托盘、热键、提醒全停），而那个组被一遍遍地重跑——它里面可能有关机、注销、发送按键。
        // 取到同一条就收手：请求文件只存一条（后写覆盖先写），重复出现只可能是没删掉。
        string? last = null;
        while (RunRequest.Take() is string want)
        {
            if (want == last) break;
            last = want;
            RunGroupByName(want);
        }
    }

    // 按名称或 id 找组并跑。找不到 / 被禁用都如实报——外部触发的场合没人盯着屏幕，
    // 静默失败会让人以为快捷方式坏了，然后去改一个根本没问题的命令行。
    // 名称匹配大小写不敏感且忽略首尾空格：命令行里的名字是手打的，不该因为大小写不对就不跑。
    public void RunGroupByName(string nameOrId)
    {
        var want = (nameOrId ?? "").Trim();
        var g = _config.ActionGroups.FirstOrDefault(x => x.Id == want)
             ?? _config.ActionGroups.FirstOrDefault(x => string.Equals(x.Name?.Trim(), want, StringComparison.OrdinalIgnoreCase));
        if (g == null) { WarnToast(Lf("Warn_GroupNotFound", want)); return; }
        if (!g.Enabled) { WarnToast(Lf("Warn_GroupDisabled", g.Name)); return; }
        RunGroupAsync(g, unattended: true);   // 外部触发（计划任务 / AHK / 快捷方式），同上面那句注释
    }

    private void ShowInitialWindow(string[] args)
    {
        bool forceShow = args.Contains("--show");   // 语言切换重启后：强制显示窗口，忽略「启动时最小化」
        if (args.Contains("--boot"))
        {
            _main!.ShowInTaskbar = false;   // 自启：不显窗、只入托盘
            // 逃生口先判：跳过时连计时器都不排，别给自己留一条 800 毫秒后照样开跑的暗路。
            // 直接把 why 送进 toast：SkipStartupReason 返回的**已经是本地化好的整句**（见其注释）。
            // 原来这里又包了一层 Lf("Warn_StartupSkipped", why)，而那个键在 18 份 resx 里一个都没有 ——
            // Strings.Get 的兜底是「找不到就返回键名」，于是这条救援提示会显示成字面的
            // 「Warn_StartupSkipped」，而且 {0} 不存在、路径被 string.Format 丢掉。
            // 这恰恰是用户已经进不去、正靠删文件自救的那条路径：他最需要知道该删哪个文件。
            if (SkipStartupReason() is string why) WarnToast(why);
            else
            {
                var bt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                bt.Tick += (s, _) => { bt.Stop(); RunLaunchAsync(true); };
                bt.Start();
            }
        }
        else if (_config.Settings.StartMinimized && !forceShow)
        {
            _main!.ShowInTaskbar = false;
        }
        else
        {
            _main!.Show();
        }

        // --run-group 且托盘里还没有实例：本进程就是那个实例，请求由自己执行。
        // 不跑完就退——这是个托盘程序，用户（或计划任务）刚把它拉起来，退掉等于「第一次触发不算数、
        // 第二次才生效」，那是最难自查的一种偶发。要一次性执行请让常驻实例先跑着。
        if (RunRequest.ParseGroupArg(args) is string want) Dispatcher.BeginInvoke(() => RunGroupByName(want), DispatcherPriority.ApplicationIdle);

        // 这次不显示 ≠ 不会被显示。开机自启之后，当天第一次双击托盘要现场把整棵可视树建出来
        // （现为 6 个 Tab、5 个 DataGrid、1119 行 Theme.xaml 的模板）。
        // 141 毫秒 / 跑过一次后 15 毫秒 是这棵树还是 5 Tab、4 DataGrid、943 行 Theme 时的实测值，
        // 之后只长不缩，所以现在只会更久——这条结论的方向没变，别把那两个数字当成当前读数。
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
        // 全局钩子必须显式摘掉：留着不摘，退出过程中任何一次鼠标移动都会回调进一个正在拆的对象。
        _mouseHook?.Dispose();
        _mouseHook = null;
        CloseTrail();   // 退出时手可能正按着：不收笔，那条线会跟着桌面一直留到重启
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

    /// <summary>开机清单的逃生口标记文件名（放在配置旁）。存在即跳过自动运行，直到用户删掉它。</summary>
    public const string SkipStartupFileName = "clockwork.disable-startup";

    // 这一次自动运行该不该跳过。返回 null=照常跑，非 null=已经本地化好的整句话（直接进 toast）。
    // 返回整句而不是「原因片段」：两条口子要说的下一步完全不同（一条要你去删文件、一条是本次有效），
    // 塞进同一个 {0} 占位的话，句子只能写成两边都不得罪的废话。
    //
    // 为什么需要这个口子：本程序会把自己注册成登录计划任务，然后 --boot 自动跑整份清单。
    // 清单里只要有一步会抢焦点、锁屏、注销或关机，登录就成了一个闭环——每次进桌面都被同一步顶出去，
    // 而修正它需要先进得去。急停键不算退路：它要求你在那一步动手之前抢先按到，且程序已经起来了。
    //
    // 两条口子刻意都留着，因为它们救的是两种不同的困境：
    //   · 标记文件 —— 你还能进桌面，只是不想让清单再跑（调试某一步、今天不想开那一堆软件）。
    //     持久生效，与 Quicker 的 qk_disable_auto.txt 同一思路；不自动删是有意的，
    //     自动删就等于「只保一次」，而人往往要连试几次才找到那一步。
    //   · 开机时按住 Shift —— 你已经进不去了。这时没有任何机会去创建文件，
    //     唯一还能用的输入就是登录那一刻按住一个键。这条不持久，本次有效。
    // 只有文件那条会写进 toast 的原因串里带路径——它需要用户回头去删；Shift 那条说完就完了。
    private string? SkipStartupReason()
    {
        try
        {
            var flag = Path.Combine(CfgDir, SkipStartupFileName);
            if (File.Exists(flag)) return Lf("Warn_StartupSkippedFile", flag);
        }
        catch { }   // 配置目录读不了：当成没有标记，照常跑（同 ShiftHeld 的兜底方向）
        return Win32.ShiftHeld() ? Strings.Get("Warn_StartupSkippedShift") : null;
    }

    // 后台跑启动清单（手动重跑或 --boot）。并发守卫防重入交错。
    public void RunLaunchAsync(bool boot)
    {
        if (Interlocked.Exchange(ref _launchRunning, 1) == 1) return;   // 已有一次在跑，忽略
        Native.WindowManager.MarkForegroundBaseline();   // 「恢复活动窗口」要回的就是此刻这个
        var cfg = _config.SnapshotForRun();   // 快照列表：开机延迟期间 UI 增删步骤不会让后台枚举抛「集合已修改」
        var selfPaths = new[] { _exePath };
        var cfgDir = CfgDir;
        Task.Run(() =>
        {
            try
            {
                // Begin 在 try **里面**：它（连同广播 ActiveChanged 的订阅方）可能抛，
                // 抛在 try 外的话 finally 不跑，_runGate.End() 与 _launchRunning 的复位一起被跳过——
                // 主窗口的急停按钮从此永远亮着，而 _launchRunning 卡在 1 会让「重跑清单」再也点不动。
                // RunGroupAsync 早就把这一句挪进来了并写明了理由，这两处是漏的。
                _runGate.Begin();   // 首个并发运行才清急停；不再无条件 Clear（避免抹掉在途急停）
                var result = LaunchSequence.Run(cfg, boot, -1, 0,
                    // 卡片形态的 message 在这里截下：InvokeStepAction 对 message 一律静默返回 ✓
                    // （模态形态在启动路径本就该静默），只有这一层知道该弹卡片。顶层与组展开共用本 lambda。
                    s => s.Kind == "message" && StepHelpers.MessageFormOf(s) == MessageForm.Card
                         ? ShowStepCard(s)
                         // boot 时传 null = 「这条路上没有可以问的人」（见 StepRunner 的 system 那一支）。
                         // 手动重跑清单不传 null：那时用户就在键盘前，弹一句确认是对的。
                         : StepRunner.RunStepMark(s, boot ? null : a => ConfirmDestructive(a), selfPaths),
                    () => DateTime.Now);
                LaunchSequence.WriteLog(Path.Combine(cfgDir, "clockwork.run.log"), result, DateTime.Now);
                // 开机那一次以前被排除在回执之外（原写法 if (!boot)），本意大概是「登录时别吵」。
                // 但 NotifyRunResult 成功时本来就一言不发，只在撞步数上限 / 被急停 / 有步骤失败时开口——
                // 于是「排除开机」实际排掉的只有坏消息：12 步里 3 步失败，屏幕上一声不响，
                // 真相躺在一个要用户自己想起来去翻的日志文件里。而这恰恰是最该被告知的一次运行:
                // 手动重跑时人就在屏幕前，失败当场就看见了；开机那次没人看着，日志是唯一的证人。
                Dispatcher.Invoke(() => NotifyRunResult(result));
            }
            // 没有 catch 时任何异常都让整个开机序列静默中止（无日志/无 toast/什么都没启动）——如实报出来。
            catch (Exception ex) { WarnToast(Lf("Warn_LaunchRunCrashed", ex.Message)); }
            finally { _runGate.End(); Interlocked.Exchange(ref _launchRunning, 0); }
        });
    }

    // —— 全局热键（急停 + 动作组） ——
    private const int HotkeyId = 0xB001;          // 急停
    private const int PanelHotkeyId = 0xB002;     // 快捷面板（与急停同属「功能键」，占固定号，不进组的槽位区间）
    private const int GroupHotkeyBase = 0xB100;   // 动作组热键 id 区间起点
    private const int GroupSlotMax = 0xBFF0;      // RegisterHotKey 应用侧 id 上限 0xBFFF，留余量
    private nint _hotkeyHwnd;                     // 主窗口句柄，注册/注销共用
    private bool _hotkeysSuspended;               // 捕捉期间为真：SaveConfig 不得重建热键（否则组会抢走正被改绑的急停组合）
    private readonly Dictionary<int, string> _groupHotkeyIds = new();   // 当前已注册的 id → 组 Id（WM_HOTKEY 查表）
    private readonly Dictionary<string, int> _groupIdSlots = new();     // 组 Id → 固定 id 槽位：重建不换号，队列里滞留的旧 WM_HOTKEY 不会错派到别的组
    private int _nextGroupSlot = GroupHotkeyBase;
    private HashSet<string> _hotkeyFails = new();                       // 上一轮注册失败的「组Id|键」：同一失败只 toast 一次，不随每次保存刷屏
    private string? _stopHotkeyFail;                                    // 急停键上次失败的组合：同一失败只 toast 一次（每次捕捉进出都会重注册）
    private string? _panelHotkeyFail;                                   // 面板键同上，各记各的：两个键失败的原因通常不同，去重不能共用一个格子

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
        RebindFunctionHotkey(HotkeyId, _config.Settings.StopHotkey, ref _stopHotkeyFail);
        // 面板关掉就别占着这个组合键：留着它既会拦住别的程序，按下去又什么都不会发生
        // （TogglePanel 已经在开头返回了）。传空串走的正是 RebindFunctionHotkey 里「不注册」那条路。
        RebindFunctionHotkey(PanelHotkeyId, _config.Settings.PanelEnabled ? _config.Settings.PanelHotkey : "",
                             ref _panelHotkeyFail);
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
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, PanelHotkeyId); } catch { }
        UnregisterGroupHotkeys();
    }

    // 功能键（急停 / 快捷面板）：先注销旧的、再注册新的（空/无效=停用）。失败/无效 toast 按组合去重——
    // 每次进出捕捉框都会重注册，被占用时不该每次都弹同一条（组热键同款策略）。
    //
    // 两个键共用这一份而不是各抄一遍：它们的注册流程逐字相同（注销 → 空则停用 → 解析 → 拦保留组合 →
    // 注册 → 分类报错 → 去重记账），差别只有热键 id 和「上次失败的组合」记在哪个格子里。
    // 抄一遍的代价不是多 25 行，是日后修 bug 只修了一边——去重逻辑尤其容易只补一处。
    // lastFail 用 ref 传：去重状态必须留在字段里跨调用存活，两个键各记各的（失败原因通常不同）。
    private void RebindFunctionHotkey(int id, string? combo, ref string? lastFail)
    {
        // 句柄已销毁（退出时主窗 Closed 触发的恢复）就跳过：否则在死句柄上注册失败、又弹「注册失败」气泡。
        if (_hotkeyHwnd == 0 || !HotKey.IsWindow(_hotkeyHwnd)) return;
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, id); } catch { }
        if (string.IsNullOrWhiteSpace(combo)) { lastFail = null; return; }   // 空=禁用
        var p = KeyInput.ToHotkeyParams(combo);
        // 保留组合也在此拦：配置可手改/可带旧版遗留，只挡捕捉 UI 挡不住 JSON 里写进来的 Alt+F4。
        // 文案分开：解析不了 → 无法识别；解析得了但系统保留 → 明说保留，别让用户以为拼写错了反复试。
        if (p == null || HotkeyCapture.IsReserved(combo))
        {
            var msgKey = p == null ? "Hotkey_Unrecognized" : "Hotkey_Reserved";
            if (lastFail != combo) ShowToast("Clockwork", Lf(msgKey, combo), Views.ToastLevel.Warn);
            lastFail = combo;
            return;
        }
        bool ok = false;
        try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, id, p.Modifiers | HotKey.MOD_NOREPEAT, p.Vk); } catch { }
        if (!ok)
        {
            if (lastFail != combo) ShowToast("Clockwork", Lf("Hotkey_RegisterFail", combo), Views.ToastLevel.Warn);
            lastFail = combo;
            return;
        }
        lastFail = null;
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
        // U 盘插入：Windows 把 WM_DEVICECHANGE 广播给所有顶层窗口，不需要 RegisterDeviceNotification。
        // 搭这个钩子的车而不是另开一个消息窗口——主窗口的句柄本来就常驻（热键注册就依赖它），
        // 为一个事件再造一个隐藏窗口纯属多一份生命周期要管。
        // 不置 handled：设备通知是广播，吞掉它不礼貌也没好处（本程序不是唯一的收听者）。
        // 一个多分区的 U 盘会连发几条，靠 FireReminderEvent 门口的 3 秒去抖收敛成一次。
        if (msg == Win32.WM_DEVICECHANGE)
        {
            // **异步派发，不在窗口过程里直接跑。** 这条消息是 Windows 用 SendMessage 广播的：
            // 我们不返回，它就一直卡在那儿等——而 FireReminderEvent 可能弹一个提醒框，
            // 那是个模态窗（自带消息循环），一等就是整个提醒时长，甚至等到用户来点。
            // 那段时间里整个即插即用广播被这一个窗口拖住。
            //
            // 其余每一个事件源（会话切换、电源、显示器、网络）本来就在工作线程上到达，
            // 靠 FireReminderEvent 门口那句 CheckAccess 编组回 UI 线程；只有这一条是在 UI 线程上
            // 直接进来的，于是那句编组是空转——只能在这里显式排队。
            if (wParam.ToInt32() == Win32.DBT_DEVICEARRIVAL && Win32.IsVolumeArrival(lParam))
                Dispatcher.BeginInvoke(new Action(() => FireReminderEvent("usb")));
            return IntPtr.Zero;
        }
        if (msg != HotKey.WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id == HotkeyId)
        {
            RequestStop();
            handled = true;
        }
        else if (id == PanelHotkeyId)
        {
            TogglePanel();
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
    private void ToggleGroupByHotkey(ActionGroup g, IntPtr gestureOrigin = default)
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
        RunGroupAsync(g, gestureOrigin: gestureOrigin);
        ShowToast("Clockwork", Lf("Toast_GroupStarted", g.Name), Views.ToastLevel.Info, key: key);
    }

    // —— 快捷面板 ——
    private Views.QuickPanelWindow? _panel;
    private Native.MouseHook? _mouseHook;
    private int _mouseHookMs;          // 当前钩子是按哪个阈值装的：只有阈值真变了才重装（0=中键关着）
    private bool _mouseHookGesture;    // 当前钩子装没装手势那半
    // 当前那个 GestureGate 是按哪个最小笔画长度造的。GestureGate 不可变，所以换主屏 / 换分辨率
    // 之后必须整个重建——这一格就是「要不要重建」的判据（见 ApplyMouseHook 里那段）。
    private int _mouseHookMinLeg;
    // 画手势时屏幕上那条看得见的线。只在监听右键期间存在——没配手势的人不该多出一扇窗。
    private Views.GestureTrailWindow? _trail;
    private bool _mouseHookFailed;     // 装失败已经报过一次：别在每次保存配置时反复弹同一条

    /// <summary>右键监听此刻是什么状态。手势管理器把它显示出来。</summary>
    //
    // 这个功能所有的失败模式都是**不可见**的：钩子装不上（受限令牌 / 组策略 / 安全软件拦）、
    // 装完被 Windows 静默卸掉（UI 线程占满 LowLevelHooksTimeout 就会，而且不通知任何人）、
    // 没提权因此看不到提权窗口上的输入。三件事在界面上一个字都没有，
    // 用户能看到的只有「按了没反应」——而那与「我这一笔画歪了」长得一模一样。
    // 报出来之后，「功能是不是活着」就不必再靠猜。
    internal GestureWatchState GestureWatch
    {
        get
        {
            if (_mouseHookFailed) return GestureWatchState.Failed;
            if (_mouseHook == null || !_mouseHookGesture) return GestureWatchState.Off;
            // **看心跳，不看「我们装没装」。** 这两个字段记的是当初的打算：Windows 把低级钩子
            // 静默卸掉之后它们原样不动，于是状态会在最需要说真话的那一次显示「监听中」。
            // 打开这扇窗必然刚动过鼠标，所以「几秒内没有回调」就是「它已经不在了」。
            // 「收得到抬起、从没收到按下」优先于其余判断：那时心跳是正常的（移动照收），
            // 状态会显示「监听中」，而手势永远起不了头——这正是最难查的那一种，
            // 它必须自己说出来，否则用户看到的又是一个「一切正常但没反应」。
            if (_mouseHook.RightDownSwallowed) return GestureWatchState.Blocked;
            if (_mouseHook.SinceBeatMs <= StaleBeatMs) return GestureWatchState.Live;
            // 「一次都没收到过」与「收到过、后来停了」分成两档：成因和补救完全不同，
            // 混成一句话的话，用户面对的又是一个「按了没反应」。
            return _mouseHook.EverBeat ? GestureWatchState.Stale : GestureWatchState.Deaf;
        }
    }

    // 多久没有回调就算它已经掉了。取 4 秒：鼠标一动就有 WM_MOUSEMOVE，
    // 而人从动鼠标到看这一行，中间隔不了这么久。
    private const long StaleBeatMs = 4000;

    /// <summary>钩子掉了就重新装上。</summary>
    //
    // **Windows 会把低级鼠标钩子静默卸掉**：回调占用超过 LowLevelHooksTimeout（默认 300ms）
    // 就会被摘，不通知任何人，而本进程这边句柄原样不动——于是「装着」这个状态一直是真的，
    // 手势却从此再也不响应，直到重启程序。这个项目里到处是为那 300ms 写的防线
    //（IconLoader 不阻塞、PathProbe 不阻塞、动作一律 post 出回调再跑），但防线不是保证：
    // 别的进程拖慢整机、调试器断下、系统卡顿都能踩到它。
    //
    // 所以补一条退路：心跳停了就重装。摘掉再装是廉价的（一次 SetWindowsHookEx），
    // 而代价是零——真没掉的时候这个判断根本不会成立。
    //
    // ponytail: 靠鼠标动才有心跳，所以「久坐不动」也会被判成掉了，那时会白重装一次。
    //   无害（用户没在用鼠标），也就不为它单设一套「鼠标有没有在动」的旁路。
    private (int X, int Y) _lastCursor;
    private bool _cursorSeen;   // 采过第一次没有——第一次只定基线，见 CursorWatchTick
    // 上一个钩子的账，在摘掉它之前抄下来。**只有自愈与手动「重新挂钩」这两条路会填它**，
    // 而 ApplyMouseHook 也正是拿「它有没有值」当作「这次重装值不值得记一行」的判据。
    private string? _prevBeat, _prevRight;
    private long _prevSince;
    private int _prevRejected;

    // 光标最后一次被观察到「变了位置」的时刻。判「钩子死了没有」全靠它。
    private long _cursorMovedAt;
    private System.Windows.Threading.DispatcherTimer? _cursorWatch;

    /// <summary>按「此刻监听不监听手势」开关那个每秒采样光标的计时器。</summary>
    //
    // 只在真的监听手势时才跑：没配手势的人一次 GetCursorPos 都不会多花。
    private void ApplyCursorWatch(bool on)
    {
        if (on)
        {
            if (_cursorWatch != null) return;
            _cursorWatch = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1) };
            _cursorWatch.Tick += (_, _) => CursorWatchTick();
            _cursorWatch.Start();
            _cursorMovedAt = 0;   // 刚开的这一轮不带上一轮的旧账
            _cursorSeen = false;  // 基线也要重新定，否则用的是上一轮关掉时的那个位置
            return;
        }
        _cursorWatch?.Stop();
        _cursorWatch = null;
    }

    /// <summary>每秒看一眼光标。只在监听手势期间跑。</summary>
    //
    // 这是「钩子还活着吗」唯一站得住的判据，而它必须**采样得够密**。
    // 一度是搭在 30 秒的提醒计时器上：那时「光标动过」比的是相隔 30 秒的两次位置，
    // 于是「10 秒前动过鼠标、之后停下来看屏幕」——一个再常见不过的状态——
    // 会被判成「钩子死了」，白摘白装一次，还把日志刷满、让状态行一直挂着「已失效」。
    // 实测日志里那几条「静默 6563ms / 9610ms / 14765ms」全是这么来的，一条真故障都没有。
    //
    // 一秒一次 GetCursorPos 的代价可以忽略，而且只在真的监听手势时才开——
    // 没配手势的人一次都不会跑到这儿。
    private void CursorWatchTick()
    {
        var now = Native.Win32.CursorPos();
        // **第一次采样只用来定基线，不算「动过」。**
        // _lastCursor 的初值是 (0,0)，而真实光标几乎不可能正好在那儿——不特判的话，
        // 开启监视后的第一跳必然「不等于上次」，于是即使用户根本没碰鼠标也会被记成刚动过。
        // 那一记会让接下来两秒里的自愈判据成立（钩子安安静静没心跳是因为鼠标没动，不是因为它死了），
        // 于是每次启动都白白重装一次钩子、日志里多一行「静默 4141ms」——实测就是这么来的。
        if (!_cursorSeen) { _cursorSeen = true; _lastCursor = now; }
        else if (now != _lastCursor) { _lastCursor = now; _cursorMovedAt = Environment.TickCount64; }
        HealMouseHookIfDead();
    }

    private void HealMouseHookIfDead()
    {
        if (_mouseHook == null || !_mouseHookGesture) return;
        // **刚刚动过光标、却刚刚没有心跳**，才算死了。两个「刚刚」都以同一条时间轴度量：
        // 光标是每秒采一次的，所以「2 秒内动过」是可信的近况，而不是三十秒里的某一刻。
        // **从没跳过一次的钩子不算「死了」，压根别自愈。**
        //
        // SinceBeatMs 在从未收到输入时返回 long.MaxValue，于是一个**刚装上、还没被用过**的钩子
        // 和一个真死了的钩子长得一模一样。加上第一次采样必然与初值不同、被算成「刚动过光标」，
        // 每次启动都会稳定地空转重装——实测日志里那 3 行一簇（yes → no → no）就是它，
        // 装到那个「试三次就放弃」的计数器封顶才停。85 行日志里 78 行是这么来的。
        //
        // 而这个方向本来就是错的：「掉了就重装」只对**装对了、事后被摘掉**那一种有效
        //（Windows 因 LowLevelHooksTimeout 把它静默摘走）。一次输入都没收到过则是另一回事——
        // 那是装的那一刻就不成立（进程没提权、有人排在输入链前面），重装一百次也一样。
        // 界面上那一档本来就叫 Deaf，与 Stale 分开报，说的正是「这条路重装治不了」。
        //
        // 于是判据从「试三次再放弃」改成「根本不试」：少了三次无谓的装卸，日志里那 3 行一簇没了，
        // 而真正的自愈（收到过输入、后来停了）一次都没少——那一档 EverBeat 恒为真。
        if (!_mouseHook.EverBeat) return;
        bool movingNow = Environment.TickCount64 - _cursorMovedAt < 2000;
        if (!movingNow || _mouseHook.SinceBeatMs <= StaleBeatMs) return;
        // **先把上一个钩子的账记下来，再摘掉它。** 这两格（收到过输入吗、收到过右键吗）
        // 是判断「为什么没工作」的全部依据，而摘掉之后就再也问不到了——
        // 此前这里先置空再走 ApplyMouseHook，于是自愈这条路上日志永远是「-」：
        // 恰恰在唯一反复触发的那条路上，诊断是瞎的。
        _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
        // 按下与抬起**分开记**。合成一格（或）会把这个功能唯一能自证的信号抹掉：
        // 「收到过抬起、从没收到按下」只有一种成因——有人排在前面把按下吞了。
        _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                   + (_mouseHook.EverRightUp ? " up+" : " up-");
        _prevSince = _mouseHook.SinceBeatMs;
        _prevRejected = _mouseHook.InjectRejected;
        var wasGesture = _mouseHookGesture;
        _mouseHook.Dispose();
        _mouseHook = null;
        _mouseHookMs = -1;                 // 逼 ApplyMouseHook 走完整的重装，别被那句「已经是想要的状态」挡回去
        _mouseHookGesture = !wasGesture;
        ApplyMouseHook();
    }

    /// <summary>把鼠标钩子摘掉再装一次，插到钩子链最前面。</summary>
    //
    // **钩子链里后装的排在前面**，而排在前面的那个若吞掉右键按下，后面的人根本收不到。
    // 所以「另一个程序抢走了右键」这件事，唯一的解法就是重装一次抢回队首——
    // 同类工具（Quicker）为此专门有一条「重新挂钩键鼠」的命令，理由一模一样。
    //
    // 自愈够不着这一格：被抢在前面时我们的钩子**活得好好的**（鼠标移动照收、心跳正常），
    // 只是右键先被人吞了，于是「掉了就重装」永远不触发，也就永远排在后面。
    // 那台机器上什么时候会有人插到前面，我们无从预知，所以这件事只能留给用户按。
    internal void RehookMouse()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(RehookMouse); return; }
        // 这条路也要记账。此前不记，于是日志里连按几下「重新挂钩」全是「上一个 = -」——
        // 而那几下恰恰是用户在排障时按的，最需要知道上一个钩子到底收到过什么。
        if (_mouseHook != null)
        {
            _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
            _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                       + (_mouseHook.EverRightUp ? " up+" : " up-");
            _prevSince = _mouseHook.SinceBeatMs;
            _prevRejected = _mouseHook.InjectRejected;
        }
        _mouseHook?.Dispose();
        _mouseHook = null;
        _mouseHookMs = -1;               // 逼 ApplyMouseHook 走完整重装，别被「已经是想要的状态」挡回去
        _mouseHookFailed = false;        // 上一次装失败不该让这一次连报错都不报
        ApplyMouseHook();
    }

    /// <summary>打开手势管理器。面板表圈上那颗按钮与主界面那颗走同一条路。</summary>
    public void OpenGestureManager()
    {
        _panel?.Dismiss();
        var owner = _main is { IsVisible: true } ? _main : null;
        var w = new Views.GestureManagerWindow(_config, SaveConfig) { Owner = owner };
        // 没有 Owner 时（主窗口还在托盘里）自己置顶，否则这扇窗会开在所有窗口后面——
        // 同 OpenPanelManager 那条，理由一模一样。
        if (owner == null) { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true; }
        w.ShowDialog();
    }

    // 按当前设置装 / 卸中键长按的钩子。启动时与每次保存配置后都会调，故必须是幂等的。
    //
    // 阈值改了要重装：LongPressGate 的阈值在构造时就定了（那是它保持纯粹、可测的代价），
    // 所以调阈值等于换一个 gate。比较 _mouseHookMs 而不是无条件重装——每次保存配置都摘一次
    // 再装一次全局钩子是没必要的抖动，而钩子重装的瞬间正按着的中键会丢掉状态。
    private void ApplyMouseHook()
    {
        // **必须在 UI 线程上跑这个方法**——但原因已经不是钩子本身：MouseHook 现在自带专用
        // 钩子线程装钩（见 MouseHook 类头第 1 条，回调存活不再受 WPF/DWM 卡顿影响）。真正要求
        // UI 线程的是下面那扇 WPF 笔迹窗（_trail）的创建。这个方法还从 SaveConfig 末尾被调，
        // 而 SaveConfig 有若干调用点（跑完动作、提醒到点自行停用），并不都在 UI 线程上。
        // 与其逐个去审那些调用点、并指望以后加的每一个也记得，不如在入口处一次夹住。
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ApplyMouseHook); return; }

        // 面板总开关是**前置条件**：关了就没有「长按中键唤出面板」这件事可谈，
        // 那半个钩子（以及它必须先吞掉每一次中键按下的那套机制）也就不该装。
        bool wantPress = LongPressGate.ShouldWatch(_config.Settings.PanelEnabled, _config.Settings.PanelMiddleLongPress);
        // 有任何启用的组绑了手势就要监听右键。gate 每次都对着**当前**配置现查（闭包捕获 _config），
        // 所以改手势串不用重装钩子——只有「要不要装 / 中键阈值」这两件事变了才重装。
        // 总开关关掉就完全不监听右键，哪怕底下还留着一堆启用中的手势——
        // 「临时让右键恢复原样」正是这个开关存在的理由，逐条取消勾选再逐条勾回来不是办法。
        // 判据在 GestureGate.ShouldWatch（纯函数、有测试）：这儿碰 Win32，断言够不着。
        bool wantGesture = GestureGate.ShouldWatch(_config.Settings.GesturesEnabled, _config.Gestures);
        // 夹在这里而不是只夹在设置页：这个值能从手改 / 导入的 json 直接进来，
        // 而太小的阀值会让每一次普通中键点击都开面板、中键从此全系统失效（见 ClampLongPressMs）。
        int ms = wantPress ? StepHelpers.ClampLongPressMs(_config.Settings.PanelLongPressMs) : 0;   // 0 = 钩子里不管中键
        if (!wantPress && !wantGesture)
        {
            _mouseHook?.Dispose();
            _mouseHook = null;
            CloseTrail();
            ApplyCursorWatch(false);
            _mouseHookFailed = false;   // 关掉再开时允许重新报错
            return;
        }
        // 最小笔画长度跟着主屏宽度走（见 GestureGate.MinLegForScreen）——固定 40px 在大屏上小得离谱，
        // 右键点一下的手抖就够格成一条腿，把菜单吞了还弹一句没匹配。
        //
        // **它也要进「已经是想要的状态」那个判据。** GestureGate 是不可变的，这个数在构造时就烧进去了；
        // 而下面那个早退只比中键阈值和总开关，于是换分辨率、或者插拔外屏让主屏换了一块之后，
        // 钩子根本不会重建：4K 上装好的 96px 门槛留在 1080p 上（等于要求画满屏宽的 5%，短腿一律被
        // Analyze 判成 ""，手势全部失配、右键菜单照常弹，看起来就是「手势忽然不灵了」，重启才好），
        // 反过来 1080p 装好的 40px 留在 4K 上，正是这个函数存在的意义——过于灵敏——的原样重现。
        // 已知局限（取舍，不是疏漏）：这一个阀值给**所有**屏幕用。上面那段说的是
        // 「主屏换了要重建钩子」，而 4K 主屏 + 1080p 副屏 **同时存在**时，副屏上用的仍是
        // 按主屏算出的 96px（占 1920 宽的 5%，而在 4K 上只占 2.5%）——副屏上手势会难画一些。
        // 没改成逐屏查：那要在右键按下那一刻拿光标去 MonitorFromPoint，而那是钩子回调里的
        // 300ms 硬预算上又多一次 P/Invoke，换来的是副屏上略微好一点的手感。让用户按主屏
        // 调一次更划得来。（选 SM_CXSCREEN 而不是 SM_CXVIRTUALSCREEN 的理由见 PrimaryScreenWidth。）
        int minLeg = wantGesture ? GestureGate.MinLegForScreen(Win32.PrimaryScreenWidth(), _config.Settings.GestureSensitivity) : 0;
        if (wantGesture && _trail != null) _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        if (_mouseHook != null && _mouseHookMs == ms && _mouseHookGesture == wantGesture
            && _mouseHookMinLeg == minLeg) return;   // 已经是想要的状态
        _mouseHook?.Dispose();
        var gesture = wantGesture
            ? new GestureGate((p, proc) => GestureGate.Match(_config.Gestures, p, proc) != null, minLeg)
            : null;
        // 覆盖窗跟着 gate 走：装了手势才建，卸了就关——没配手势的人不该多出一扇窗，哪怕它是隐藏的。
        if (wantGesture)
        {
            _trail ??= new Views.GestureTrailWindow();
            _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        }
        else CloseTrail();
        _mouseHook = new Native.MouseHook(ms, TogglePanel, a => Dispatcher.BeginInvoke(a), gesture, RunByGesture,
            trailPoint: (x, y, armed) => _trail?.Point(x, y, armed), trailEnd: () => _trail?.Finish(),
            unmatched: GestureUnmatched);
        _mouseHookMs = ms;
        _mouseHookGesture = wantGesture;
        _mouseHookMinLeg = minLeg;
        ApplyCursorWatch(wantGesture);   // 只有监听手势时才需要判「钩子还活着吗」
        if (_mouseHook.Install())
        {
            _mouseHookFailed = false;
            // **只在有「前任的账」时记一行，也就是只在自愈和手动「重新挂钩」之后。**
            //
            // 此前这里对**每一次**装上都记一行成功，于是：每次启动一行、每次改中键阈值或手势开关
            // （SaveConfig 末尾会调到这儿）再一行。它们全是「一切正常」，而这个文件是唯一的事后线索、
            // 又只有 128KB——用成功刷掉真故障的历史，恰恰废掉了它存在的理由。
            // 「装上了」这件事状态行已经在报（Deaf / Stale 两档），不必再往错误日志里记一笔。
            //
            // 剩下这两条路值钱，因为它们各自在摘掉旧钩子**之前**抄了一份账（_prevBeat/_prevRight）：
            //   收到过输入吗：一次都没有 = 装的那一刻就不对；有过、停了 = 被 Windows 摘的。两者修法完全不同。
            //   收到过右键吗（按下与抬起分开）：前者 yes 而后者 no，说明右键在到达钩子链之前就被
            //   别人拿走了（鼠标驱动改键、或别的程序的低级钩子吞了它）——那时本程序一切正常，
            //   光看前一格永远查不出来。
            // 而这两条路上 _mouseHook 都已被调用方置空，所以这里不必再自己抄一份 prev：
            // 那个捕获只在「改设置触发的重装」上非空，而那一条正是现在不记的。
            if (_prevBeat == null) return;
            // 英文常量，不走 resx：这个文件是拿去贴 issue 的，读它的人未必读得懂用户那门语言，
            // 而 18 份译文里同一条线索会长出 18 种写法，grep 不到一起。用户要读的那份日志是
            // clockwork.run.log（托盘「查看上次启动日志」），那一份仍然全本地化。
            AppendErrorLog($"mouse hook reinstalled: middleHoldMs={ms} gestures={wantGesture} "
                           + $"prevEverFired={_prevBeat} prevRightButton={_prevRight ?? "-"} "
                           + $"silentFor={(_prevSince == long.MaxValue ? "never" : _prevSince + "ms")} "
                           // 发不出去 vs 收不到，是两种表现一样、修法完全相反的故障（见 MouseHook.InjectRejected）。
                           + $"injectRejected={_prevRejected}");
            _prevBeat = _prevRight = null;   // 这份账只用一次，别让它污染下一条非自愈的记录
            return;
        }
        // 装不上（受限令牌、组策略、被安全软件拦）：如实说。静默失败的话，用户会以为
        // 自己按得不够久，一直加长按时间去试一个根本没装上的钩子。
        //
        // **这一类才是这个文件该记的。** 改之前刚好相反：每次装上都记一行成功，装不上却只弹一句气泡——
        // 气泡关掉就没了，于是唯一真正需要事后追查的那种故障，日志里一个字都没有。
        // 和气泡同受 _mouseHookFailed 夹一次：ApplyMouseHook 每次保存配置都会走到，
        // 不夹的话一个装不上的钩子会把日志按保存次数刷满——那正是这轮要修的毛病。
        var win32 = _mouseHook.LastError;   // 先取错码，Dispose 之后就没人能问了
        // **按不上也要按关掉那条路收尾**，与上面 `!wantPress && !wantGesture` 那个分支同一套。
        // 漏了不是漏一次：ApplyMouseHook 在每次 SaveConfig 末尾都会走到，而 `_mouseHook` 已经是 null
        // 让上面那个「已经是想要的状态」早退永远不成立——于是每保存一次就新建一个 MouseHook，
        // 每个带三个 System.Threading.Timer，上一个连 Dispose 都没调就被丢了。
        // 轨迹窗与 1 Hz 的「钩子还活着吗」同理：前面刚建完 / 刚上弦，而自愈那一句一看 `_mouseHook == null`
        // 就立即返回，于是那个计时器永远转着、每秒一次 GetCursorPos，什么也不做。
        _mouseHook.Dispose();
        _mouseHook = null;
        CloseTrail();
        ApplyCursorWatch(false);
        if (!_mouseHookFailed)
        {
            AppendErrorLog($"mouse hook install failed: middleHoldMs={ms} gestures={wantGesture} win32={win32}");
            WarnToast(Strings.Get("Warn_MouseHookFail"));
        }
        _mouseHookFailed = true;
    }

    private void CloseTrail()
    {
        if (_trail == null) return;
        _trail.Finish();
        _trail.Close();
        _trail = null;
    }

    // 手势命中：按方向串找**步骤**来跑。串是 Fire 时刻从 gate 拿的快照，
    // 这里再查一遍配置——手势可能刚被改掉，查空就算了。
    //
    // group 类型单拎出来走 ToggleGroupByHotkey 而不是 RunStepAsync：整组运行有「再触发即停」的语义，
    // 热键那边就是这么做的，18 种语言的说明也都是这么写的。走单步执行会丢掉这个停法，
    // 而一个跑了一半停不下来的组，正是急停键存在的理由——不该让手势制造这种局面。
    // 画出来了却没绑任何东西：说一句，别沉默地吞掉。
    //
    // 沉默把三件完全不同的事混成一件——「手势总开关关着」「这条笔画没绑动作」「动作跑失败了」，
    // 在用户眼里都是「按了没反应」，而这三件事要做的补救完全不同。报出**画出来的那一串**，
    // 用户当场就能对照管理器里的列表：形状对不上就再画一次，形状对得上就是别处出了问题。
    // **就地说，别弹通知卡。** 从前这里走 ShowToast：一张带标题带图标、从屏幕角落滑进来的卡片，
    // 只为说一句「↑↓ 没绑动作」。份量和事情完全不匹配——手势是一划而过的操作，反馈却比操作本身还重，
    // 而且出现在离你的手一整个屏幕远的地方，眼睛得先找到它。
    // 现在把这一串画在笔迹那扇覆盖窗上、收笔点旁边，亮 0.9 秒自己消失：你在哪收的笔，答案就出在哪。
    // 要报的信息一个字没少（画出来的那一串），只是不再打断你。
    private void GestureUnmatched(string path)
        => _trail?.Note(GestureGate.Arrows(path));

    private void RunByGesture(string path, IntPtr origin)
    {
        var proc = Win32.ProcessNameForWindow(origin);
        var step = GestureGate.Match(_config.Gestures, path, proc);
        if (step == null) return;
        // 面板若还开着，先关掉。中键面板是靠 ForceForeground 抢过前台的，而右键手势的按下被钩子吞掉、
        // 不会激活光标下的窗口——于是面板会一直占着前台，动作里凡读前台的（「当前窗口」、发键、发文本）
        // 全都落到 Clockwork 自己头上，被自家进程守卫拒掉，看起来就是「手势丢了焦点、没反应」。
        // 每个面板格子在跑之前也是先 Dismiss()（见 QuickPanelWindow），手势这条路得照做。
        _panel?.Dismiss();
        // origin 是起笔点所在的窗口（MouseHook 在按下那一刻解析）：窗口管理类「当前窗口」打它，
        // 而不是前台窗——在后台窗口上起笔时这两者不是同一个（见 Win32.WindowAtPoint）。
        // 手势这一档：成功不弹回执、也不设防连点闸（两条理由都在 RunStepAsync 的 by 参数上）。
        RunStep(step, by: StepTrigger.Gesture, gestureOrigin: origin);
    }

    // 面板是开关：热键再按一次收起来。这一点必须做对——面板没有标题栏也没有关闭按钮，
    // 而用户按出它之后的第一反应就是再按一次；那一下要是把面板重开一份（旧的还在），
    // 屏幕上会叠出两个一模一样的浮窗，谁也说不清该点哪个。
    public void TogglePanel()
    {
        // 总闸放在最前面。热键不注册、托盘那项不显示之后，理论上没人还能叫到这儿，
        // 但这个方法是 public 的（托盘、热键、中键钩子三条路都调它），闸门守在入口最省心。
        if (!_config.Settings.PanelEnabled) return;
        if (_panel != null) { _panel.Dismiss(); return; }
        // 前台进程必须**在建面板之前**问：面板一显示就把前台抢走了，那之后再问只会得到 Clockwork 自己。
        // 这是场景页整个功能的时序命门，别把它挪到下面去（见 Win32.ForegroundProcessName 的注释）。
        var foreground = Win32.ForegroundProcessName();
        var s = _config.Settings;
        var look = new Views.PanelLook(s.PanelColumns, s.PanelTileSize, s.PanelIconOnly, s.PanelShowOps,
                                       s.PanelTopRows, s.PanelBottomRows, s.PanelLeftTabs, s.PanelTopTabs);
        // 一页的容量由两条带的行数×列数决定，装不下的自动续到下一页。
        var capacity = PanelMetrics.PageCapacity(s.PanelTopRows, s.PanelBottomRows, s.PanelColumns);
        var w = new Views.QuickPanelWindow(BuildPanelPages(foreground, capacity), BuildPanelOps(), look,
                                           BuildPanelWaistOps());
        _panel = w;
        // 清引用挂在 Closed 上而不是各处手动置 null：面板有三条关闭路径（热键再按 / Esc / 焦点离开），
        // 漏掉任何一条，_panel 就会一直指着一个已经关掉的窗口，此后热键永远只走 Dismiss 分支，
        // 面板再也打不开——而这种状态重启前自己不会恢复。
        w.Closed += (_, _) => { if (ReferenceEquals(_panel, w)) _panel = null; };
        w.Popup();
    }

    // 面板上「你自己的东西」那一片：摆哪些格子由 Core.PanelLayout（纯函数、有测试）决定，
    // 这里只负责给每一格接上真正要执行的动作。一格 = 一个步骤，走 RunStep（下面那个）。
    //
    // 这段注释一度写着「group 类型在 StepRunner 里展开成整组运行，所以面板只有一条执行路径」——
    // **StepRunner 里根本没有 group 分支**（`default: Warn_UnknownKind`）。于是每一个
    // 「运行动作组」的格子点下去都只弹一句「未知步骤类型：group」，什么也没跑，
    // 而 PanelLayout 还特意为它取被引用那个组的图标，看上去完全正常。
    // 另外三个调用方（手势、主列表、组编辑器的试跑）都先分流，只有这条漏了。
    /// <summary>跑一个步骤：group 类型转交整组运行，其余走单步。</summary>
    //
    // 整组运行有「再触发即停」的语义（热键与手势都是这么做的，18 种语言的说明也都这么写），
    // 走单步执行会丢掉这个停法——而一个跑了一半停不下来的组，正是急停键存在的理由。
    private void RunStep(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                         IntPtr gestureOrigin = default)
    {
        if (step.Kind == "group")
        {
            // 用 ResolveForRun 而不是 Resolve：目标缺失 / 已禁用要**说一声**。
            // 从前这里是 `if (g is { Enabled: true })`——两种落空都静默 return，于是一个指着
            // 已删动作的面板格子（或手势）画得出来、点得到、图标名字都正常，点下去什么都不发生：
            // 没有气泡、没有日志、没有任何线索。而别的每一条运行路径都会如实报
            //（LaunchSequence 有「⚠ 找不到动作组」，动作组里的嵌套引用走 OnStepSkipped）。
            // 措辞与良性判定都在 ActionGroupResolver 里定过一次，这里只是把它用上。
            var target = ActionGroupResolver.ResolveForRun(_config.ActionGroups, step.GroupId);
            if (target.Skip is { } skip)
            {
                ShowToast("Clockwork", Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), skip.Text()),
                          skip.Benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn,
                          key: "runstepgroup:" + step.GroupId);
                AppendErrorLog($"panel/gesture group step {StepDisplay.StepSummary(step)} - {skip.TextEn()}");
                return;
            }
            var g = target.Group;
            if (g is { Enabled: true }) ToggleGroupByHotkey(g, gestureOrigin);
            return;
        }
        RunStepAsync(step, owner, by, gestureOrigin);
    }

    private List<Views.PanelTilePage> BuildPanelPages(string? foreground = null, int capacity = 0)
        => PanelLayout.BuildPages(_config.PanelPages, foreground, capacity, actions: _config.ActionGroups)
            .Select(p => new Views.PanelTilePage(
                p.Title,
                p.Items.Select(it => new Views.PanelTile(it.Label, it.Icon, () => RunStep(it.Step), it.Enabled,
                                                         OnEdit: () => EditPanelItem(it),
                                                         OnDelete: () => DeletePanelItem(it),
                                                         Tip: StepDisplay.StepSummary(it.Step))).ToList(),
                AddStepTo(p), p.Tab, p.Screen))
            .ToList();

    // —— 面板上的增删改：一律复用主界面那两个编辑器 ——
    //
    // 面板不自己造编辑界面。步骤编辑器（十几种类型、条件、重复、试跑）已经在那儿了，
    // 面板要做的只是「知道这一格是谁」然后把它交给它——PanelItem 本来就带着 Page 和 Step，
    // 那两个字段一开始就是为这件事留的。
    //
    // 三个动作都先 Dismiss()：编辑器是模态窗，面板又是失焦即关，不先收掉的话面板会在编辑器打开的
    // 同一瞬间消失，而它持有的回调还指着一个正在关的窗口。收掉之后编辑完不自动重开面板——
    // 你点的是「编辑」，此刻该看到的是编辑器；要接着用面板，再按一次热键就是了。

    // 从面板自己打开管理器。先收面板：管理器是模态窗，而面板失焦即关，
    // 不先收掉的话面板会在管理器打开的同一瞬间消失，看着像"点了一下就没了"。
    public void OpenPanelManager()
    {
        _panel?.Dismiss();
        var owner = _main is { IsVisible: true } ? _main : null;
        var w = new Views.PanelManagerWindow(_config, SaveConfig) { Owner = owner };
        // 没有 Owner 时（主窗口还在托盘里）必须自己置顶，否则这扇窗会开在所有窗口后面，
        // 用户只看到面板消失、什么都没出来。
        if (owner == null) { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true; }
        w.ShowDialog();
        // 管理器现在只动 _config.PanelPages，动作列表一根汗毛都不碰（页独立成实体之后，
        // 「添加面板页」不再是「新建一个动作组」）。但它能改**格子里的步骤**，而那些步骤可能
        // 指向某个动作——重画一次动作列表最省心，也不必去分辨这次到底改没改到。
        _main?.ResyncGroups();
    }

    // 每一格都是某一页里的某一步，所以编辑一律是「编辑那一步」——没有第二种分支要判。
    // 换出来的是新对象，得替回它在组里的原位，不能只改副本。
    private void EditPanelItem(PanelItem it)
    {
        _panel?.Dismiss();
        var owner = _main is { IsVisible: true } ? _main : null;
        var edited = Views.StepEditorWindow.Edit(owner, it.Step, it.Step.Kind, _config.ActionGroups);
        if (edited == null) return;
        int i = it.Page.Steps.IndexOf(it.Step);
        if (i < 0) return;   // 期间被别处删了：什么都不做，好过把它又插回去
        it.Page.Steps[i] = edited;
        SaveConfig();
        _main?.RefreshGroupRows();
    }

    // 删的是这一页里的这一格。指向某个动作的格子（group 类型的步骤）删掉的也只是这个引用，
    // 那个动作本身还在——要删动作仍得去主界面，那里会先把「谁在引用它」摆给你看。
    private void DeletePanelItem(PanelItem it)
    {
        _panel?.Dismiss();
        var owner = _main is { IsVisible: true } ? _main : null;
        // 与三个列表页、组编辑器同一条删除契约：必先确认，且确认框里点名删的是什么。
        if (!Views.BrandDialog.ConfirmDelete(owner, StepDisplay.StepListSummary(it.Step))) return;
        if (!it.Page.Steps.Remove(it.Step)) return;
        SaveConfig();
        _main?.RefreshGroupRows();
    }

    // 每一页都收得下新东西——往它的格子列表末尾加一步而已。
    // 取的是 p.Source 而不是 p.Items[0].Page：空页没有第一个格子，从格子反推会得到 null，
    // 于是「面板上唯一一张空页」反而是唯一加不进东西的那张。
    private Action? AddStepTo(PanelPageView p)
    {
        if (p.Source is not { } page) return null;
        return () =>
        {
            _panel?.Dismiss();
            var owner = _main is { IsVisible: true } ? _main : null;
            // 以「运行程序」开场：它是最常放上面板的一类，而编辑器顶上就是类型下拉，
            // 想加别的类型当场换即可——不必先弹一层「选类型」再弹编辑器。
            var s = Views.StepEditorWindow.Edit(owner, null, "app", _config.ActionGroups);
            if (s == null) return;
            page.Steps.Add(s);
            SaveConfig();
            _main?.RefreshGroupRows();
        };
    }

    // 面板底部那一片：程序自带的几个高频操作。与上面那片用分隔线隔开——
    // 混在一起时，格子一多就分不清哪些是「我配的动作」哪些是「程序自带的按钮」。
    //
    // 文案复用托盘那几条（都已 18 语），不为面板另造一套说法：
    // 同一个操作在两个地方叫不同的名字，是最没必要的一种困惑。
    private List<Views.PanelTile> BuildPanelOps()
    {
        // 图标沿用托盘菜单那一套（TrayGlyph）：同一个操作在两处长得一样，认一次就够。
        var ops = new List<Views.PanelTile>
        {
            new(Strings.Get("Tray_Rerun"), TrayGlyph.Rerun, () => RunLaunchAsync(false)),
            new(Strings.Get("Tray_Stop"), TrayGlyph.Stop, RequestStop),
            // 勿扰是个开关，标签跟着当前状态走：生效中显示「恢复提醒（剩 N 分钟）」，否则「暂停 1 小时」。
            // 一格两态，而不是并排摆「暂停」和「恢复」两格——后者任何时刻都有一格是废的。
            DndRemaining is TimeSpan left
                ? new(DndResumeLabel(left), TrayGlyph.Run, ResumeReminders)
                : new Views.PanelTile(Lf("Tray_Hours", 1), TrayGlyph.Dnd, () => PauseReminders(1)),
            new(Strings.Get("Tray_Show"), TrayGlyph.Window, ShowMain),
        };
        return ops;
    }

    // 表圈右端那几颗。与夹板下沿那排分开，是因为两者答的不是同一个问题：
    // 这里是「关于这个程序自己」（去管面板 / 去管手势），那排是「拿这台机器做点什么」
    //（重跑 / 停止 / 勿扰 / 显示主窗）。它们一度都挂在腰栏上，理由见 QuickPanelWindow.xaml。
    //
    // 「管理面板」放在面板自己身上：要改面板的人此刻正看着面板，
    // 让他先去开主窗口、再翻到动作组页、再找一颗按钮，是把最近的路绕成最远的。
    // 用齿轮（MDL2 Settings），与管理器页眉上那颗「编辑这一页」同一个符号——都是「去调它」。
    // 表圈右端那几颗：都是「关于这个程序自己」的入口，不是你配的动作。
    // 手势那颗在这儿，是因为手势和面板是同一批人用的同一类东西（都是「不用键盘就能触发一个动作」），
    // 而在此之前它只在主界面的标题栏上有一个入口——想改一条手势得先把主窗口翻出来。
    private List<Views.PanelTile> BuildPanelWaistOps() => new()
    {
        // 0xE962 = MDL2 的 Mouse，与「鼠标动作」那类步骤的格子用的是同一个字形（PanelGlyph）。
        // 复用是有意的：都在说「鼠标」。代价是面板里一个「鼠标动作」格子和这颗按钮长得一样，
        // 但两者分处表圈与格子区、不挨着，比换一个语义更差的字形（触屏的手）强。
        //
        // 这里一度写的是 0xF126。**它不是「空心方框」那种错**——实测 segmdl2.ttf 里 F126 有字形，
        // 画出来是一个光秃秃的圆环，一个与手势毫无关系的真图标。这才是挑错私用区码位的典型后果：
        // 不报错、不留白，而是理直气壮地画错。GlyphCoverageTests 能挡住的只是「码位根本不存在」
        // 那一半（渲染成方框），语义挑错那一半只有人眼能验——改完跑 `--shots` 看一眼 QuickPanel。
        new(Strings.Get("Gesture_Manager"), char.ConvertFromUtf32(0xE962), OpenGestureManager),
        // fillsEmptyPanel：面板空着时那颗行动按钮指向这里——把面板填满的正是「管理面板」。
        // 显式标记而不是靠排在第几个：上面那条手势入口就是加在前面的，靠顺序的话它会把空态按钮劫走。
        new(Strings.Get("Panel_Manager"), char.ConvertFromUtf32(0xE713), OpenPanelManager, fillsEmptyPanel: true),
    };

    // —— 提醒计时器 ——
    private void StartReminderTimer()
    {
        int tick = _config.Settings.TickSeconds;
        if (tick < 5) tick = 30;
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(tick) };
        // 顺带查一次鼠标钩子还活着没有。搭在这个已有的计时器上，不另起一个：
        // 这件事不急（掉了之后早一秒晚一秒重装没区别），而多一个计时器就是多一份常驻开销。
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
                _busyFired.RemoveWhere(k => !live.Contains(k));
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
        if (DeferForFullScreen(r, st)) return;
        var (action, snooze) = FireReminder(r);
        // 排下一步（稍后/重复）必须用弹窗返回后的时刻，不能用触发前的 now——弹窗是模态的，
        // 自动关闭设得长（如 30 分钟）时 now 已陈旧半小时，「稍后 10 分钟」会算出一个已经过去的
        // SnoozeUntil，下个 tick 立即重弹、再挂 30 分钟，成了永久模态循环；重复催促同理会背靠背连弹。
        var after = DateTime.Now;
        if (snooze is int m)
        {
            // 自动稍后有上限（人不在，别对着空屏幕弹一天）；手点的没有（人的明确决定）。
            // 到顶降级：链结束，改挂常驻卡片等人回来——不再抢焦点，提醒也不静默消失。
            // 卡片没有「是/否」按钮：降级只保留「看到」，onYes 动作不随卡片走。
            if (action == "autosnooze")
            {
                if (!ReminderEngine.AutoSnooze(st, after, m))
                {
                    // 降级成常驻卡片。Warn 级不是为了吓人，是为了别骗人：FireReminder 刚按同一个 key 记了一条
                    // 警示级「无人应答」，而 NotificationLog.Add 会先删同 key 的旧条目——这里用 Info 会把那条
                    // 警示改写成普通回执，托盘「最近通知」里最坏的情况反倒看着最正常。
                    ShowToast(Strings.Get("Tray_ReminderTitle"), r.Message, Views.ToastLevel.Warn, 0, key: ReminderToastKey(r));
                    // 催促链到此结束，但「循环运行」是另一条链，必须在这个出口照常排下一轮——
                    // 不排的话，配了循环的提醒一降级就把当天余下所有轮次静默丢掉，而卡片没有按钮，
                    // 用户回来也无从把它接回去（与 UpdateAfterFire 各出口都排下一轮同一条口径）。
                    ReminderEngine.UpdateAfterFire(r, after, "ok", st);
                }
                // ponytail: 降级卡片是会话态，重启即消失（LastFiredDate 已记今天，不会重弹）；「仅一次」的
                // 提醒还会在下面被自动取消勾选——即卡片被挤掉/重启后，它在界面上不留任何痕迹。
                // 要跨重启得给 ReminderState 再落一个字段并在启动时重挂卡片，等真有人踩到再说。
            }
            else ReminderEngine.Snooze(r, st, after, m);   // 手点=人在场，无人应答计数由引擎清零
        }
        else ReminderEngine.UpdateAfterFire(r, after, action, st);
        // 「仅一次」触发完成（催促/稍后链都结束）→ 自动取消勾选：条目保留（想再用改个日期重新勾上），
        // 用完即焚会让误设时间没得救。时机必须在链结束后——立刻停用会被 Decide 的 !Enabled 早退掐死在途链。
        // 例外是托盘「快速提醒」（Temporary）：那是个当场用完的计时器，不是配置，留行只会堆垃圾——整条删掉。
        // 状态字典里的那份由 ReminderTick 的孤儿清理顺手回收（按 id 比对存活提醒），这儿不必手动删。
        if (ReminderEngine.ShouldDisableAfterOnce(r, st))
        {
            // 删除必须走列表 VM：ListVm.Models 就是 _config.Reminders 这份 List 本身，而 Rows 是与它平行的
            // 另一份集合，RefreshReminderRows 只会逐行 Refresh()、加不了也删不掉行。直接动 _config.Reminders
            // 会让两者错位一位，之后每一行都映射到相邻的那条提醒（编辑/删除/预览全部张冠李戴），
            // 选中最后一行还会让 ListVm.Selected 越界抛异常。
            if (r.Temporary) _main?.RemoveReminderRow(r);   // VM 内部会存盘
            else { r.Enabled = false; SaveConfig(); }
            _main?.RefreshReminderRows();
        }
    }

    /// <summary>全屏 / 演示中让路时，往后推多久再试。见 <see cref="DeferForFullScreen"/>。</summary>
    private const int FullScreenDeferMinutes = 5;

    // 全屏游戏 / 投影演示中：不弹，改挂一次「稍后」，让路给屏幕上正在发生的事。
    // 返回 true = 本次已让路，调用方就此收手（状态已推进，不能再走正常触发路径）。
    //
    // 走手点稍后（Snooze）而不是自动稍后（AutoSnooze），差别是实打实的：后者带 MaxAutoSnoozes 轮上限，
    // 到顶就降级成常驻卡片——那是为「弹了没人理」设计的退路，用来数「我们主动没弹」的次数是错的，
    // 一场两小时的游戏会把额度耗光，然后在全屏正中央拍一张卡片，恰好是本功能要避免的那件事。
    // 手点语义没有轮数上限：全屏挂着就一直往后推，出了全屏第一个 tick 就正常弹，这才是让路该有的样子。
    //
    // 三类刻意不让路的：
    //   · 静默动作组 —— 它根本不上屏，没有可打断的东西。为它让路等于凭空推迟一次自动化。
    //   · 托盘「快速提醒」（Temporary）—— 你几分钟前亲手设的那个计时器。边打游戏边煮面正是它的用法，
    //     "你说 5 分钟" 的承诺高于"别打断全屏"的礼貌。
    //   · 探测失败 —— UserNotificationState.Busy() 取不到时返回 false，照常弹（见那边的注释）。
    private bool DeferForFullScreen(Reminder r, ReminderState st)
    {
        if (r.Temporary || !string.IsNullOrWhiteSpace(r.SilentGroupId)) return false;
        if (!Native.UserNotificationState.Busy()) return false;
        ReminderEngine.Snooze(r, st, DateTime.Now, FullScreenDeferMinutes);
        // 有意不发 toast：让路的全部意义就是此刻别在屏幕上出现，为此弹一张「已推迟」的卡片是自相矛盾。
        // 用户看到的是它在出全屏之后正常响——而不是一条解释自己为什么迟到的通知。
        return true;
    }

    // 托盘「快速提醒」：N 分钟后响一次，响完自删。复用「仅一次」那套机制，不新开一条计时路径。
    //
    // 精度：直接预置 PendingFireAt = 现在+N（秒级），让计时器的 pending 分支引爆，误差 ≤ 一个 tick(默认 30s)
    // 且绝不提前。不这么做就得走「到点判定 → arm → 下一跳才 fire」，那条路要吃两次 tick，再叠上
    // Time 只有 HH:mm 精度带来的取整——5 分钟的计时器实测能拖到近 7 分钟，对一个计时器是不能接受的误差。
    // Time 字段仍照常填（向上取整，绝不提前），它是列表和气泡上显示的那个时刻，也是 PendingFireAt
    // 这个会话态被重启抹掉之后的兜底路径。
    public void QuickReminder(int minutes)
    {
        var at = DateTime.Now.AddMinutes(minutes).AddSeconds(59);
        var r = new Reminder
        {
            Trigger = "time",
            Time = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            RecurType = "once",
            OnceDate = ReminderEngine.DateKey(at),   // 跨午夜时自然落到明天
            Message = Lf("Quick_Done", minutes),
            Sound = true,             // 计时器无声等于没有计时器
            PopupTimeoutSeconds = 0,  // 卡片形态下 0=常驻到点掉，别让人错过自己刚设的那一下
            GraceMinutes = 5,
            CatchUpIfMissed = true,   // 那一分钟恰好在勿扰/睡眠里错过时补响，而不是变成一条永不触发的死行
            Temporary = true,
        };
        // 必须经 VM 增加，理由同 FireAndAdvance 里的删除：Rows 与 Models 是两份平行集合。
        // 直接 _config.Reminders.Add 的话这条根本不会出现在列表里——用户想反悔都没有入口，
        // 而文档写着「响过就自己从列表里删掉」，等于承诺了一个它从没进过的列表。
        // 直接调、不做 null 兜底：BuildShell 先建 _main 再建托盘，而本方法只能由托盘菜单进来，
        // _main 必然已在；写成 _main?. 只会造一个谁也测不到的分支，还让"气泡说已设、其实没加"成为可能。
        _main!.AddReminderRow(r);   // VM 内部会存盘
        // 「错过必补」只对启动时就存在的提醒生效（防「刚建一条早时刻的就立刻补弹」）。快速提醒的时刻
        // 永远在未来，那道防线对它没有意义，却会让上面这个 CatchUpIfMissed 完全失效——补登记一下。
        _startupReminderIds.Add(r.Id);
        // 预置引爆时刻（见方法头的精度说明）。PendingForDate 要跟着记，否则跨午夜引爆时
        // LastFiredDate 会记成次日，与 ArmAt 同一个坑。
        var st = StateOf(r);
        st.PendingFireAt = DateTime.Now.AddMinutes(minutes);
        st.PendingForDate = ReminderEngine.DateKey(st.PendingFireAt.Value);
        // 勿扰期间 ReminderTick 整轮直接返回，这条也不例外——气泡必须说实话，不能承诺一个做不到的时刻。
        bool dnd = DndRemaining != null;
        ShowToast("Clockwork", dnd ? Lf("Toast_QuickSetDnd", minutes) : Lf("Toast_QuickSet", minutes, r.Time),
                  dnd ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
    }

    // 「今天不再提醒」：当天剩余的这条全部作废（含在途的催促/循环/稍后），明天照常。
    // 比取消勾选安全——取消勾选没有到期日，忘了打开就是一条永久静默失效的提醒。
    //
    // 做成开关：再点一次即撤销。按钮就在「运行」正上方、单击即生效、没有确认框，误点必然发生；
    // 而列表行看不出任何差别，编辑保存也撤不掉（迁移会把 SkippedDate 原样带到新 id），
    // 没有这个反向操作就只剩「删掉重建」和「手改状态文件」两条路。
    public void SkipReminderToday(Reminder r)
    {
        var st = StateOf(r);
        var now = DateTime.Now;
        bool undo = st.SkippedDate == ReminderEngine.DateKey(now);
        if (undo) st.SkippedDate = "";
        else
        {
            ReminderEngine.SkipToday(st, now);
            // 「仅一次」且这一次就是今天 → 跳过之后它永远不会再触发（周期过滤此后一直为 false），
            // 而 ShouldDisableAfterOnce 只在触发路径上判定，跳过永远走不到那儿。不在这里收尾的话，
            // 列表里就留下一条勾着的、和活提醒长得一模一样、却再也不会响的死行。
            if (r.RecurType == "once" && ReminderEvent.UsesRecurrence(r.Trigger)
                && ReminderEngine.IsRecurrenceDueToday(r, now))
            {
                if (r.Temporary) _main!.RemoveReminderRow(r);   // 快速提醒被跳过=不要了，整条删掉
                else { r.Enabled = false; SaveConfig(); _main?.RefreshReminderRows(); }
            }
        }
        ReminderStateStore.Save(_statePath, _reminderStates, durable: true);
        _main?.RefreshReminderRows();   // 跳过状态显示在时间列上，改了就得让那一行重画
        ShowToast("Clockwork", Lf(undo ? "Toast_SkipTodayOff" : "Toast_SkipToday",
                                  ReminderDisplay.TextSummary(r, _config.ActionGroups)),
                  Views.ToastLevel.Info, key: "skip:" + r.Id);   // TextSummary 自己已经截断，别再套一层
    }

    // —— 事件触发（空闲 / 锁屏 / 解锁 / 唤醒 / 插拔电源 / 低电量）——
    // 会话与电源事件由 Windows 广播，订阅即得；空闲与电量没有通知可订，搭提醒计时器轮询。
    private readonly HashSet<string> _idleFired = new();         // 本轮离开已触发过的提醒 id（人回来即清）
    private readonly HashSet<string> _busyFired = new();          // 本轮连续使用已触发过的（离开满一分钟即清）
    private readonly HashSet<string> _lowBatteryFired = new();   // 本轮低电量已触发过的（充回阈值以上即清）
    private bool? _lastOnAc;                                     // 上次读到的电源状态；null=还没读过（首轮只记基线）
    // 当前这轮连续使用的起点；null=正在离开中（或还没起算）。
    // **单调 tick，不是墙钟。** 它量的是「这一段连续用了多久」，而墙钟会跳：夏令时前移、
    // 一次 NTP 校正，都会让差值凭空多出一个小时——用户刚坐下，所有久坐提醒同时炸出来；
    // 往回跳则差值为负、`(int)` 向零截断，久坐提醒被静默压制最多一小时，一声不响。
    // 它的镜像（空闲）本来就是单调的：Native.IdleTime.Minutes() 走的是 Environment.TickCount。
    // 两个数放在同一个判据里比较，就必须来自同一种钟（LongPressGate 里那条「单调递增且不受
    // 改系统时间影响」写的是同一件事）。
    private long? _busySinceTick;
    private string _lastEvent = "";
    private DateTime _lastEventAt = DateTime.MinValue;

    private void WireSystemEvents()
    {
        // 订阅这些是纯白拿：Windows 本来就在广播，不订就只能靠轮询猜锁屏、唤醒、插显示器。
        // 回调在各自的通知线程上来（SystemEvents 有自己的线程，NetworkChange 走线程池），
        // FireReminderEvent 内部统一切回 UI 线程，故这里不必各自 Dispatcher。
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch { }   // 无交互式桌面等边角场景下会抛；事件触发失效即可，不该拖垮启动
    }

    private void UnwireSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        catch { }
    }

    // 插拔显示器、改分辨率、切换投影模式，都从这一个通知来（对应 SystemEvents.DisplaySettingsChanged）。
    // 一次插拔 Windows 常连发好几条（先认出设备、再重排桌面、DPI 再定一次），全靠 FireReminderEvent
    // 门口那个 3 秒同名去抖收敛成一次——这个事件是去抖最吃紧的一个，别把那道闸拆了。
    //
    // 只给一个「变了」，不区分接上还是拔掉：要真正分清得自己缓存上一轮的显示器集合并做差集，
    // 而扩展屏路由的两种写法（接上→扩展模式+开那几个窗口 / 拔掉→仅电脑）在动作组里用「显示模式」
    // 系统命令加进程条件就能各自表达。真要「仅接上时」再说，别为它先背一份显示器拓扑快照。
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        FireReminderEvent("display");
        // 手势的最小笔画长度是按主屏宽度算的，而屏幕刚变了。ApplyMouseHook 自己会比
        // 「算出来的值和正在用的那个一样吗」，一样就什么都不做，所以这里无条件叫一次是安全的。
        ApplyMouseHook();
    }

    // 网络通断。NetworkAvailabilityChanged 给的是「这台机器还有没有可用网络」这个粗粒度事实，
    // 正好对上「重连 VPN / 重挂网络盘」这类需求——它们要等的就是"网络回来了"，而不是某块网卡的状态。
    //
    // 刻意不做「限定网络名 / SSID」：那要引 WinRT 的 NetworkInformation（家里 wifi 一套、公司一套
    // 是它的招牌场景），而本程序的进程条件 + 路径存在条件已经能表达大部分「在哪台机器上」的区分。
    // 真有人要按 SSID 分流，那时再加一个条件字段，比现在先背一个 WinRT 依赖划算。
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        => FireReminderEvent(e.IsAvailable ? "netUp" : "netDown");

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
            if (!ReminderEvent.ShouldFire(r, ev, now, PeekState(r))) continue;
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
        // 与 ReminderEngine 的 arm 出口同口径：记基准日而非引爆日，否则延迟把引爆推过午夜时，
        // LastFiredDate 会记成次日、把次日那次挡掉。事件触发只经这条路，Decide 的 Arm() 管不到。
        st.PendingForDate = ReminderEngine.DateKey(baseTime);
    }

    // 只读查询运行态，查不到给 null。给 ShouldFire 这类纯谓词用——C# 求值实参在前，
    // 写 StateOf(r) 的话每次锁屏/解锁/插拔电源都会给配置里每一条提醒（包括禁用的、按时间触发的）
    // 建一行状态，把「没状态就不建行」这个前提（孤儿清理与 Save 跳空都建立在它上面）打掉。
    private ReminderState? PeekState(Reminder r) => _reminderStates.GetValueOrDefault(r.Id);

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
        // 连续使用的计时基线，与「本轮已触发」的复位一起维护——它是空闲的镜像，所以两件事必须在同一处算：
        // 离开满一分钟 = 歇过了，streak 归零、下一轮重新起算；还在用则起点保持不动（第一次见到就落桩）。
        // 基线维护刻意在 fire 之外（勿扰期间也照走），与 _lastOnAc 同一条口径：勿扰的语义是「期间发生的
        // 事不算数」，而不是「期间的时间不存在」——不然勿扰结束后会拿一个两小时前的起点当场判成久坐。
        if (idle >= 1) { _busySinceTick = null; _busyFired.Clear(); }
        else _busySinceTick ??= Environment.TickCount64;
        int busyMinutes = _busySinceTick is long bs ? (int)((Environment.TickCount64 - bs) / 60000) : 0;
        var (onAc, pct) = Native.PowerStatus.Read();
        // 首轮只记基线不触发：否则程序一启动就会把「现在接着电源」当成一次刚插上。
        if (fire && _lastOnAc is bool was && was != onAc) changed |= FireEventNow(onAc ? "acPlugged" : "acUnplugged", now);
        _lastOnAc = onAc;

        foreach (var r in _config.Reminders.ToList())
        {
            if (r.Trigger == "idle")
            {
                if (!fire) continue;   // 勿扰中不触发也不置位：空闲跨过勿扰结束仍在持续的，结束后照常响
                if (!ReminderEvent.ShouldFire(r, "idle", now, PeekState(r))) continue;
                if (!ReminderEvent.IdleDue(r, idle, _idleFired.Contains(r.Id))) continue;
                _idleFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
            else if (r.Trigger == "busy")
            {
                // 与 idle 完全对称：勿扰中不触发也不置位，跨过勿扰结束仍在持续的那一轮照常响。
                // （复位不在这儿——它在循环外和 _busySinceTick 一起算，理由同 lowBattery 的复位：
                //   「人歇过了」是个与这条现在该不该响无关的事实。）
                if (!fire) continue;
                if (!ReminderEvent.ShouldFire(r, "busy", now, PeekState(r))) continue;
                if (!ReminderEvent.BusyDue(r, busyMinutes, _busyFired.Contains(r.Id))) continue;
                _busyFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
            else if (r.Trigger == "lowBattery")
            {
                // 复位判定不看 Enabled/星期/勿扰：电量涨回去了就是涨回去了，跟这条现在该不该响无关。
                if (ReminderEvent.LowBatteryReset(r, pct, onAc)) { _lowBatteryFired.Remove(r.Id); continue; }
                if (!fire) continue;
                if (!ReminderEvent.ShouldFire(r, "lowBattery", now, PeekState(r))) continue;
                if (!ReminderEvent.LowBatteryDue(r, pct, onAc, _lowBatteryFired.Contains(r.Id))) continue;
                _lowBatteryFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
        }
        return changed;
    }

    // 触发一条提醒：静默组 / 语音 / 通知 / 弹窗（是-否-稍后）。
    // 返回 (result, snoozeMinutes)：result ∈ yes/no/ok/snooze(手点稍后)/autosnooze(超时自动稍后)/""(超时未确认)。
    // 手点与自动必须分开传到 FireAndAdvance——只有后者计入「连续无人应答」的降级计数。
    // preview=编辑器「预览这条」：被动提醒 toast 固定几秒自动消失（预览是试看，不该常驻堆屏）。
    private (string Action, int? Snooze) FireReminder(Reminder r, bool preview = false)
    {
        if (!string.IsNullOrWhiteSpace(r.SilentGroupId))
        {
            var g = ActionGroupResolver.Resolve(_config.ActionGroups, r.SilentGroupId);
            if (g != null && g.Enabled) RunGroupAsync(g, unattended: true);   // 提醒到点自己跑的，没人在看
            // 引用的组被删/被禁用时不再静默装作成功——夜间例程停摆却零反馈是最难察觉的故障；警告但仍记已处理（不重弹刷屏）。
            else WarnToast(Lf(g == null ? "Warn_SilentGroupMissing" : "Warn_SilentGroupDisabled", StepHelpers.Ellipsis(r.Message)));
            // 静默组无确认交互，跑一次即完结——返回 "ok"：让 UpdateAfterFire 停掉催促（否则配了 repeatMinutes
            // 会每 N 分钟把整组（可能含静音/关应用/锁屏）重跑），同时排下一轮「循环运行」（intervalMinutes）——
            // 静默任务的周期轮询正是靠这个返回值成立，改动它会悄悄弄断循环。
            return ("ok", null);
        }
        // 提示音先于朗读：一声短提示把头抬起来，随后的朗读才有人在听。两者都开时必须给朗读一段前导——
        // SystemSounds.Play() 是异步的，不传前导的话「叮」会和第一个字同时响，等于两个都没听清。
        // 静默组走不到这里（上面已 return）——「静默」就该是静默的。
        if (r.Sound) ReminderActions.Ding();
        if (r.Speak) ReminderActions.Speak(r.Message, r.Sound ? ReminderActions.SoundLeadInMs : 0);
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
        // 唯一的例外在链的末端：连续 MaxAutoSnoozes 轮无人应答后降级成常驻卡片（见 FireAndAdvance）。
        // 那不是把投递保证交给 UI，而是承认「一小时没人理=人不在」，此时继续抢焦点已无收件人；
        // 卡片够撑到人回来，代价（会话态、重启即失、没有是/否按钮）都写在降级那处。
        int psecs = ReminderEngine.PopupTimeoutSeconds(r);
        int timeoutSecs = psecs > 0 ? psecs : ReminderEngine.UnattendedPopupSeconds;
        int? autoSnooze = r.RepeatMinutes > 0 ? null : ReminderEngine.UnattendedSnoozeMinutes;
        var shownAt = DateTime.Now;
        var (act, snooze) = Views.ReminderPopupWindow.Show(r.Message, confirm, timeoutSecs, autoSnooze);
        // 弹窗路径也在托盘「最近通知」留痕——卡片路径由 ShowToast 顺手记，这条路之前没人记，
        // 于是「22:00 到底弹没弹」只能去翻状态文件反推。无人应答（超时未确认/自动稍后）记警示级：
        // 那是「你需要知道」的结果，不是看过就算的回执。预览不留痕，与卡片路径同口径。
        // DurationMs 必须给真值：NotificationEntry 的契约是「重放须忠实还原原卡片的时长」，
        // 留默认 0 等于声明"常驻"，于是回看一条 60 秒的提醒会得到一张永不自动关的卡片。
        if (!preview) _notifications.Add(new NotificationEntry(shownAt, Strings.Get("Tray_ReminderTitle"), r.Message,
            Warn: act is "" or "autosnooze", Key: ReminderToastKey(r), DurationMs: timeoutSecs * 1000));
        if (act == "yes") ReminderActions.RunOnYes(r.OnYes, _config.ActionGroups, g => RunGroupAsync(g), WarnToast);
        if (act is "snooze" or "autosnooze") return (act, snooze);
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
    /// <summary>快捷面板的总开关。托盘菜单据此决定要不要摆那一项。</summary>
    //
    // 托盘那一层只拿得到一个 App（见 TrayIcon.Rebuild 的签名），所以状态得从这儿露出去，
    // 与 DndRemaining 同一条路。菜单是在 Opening 时重建的，于是它每次弹出都读到最新的值。
    public bool PanelEnabled => _config.Settings.PanelEnabled;

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

    /// <summary>安静到今天结束（次日 0 点）。</summary>
    //
    // 按小时的档位（1/2/4/8）盖不住真实场景里最常见的那一句：「我今天不想被提醒」。
    // 而它此前唯一的表达方式是**把每一条提醒挨个取消勾选**，第二天再挨个勾回来——
    // 那既麻烦，又留下一个第二天想不起来复原的状态（表现成「提醒功能坏了」，
    // 而真相是自己昨天关的）。这一档到点自动失效，不需要记得回来开。
    //
    // 刻意**不做**「永久关掉提醒」的总开关：那正是上面那种失败模式的制度化版本。
    // 勿扰的语义一直是「暂时」，这一档也是——只是把「暂时」的上限从 8 小时推到今天结束。
    public void PauseRemindersUntilEndOfDay()
    {
        var now = DateTime.Now;
        _dndUntil = now.Date.AddDays(1);   // 次日 0 点
        ShowToast("Clockwork", Strings.Get("Toast_DndEndOfDay"), Views.ToastLevel.Info);
    }

    /// <summary>「恢复提醒（剩 …）」那一行的文案。托盘与面板腰带共用这一份。</summary>
    //
    // 一小时以内按分钟、超过就按小时。加了 8 小时和「到今天结束」两档之后，
    // 原来那句固定按分钟的写法会说出「剩 480 分钟」——一个要在心里除一遍才知道是多久的数。
    // 分界取 60 分钟：那正好是原来最小的一档，所以旧行为在旧档位上一字不变。
    //
    // 两档的取整方式**故意不同**：
    //   分钟：向上取整。还剩 30 秒时要说「剩 1 分钟」——说「剩 0 分钟」会被读成「已经结束了」。
    //   小时：四舍五入。这一档不存在读成 0 的风险（低于 60 分钟就走上面那支了），
    //         而向上取整会让「还剩 61 分钟」说成「剩 2 小时」，凭空多报 59 分钟。
    // **分支要看「要显示的那个数」，不是原始值。** 曾经写的是 `left.TotalMinutes < 60`，
    // 而分钟那支又向上取整——于是刚点下「1 小时」那一刻 left 是 59.99 分，分支选分钟、
    // 取整又抬成 60，菜单上读出来就是「剩 60 分钟」。档位写着 1 小时、回手说 60 分钟，
    // 而那正是用户点完立刻会去核对的一行。根因是分支与显示各自取整，中间那一秒两边不一致。
    // 先取整再分支，两边就永远是同一个数。
    public string DndResumeLabel(TimeSpan left)
    {
        int mins = (int)Math.Ceiling(left.TotalMinutes);
        return mins < 60
            ? Lf("Tray_DndResume", mins)
            : Lf("Tray_DndResumeHours", (int)Math.Round(left.TotalHours, MidpointRounding.AwayFromZero));
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

    /// <param name="by">谁在触发这一步。**两件事都跟着它走**：成功要不要弹回执、要不要防连点。</param>
    //
    // 合成一个参数而不是两个 bool：它们不是两个独立开关，是同一个事实（谁按的）的两个后果，
    // 分开写迟早会出现「安静但仍然防连点」这种没人想要的组合。
    //
    // **回执**：「点了运行按钮」和「划了一道手势」要的东西相反——
    //   按钮是在试跑，屏幕上多半什么也看不出来，那张卡片就是它**唯一**的结果，必须留；
    //   手势是一挥而过的操作，十条默认手势里八条效果肉眼直接可见，再弹一张卡等于每天
    //   几十次地把一件你已经看见的事又说一遍。
    //   但**失败一定要说**：手势失败是彻底无声的（窗口被未保存对话框挡着关不掉、搜索时
    //   其实什么都没选中），没有那张卡就只剩「我画了，没反应」。所以安静的只有成功这一档。
    //
    // **防连点**：那道闸挡的是「气泡回执要几秒才出，急着连点运行按钮会把同一步跑两遍」——
    //   那是按钮的毛病。手势要花一秒认真画出来，画两次就是**故意**要跑两次
    //   （↖ 切换置顶正是这种用法：钉住、再放开）。而闸一旦拦下就是直接 return、什么都不说，
    //   于是连着画两次「最小化」时第二次无声消失——正是这个功能里最难自查的那种表现。
    //   注入本身已由 InjectionLock 串行化，手势这一档不需要再加一道。
    public void RunStepAsync(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                             IntPtr gestureOrigin = default)
    {
        bool quiet = by == StepTrigger.Gesture;
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
        // 问句步骤：与 message 同理，得在 UI 线程弹框，不能丢进后台当普通步骤跑
        //（丢进去会撞上 InvokeStepAction 那句「这条路径上没有可以发问的界面」）。
        // 单步跑一个问句步骤，答案无处可去——后面没有步骤了——所以问完就把它显示出来，
        // 那是这一步在「试一试」语境下唯一能给出的结果。
        if (step.Kind is "prompt" or "choice")
        {
            var one = new RunVars();
            var answer = AskUser(step, one, owner);
            if (answer != null && !quiet)
                ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + answer, Views.ToastLevel.Info);
            return;
        }
        // 单飞守卫：只对按钮那一档（理由见 by 参数）。手势不设闸——它拦下时是无声的，
        // 而「画了没反应」正是这个功能最难自查的状态。
        bool guard = by == StepTrigger.Button;
        if (guard && Interlocked.Exchange(ref _stepRunning, 1) == 1) return;
        // 在 UI 线程、派后台之前记：这一刻前台还是用户那个窗口，等步骤自己去抢就晚了。
        // 手势触发额外带起笔窗口（gestureOrigin），窗口管理类「当前窗口」优先打它。
        Native.WindowManager.MarkRunBaseline(gestureOrigin);
        var selfPaths = new[] { _exePath };
        Task.Run(() =>
        {
            try
            {
                _runGate.Begin();   // 同上：放进 try，否则 finally 跳过后「运行这一步」会永久点不动
                var mark = StepRunner.RunStepMarkRepeat(step, a => ConfirmDestructive(a, owner), selfPaths);
                if (!quiet || mark.Fail > 0)
                    ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + mark.Mark, mark.Fail > 0 ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
            }
            finally { _runGate.End(); if (guard) Interlocked.Exchange(ref _stepRunning, 0); }
        });
    }

    // 「运行整组」/提醒静默组/onYes 组/组编辑器试跑：后台跑动作组。返回本次运行的取消闸——
    // 组编辑器据此把「试跑」按钮就地变「停止」，并在关窗时收掉这次运行（不留孤儿）。
    // onDone：跑完（或被取消/异常）后在 UI 线程回调一次，用于把按钮翻回去。
    //
    // 竞态窗口：本方法把 Task 派下去就返回，EnterTopLevel 的登记要等后台线程真正跑到那一行才发生——
    // 派发与登记之间有个空窗，不是「几微秒」那种可以忽略的窗口（ToggleGroupByHotkey 的连按去重正是
    // 撞在这个窗口上，那边有更完整的说明）。落进空窗期间查「是否已顶层在跑」，看到的还是登记前的状态。
    public RunCancel RunGroupAsync(ActionGroup group, Window? owner = null, Action? onDone = null,
                                   bool unattended = false, IntPtr gestureOrigin = default)
    {
        // 快照与 deps 都在调用线程（UI）上先建好，不把活对象带进后台：
        //   · SnapshotForRun 复制步骤列表——后台 foreach 组步骤时，UI 线程若在改同一个组（删除守卫的
        //     联动清理会就地改其他组的 Steps），背景枚举撞见活列表被改写会抛「集合已修改」。这条注释
        //     曾经只是给「万一手快」的历史兜底；组编辑器上线试跑（本方法的 owner/onDone 参数正是为它加的）
        //     之后，它是第一个能在运行进行中还继续编辑同一份步骤列表的调用方——背景枚举撞见活列表被
        //     UI 线程改写从「理论风险」变成「必然发生」，这份注释因此从历史说明升级成不可删的强制约束。
        //   · BuildGroupDeps 同理在 UI 线程取 _config.ActionGroups 的快照，道理一样（见其内部注释）。
        var snap = group.SnapshotForRun();
        // 同 RunStepAsync：在 UI 线程、派后台之前记下前台，供组里的「恢复活动窗口」用；
        // 手势触发额外带起笔窗口，组里的「当前窗口」窗口动作优先打它。
        Native.WindowManager.MarkRunBaseline(gestureOrigin);
        var deps = BuildGroupDeps(owner, unattended);
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

    // owner：本次运行的弹窗归属窗口（null=无归属：后台触发，弹窗置顶但不认主窗为父）。嵌套子组与 onYes 组共用同一份 deps，
    // 故 owner 自动传遍整条引用链，不必在每一层再传一次。
    //
    // unattended：这一次运行有没有人在看。**不能拿 owner==null 当判据**——热键、托盘、面板格子
    // 同样没有归属窗口，而那几条路上用户就在键盘前，破坏性动作正该弹一句确认。
    // 真正没人的只有两条：提醒到点跑的静默动作组，和 --run-group 那种外部触发
    //（计划任务 / AHK / Stream Deck，RunGroupByName 的注释自己写着「没人盯着屏幕」）。
    // 传 null 给 confirmDestructive 就是告诉 StepRunner「这条路上没有可以问的人」，
    // 那边会如实跳过并记一行，而不是弹一个置顶模态把整组卡住——急停键只在步骤之间被检查，解不开它。
    private GroupDeps BuildGroupDeps(Window? owner = null, bool unattended = false)
    {
        var selfPaths = new[] { _exePath };
        var groups = _config.ActionGroups.ToList();   // 组列表快照（UI 线程取）：后台 Resolve 不再枚举 UI 正在增删的活列表
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            // 把本次运行的取消闸一路传进步骤执行层：等窗口 / 置前重试 / 置前延时都可能挂几秒到几十秒，
            // 只查全局急停的话，用户取消了动作组，这些步骤过一会儿照样把窗口拽到前台、把按键打进去。
            // 接住 InvokeStepAction 的返回值。以前这里是 `RunStep = s => StepRunner.InvokeStepAction(...)`，
            // ActionResult 被 C# 当表达式语句静默丢弃——于是「脚本不存在」「找不到窗口」「启动失败」这类
            // Warn 在动作组这条路径上一个都到不了用户面前，组照样「跑完」。同一个步骤放开机清单里
            // 有「⚠」可查（LaunchSequence 走 StepRunner.MarkOf），而热键 / 定时静默组——正是无人值守跑的
            // 那条——什么都不说。Unverified（「发出去了但没法证实」）有意不报：它不是失败，报了只会变成噪音。
            RunStep = s =>
            {
                var r = StepRunner.InvokeStepAction(s, unattended ? null : a => ConfirmDestructive(a, owner), selfPaths, deps.Cancel, deps.Vars);
                if (r.HasWarning) LogGroupStepWarn(s, r);
            },
            // 这两个回调都会开模态（ShowGroupMessage 的 Confirm / Info 两种形态、AskUser 全部），
            // 而模态在没人看的运行里会把整组挂到永远——BrandDialog 自己写明急停热键破不开模态，
            // 而 deps.Cancel 只在回调返回之后才里得到。所以 unattended 必须一路传进去，
            // 与上面 RunStep 那半（`unattended ? null : ConfirmDestructive`）同一个口径。
            ShowMessage = s => ShowGroupMessage(s, owner, unattended),
            AskUser = s => AskUser(s, deps.Vars, owner, unattended),
            // onYes 组的结局也丢弃：ReminderActions.RunOnYes 是 Action<ActionGroup> 契约（提醒弹窗路径共用），
            // 且语义上 onYes 是「是」分支的副作用出口——它内部的确认框属于那条子流程，把它的中止回灌成父组中止，
            // 会变成「点了是反而整组停了」，比现状更难解释。
            RunOnYes = s => ReminderActions.RunOnYes(s.OnYes, groups, g => { _ = ActionGroupRunner.RunGroup(g.SnapshotForRun(), deps); }, WarnToast),
            Speak = t => ReminderActions.Speak(t),   // 显式 lambda：Speak 有可选参数，方法组匹配不上 Action<string>
            OnStepError = (s, ex) => LogGroupStepError(s, ex),
            OnStepSkipped = (s, skip) => LogGroupStepSkipped(s, skip),
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
                if (target.Skip != null) { deps.OnStepSkipped(s, target.Skip); return GroupRunResult.Skipped; }
                var res = ActionGroupRunner.RunGroup(target.Group!.SnapshotForRun(), deps);
                if (res == GroupRunResult.Skipped)
                {
                    deps.OnStepSkipped(s, ActionGroupResolver.Reentrant());
                }
                return res;
            },
        };
        return deps;
    }

    // 动作组内某步没有正常跑完的统一出口：记一笔到错误日志，并弹一次托盘气泡，随后整组继续（不静默中止）。
    // 三种结局只在三件事上不同——日志措辞、气泡文案与等级、合并键分桶——所以只在这里传参，别再各抄一份。
    // 措辞不能互借：说成「已跳过」会让人以为是条件没满足，说成「异常」又是给一个不存在的故障去查。
    // 合并键先按结局分桶再按步骤摘要分桶：同一步上次是真异常、这次只是良性跳过（或反过来），两张卡不该
    // 互相盖掉——用户需要分别看到「确实失败过」与「最近一次只是被跳过」；而同一步在多轮/多次引用里反复
    // 出事时靠合并键叠加计数，不会堆一摞 12 秒的卡片（整组 Repeat 会让同一步出事很多次）。
    private void LogGroupStep(LaunchStep step, string what, string detail, string bucket, string toast, Views.ToastLevel level)
    {
        var summary = StepDisplay.StepSummary(step);
        // 日志行整行英文、气泡仍然本地化：理由见 AppendErrorLog 上方那段。
        // 三种结局的措辞不能互借（见本方法上面那条），英文里同样分成 failed / had no effect / was skipped。
        // 摘要要单独按英文再渲染一次：StepDisplay 走 resx（「设音量 30%」），
        // 不换文化的话这一行会是「英文框 + 界面语言的摘要」，贴到 issue 里读的人认不出那是哪一步。
        AppendErrorLog($"action-group step {what}: {Strings.InEnglish(() => StepDisplay.StepSummary(step))} - {detail}");
        ShowToast("Clockwork", toast, level, key: bucket + ":" + summary);
    }

    // 真抛出的异常。「这次没跑」但没有异常的情况（嵌套组引用缺失/禁用/重入）走 LogGroupStepSkipped。
    private void LogGroupStepError(LaunchStep step, Exception ex)
        => LogGroupStep(step, "failed (skipped, the action carried on)", ex.Message, "groupstep",
                        Lf("Mark_Exception", StepDisplay.StepSummary(step)), Views.ToastLevel.Warn);

    // 跑了但没生效（ActionResult：脚本不存在 / 需要 PowerShell 7 / 启动失败 / 找不到窗口…）。
    // 气泡复用 Toast_GroupStepSkipped——它的文案就是中性的「{0}，{1}」，「已跳过」只出现在日志行里。
    // 收整个 ActionResult 而不是一个字符串：原因要两种语言各渲染一次（气泡跟界面、日志固定英文）。
    private void LogGroupStepWarn(LaunchStep step, ActionResult r)
        => LogGroupStep(step, "had no effect (the action carried on)", r.WarningEn ?? "", "groupwarn",
                        Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), r.Warning ?? ""),
                        Views.ToastLevel.Warn);

    // 压根没跑（嵌套组引用的目标缺失/已禁用/重入）。气泡文案必须带上具体原因，不能只报步骤摘要，
    // 否则用户只能打开日志文件才知道为什么。已禁用是正常配置状态（与 LaunchSequence 同口径），用 Info。
    private void LogGroupStepSkipped(LaunchStep step, GroupSkip skip)
        => LogGroupStep(step, "was skipped (the action carried on)", skip.TextEn(), "groupskip",
                        Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), skip.Text()),
                        skip.Benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn);

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
    private MsgResult ShowGroupMessage(LaunchStep step, Window? owner = null, bool unattended = false)
    {
        var form = StepHelpers.MessageFormOf(step);
        // 无人值守：降级成气泡，不开模态。消息步骤只是告知，没人点不该把整组停住，
        // 所以返回 Ok（不是 Yes/No）：那两个是「有人答了」的意思，onYes 不能凭空跑、
        // 整组也不能凭空中止。Card 形态本来就是这条路，这里只是把另两种归并过来。
        if (form == MessageForm.Card || unattended)
        {
            ShowToast("Clockwork", step.Message, Views.ToastLevel.Info,
                      step.PopupSeconds > 0 ? step.PopupSeconds * 1000 : 0, key: StepCardKey(step));
            return MsgResult.Ok;
        }
        return Dispatcher.Invoke(() =>
        {
            if (form == MessageForm.Confirm)
                return Views.BrandDialog.Confirm(owner, Strings.Get("Confirm_Title"), step.Message) ? MsgResult.Yes : MsgResult.No;
            Views.BrandDialog.Info(owner, "Clockwork", step.Message);
            return MsgResult.Ok;
        });
    }

    // 问句步骤（用户输入 / 用户选择）的弹框。与 ShowGroupMessage 并排：同样跑在后台线程上，
    // 同样必须跳回 UI 线程才敢碰窗口。返回 null = 用户取消，调用方据此中止整组。
    //
    // 提示语走一次占位替换：「搜索『{关键词}』？」这种二次确认要用得上前面问来的值。
    // 选项同理——一串选项可以是上一步产出的（虽然现在还没有哪一步能产出多行，但替换是同一行代码）。
    private string? AskUser(LaunchStep step, RunVars vars, Window? owner = null, bool unattended = false)
    {
        // 无人值守：不开模态，如实说一声，当作取消。
        // 返 null （= 中止整组）而不是跳过继续：这一步的结果要写进变量给后面的步骤用，
        // 问不出答案时那些步骤本就没有正确可做的事（拿个空值接着跑更糟）。
        // 不能默不作声：热键 / 定时静默组正是无人看的那条路，它不说就真的没人知道。
        if (unattended)
        {
            LogGroupStepWarn(step, ActionResult.Warn("Warn_AskNoUi"));
            return null;
        }

        var title = Strings.Get("Ask_Title");
        var msg = FillForUi(step.Message, vars);
        if (step.Kind == "choice")
        {
            var options = StepHelpers.ChoiceOptions(FillForUi(step.Text, vars));
            // 一个选项都没配：弹一个空列表等于让用户面对一个点不动的框。如实说一声，当作取消处理。
            if (options.Count == 0)
            {
                WarnToast(Strings.Get("Warn_ChoiceNoOptions"));
                return null;
            }
            return Dispatcher.Invoke(() => Views.BrandDialog.Pick(owner, title, msg, options));
        }
        return Dispatcher.Invoke(() => Views.BrandDialog.Ask(owner, title, msg, FillForUi(step.Text, vars)));
    }

    // 弹框上要显示的文字里的占位替换。与 StepRunner.Fill 是同一件事，只是那一份在 Engine 里、
    // 是 private，而这里替换的是**要显示给人看的**东西，一律不转义（转义了会把中文变成一串 %E4）。
    private static string FillForUi(string? text, RunVars vars)
    {
        if (!StepPlaceholder.Has(text)) return text ?? "";
        string clip = "";
        if (StepPlaceholder.UsesClipboard(text))
        { try { clip = Engine.SystemCommands.ClipboardText(); } catch { } }
        return StepPlaceholder.Apply(text, clip, urlEncode: false, vars);
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
    // owner=null（开机清单/托盘重跑/热键/提醒触发）不再补成主窗：无主时 BrandDialog 自己置顶，
    // 见得着且不会把主界面一起拽到前台。
    private bool ConfirmDestructive(string action, Window? owner = null)
        => Dispatcher.Invoke(() => Views.BrandDialog.Confirm(
            owner, Strings.Get("Confirm_Title"), Lf("Confirm_Destructive", action), Views.ToastLevel.Warn));

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
        // 中键长按的开关与阈值也在设置页，跟着同一次保存生效——否则改完得重启才算，
        // 而这个开关恰恰是用户会反复开关来试手感的那种。
        ApplyMouseHook();
        // 面板总开关同理，而它管的是**热键注册**：关掉就得当场把 Ctrl+Alt+Space 让出来，
        // 开回来就得当场抢回。只有录键那条路会走 ResumeHotkeys（那里统一重建功能键），
        // 而设置页那个复选框不经过录键——不在这儿补一句，改完要等下一次录键或重启才生效。
        // 与 RebindGroupHotkeys 同一道 _hotkeysSuspended 闸：捕捉期间一律不动键，
        // 捕捉一定以 ResumeHotkeys 收尾，那里会按「急停先、组后」的次序统一重建。
        if (!_hotkeysSuspended)
            RebindFunctionHotkey(PanelHotkeyId, _config.Settings.PanelEnabled ? _config.Settings.PanelHotkey : "",
                                 ref _panelHotkeyFail);
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
        // SkippedDate 必须跟着迁：用户上午点了「今天不再」、随后改了一下文案，跳过就被悄悄撤销、
        // 提醒照常响——而编辑是撤销跳过的唯一入口，等于让人在毫不知情的情况下误撤。
        // 它也是三者中唯一可能单独存在的（SkipToday 会把 SnoozeUntil/NextIntervalAt 一起清成 null），
        // 所以下面的建档条件必须把它算进去，否则永远走不到赋值那一步。
        if (old.SnoozeUntil != null || carryInterval != null || !string.IsNullOrEmpty(old.SkippedDate))
        {
            if (!_reminderStates.TryGetValue(newId, out var st)) { st = new ReminderState(); _reminderStates[newId] = st; }
            st.SnoozeUntil = old.SnoozeUntil;
            st.NextIntervalAt = carryInterval;
            st.SkippedDate = old.SkippedDate;
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

    // 错误日志路径与写入格式的唯一出处。best-effort：写不进去不影响主流程。
    // 任何线程可调（UnobservedTaskException 在终结器线程上进来）。
    private string ErrorLogPath => Path.Combine(CfgDir, "clockwork.error.log");

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
    private readonly object _logLock = new();

    private void AppendErrorLog(string line)
    {
        // 任何线程可调（UnobservedTaskException 从终结器线程进来），而这里既读改 _dedup 的状态、
        // 又对同一个文件先截断再追加——不夹一把锁，两个线程能把彼此的行写丢。
        lock (_logLock)
        {
            var write = _dedup.Next(line);
            if (write == null) return;
            line = write;
            try
            {
                var path = ErrorLogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > ErrorLogMaxBytes)
                {
                    var all = File.ReadAllText(path);
                    int cut = all.IndexOf('\n', all.Length / 2);
                    File.WriteAllText(path, cut < 0 ? "" : all[(cut + 1)..]);
                    // 前一半被丢掉了，_dedup 记着的「上一行」可能已经不在文件里——
                    // 那时再写「同上一行」就是指着一行读者看不到的话。清账，让下一条原样写出去。
                    _dedup.Reset();
                }
                File.AppendAllText(path, $"[{Stamp()}] {line}\r\n");
            }
            catch { }   // best-effort：写不进去不影响主流程
        }
    }

    // 崩溃日志额外空一行分隔（异常带多行堆栈，挤在一起没法读），故不走 AppendErrorLog。返回路径供崩溃框指路。
    // 不参与「连续重复只记一条」：每个堆栈各不相同，而崩溃从来不是刷屏的那一类。
    // 但共用同一把锁——两处写的是同一个文件。
    private string LogError(Exception? ex)
    {
        var path = ErrorLogPath;
        lock (_logLock)
        {
            // **写之前先清账。** 这一段堆栈插在 _dedup 的账本之外，它并不知道中间隔了东西。
            // 不清的话：先记了 L，崩一次，同样的 L 再来时被判成「与上一行相同」，
            // 日志里就成了「一整段堆栈 + 一句『同上一行，且一直在发生』」——那句标记指着一行
            // 跟它毫无关系的话，而 L 本身一次都没写进去。判据本来就是「隔了别的事件之后
            // 再出现的同一条是新事件」（见 Core/LogDedup.cs）。
            _dedup.Reset();
            try { File.AppendAllText(path, $"[{Stamp()}] {ex}\r\n\r\n"); } catch { }
        }
        return path;
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
