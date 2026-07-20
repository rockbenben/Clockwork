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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 一次性提权子任务：由非提权主实例在 schtasks 拒绝时以管理员身份重开自己触发。
        // 仅执行自启注册/注销后立即退出——不建窗口/托盘/计时器，也不参与单实例，避免与运行中的主实例冲突。
        bool regTask = e.Args.Contains("--register-autostart");
        if (regTask || e.Args.Contains("--unregister-autostart"))
        {
            string res;
            try { res = regTask ? Autostart.Register(Environment.ProcessPath ?? "") : Autostart.Unregister(); }
            catch { res = "Error"; }
            Environment.ExitCode = res == "Ok" ? 0 : 2;   // 主实例据退出码刷新/报错
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // 关窗=隐到托盘；退出仅经托盘

        DispatcherUnhandledException += (s, ex) => { ShowCrash(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) => ShowCrash(ex.ExceptionObject as Exception);

        // 单实例（best-effort）：已运行则置信号让旧实例显示窗口，自己退出。同步对象创建/打开失败
        // （另有提权实例持有 Global 命名对象、ACL 受限等）绝不因此崩溃——按「本实例照常运行」放行。
        try
        {
            _mutex = new Mutex(true, @"Global\rockbenben.clockwork.mutex", out bool createdNew);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\rockbenben.clockwork.show");
            if (!createdNew)
            {
                bool got = false;
                try { got = _mutex.WaitOne(1200); } catch (AbandonedMutexException) { got = true; }   // 旧实例正退出则接管
                if (!got) { _showEvent.Set(); Shutdown(); return; }
            }
        }
        catch { _mutex = null; _showEvent = null; }

        _exePath = Environment.ProcessPath ?? "";
        _exeDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
        RegisterAumid();

        _cfgPath = ConfigPath.Resolve(_exeDir);
        EnsureConfigFile();
        _config = ConfigStore.Read(_cfgPath, out var normalized);
        // 规范化界面语言到「必是受支持的一门」：空→跟随系统；不在 18 项列表里的有效文化→映射最接近（pt-BR→pt）；
        // 无效→跟随系统。既尊重示例配置指定的语言，又保证送进 MainWindow 下拉的语言必能匹配——
        // 否则「非空但不在列表」会被下拉初始化当不匹配、强存 zh-CN 并重启，弄丢用户/系统语言。变了就落盘。
        var normLang = Languages.Normalize(_config.Settings.Language);
        if (!string.Equals(normLang, _config.Settings.Language, StringComparison.Ordinal))
        { _config.Settings.Language = normLang; normalized = true; }
        // 读入时若发生了重启后有影响的规范化（剔 null / 补生或重发 id / 补语言），立即写回——
        // 尤其去重重发的提醒 id：不落盘则每次启动都换新 id，运行态接不上、被去重那条每次重启都重弹。
        if (normalized) { try { ConfigStore.Write(_config, _cfgPath); } catch { } }
        // 提醒运行态落盘路径 + 载入上次的耐久态（上次触发日期/稍后到点）。重启后不再重复弹当天已弹过的。
        _statePath = Path.Combine(CfgDir, "clockwork.state.json");
        foreach (var kv in ReminderStateStore.Load(_statePath)) _reminderStates[kv.Key] = kv.Value;
        // 载入时顺手清掉过期(早于今天)的稍后，别让陈旧记录长期留在盘里（Decide 也有运行期兜底）。
        bool cleaned = false;
        foreach (var st in _reminderStates.Values)
            if (st.SnoozeUntil is DateTime su && su.Date < DateTime.Now.Date) { st.SnoozeUntil = null; cleaned = true; }
        if (cleaned) ReminderStateStore.Save(_statePath, _reminderStates);
        _startupReminderIds = new HashSet<string>(_config.Reminders.Select(x => x.Id));
        Strings.ApplyCulture(_config.Settings.Language);   // 建任何窗口前设 UI 文化
        if (Strings.IsRightToLeft)                          // 阿拉伯语等：全窗口默认从右向左（须在建任何窗口前覆盖元数据）
            FrameworkElement.FlowDirectionProperty.OverrideMetadata(
                typeof(Window), new FrameworkPropertyMetadata(System.Windows.FlowDirection.RightToLeft));

        _main = new MainWindow(_config, SaveConfig, MigrateReminderState);
        _tray = new TrayIcon(this);

        // 提醒计时器：记录启动时刻与开机分钟数（供「登录时」提醒门控），按 tickSeconds 轮询。
        _startTime = DateTime.Now;
        _uptimeAtLaunch = SystemInfo.UptimeMinutes();
        StartReminderTimer();
        RegisterStopHotkey();

        // 跨实例「显示窗口」信号：事件驱动等待（原每秒轮询）。AutoReset 事件被 Set 才回调，常态零唤醒；
        // executeOnlyOnce:false = 每次信号都再等下一次。（单实例对象创建失败时 _showEvent 为 null，跳过。）
        if (_showEvent != null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, executeOnlyOnce: false);

        bool boot = e.Args.Contains("--boot");
        bool forceShow = e.Args.Contains("--show");   // 语言切换重启后：强制显示窗口，忽略「启动时最小化」
        if (boot)
        {
            _main.ShowInTaskbar = false;   // 自启：不显窗、只入托盘
            var bt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            bt.Tick += (s, _) => { bt.Stop(); RunLaunchAsync(true); };
            bt.Start();
        }
        else if (_config.Settings.StartMinimized && !forceShow)
        {
            _main.ShowInTaskbar = false;
        }
        else
        {
            _main.Show();
        }
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
                    s => StepRunner.RunStepMark(s, ConfirmDestructive, selfPaths),
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
        try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, HotkeyId, p.Modifiers, p.Vk); } catch { }
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
                try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, slot, p.Modifiers, p.Vk); } catch { }
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
            StopSignal.Request();
            ShowToast("Clockwork", Strings.Get("Hotkey_Stopped"), Views.ToastLevel.Warn);
            handled = true;
        }
        else if (_groupHotkeyIds.TryGetValue(id, out var gid))
        {
            // 按组 Id 现查（注册后组可能已被编辑），禁用兜底跳过——正常情况下禁用组根本不会注册。
            var g = _config.ActionGroups.FirstOrDefault(x => x.Id == gid);
            if (g is { Enabled: true }) RunGroupAsync(g);
            handled = true;
        }
        return IntPtr.Zero;
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
        if (DndRemaining != null) return;   // 勿扰生效：本 tick 整体跳过（含静默组），到期自动恢复
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
            }
            foreach (var r in _config.Reminders.ToList())
            {
                if (!_reminderStates.TryGetValue(r.Id, out var st)) { st = new ReminderState(); _reminderStates[r.Id] = st; }
                string firedBefore = st.LastFiredDate;
                var d = ReminderEngine.Decide(r, now, _startTime, st, _uptimeAtLaunch, _startupReminderIds.Contains(r.Id));
                if (d.Action == "arm" && d.Base is DateTime b)
                {
                    // 到点后延迟：固定 + 随机（错峰）。'arm' 交这里算 pendingFireAt。
                    // 随机上界 +1 处防 int.MaxValue 溢出（否则 _rng.Next 抛异常、每 tick 崩溃循环）；long 累加避免 int 溢出。
                    // 固定延时不设上限（用户可能有意配多天错峰）。
                    int rd = r.RandomDelaySeconds;
                    long rand = rd > 0 ? _rng.Next(0, rd == int.MaxValue ? rd : rd + 1) : 0;
                    long extra = (long)r.DelaySeconds + rand;
                    st.PendingFireAt = b.AddSeconds(extra);
                }
                else if (d.Action == "fire")
                {
                    // 仅"时间型首触发"(本次 Decide 刚把 LastFiredDate 置为今天)在弹模态前先落盘，防被杀/断电后次日重复弹。
                    // 稍后/重复型触发不预存——它们清掉的 SnoozeUntil/NextRepeatAt 若在弹窗时被杀，宁可从盘上旧值恢复重弹也别丢。
                    // durable：此处的意义就是「先写成盘再弹窗」，不能走失败转后台的快路径（后台没落地就被杀等于没存）。
                    if (st.LastFiredDate != firedBefore) ReminderStateStore.Save(_statePath, _reminderStates, durable: true);
                    var (action, snooze) = FireReminder(r);
                    if (snooze is int m) ReminderEngine.Snooze(st, now, m);
                    else ReminderEngine.UpdateAfterFire(r, now, action, st);
                    durableChanged = true;   // 稍后/重复又改了状态 → 循环末再存一次
                }
            }
            if (durableChanged) ReminderStateStore.Save(_statePath, _reminderStates);
        }
        finally { _reminderTickBusy = false; }
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
            // 静默组无确认交互，跑一次即完结（返回 "ok" 让 UpdateAfterFire 停）——否则配了 repeatMinutes
            // 会每 N 分钟把整组（可能含静音/关应用/锁屏）重跑，最多 20 次。要周期催促请用非静默提醒。
            return ("ok", null);
        }
        if (r.Speak) ReminderActions.Speak(r.Message);
        bool confirm = r.OnYes != null && r.OnYes.Type != "none";
        // 无动作、非重复 → 走托盘气泡（不置顶抢视线）。气泡时长遵循配置的显示时长（未设则 5s）。
        if (!confirm && r.RepeatMinutes <= 0)
        {
            // 提醒 toast：显示时长取配置的「自动关闭(秒)」；0=不自动关(常驻到点击)——离屏也不会错过，
            // 与编辑器"自动关闭 0=不关"的说明一致（状态类 toast 仍固定几秒自动关）。
            int secs = ReminderEngine.PopupTimeoutSeconds(r);   // 已在源头封顶 24h，secs*1000 不会越界
            int dur = secs > 0 ? secs * 1000 : (preview ? 5000 : 0);   // 预览固定 5s 自动关；真触发 0=常驻
            ShowToast(Strings.Get("Tray_ReminderTitle"), r.Message, Views.ToastLevel.Info, dur);
            return ("ok", null);
        }
        int autoDismiss = ReminderEngine.PopupTimeoutSeconds(r);
        var (act, snooze) = Views.ReminderPopupWindow.Show(_main, r.Message, confirm, autoDismiss);
        if (act == "yes") ReminderActions.RunOnYes(r.OnYes, _config.ActionGroups, RunGroupAsync, WarnToast);
        if (act == "snooze") return ("", snooze);
        return (act, null);
    }

    // 「预览这条」：立即触发一次（不改运行状态）。
    public void PreviewReminder(Reminder r) => FireReminder(r, preview: true);

    // 配置所在目录（state/run.log/error.log 都落在配置旁）：一处定义，5 个落点共用。
    private string CfgDir => Path.GetDirectoryName(_cfgPath) ?? _exeDir;

    // 警告气泡的便捷入口（RunOnYes 等回调用）。ShowToast 自身已全 try/catch 守护、可跨线程调。
    private void WarnToast(string msg) => ShowToast("Clockwork", msg, Views.ToastLevel.Warn);

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

    public void RunStepAsync(LaunchStep step)
    {
        // 消息步骤：在 UI 线程弹窗（是/否闸门 + 可选朗读/onYes），不走后台执行——否则会被当作未知类型告警。
        if (step.Kind == "message")
        {
            if (step.Speak) ReminderActions.Speak(step.Message);
            if (ShowGroupMessage(step) == MsgResult.Yes)
                ReminderActions.RunOnYes(step.OnYes, _config.ActionGroups, RunGroupAsync, WarnToast);
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
                var mark = StepRunner.RunStepMarkRepeat(step, ConfirmDestructive, selfPaths);
                ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + mark.Mark, mark.Fail > 0 ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
            }
            finally { _runGate.End(); Interlocked.Exchange(ref _stepRunning, 0); }
        });
    }

    // 「运行整组」/提醒静默组/onYes 组：后台跑动作组。
    // 跑快照而非活对象：后台 foreach 组步骤时，UI 线程可能正在编辑/删除组（删除守卫的联动清理会就地改
    // 其他组的 Steps），枚举活列表会抛「集合已修改」；且 Task.Run 无 catch 时整组静默中止。
    public void RunGroupAsync(ActionGroup group)
    {
        // 快照与 deps 都在调用线程（UI）上先建好：后台再碰活的 _config.ActionGroups 会与 UI 增删组竞态。
        var snap = group.SnapshotForRun();
        var deps = BuildGroupDeps();
        Task.Run(() =>
        {
            _runGate.Begin();
            try { ActionGroupRunner.RunGroup(snap, deps); }
            catch (Exception ex) { WarnToast(Lf("Mark_Exception", ex.Message)); }
            finally { _runGate.End(); }
        });
    }

    private GroupDeps BuildGroupDeps()
    {
        var selfPaths = new[] { _exePath };
        var groups = _config.ActionGroups.ToList();   // 组列表快照（UI 线程取）：后台 Resolve 不再枚举 UI 正在增删的活列表
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            RunStep = s => StepRunner.InvokeStepAction(s, ConfirmDestructive, selfPaths),
            ShowMessage = ShowGroupMessage,
            RunOnYes = s => ReminderActions.RunOnYes(s.OnYes, groups, g => ActionGroupRunner.RunGroup(g.SnapshotForRun(), deps), WarnToast),
            Speak = ReminderActions.Speak,
            OnStepError = (s, ex) => LogGroupStepError(s, ex),
            // 组内嵌套「动作组」步骤：跑引用组的快照（防运行中被编辑/清理）。按 id 互斥天然防环（A→B→A 时内层 A 直接返回）。
            RunGroupStep = s => { var ng = ActionGroupResolver.Resolve(groups, s.GroupId); if (ng != null && ng.Enabled) ActionGroupRunner.RunGroup(ng.SnapshotForRun(), deps); },
        };
        return deps;
    }

    // 动作组内某步抛异常：记一笔到错误日志并弹一次托盘气泡，随后整组继续（不静默中止）。
    private void LogGroupStepError(LaunchStep step, Exception ex)
    {
        var logPath = Path.Combine(CfgDir, "clockwork.error.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 动作组步骤失败（已跳过、整组继续）: {StepDisplay.StepSummary(step)} — {ex.Message}\r\n"); } catch { }
        ShowToast("Clockwork", Lf("Mark_Exception", StepDisplay.StepSummary(step)), Views.ToastLevel.Warn);
    }

    // 动作组 message 步骤弹窗（confirm=是/否闸门；否则仅确定）。在 UI 线程弹。
    private MsgResult ShowGroupMessage(LaunchStep step)
    {
        bool confirm = step.Confirm || (step.OnYes != null && step.OnYes.Type != "none");
        return Dispatcher.Invoke(() =>
        {
            if (confirm)
                return Views.BrandDialog.Confirm(_main, "Clockwork", step.Message) ? MsgResult.Yes : MsgResult.No;
            Views.BrandDialog.Info(_main, "Clockwork", step.Message);
            return MsgResult.Ok;
        });
    }

    private void NotifyRunResult(LaunchRunResult r)
    {
        var s = r.Summary;
        if (s.Stopped) ShowToast("Clockwork", Lf("Tray_LaunchStopped", s.Total), Views.ToastLevel.Warn);
        else if (s.Fail > 0) ShowToast("Clockwork", Lf("Tray_LaunchWarn", s.Total, s.Fail), Views.ToastLevel.Warn);
    }

    // 品牌化非模态通知（右下角 toast，替代系统托盘气泡）。自动切到 UI 线程；整体兜底绝不抛。
    // 后台线程(动作组/单步)调用时 Dispatcher.Invoke 遇正在关闭的调度器会抛(TaskCanceled/InvalidOperation)，
    // 必须一并吞掉——否则会从 OnStepError 逃出、掀掉动作组剩余步骤(收工/睡前组的锁屏/关机就不执行了)。
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info, int durationMs = 5000)
    {
        try
        {
            if (Dispatcher.CheckAccess()) Views.NotificationToast.Show(title, message, level, durationMs);
            else Dispatcher.Invoke(() => Views.NotificationToast.Show(title, message, level, durationMs));
        }
        catch { }
    }

    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    // 破坏性系统命令确认（在 UI 线程弹框）。破坏性 → clay 警示轨。
    private bool ConfirmDestructive(string action)
        => Dispatcher.Invoke(() => Views.BrandDialog.Confirm(
            _main, Strings.Get("Confirm_Title"), Lf("Confirm_Destructive", action), Views.ToastLevel.Warn));

    // 配置存盘（原子写）。ViewModel 增删改移时回调。持续写失败（OneDrive/杀软锁死超过重试）不再静默吞——
    // 界面看着已保存、重启全回退是静默数据丢失，至少弹个警告让用户知道改动只在内存里。
    public void SaveConfig()
    {
        try { ConfigStore.Write(_config, _cfgPath); }
        catch (Exception ex) { ShowToast("Clockwork", Lf("Warn_SaveConfigFail", ex.Message), Views.ToastLevel.Warn); }
        // 组增删改/启停/改键都走此保存——热键跟着当前配置即时重建。
        // 捕捉挂起期间跳过（改急停键的保存正发生在挂起中）：此刻重建会让组抢注急停的新组合；
        // 捕捉一定以 ResumeHotkeys 收尾，那里会按「急停先、组后」的次序统一重建。
        if (!_hotkeysSuspended) RebindGroupHotkeys();
    }

    // 编辑提醒会换新 id（借此重置「今天已弹」态），但正在进行的「稍后」不该丢：把 SnoozeUntil 迁到新 id。
    // 只迁 snooze，不迁 LastFiredDate——「编辑即可当天重弹」正是换 id 的本意。迁完即耐久落盘，防编辑后崩溃丢 snooze。
    public void MigrateReminderState(string oldId, string newId)
    {
        if (string.IsNullOrEmpty(oldId) || oldId == newId) return;
        // 「启动时就存在」资格随编辑迁移：否则编辑过的提醒 existedAtStartup=false，「错过必补」当天失效。
        if (_startupReminderIds.Remove(oldId)) _startupReminderIds.Add(newId);
        if (!_reminderStates.TryGetValue(oldId, out var old)) return;
        if (old.SnoozeUntil is DateTime)
        {
            if (!_reminderStates.TryGetValue(newId, out var st)) { st = new ReminderState(); _reminderStates[newId] = st; }
            st.SnoozeUntil = old.SnoozeUntil;
        }
        // PendingFireAt 有意不迁：它按旧时间算出，编辑就是要按新配置重新判定。
        _reminderStates.Remove(oldId);   // 旧 id 已不被任何提醒引用，成孤儿；显式移除并落盘
        ReminderStateStore.Save(_statePath, _reminderStates);
    }

    private void RegisterAumid()
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
        try { Native.Shell.SetCurrentProcessExplicitAppUserModelID(Aumid); } catch { }
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

    private void ShowCrash(Exception? ex)
    {
        var logPath = Path.Combine(CfgDir, "clockwork.error.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n"); } catch { }
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
