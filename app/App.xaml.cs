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
    // toast / 错误日志 / 最近通知的整体出口（Services/AppDiagnostics.cs）。配置目录归属仍在 App，以回调传入。
    private readonly Services.AppDiagnostics _diag;

    public App()
    {
        _diag = new(() => CfgDir);
        // _config 此刻还是占位对象——编排器拿的是 () => _config 回调，现取不抓旧引用。
        _runner = new(() => _config, () => CfgDir, () => _exePath, _diag);
        _hotkeys = new(() => _config, () => new WindowInteropHelper(_main!).EnsureHandle(), _diag, _runner);
        _reminders = new(() => _config, () => CfgDir, SaveConfig, _diag, _runner);
    }
    private string _cfgPath = "";
    private bool _configSuperseded;   // 导入已把新配置写盘：本实例内存里的 _config 从此作废，禁止回写（见 MarkConfigSuperseded）
    private string _statePath = "";   // clockwork.state.json：提醒耐久运行态

    // 面板格子的点击统计（Services/PanelUsageTracker.cs；键是 LaunchStep.Id，见 Core.PanelUsageStore）。
    private readonly Services.PanelUsageTracker _usage = new();
    // 面板管理器要的活字典别名；守卫（PanelClickHookTests）按「_panelUsage, ResetPanelUsage」这段
    // 传参文本钉住「管理器必须拿到统计与清空动作」，所以别名与下面那个转发方法名都是承重的。
    private Dictionary<string, Core.PanelUsageStore.TileUsage> _panelUsage => _usage._panelUsage;
    private void ResetPanelUsage() => _usage.ResetPanelUsage();
    private string _exeDir = "";
    private string _exePath = "";
    // 运行编排（单步/整组/启动清单/急停）的整体实现，见 Services/RunOrchestrator.cs。
    // 在 App 构造里装配：DevChecks 的 --smoke 也会建主窗口并订阅 RunStateChanged，不能等到 OnStartup。
    private readonly Services.RunOrchestrator _runner;
    // 全局热键（急停/面板/一键直达/组键）的整体实现，见 Services/HotkeyManager.cs。
    private readonly Services.HotkeyManager _hotkeys;

    // 提醒引擎的运行时外壳（计时器/触发/事件线/勿扰/耐久态），见 Services/ReminderScheduler.cs。
    private readonly Services.ReminderScheduler _reminders;

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
        TaskScheduler.UnobservedTaskException += (s, ex) => { ex.SetObserved(); _diag.LogError(ex.Exception); };

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

    // 提醒耐久态 + 面板点击统计：都是「配置之外的运行态」，同一段代码读盘（实现各归
    // ReminderScheduler.LoadState / PanelUsageTracker.LoadFrom）。快速提醒清理必须赶在建
    // MainWindow（列表 VM 由 _config.Reminders 建行）之前做——LoadState 里写着原因。
    private void LoadReminderState()
    {
        _reminders.LoadState();
        _usage.LoadFrom(Path.Combine(CfgDir, "clockwork.usage.json"),
                        _config.PanelPages.SelectMany(p => p.Steps).Select(s => s.Id));
    }

    // 面板格子被点一下：计数与「跑不跑」在同一个漏斗里发生（守卫见 PanelClickHookTests），
    // 统计与去抖落盘的实现在 Services/PanelUsageTracker.cs。
    private void RecordPanelClick(LaunchStep step)
    {
        if (!_usage.RecordClick(step.Id)) return;
        RunStep(step);
    }

    // OnExit 用的同步收尾。
    private void FlushUsageNow() => _usage.FlushUsageNow();

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
        // 运行闸的 UI 线程广播 + 开机清单跑完后的「重挂鼠标钩子抢链首」接线（见 RunOrchestrator）。
        _runner.Attach();
        _runner.OnBootSequenceDone = RehookMouseAfterBoot;
        _main = new MainWindow(_config, SaveConfig, MigrateReminderState,
                               r => _reminders.PeekState(r)?.SkippedDate == ReminderEngine.DateKey(DateTime.Now));
        // 提醒行必须经主窗列表 VM 增删（Rows 与 Models 是两份平行集合，绕开 VM 会错位一位）；
        // 显示器变更后按新主屏宽度重挂钩子。出站接线在这里，构造期还没有 _main。
        _reminders.AddReminderRow = r => _main!.AddReminderRow(r);
        _reminders.RemoveReminderRow = r => _main!.RemoveReminderRow(r);
        _reminders.RefreshReminderRows = () => _main?.RefreshReminderRows();
        _reminders.OnDisplayChangedRehook = ApplyMouseHook;
        _tray = new TrayIcon(this);
    }

    private void StartEngines()
    {
        // 提醒计时器 + 系统事件订阅（启动时刻/开机分钟数的记录在 Start 里）。
        _reminders.Start();
        // 热键出站接线：面板键归面板，U 盘事件归提醒事件线（装配点在 StartEngines，
        // 此刻 _main/提醒状态都已就位，而注册动作还没有开始跑）。
        _hotkeys.OnPanelHotkey = TogglePanel;
        _hotkeys.OnDeviceEvent = _reminders.FireReminderEvent;
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
        // Activate 在前台锁拒绝时只闪任务栏、不抛异常。托盘点击通常附带前台权，失败少见，
        // 但与面板 Popup 同一套补救：Activate 没拿到就 ForceForeground（AttachThreadInput），
        // 再不行留一行一次性证据，别让「点了托盘主界面没上来」查无此案。
        if (!_main.Activate() || !_main.IsActive)
        {
            var h = new System.Windows.Interop.WindowInteropHelper(_main).Handle;
            // 失败每次都记：AppendErrorLog 自带 LogDedup，连续重复会收敛成一行，
            // 不必再夹一次性开关（与 DialogForeground 同一口径）。
            if (!Native.ForegroundNudge.Activate(h))
                AppendErrorLog("main window shown but foreground not acquired");
        }
    }

    public void ExitApp()
    {
        _reminders.Stop();
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
    //
    // 错误日志同理：AppendErrorLog 改成入队之后，最后那几行正躺在 _logQueue 里等写盘线程，
    // 而写盘线程是 IsBackground——进程一走它就没了，队列跟着一起没。所以这里补一次同步排空。
    // 它带 300ms 超时（见 LogFlushTimeoutMs），最坏情况是少写最后几行，绝不会让「点退出」变成「程序赖着不走」。
    protected override void OnExit(ExitEventArgs e)
    {
        ReminderStateStore.FlushPending();
        FlushUsageNow();
        FlushErrorLog();
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

    // —— 全局热键：实现整体在 Services/HotkeyManager.cs，这里只留门面转发。 ——
    // 供 Views 层取 App 实例（挂起/恢复热键等），唯一出处——别再各处手写 Application.Current as App。
    public static App? Instance => System.Windows.Application.Current as App;

    private void RegisterStopHotkey() => _hotkeys.RegisterStopHotkey();

    // 捕捉结束/配置变更后按当前配置重注册全部热键（急停先、组后）。
    public void ResumeHotkeys() => _hotkeys.ResumeHotkeys();

    // 录键期间暂时注销全部热键（e.Handled 拦不住 OS 级 WM_HOTKEY）。
    public void SuspendHotkeys() => _hotkeys.SuspendHotkeys();

    // 动作组热键是开关：没在跑→跑；正在跑→取消这一次运行（语义细节见 HotkeyManager）。
    private void ToggleGroupByHotkey(ActionGroup g, IntPtr gestureOrigin = default)
        => _hotkeys.ToggleGroupByHotkey(g, gestureOrigin);

    // —— 快捷面板 ——
    private Views.QuickPanelWindow? _panel;
    private Native.MouseHook? _mouseHook;
    private int _mouseHookMs;          // 当前钩子是按哪个阈值装的：只有阈值真变了才重装（0=中键关着）
    private bool _mouseHookGesture;    // 当前钩子装没装手势那半
    // 当前那个 GestureGate 是按哪个最小笔画长度造的。GestureGate 不可变，所以换主屏 / 换分辨率
    // 之后必须整个重建——这一格就是「要不要重建」的判据（见 ApplyMouseHook 里那段）。
    private int _mouseHookMinLeg;
    // 当前中键拖拽容差（物理像素）。按主屏 DPI 换算，缩放变了要跟着重装，理由同 _mouseHookMinLeg。
    private int _mouseHookTolerance;
    // 画手势时屏幕上那条看得见的线。只在监听右键期间存在——没配手势的人不该多出一扇窗。
    private Views.GestureTrailWindow? _trail;
    private bool _mouseHookFailed;     // 装失败已经报过一次：别在每次保存配置时反复弹同一条
    // 每次起一次安装就 +1，用来丢弃「已经在飞的那次」的结果（见 OnHookInstalled）。
    // 只增不减、不做回绕——它是比较相等的号，不是计数器，int 溢出要 21 亿次重装。
    private int _hookInstallToken;

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

    // 多久没有回调就算它已经掉了。**这格是给「状态行」看的**：鼠标一动就有 WM_MOUSEMOVE，
    // 而人从动鼠标到看这一行，中间隔不了这么久，4 秒足够判「已经不在了」。
    private const long StaleBeatMs = 4000;

    // 自愈**没有**「沉默多少秒算死」这种固定阈值了：判据改成在同一个 500ms 采样区间里对齐
    // 「光标动了吗」与「钩子心跳新吗」两格信号（见 CursorWatchTick 与 HookWatch.ShouldHeal）。
    // 这一格只留对齐用的余量：活着的钩子在「区间内有移动」时心跳必然落在区间内
    //（SinceBeatMs ≤ 实测区间长度），余量只需盖住 DispatcherTimer 抖动与 TickCount64
    // 约 15.6ms 的时钟粒度。**不盖系统卡顿**——卡顿时实测区间自己就变长，界跟着长，
    // 不会把卡顿误判成死亡（HookWatchTests 里 A_long_sample_window_extends_the_freshness_bound
    // 钉着这一条）。
    //
    // 旧版在这儿放过一个 1500ms 的固定沉默阈值，配上「光标 2000ms 内动过」的活动窗，
    // 两扇窗错位，用户每次移动后停手 1.5~2 秒必被当成钩子死了。实测 clockwork.error.log，
    // 2026-09-15 一天 518 行 reinstall、silentFor 有 512 行落在 1515–2000ms 贴着阈值成簇，
    // prevEverFired 全是 yes——钩子一次都没死过。
    private const long HealBeatSkewMs = 250;

    // 两次自愈之间最短间隔。同区间对齐已经消掉了误报（停手的区间根本不检测），这条下限只是
    // 保险：真碰上「装上几秒就被 Windows 摘掉」的环境，别让循环摘装的频率高于此——
    // 每次摘装之间有个输入空窗，越频繁越可能正好压掉一次中键按下。
    // 这条上限治不了病（钩子该死还是死），只保证病发时不额外推高摘装频率。
    private const long HealMinGapMs = 3000;

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
    // ponytail: 靠鼠标动才有心跳，所以「久坐不动」在心跳这一格上与「钩子死了」无法区分。
    //   现在的解法见 CursorWatchTick：只在**光标本采样区间真的动过**的区间里问心跳，
    //   停手的区间不检测——久坐不再白重装，也不需要另设一条旁路。
    private (int X, int Y) _lastCursor;
    private bool _cursorSeen;   // 采过第一次没有——第一次只定基线，见 CursorWatchTick
    // 上一个钩子的账，在摘掉它之前抄下来。**只有自愈、手动「重新挂钩」、开机后抢链首这三条路会填它**，
    // 而 ApplyMouseHook 也正是拿「它有没有值」当作「这次重装值不值得记一行」的判据。
    private string? _prevBeat, _prevRight, _prevMiddle, _prevSource;
    private long _prevSince;
    private int _prevRejected;
    // 上一个钩子装上以来，事件最长迟到了多久（见 MouseHook.UpstreamMaxMs）。与 _prevSince 一起读：
    //   死寂大 + 迟到也大 → 钩子链被排在我们前面的工具堵住了（重装治不了，只能抢链首）；
    //   死寂大 + 迟到不大 → 我们自己的钩子被系统摘了（LowLevelHooksTimeout，重装正是解药）。
    private uint _prevUpstream;
    // 上一个钩子**回调自身耗时**的峰值（见 MouseHook.CallbackMaxMs）。这一格和 _prevUpstream 是一对，
    // 回答的是两个相反的问题：
    //   upstreamMax 大 → **别人**堵了链（重装治不了）；
    //   callbackMax 接近 300 → **我们自己**正在被摘（LowLevelHooksTimeout 是回调的预算，重装只是解药）。
    //
    // 为什么非要有这一格：`hook-callback` 那行 slow 日志的阈值是 500ms，而预算是 300ms ——
    // 300~500ms 这一段正是「我们把自己搞死」的区间，却一声不响。摘掉之后 upstreamMax 还会
    // 停在最后那个小值上，看着一切正常；只有这一格会把那段历史带出来。
    private uint _prevCallbackMax;
    // 自愈那一刻**在跑的那些「会装低级鼠标钩子」的程序**（见 Core.HookSuspects）。
    //
    // 上游延迟只能证明「链被堵了」，证明不了「谁堵的」——Windows 不给枚举别人钩子的 API，
    // 唯一的旁证就是谁在场。这一格是整条诊断链的最后一环：`silentFor` 说死了多久、
    // `upstreamMax` 说是被堵还是被摘、`suspects` 说当时谁在场（一家一家关掉去试）。
    //
    // **只在摘钩那一刻采一次**，而且它证明的是「在不在场」不是「有没有罪」：
    // 在场不等于作案，名单外的工具堵了链这里一个字也不会有（理由全写在 Core.HookSuspects）。
    private string? _prevSuspects;
    // 上一次摘钩重装的时刻（TickCount64）：自愈和手动「重新挂钩」都写它。只供 HealMinGapMs 那条
    // 下限用，不参与日志。0 表示「从没摘过」——TickCount64 从开机起算不会撞到 0，拿它当哨兵安全。
    private long _lastHealAt;

    // 摘钩前抄一份中键的账，格式与右键那段对称。fired 是到点唤出次数，
    // dragEscapes 非零 = 「按住很久也不弹」其实是抖动容差把长按判成了拖拽。
    private static string MiddleAccount(Native.MouseHook h)
        => "middle " + (h.EverMiddleDown ? "down+" : "down-")
          + (h.EverMiddleUp ? " up+" : " up-")
          + $" fired={h.MiddleFireCount} dragEscapes={h.MiddleDragEscapeCount}";

    // 上一次采样的时刻（TickCount64）。量的是「本采样区间实测有多长」，
    // 与心跳年龄同一条时间轴——同区间对齐判据的一半，见 CursorWatchTick。
    private long _lastSampleAt;
    private System.Windows.Threading.DispatcherTimer? _cursorWatch;

    /// <summary>按「此刻装没装鼠标钩子」开关那个每半秒采样光标的计时器（手势与中键面板任一在听就要开）。</summary>
    //
    // 只在真的装了钩子时才跑：两样都关的人一次 GetCursorPos 都不会多花。
    private void ApplyCursorWatch(bool on)
    {
        if (on)
        {
            if (_cursorWatch != null) return;
            _cursorWatch = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(500) };
            _cursorWatch.Tick += (_, _) => CursorWatchTick();
            _cursorWatch.Start();
            _lastSampleAt = 0;    // 刚开的这一轮不带上一轮的旧账
            _cursorSeen = false;  // 基线也要重新定，否则用的是上一轮关掉时的那个位置
            return;
        }
        _cursorWatch?.Stop();
        _cursorWatch = null;
    }

    // UI 线程卡顿仪表的拍长。250ms 而不是 500ms：幽灵化的阈值是 5 秒，而这里要的是
    // 「卡了多久」这个数本身——拍得越密，解卡后第一拍测出来的时长越准（误差上限就是一拍）。
    // 代价只是一次减法，比 _cursorWatch 那次 GetCursorPos 还便宜。
    private const int StallTickMs = 250;
    private System.Windows.Threading.DispatcherTimer? _stallWatch;

    /// <summary>开关 UI 线程卡顿仪表。跟着全屏笔迹窗一起开——只有那时卡顿才不只是「本程序慢」。</summary>
    //
    // **只在会摆出全屏覆盖窗的那条路上开**，与 Win32.DisableWindowGhosting 同一格理由、同一份代价观：
    // 没开手势的人一点没变。仪表本身不改任何行为，它只回答「卡了多久」，以及（配 Probe）
    // 「卡的时候在做什么」——这两格正是前两次故障事后追查时唯一缺的东西（见 Core.UiStallWatch）。
    //
    // 不用另起线程去戳 UI 线程：DispatcherTimer 在 UI 线程被卡住时**自己不会走**，
    // 所以解卡后第一拍的「实际间隔 − 名义间隔」就是卡顿时长。少一条线程、少一处竞态。
    private void ApplyStallWatch(bool on)
    {
        if (on)
        {
            if (_stallWatch != null) return;
            // 基线先定下来，否则第一次卡顿报出来的是「从开机到现在的全部 GC / 全部分配」，
            // 那串数字很大、看着很像元凶，其实跟这一次卡顿没有关系。
            _stallGc2 = GC.CollectionCount(2);
            _stallAllocMb = GC.GetTotalAllocatedBytes(precise: false) / (1024 * 1024);
            Core.UiStallWatch.Start(AppendErrorLog, Environment.TickCount64,
                                    Environment.CurrentManagedThreadId,
                                    context: StallContext);
            _stallWatch = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(StallTickMs) };
            _stallWatch.Tick += (_, _) => Core.UiStallWatch.Tick(Environment.TickCount64, StallTickMs);
            _stallWatch.Start();
            return;
        }
        _stallWatch?.Stop();
        _stallWatch = null;
        Core.UiStallWatch.Stop();
    }

    private int _stallGc2;
    private long _stallAllocMb;

    // 「自上一行卡顿以来，笔迹窗有没有亮过」——**锁存，不是现读**。
    //
    // 为什么不能现读：卡顿行是在**解卡之后**才落笔的，而一笔手势只有几百毫秒、一次卡顿却有
    // 4.6 秒（2026-09-18 那行就是），解卡那一刻窗早被 HideSoon 藏掉了，现读 IsVisible 必然
    // 得到 hidden。于是**恰恰在「整屏覆盖窗就是元凶」的那种复现里，这一格会报成 hidden**，
    // 把人往「与覆盖层无关」上引——一个只在关键场合说谎的字段比没有这个字段更糟。
    // 锁存之后它答的是「这段时间里覆盖窗在不在屏幕上」，那才是要问的问题。
    private bool _trailUpSinceStall;

    /// <summary>卡顿那一行尾巴上的补充事实：这一段时间里整机在干什么。</summary>
    //
    // 为什么必须有：2026-09-18 那次复现里，日志只写下了 `ui thread stalled 4594ms`——
    // **时长有了，名字没有**。而「我们自己的代码等在某处」和「一次阻塞式 GC 停了整个世界」
    // 在只有时长时**长得一模一样**，却要走两条完全不同的修法。前者的指纹是某个 Probe 行，
    // 后者没有名字，只能在整机计数上现形，所以这两格必须一起报。
    //
    // gc2 = 第 2 代回收次数增量（阻塞式、会停掉所有线程，包括钩子线程——而钩子回调超
    // LowLevelHooksTimeout 会被 Windows 静默摘掉，正是那次日志里 `silentFor=937ms` 的来源）。
    // alloc = 分配增量（MB）。整屏分层窗每帧要重新合成一遍 2560×1440 ≈ 14MB，
    // 这个数暴涨就说明渲染压力是元凶。
    // sinceBeat = 钩子距上次回调多久（- 表示没装钩子）——它和卡顿同时发生，才谈得上「同一次事件」。
    //
    // 这两个 API 都是常数级读取，不会给「刚解卡」的 UI 线程再添负担。
    private string StallContext()
    {
        int gc2 = GC.CollectionCount(2);
        long allocMb = GC.GetTotalAllocatedBytes(precise: false) / (1024 * 1024);
        int dGc2 = gc2 - _stallGc2;
        long dAllocMb = allocMb - _stallAllocMb;
        _stallGc2 = gc2;
        _stallAllocMb = allocMb;
        long beat = _mouseHook?.SinceBeatMs ?? -1;
        // 上游延迟与 sinceBeat 必须**同时**出现在这一行：单看任何一个都分不出
        // 「链被排在前面的工具堵住」和「我们自己的钩子被系统摘掉」——而这台机器上
        // Logi Options+ / PowerToys / 热键助手 三家都装着 WH_MOUSE_LL（见 MouseHook._upstreamMax）。
        uint upstream = _mouseHook?.UpstreamMaxMs ?? 0;
        // 与上一格成对：那个量别人堵了我们多久，这个量我们自己离被摘还有多远（见 MouseHook.CallbackMaxMs）。
        // 卡顿行里两格一起报，才分得开「一次阻塞式 GC 停掉了整个世界（两个都会大）」
        // 和「上游某个工具堵了链（只有 upstreamMax 大）」。
        uint cbMax = _mouseHook?.CallbackMaxMs ?? 0;
        // trailUp 用**锁存值**（见 _trailUpSinceStall）：问的是「这段时间里覆盖窗在不在屏幕上」，
        // 不是「此刻在不在」——解卡那一刻它必然已经藏了，现读只会给出一个误导的 hidden。
        // 名字从 trail= 改成 trailUp= 是承重的：语义变了，沿用旧名字会让读过旧日志的人
        // 以为两者可比（旧的那一半几乎恒为 hidden）。
        string trail = _trail == null ? "none" : (_trailUpSinceStall ? "up" : "never");
        _trailUpSinceStall = false;   // 这一份账只用一次，同 _stallGc2 / _stallAllocMb
        return $"gc2=+{dGc2} alloc=+{dAllocMb}MB sinceBeat={(beat < 0 ? "-" : beat + "ms")} "
             + $"upstreamMax={(_mouseHook == null ? "-" : upstream + "ms")} "
             + $"callbackMax={(_mouseHook == null ? "-" : cbMax + "ms")} trailUp={trail}";
    }

    /// <summary>每半秒看一眼光标。装了钩子（手势或中键面板）期间跑。</summary>
    //
    // 判据在这儿采，而且必须**采得够密、且两格信号在同一区间对齐**。
    // 一度是搭在 30 秒的提醒计时器上：那时「光标动过」比的是相隔 30 秒的两次位置，
    // 于是「10 秒前动过鼠标、之后停下来看屏幕」——一个再常见不过的状态——
    // 会被判成「钩子死了」，白摘白装一次，还把日志刷满、让状态行一直挂着「已失效」。
    // 后来改成独立计时器，却又用过两扇各自计时的窗（活动 2000ms / 沉默 1500ms），
    // 用户移动后停手 1.5~2 秒必落进重叠带：一天 518 行 reinstall 就是这么来的。
    //
    // 现在每拍只回答一个问题：**这一个区间里光标动了吗？动了，那同一区间里心跳新不新？**
    // 不保留跨区间的「活动窗」，停手的区间直接不检测，重叠带在数学上不存在。
    //
    // 半秒而不是 1 秒：钩子真在连续使用中被摘掉时，最坏要到第二个移动区间才认得出
    //（死亡发生的那个区间里可能还留着死亡前的心跳），检测上界 ≈ 两个采样间隔。
    // 代价仍是一次 GetCursorPos，而且只在真的装了钩子（手势或中键面板）时才开——
    // 两样都关的人一次都不会跑到这儿。
    private void CursorWatchTick()
    {
        // 包一圈计时：这个 tick 里跑着自愈（摘钩 + 重装），而摘钩要 Join 钩子线程——
        // **这是 UI 线程上唯一一条能卡满 2 秒、之前又完全没计时的路**。
        // 2026-09-18 那次复现只留下 `ui thread stalled 4594ms`，没有名字；两种成因都落在这一带，
        // 而 Probe 行是唯一能把它们分开的东西（见 Core.UiStallWatch 的注释）。
        long t0 = Core.UiStallWatch.Begin();
        try { CursorWatchTickCore(); }
        finally { Core.UiStallWatch.End("cursor-watch", t0); }
    }

    private void CursorWatchTickCore()
    {
        // 时刻先取、只取这一次：区间长度和心跳年龄都得站在同一个瞬间上，否则又造出一扇错位的窗。
        long nowMs = Environment.TickCount64;
        var pos = Native.Win32.CursorPos();
        // **第一次采样只用来定基线，不构成区间。**
        // _lastCursor 的初值是 (0,0)，而真实光标几乎不可能正好在那儿——不特判的话，
        // 开启监视后的第一跳必然「不等于上次」，于是即使用户根本没碰鼠标也会被记成刚动过。
        if (!_cursorSeen) { _cursorSeen = true; _lastCursor = pos; _lastSampleAt = nowMs; return; }
        bool moved = pos != _lastCursor;
        long intervalMs = nowMs - _lastSampleAt;   // 实测长度：计时器迟到时它自己变长，新鲜度的界跟着长
        _lastCursor = pos;
        _lastSampleAt = nowMs;
        HealMouseHookIfDead(moved, intervalMs);
    }

    // movedInSample / intervalMs 是**同一个** 500ms 采样区间的两面（见 CursorWatchTick）：
    // 区间里光标确实动了、同一区间的心跳却老于区间长度，才是「移动发生了、回调没来」。
    /// <summary>摘掉鼠标钩子，并给这次等待计时。<paramref name="why"/> 进日志名（heal / manual / off / reapply）。</summary>
    //
    // 为什么值得单独封一层：`MouseHook.Dispose()` 内部有**两次 `Join(1000)`**（等钩子线程退出），
    // 而它是在 UI 线程上被调的。钩子线程正卡在回调里时（上游钩子链被堵住、或一次阻塞式 GC
    // 停掉了所有线程），这两次 Join 会**双双等满 = UI 线程卡 2 秒**——而这恰恰发生在用户
    // 已经在出问题的时候（自愈就是被钩子沉默触发的），于是「有点卡」被放大成「点不动」。
    //
    // 此前这里一个计时都没有，所以 2026-09-18 的日志里只有一句没有名字的
    // `ui thread stalled 4594ms`。名字是承重的：它和 `hook-install-wait`（在后台线程上）
    // 是**完全不同**的两条路，修法也不同，混在一起只能靠猜。
    private void DisposeMouseHook(string why)
    {
        if (_mouseHook == null) return;
        long t0 = Core.UiStallWatch.Begin();
        try { _mouseHook.Dispose(); }
        finally { Core.UiStallWatch.End("hook-dispose:" + why, t0); }
        _mouseHook = null;
    }

    /// <summary>笔迹采样点到达（UI 线程，由钩子线程经 Dispatcher 派来）。包一圈计时。</summary>
    //
    // 为什么这两个也要计时：笔迹窗是 `AllowsTransparency=true` 的分层窗，而 WPF 对分层窗
    // **走软件渲染**——每重画一帧要重新合成整块屏（2560×1440 ≈ 14MB）。所以「UI 线程被自己的
    // 渲染按住」是一条完全有可能的路，而它此前和「自愈时 Join 钩子线程」一样，是**没有名字**的。
    //
    // **但「钩子每个采样点都会让它重画一次、一笔能来几百个点」是错的，别再照着估。**
    // 采样点在 `MouseHook.ScheduleTrail` 里已经**合帧**（`Interlocked.Exchange(ref _trailQueued, 1)`，
    // 至多排一个 drain），所以一笔的点数 ≈ 这一笔跨了多少个 UI 帧，而不是钩子收到多少个鼠标事件：
    // 500ms 的一笔在 60Hz 上约 30 个点。14MB/帧 × 30 帧 ≈ 420MB/笔，摊到每帧约 1.4ms
    //（按 10GB/s 的拷贝带宽），**够不上「把 UI 线程按住数秒」的量级**——渲染压力真有那么大时，
    // 证据在卡顿行的 alloc= 增量里，不在这条注释里。
    private void TrailPoint(int x, int y, bool armed)
    {
        // **先锁存再干活**：卡顿可能就发生在下面那句调用里面，而卡顿行是解卡之后才写的
        //（见 _trailUpSinceStall）。放在前面，这一笔开头那一下就已经把「窗亮着」记下了。
        _trailUpSinceStall |= _trail?.IsVisible == true;
        long t0 = Core.UiStallWatch.Begin();
        try { _trail?.Point(x, y, armed); }
        finally { Core.UiStallWatch.End("trail-point", t0); }
        // 再锁一次：第一个采样点不显窗（第 2 个才 Show），所以「本句把它点亮」也要算进来。
        _trailUpSinceStall |= _trail?.IsVisible == true;
    }

    private void TrailEnd()
    {
        // 收笔这一刻窗还亮着（Finish 走的是推迟藏，见 GestureTrailWindow.HideSoon）——
        // 这是「一笔确实把覆盖窗摆上过屏」最便宜的取证点。
        _trailUpSinceStall |= _trail?.IsVisible == true;
        long t0 = Core.UiStallWatch.Begin();
        try { _trail?.Finish(); }
        finally { Core.UiStallWatch.End("trail-finish", t0); }
    }

    private void HealMouseHookIfDead(bool movedInSample, long intervalMs)
    {
        // 手势与中键面板共用同一个钩子：只开中键面板（没配手势）时钩子一样会被静默摘掉，
        // 这一档不能只给手势用户兜底。
        if (_mouseHook == null) return;
        // **从没跳过一次的钩子不算「死了」，压根别自愈。**
        //
        // SinceBeatMs 在从未收到输入时返回 long.MaxValue，于是一个**刚装上、还没被用过**的钩子
        // 和一个真死了的钩子长得一模一样：用户第一次移动鼠标的那个区间，moved=true、心跳却是
        // MaxValue，不挡这一格每次启动都会稳定地空转重装——实测日志里那 3 行一簇（yes → no → no）
        // 就是它，装到那个「试三次就放弃」的计数器封顶才停。85 行日志里 78 行是这么来的。
        //
        // 而这个方向本来就是错的：「掉了就重装」只对**装对了、事后被摘掉**那一种有效
        //（Windows 因 LowLevelHooksTimeout 把它静默摘走）。一次输入都没收到过则是另一回事——
        // 那是装的那一刻就不成立（进程没提权、有人排在输入链前面），重装一百次也一样。
        // 界面上那一档本来就叫 Deaf，与 Stale 分开报，说的正是「这条路重装治不了」。
        //
        // 于是判据从「试三次再放弃」改成「根本不试」：少了三次无谓的装卸，日志里那 3 行一簇没了，
        // 而真正的自愈（收到过输入、后来停了）一次都没少——那一档 EverBeat 恒为真。
        if (!_mouseHook.EverBeat) return;
        // 三条守卫合起来才动手（区间内有移动、心跳老于本区间、距上次摘钩够久）：
        // 抽成 HookWatch.ShouldHeal 就是要让每条都有断言盯着（挪错一条阈值、或把下限删掉，
        // 都有测试变红）。状态行用的 StaleBeatMs 与此无关：那格取宽是给人看的，这里只要
        // 同一个采样区间里的事实。
        // _lastHealAt == 0 表示「从没自愈过」。TickCount64 从开机起算，实际不会撞到 0，拿它当哨兵是安全的；
        // 不做这一步的话，开机不到 HealMinGapMs 就启动的程序，第一次真故障会被那条下限误挡住。
        long sinceLastHeal = _lastHealAt == 0 ? long.MaxValue : Environment.TickCount64 - _lastHealAt;
        if (!HookWatch.ShouldHeal(movedInSample, _mouseHook.SinceBeatMs, intervalMs,
                                  sinceLastHeal, HealBeatSkewMs, HealMinGapMs))
            return;
        // **先把上一个钩子的账记下来，再摘掉它。** 这两格（收到过输入吗、收到过右键吗）
        // 是判断「为什么没工作」的全部依据，而摘掉之后就再也问不到了——
        // 此前这里先置空再走 ApplyMouseHook，于是自愈这条路上日志永远是「-」：
        // 恰恰在唯一反复触发的那条路上，诊断是瞎的。
        _prevSource = "heal";
        _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
        // 按下与抬起**分开记**。合成一格（或）会把这个功能唯一能自证的信号抹掉：
        // 「收到过抬起、从没收到按下」只有一种成因——有人排在前面把按下吞了。
        _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                   + (_mouseHook.EverRightUp ? " up+" : " up-");
        _prevMiddle = MiddleAccount(_mouseHook);
        _prevSince = _mouseHook.SinceBeatMs;
        _prevRejected = _mouseHook.InjectRejected;
        _prevUpstream = _mouseHook.UpstreamMaxMs;
        // 与上一格成对：那个量别人堵了我们多久，这个量我们自己离被摘还有多远（见 _prevCallbackMax）。
        _prevCallbackMax = _mouseHook.CallbackMaxMs;
        // 顺手采一次在场名单（见 _prevSuspects）。放在**摘钩之前**与上面那几格同一理由：
        // 这一格记的是「出事那一刻谁在跑」，而不是「我们查日志那一刻谁在跑」——
        // 用户关掉某个工具之后再来看日志，两者就不是一回事了。
        _prevSuspects = HookSuspects.Describe(HookSuspects.RunningProcessNames());
        var wasGesture = _mouseHookGesture;
        _lastHealAt = Environment.TickCount64;   // 记在真动手之前：万一重装又失败，下限仍得拦住立刻再摘一次
        DisposeMouseHook("heal");
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
    /// <summary>鼠标钩子此刻装着没有（不区分手势还是中键面板）。托盘菜单据此决定要不要给「重新挂钩」入口。</summary>
    internal bool MouseHookActive => _mouseHook != null;

    // source 写进 error.log 的 source= 字段：自愈与手动摘钩的**表现完全一样**（都是一行
    // reinstalled + 一份前任账），但成因与处理方式不同——手动那几行通常只是用户在排障时点的，
    // 不该被当成故障。分不清的话，日志里每一次摘钩都要去问用户「你当时点了吗」。
    internal void RehookMouse(string source = "manual")
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => RehookMouse(source)); return; }
        // 这条路也要记账。此前不记，于是日志里连按几下「重新挂钩」全是「上一个 = -」——
        // 而那几下恰恰是用户在排障时按的，最需要知道上一个钩子到底收到过什么。
        if (_mouseHook != null)
        {
            _prevSource = source;
            _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
            _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                       + (_mouseHook.EverRightUp ? " up+" : " up-");
            _prevMiddle = MiddleAccount(_mouseHook);
            _prevSince = _mouseHook.SinceBeatMs;
            _prevRejected = _mouseHook.InjectRejected;
            _prevUpstream = _mouseHook.UpstreamMaxMs;
            _prevCallbackMax = _mouseHook.CallbackMaxMs;   // 同上：与上一格成对
            // 手动「重新挂钩」也记这一格：那几下恰恰是用户排障时按的，
            // 而「他按的时候链上都有谁」正是他下一步要问的（见 _prevSuspects）。
            _prevSuspects = HookSuspects.Describe(HookSuspects.RunningProcessNames());
        }
        DisposeMouseHook("manual");
        _mouseHookMs = -1;               // 逼 ApplyMouseHook 走完整重装，别被「已经是想要的状态」挡回去
        _lastHealAt = Environment.TickCount64;  // 手动摘钩也算摘钩：否则紧接着的真故障会立刻再摘一次
        _mouseHookFailed = false;        // 上一次装失败不该让这一次连报错都不报
        ApplyMouseHook();
    }

    /// <summary>开机清单跑完后重挂一次鼠标钩子抢回链首。</summary>
    //
    // 钩子链**后装的排在前面**：开机清单里延迟启动的 AHK / 启动器类工具在 Clockwork 之后装钩，
    // 它们若吞中键，排在后面的我们连 MBUTTONDOWN 都收不到——开机首次唤出无效、之后又时好时坏，
    // 这是主要嫌疑之一。自愈治不了这一种：中键被单独吞掉时移动心跳照常，不满足「全钩死寂」。
    // 清单跑完意味着那些工具都已启动并装完钩子，此刻重装一次就把队首抢回来（同类工具 Quicker
    // 专门有一条「重新挂钩键鼠」的命令，理由相同；我们把它自动化在唯一知道「开机启动结束了」的时刻）。
    // 手动「重跑清单」（boot=false）不调：那时用户正在用机器，没必要白抖一次钩子。
    private void RehookMouseAfterBoot()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(RehookMouseAfterBoot); return; }
        // 没装（面板和手势都关着）或上一次装失败：别在开机后制造一次新的失败弹窗。
        if (_mouseHook == null || _mouseHookFailed) return;
        RehookMouse("boot");
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
            DisposeMouseHook("off");
            // 号也要动：此刻可能有一次安装飞在半空（见 OnHookInstalled）。不动号的话，
            // 它回来时会以为自己还算数，于是拿一个已经 Dispose 过的钩子去报成功/失败，
            // 并把 `_mouseHook` 重新置回非 null——「关了手势却还挂着钩子」。
            _hookInstallToken++;
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
        // 中键拖拽的抖动容差按主屏缩放换算成物理像素：低级钩子坐标是物理像素，gate 里那个 6
        // 是逻辑像素的手感。不换算的话 150% 屏上只剩 4 物理像素（约 1mm），按下去那一下抖动
        // 就把长按判成了拖拽，面板永远不弹（见 MouseHook._dragEscapes 注释）。
        // 下界 6：100% 缩放时与旧行为一字不差。
        int middleTolerance = wantPress
            ? Math.Max(6, (int)Math.Round(6 * Win32.PrimaryScale()))
            : 0;
        if (wantGesture && _trail != null) _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        if (_mouseHook != null && _mouseHookMs == ms && _mouseHookGesture == wantGesture
            && _mouseHookMinLeg == minLeg && _mouseHookTolerance == middleTolerance) return;   // 已经是想要的状态
        DisposeMouseHook("reapply");
        var gesture = wantGesture
            ? new GestureGate((p, proc) => GestureGate.Match(_config.Gestures, p, proc) != null, minLeg)
            : null;
        // 覆盖窗跟着 gate 走：装了手势才建，卸了就关——没配手势的人不该多出一扇窗，哪怕它是隐藏的。
        if (wantGesture)
        {
            // **建这扇窗之前，先关掉本进程的窗口幽灵化。**
            // 它铺满整块屏、又置顶：UI 线程一旦卡顿超过 5 秒被系统判成「未响应」，DWM 就会在它
            // 上面盖一块自己的幽灵窗，而那块窗**点得中**——于是整个桌面点不动，唯一的解法是去杀
            // dwm.exe（用户报的正是这个症状，来龙去脉见 Win32.DisableWindowGhosting 那段）。
            // 放在这里而不是启动处：只有真会摆出全屏覆盖窗的人需要付那份代价（UI 线程真卡住时
            // 不再有「未响应」的壳可拖可关），没开手势的人一点没变。不可逆，所以它自己保证只发一次。
            Win32.DisableWindowGhosting();
            // 卡顿仪表与覆盖窗同时开、同时关（见 ApplyStallWatch）：从这一刻起，UI 线程卡顿
            // 才可能变成「整个桌面点不动」，也就从这一刻起值得记下卡了多久。
            ApplyStallWatch(true);
            _trail ??= new Views.GestureTrailWindow();
            _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        }
        else CloseTrail();
        _mouseHook = new Native.MouseHook(ms, TogglePanel, a => Dispatcher.BeginInvoke(a), gesture, RunByGesture,
            trailPoint: TrailPoint, trailEnd: TrailEnd,
            unmatched: GestureUnmatched, moveTolerancePhysical: middleTolerance);
        _mouseHookMs = ms;
        _mouseHookGesture = wantGesture;
        _mouseHookMinLeg = minLeg;
        _mouseHookTolerance = middleTolerance;
        // 手势与中键面板任一在监听，就要判「钩子还活着吗」：只勾中键面板的用户同样会被静默摘钩。
        ApplyCursorWatch(wantGesture || wantPress);
        // **安装这一步挪到后台线程去等，绝不在 UI 线程上等。**
        //
        // 原先这里是直接 `if (_mouseHook.Install())`，而 Install 内部是 `_installed.Wait(5000)`。
        // 那个 5000 与系统的 `HungWindowTimeout` **一模一样**：一旦真等满，这一轮结束时 UI 线程
        // 恰好被卡在幽灵化阈值上，同一轮里再多做一点点事就越线——于是 DWM 在全屏笔迹窗上盖一块
        // 点得中的幽灵窗，整个桌面点不动（来龙去脉见 Core.UiStallWatch 与 MouseHook.Install 的注释）。
        //
        // 而 ApplyMouseHook 之所以被钉在 UI 线程上，理由是**建那扇 WPF 笔迹窗**（见本方法开头那段），
        // 不是安装钩子。窗已经在上面建好了，安装没有任何理由再占着这条线程——它是纯粹的等待。
        //
        // 号（token）解决「结果回来时已经换人了」：改配置 / 自愈 / 手动重钩都可能在一次安装还在飞的
        // 时候再起一次。那时旧结果必须整个丢掉——不丢的话，旧那次若报了失败，会拿新钩子当失败收尾
        //（Dispose 掉一个已经装好的钩子、关掉笔迹窗、还弹一句「装不上」）。
        //
        // 用裸 Thread 而不是 Task.Run：这里要阻塞最多 5 秒，占着一个线程池线程不值当
        //（线程池被占满时其他 Task 会排队），而安装本来就是个低频动作，一条专属线程最省事。
        // 同 MouseHook 的钩子线程、ReminderActions 的播报线程：都是「一条线程只为一件慢事」。
        var hook = _mouseHook;
        int token = ++_hookInstallToken;
        new System.Threading.Thread(() =>
        {
            bool ok;
            try { ok = hook.Install(); } catch { ok = false; }
            // 调度器正在关闭时 BeginInvoke 会抛：那是退出路径，本来也不需要这份结果。
            try { Dispatcher.BeginInvoke(() => OnHookInstalled(hook, token, ok)); } catch { }
        })
        { IsBackground = true, Name = "Clockwork.HookInstall" }.Start();
    }

    /// <summary>安装结果回到 UI 线程才处理：成功只记日志，失败要收尾（Dispose / 关窗 / 弹气泡）。</summary>
    //
    // 两格都要对：号必须还是最新那一个，且 `_mouseHook` 必须还是本次安装的那个对象。
    // 号是主判据（每一次改动 `_mouseHook` 的路都会动它）；引用那格是兜底——
    // 号只增不减、又是 int，万一哪天有人加了条改 `_mouseHook` 的路却忘了动号，
    // 这一格还能拦住「结果落在一个已经不存在的对象上」。两格都便宜，没必要只留一个。
    private void OnHookInstalled(Native.MouseHook hook, int token, bool ok)
    {
        if (token != _hookInstallToken || !ReferenceEquals(hook, _mouseHook)) return;
        if (ok)
        {
            _mouseHookFailed = false;
            // **只在有「前任的账」时记一行，也就是只在自愈、手动「重新挂钩」、开机后抢链首之后。**
            //
            // 此前这里对**每一次**装上都记一行成功，于是：每次启动一行、每次改中键阈值或手势开关
            // （SaveConfig 末尾会调到这儿）再一行。它们全是「一切正常」，而这个文件是唯一的事后线索、
            // 又只有 128KB——用成功刷掉真故障的历史，恰恰废掉了它存在的理由。
            // 「装上了」这件事状态行已经在报（Deaf / Stale 两档），不必再往错误日志里记一笔。
            //
            // 剩下这几条路值钱，因为它们各自在摘掉旧钩子**之前**抄了一份账（_prevBeat/_prevRight）：
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
            // source 放最前：它是唯一的分支点，决定后面这些数字该往哪个方向读。
            // heal = 看门狗判死了（可能真的被摘，也可能只是上游钩子卡了一下）；
            // manual = 用户在排障时点的；boot = 开机抢链首的正常一次。
            // 三个数值取自 `_mouseHook*` 那组字段而不是局部变量：安装结果现在是**异步**回来的，
            // 局部变量早就不在作用域里了；而号与引用那两道闸保证了字段里此刻装的就是本次那套参数。
            AppendErrorLog($"mouse hook reinstalled: source={_prevSource} middleHoldMs={_mouseHookMs} "
                           + $"middleTolerance={_mouseHookTolerance} gestures={_mouseHookGesture} "
                           + $"prevEverFired={_prevBeat} prevRightButton={_prevRight ?? "-"} "
                           + $"prev{_prevMiddle ?? "-"} "
                           + $"silentFor={(_prevSince == long.MaxValue ? "never" : _prevSince + "ms")} "
                           // 发不出去 vs 收不到，是两种表现一样、修法完全相反的故障（见 MouseHook.InjectRejected）。
                           + $"injectRejected={_prevRejected} "
                           // 与 silentFor 合看才分得开「链被上游堵」和「钩子被系统摘」（见 _prevUpstream）。
                           // 只报峰值，所以它是**这一任钩子**的账，不是这一刻的瞬时值。
                           + $"upstreamMax={_prevUpstream}ms "
                           // 与 upstreamMax 成对，回答的是**相反**的那个问题：别人堵了我们多久 / 我们自己
                           // 离被摘还有多远（LowLevelHooksTimeout 是回调的预算，本机实测 300ms）。
                           // 这一格非零且接近 300，就说明「钩子被摘」是我们自己造成的，别再去查上游。
                           + $"callbackMax={_prevCallbackMax}ms "
                           // 链被堵时唯一拿得到的人证：那一刻谁在场（见 _prevSuspects）。
                           // 只有 `upstreamMax` 大才有意义——那时是别人堵的，这一格是唯一的线索；
                           // 而它只是候选，不是判决，所以写 `suspects=` 而不是 `culprit=`。
                           + $"suspects={_prevSuspects ?? "-"}");
            _prevBeat = _prevRight = _prevMiddle = _prevSource = null;   // 这份账只用一次，别让它污染下一条非自愈的记录
            _prevSuspects = null;
            return;
        }
        // 装不上（受限令牌、组策略、被安全软件拦）：如实说。静默失败的话，用户会以为
        // 自己按得不够久，一直加长按时间去试一个根本没装上的钩子。
        //
        // **这一类才是这个文件该记的。** 改之前刚好相反：每次装上都记一行成功，装不上却只弹一句气泡——
        // 气泡关掉就没了，于是唯一真正需要事后追查的那种故障，日志里一个字都没有。
        // 和气泡同受 _mouseHookFailed 夹一次：ApplyMouseHook 每次保存配置都会走到，
        // 不夹的话一个装不上的钩子会把日志按保存次数刷满——那正是这轮要修的毛病。
        var win32 = hook.LastError;   // 先取错码，Dispose 之后就没人能问了
        // **按不上也要按关掉那条路收尾**，与上面 `!wantPress && !wantGesture` 那个分支同一套。
        // 漏了不是漏一次：ApplyMouseHook 在每次 SaveConfig 末尾都会走到，而 `_mouseHook` 已经是 null
        // 让上面那个「已经是想要的状态」早退永远不成立——于是每保存一次就新建一个 MouseHook，
        // 每个都带两个 System.Threading.Timer（_alarm、_clickUp），上一个连 Dispose 都没调就被丢了。
        // 轨迹窗与 2 Hz 的「钩子还活着吗」同理：前面刚建完 / 刚上弦，而自愈那一句一看 `_mouseHook == null`
        // 就立即返回，于是那个计时器永远转着、每半秒一次 GetCursorPos，什么也不做。
        hook.Dispose();
        _mouseHook = null;
        CloseTrail();
        ApplyCursorWatch(false);
        if (!_mouseHookFailed)
        {
            AppendErrorLog($"mouse hook install failed: middleHoldMs={_mouseHookMs} gestures={_mouseHookGesture} win32={win32}");
            WarnToast(Strings.Get("Warn_MouseHookFail"));
        }
        _mouseHookFailed = true;
    }

    private void CloseTrail()
    {
        // 仪表跟着覆盖窗一起收。放在 null 早退**之前**：两者由 ApplyMouseHook 同时置位，
        // 但收尾这条路有多个入口（关手势、装钩失败、退出），漏关一格就会留下一个空转的计时器。
        ApplyStallWatch(false);
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
        if (_panel != null)
        {
            // 僵尸与活面板要分开答（判据见 Core.PanelFocus.DecideToggle）：
            // 从未激活过的面板按「唤出」应该是救活它，而不是把用户看不见的面板关掉——
            // 那正是「长按了没反应」的观感来源。代价是清掉一块僵尸要多按一下
            //（先复活拿到焦点，再按一下才是收起），这是拿一次按键换「召唤不再落空」，
            // 不要把这条当 bug 改回无脑 Dismiss。
            if (PanelFocus.DecideToggle(_panel.EverActivated) == PanelToggleAction.Revive)
            {
                bool ok = _panel.Revive();
                AppendErrorLog(ok ? "panel revived on toggle (foreground acquired)"
                                  : "panel revive on toggle failed (foreground not acquired)");
            }
            else _panel.Dismiss();
            return;
        }
        // 前台进程必须**在建面板之前**问：面板一显示就把前台抢走了，那之后再问只会得到 Clockwork 自己。
        // 这是场景页整个功能的时序命门，别把它挪到下面去（见 Win32.ForegroundProcessName 的注释）。
        //
        // **这一段要包一圈计时，别删。** 它是 `TogglePanel` 里唯一没有名字的部分（`Popup()` 的前台
        // 等待另有 `foreground-wait`），而它有具体嫌疑：2026-09-18 有一次 **2547ms** 的 UI 线程卡顿，
        // 同一秒落下的另一行是 `panel shown but foreground not acquired`。更要紧的是——
        // 那次跑的是 `8de52b4`，那个提交里 `trail-point` / `cursor-watch` / `config-write` **一个都没有**
        //（见 clockwork-build-deploy 的口径：判断「日志里没有某行」能不能当排除依据，先确认那个计时
        // 本来就在出故障的那个提交里）。所以当时「没有具名慢调用」**不构成任何排除**，这一段始终没被查过。
        // 它要在 UI 线程上同步建页、建动作格、建整扇窗，全是没被量过的活。
        Views.QuickPanelWindow w;
        long t0 = Core.UiStallWatch.Begin();
        try
        {
            var foreground = Win32.ForegroundProcessName();
            var s = _config.Settings;
            var look = new Views.PanelLook(s.PanelColumns, s.PanelTileSize, s.PanelIconOnly, s.PanelShowOps,
                                           s.PanelTopRows, s.PanelBottomRows, s.PanelLeftTabs, s.PanelTopTabs);
            // 一页的容量由两条带的行数×列数决定，装不下的自动续到下一页。
            var capacity = PanelMetrics.PageCapacity(s.PanelTopRows, s.PanelBottomRows, s.PanelColumns);
            w = new Views.QuickPanelWindow(BuildPanelPages(foreground, capacity), BuildPanelOps(), look,
                                           BuildPanelWaistOps());
            _panel = w;
        }
        finally { Core.UiStallWatch.End("panel-build", t0); }
        // 清引用挂在 Closed 上而不是各处手动置 null：面板有三条关闭路径（热键再按 / Esc / 焦点离开），
        // 漏掉任何一条，_panel 就会一直指着一个已经关掉的窗口，此后热键永远只走 Dismiss 分支，
        // 面板再也打不开——而这种状态重启前自己不会恢复。
        w.Closed += (_, _) => { if (ReferenceEquals(_panel, w)) _panel = null; };
        // 失败每次都记（LogDedup 收敛连续重复，见 DialogForeground.cs 的同一口径）：
        // 一次性开关会让复发无从查起——「时有时无」的故障最需要知道它复发了多少次。
        // 中键长按这条路没有「刚响应用户输入」的前台锁豁免（见 QuickPanelWindow.Popup 注释）。
        // 抢不到前台时看门狗会在额度窗口内逐拍补抢，仍不成面板仍可见、可点，
        // 下一次「唤出」走上面的 Revive 分支救活它。
        if (!w.Popup())
            AppendErrorLog("panel shown but foreground not acquired");
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
                p.Items.Select(it => new Views.PanelTile(it.Label, it.Icon, () => RecordPanelClick(it.Step), it.Enabled,
                                                         OnEdit: () => EditPanelItem(it),
                                                         OnDelete: () => DeletePanelItem(it),
                                                         Tip: StepDisplay.StepSummary(it.Step),
                                                         Search: StepDisplay.StepSearchText(it.Step))).ToList(),
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
        var w = new Views.PanelManagerWindow(_config, SaveConfig, _panelUsage, ResetPanelUsage) { Owner = owner };
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

    // —— 提醒：实现整体在 Services/ReminderScheduler.cs，这里只留门面转发。 ——
    public TimeSpan? DndRemaining => _reminders.DndRemaining;
    public void PauseReminders(int hours) => _reminders.PauseReminders(hours);
    public void PauseRemindersUntilEndOfDay() => _reminders.PauseRemindersUntilEndOfDay();
    public string DndResumeLabel(TimeSpan left) => _reminders.DndResumeLabel(left);
    public void ResumeReminders() => _reminders.ResumeReminders();
    // 托盘「快速提醒」：N 分钟后响一次，响完自删。
    public void QuickReminder(int minutes) => _reminders.QuickReminder(minutes);
    public void SkipReminderToday(Reminder r) => _reminders.SkipReminderToday(r);
    public void PreviewReminder(Reminder r) => _reminders.PreviewReminder(r);

    // 配置所在目录（state/run.log/error.log 都落在配置旁）：一处定义，5 个落点共用。
    private string CfgDir => Path.GetDirectoryName(_cfgPath) ?? _exeDir;

    // —— 运行编排：实现整体在 Services/RunOrchestrator.cs，这里只留门面转发。
    // 三条运行路径共用急停闸/回执/逐步记账；按源码文本钉住它们的守卫（GestureReceiptTests 等）
    // 随实现改指新文件。 ——
    public bool IsRunning => _runner.IsRunning;
    public event Action? RunStateChanged { add => _runner.RunStateChanged += value; remove => _runner.RunStateChanged -= value; }
    // 急停唯一出口：全局热键、托盘菜单、主窗口按钮三个入口都走这里（实现在 RunOrchestrator）。
    public void RequestStop() => _runner.RequestStop();
    // 后台跑启动清单（手动重跑或 --boot）。
    public void RunLaunchAsync(bool boot) => _runner.RunLaunchAsync(boot);
    private void DrainRunRequest() => _runner.DrainRunRequest();
    public void RunGroupByName(string nameOrId) => _runner.RunGroupByName(nameOrId);
    // 单步「运行这一步」/「运行整组」：by/owner/gestureOrigin 的语义见 RunOrchestrator。
    public void RunStepAsync(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                             IntPtr gestureOrigin = default)
        => _runner.RunStepAsync(step, owner, by, gestureOrigin);
    public RunCancel RunGroupAsync(ActionGroup group, Window? owner = null, Action? onDone = null,
                                   bool unattended = false, IntPtr gestureOrigin = default)
        => _runner.RunGroupAsync(group, owner, onDone, unattended, gestureOrigin);
    // --selftest 直认真实的问句弹框链（DevSelfTest），经门面而不是绕开编排器。
    internal string? AskUser(LaunchStep step, RunVars vars, Window? owner = null, bool unattended = false)
        => _runner.AskUser(step, vars, owner, unattended);

    /// <summary>快捷面板的总开关。托盘菜单据此决定要不要摆那一项。</summary>
    //
    // 托盘那一层只拿得到一个 App（见 TrayIcon.Rebuild 的签名），所以状态得从这儿露出去。
    // 菜单是在 Opening 时重建的，于是它每次弹出都读到最新的值。
    public bool PanelEnabled => _config.Settings.PanelEnabled;

    // 托盘菜单重建用：当前动作组列表（「运行：某组」项）。
    public IReadOnlyList<ActionGroup> Groups => _config.ActionGroups;

    // 配置文件路径（导入/导出用）。
    public string ConfigFilePath => _cfgPath;

    // —— toast / 错误日志 / 最近通知：实现整体在 Services/AppDiagnostics.cs，这里只留门面转发。 ——
    // 分级默认时长、跨线程编组、入队+后台写盘线程等纪律都随实现迁走；
    // OffUiThreadTests 按源码文本钉住它们，断言指向新文件。
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                           int durationMs = -1, string? key = null, bool log = true)
        => _diag.ShowToast(title, message, level, durationMs, key, log);

    // 警告气泡的便捷入口（RunOnYes 等回调用）。ShowToast 自身已全 try/catch 守护、可跨线程调。
    private void WarnToast(string msg) => _diag.WarnToast(msg);

    internal void AppendErrorLog(string line) => _diag.AppendErrorLog(line);

    // OnExit 用的同步收尾。
    private void FlushErrorLog() => _diag.FlushErrorLog();

    public IReadOnlyList<NotificationEntry> RecentNotifications => _diag.RecentNotifications;

    public void ReplayNotification(NotificationEntry n) => _diag.ReplayNotification(n);

    // 托盘「查看上次启动日志」。
    public void OpenRunLog() => _diag.OpenRunLog();

    private void ShowCrash(Exception? ex) => _diag.ShowCrash(ex);

    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    // 配置存盘（原子写）。ViewModel 增删改移时回调。持续写失败（OneDrive/杀软锁死超过重试）不再静默吞——
    // 界面看着已保存、重启全回退是静默数据丢失，至少弹个警告让用户知道改动只在内存里。
    public void SaveConfig()
    {
        // 整条保存路径计时（含末尾那次 ApplyMouseHook）。这条路上有三件在 UI 线程上跑、
        // 又都可能以「秒」为单位的事：序列化 180KB 配置、走 ConfigStore 的带睡眠重试写盘
        // （见那边的注释：本机有坚果云的过滤驱动与 GoodSync 在持句柄）、以及重建热键 + 重装钩子。
        // 此前一句计时都没有，所以「保存配置时点不动」在日志里不留任何痕迹。
        long t0 = Core.UiStallWatch.Begin();
        try { SaveConfigCore(); }
        finally { Core.UiStallWatch.End("config-save", t0); }
    }

    private void SaveConfigCore()
    {
        if (_configSuperseded) return;   // 内存里的 _config 已作废，任何回写都是「无声还原」——见 MarkConfigSuperseded
        try { ConfigStore.Write(_config, _cfgPath); }
        // 写盘失败=界面看着已保存、重启全回退的静默数据丢失。这条不给它自动消失：常驻到用户点掉。
        // 同键合并：连续几次保存失败只留一张（标 ×N），不至于把屏幕糊满。
        catch (Exception ex) { ShowToast("Clockwork", Lf("Warn_SaveConfigFail", ex.Message), Views.ToastLevel.Warn, 0, key: "saveconfig"); }
        // 热键的即时重建（组 + 功能键；捕捉挂起期一律不动键）见 HotkeyManager.RebindAfterConfigSave；
        // 中键长按的开关与阈值也在设置页，跟着同一次保存生效（ApplyMouseHook 幂等）。
        _hotkeys.RebindAfterConfigSave();
        ApplyMouseHook();
    }

    // 导入配置：新配置已原子写入磁盘，本实例内存里的 _config 就此作废——它靠重开新实例重读生效。
    // 从此禁止任何回写。否则「写盘 → 弹『已导入』确认框 → 重开自身」中间那段模态期间，提醒计时器照常在走
    //（DispatcherTimer 在嵌套消息循环里不会停，这正是 _reminderTickBusy 存在的原因），一条「仅一次」提醒
    // 触发完毕会自动取消勾选并调 SaveConfig，把旧 _config 覆盖回刚导入的文件——用户点完确定重启，
    // 导入无声还原。RelaunchForLanguage 失败时的模态提示同理，一并被这道闸挡住。
    public void MarkConfigSuperseded() => _configSuperseded = true;

    // 编辑提醒换新 id 时在途耐久投递的迁移（判据见 ReminderScheduler.MigrateReminderState）。
    public void MigrateReminderState(string oldId, Reminder updated) => _reminders.MigrateReminderState(oldId, updated);

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

}
