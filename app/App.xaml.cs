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
        _gesture = new(() => _config, SaveConfig, _diag);
        _panelHub = new(() => _config, SaveConfig, _diag, _runner, _reminders, _usage);
    }
    private string _cfgPath = "";
    private bool _configSuperseded;   // 导入已把新配置写盘：本实例内存里的 _config 从此作废，禁止回写（见 MarkConfigSuperseded）
    private string _statePath = "";   // clockwork.state.json：提醒耐久运行态

    // 面板格子的点击统计（Services/PanelUsageTracker.cs；键是 LaunchStep.Id，见 Core.PanelUsageStore）。
    private readonly Services.PanelUsageTracker _usage = new();
    // （面板统计的传参接线随面板域迁到 PanelCoordinator，文本守卫也跟着走。）
    private string _exeDir = "";
    private string _exePath = "";
    // 运行编排（单步/整组/启动清单/急停）的整体实现，见 Services/RunOrchestrator.cs。
    // 在 App 构造里装配：DevChecks 的 --smoke 也会建主窗口并订阅 RunStateChanged，不能等到 OnStartup。
    private readonly Services.RunOrchestrator _runner;
    // 全局热键（急停/面板/一键直达/组键）的整体实现，见 Services/HotkeyManager.cs。
    private readonly Services.HotkeyManager _hotkeys;

    // 提醒引擎的运行时外壳（计时器/触发/事件线/勿扰/耐久态），见 Services/ReminderScheduler.cs。
    private readonly Services.ReminderScheduler _reminders;
    // 鼠标钩子与手势（装/卸/自愈/笔迹/仪表），见 Services/GestureCoordinator.cs。
    private readonly Services.GestureCoordinator _gesture;
    // 快捷面板（浮窗开关/瓦片构建/管理器），见 Services/PanelCoordinator.cs。
    private readonly Services.PanelCoordinator _panelHub;

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
        // 钩子→面板/单步的出站接线：中键到点唤出面板、手势起笔先收面板、命中走单步的手势档。
        // 这行 RunStep 是 GestureReceiptTests 按文本钉的「手势必须走 Gesture 档」的所在地，别改名。
        _gesture.OnTogglePanel = TogglePanel;
        _gesture.DismissPanel = _panelHub.Dismiss;
        _gesture.RunGestureStep = (LaunchStep step, IntPtr origin) => RunStep(step, by: StepTrigger.Gesture, gestureOrigin: origin);
        _gesture.DialogOwner = () => _main is { IsVisible: true } ? _main : null;
        // 面板→壳的出站接线：显示主窗、开手势管理器、组步骤的开关语义、主窗列表的行级刷新。
        _panelHub.ShowMainEdge = ShowMain;
        _panelHub.OpenGestureManager = _gesture.OpenGestureManager;
        _panelHub.ToggleGroup = (g, o) => _hotkeys.ToggleGroupByHotkey(g, o);
        _panelHub.DialogOwner = () => _main is { IsVisible: true } ? _main : null;
        _panelHub.ResyncMainGroups = () => _main?.ResyncGroups();
        _panelHub.RefreshMainGroupRows = () => _main?.RefreshGroupRows();
        _tray = new TrayIcon(this);
        // 托盘状态灯的两个刷新源：运行闸（即时）与提醒 tick（30s 心跳，兜住勿扰到点自解）。
        _runner.RunStateChanged += _tray.SyncTip;
        _reminders.Tick += _tray.SyncTip;
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
        _gesture.DisposeOnExit();   // 摘钩 + 收笔（理由见 GestureCoordinator.DisposeOnExit）
        if (_main != null) _main.AllowClose = true;
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        Shutdown();
    }

    // 所有退出路径（托盘退出/语言切换重启/提权重启）都过 Shutdown → 在此兜底：
    // 提醒状态的后台补写是 fire-and-forget，进程退出会带走未落盘的快照，退出前同步补写最后一份。
    //
    // 错误日志同理：AppendErrorLog 改成入队之后，最后那几行正躺在 AppDiagnostics 的写盘队列里等写盘线程，
    // 而写盘线程是 IsBackground——进程一走它就没了，队列跟着一起没。所以这里补一次同步排空。
    // 它带 300ms 超时（见 AppDiagnostics 的 LogFlushTimeoutMs），最坏情况是少写最后几行，绝不会让「点退出」变成「程序赖着不走」。
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


    // —— 鼠标钩子 / 手势：实现整体在 Services/GestureCoordinator.cs，这里只留门面转发 ——
    internal bool MouseHookActive => _gesture.MouseHookActive;
    internal GestureWatchState GestureWatch => _gesture.GestureWatch;
    // 托盘/手势管理器给的「重新挂钩抢链首」入口。
    internal void RehookMouse(string source = "manual") => _gesture.RehookMouse(source);
    /// <summary>打开手势管理器。面板表圈上那颗按钮与主界面那颗走同一条路。</summary>
    public void OpenGestureManager() => _gesture.OpenGestureManager();
    // 按当前设置装/卸中键长按与手势钩子（幂等，UI 线程编组在实现里）。
    private void ApplyMouseHook() => _gesture.ApplyMouseHook();
    // 开机清单跑完后重挂一次抢回钩子链首。
    private void RehookMouseAfterBoot() => _gesture.RehookMouseAfterBoot();

    // —— 快捷面板：实现整体在 Services/PanelCoordinator.cs，这里只留门面转发 ——
    // 面板是开关：热键再按一次收起来（僵尸/活面板的分辨在实现里）。
    public void TogglePanel() => _panelHub.TogglePanel();
    // 跑一个步骤：group 类型转交整组运行（ToggleGroup），其余走单步。
    // 这行门面同时是 _gesture.RunGestureStep 接线的落点（GestureReceiptTests 钉 by: Gesture 档）。
    public void RunStep(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                        IntPtr gestureOrigin = default)
        => _panelHub.RunStep(step, owner, by, gestureOrigin);
    public void OpenPanelManager() => _panelHub.OpenPanelManager();

    // —— 提醒：实现整体在 Services/ReminderScheduler.cs，这里只留门面转发。 ——
    public TimeSpan? DndRemaining => _reminders.DndRemaining;
    // 勿扰恢复钟点（托盘状态灯用）；判空语义与 DndRemaining 同源。
    public DateTime? DndUntil => _reminders.DndUntil;
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
