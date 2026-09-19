using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Native;
using Clockwork.ViewModels;
using Microsoft.Win32;

namespace Clockwork;

public partial class MainWindow : Window
{
    // 托盘「退出」置 true 后才真正关闭；否则关窗=隐到托盘。
    public bool AllowClose { get; set; }

    private readonly RootConfig? _config;
    private readonly Action? _save;
    private readonly LaunchListVm? _launch;
    private readonly ReminderListVm? _reminders;
    private readonly GroupListVm? _groups;
    private readonly SystemStartupVm? _system;
    private bool _systemLoaded;
    private readonly PortsVm? _ports;
    // 端口页的自动刷新。只在「停在本页 + 窗口可见」时跑：本程序常驻托盘，
    // 隐起来还轮询是纯浪费。扫一轮 3ms，所以 5 秒一次的成本约等于零。
    private System.Windows.Threading.DispatcherTimer? _portsTimer;
    // 「一次扫描还在飞」的闸（见 LoadPorts）。用 int 而不是 bool：它在后台线程上被放开，
    // 走 Interlocked 是为了让「读—改—写」这一步本身是原子的，不必再去想内存可见性。
    private int _portsScanning;

    // 设计器/兜底无参构造。
    public MainWindow()
    {
        InitializeComponent();
        RefreshStopButton();   // 无配置也先摆正：默认隐藏，别让设计器/兜底路径漏出一颗常驻按钮
    }

    // isSkippedToday：查一条提醒今天是否被手动跳过。运行态住在 App 的字典里（按 id 做键），
    // 列表拿不到，故由 App 注入一个只读谓词——比把这个「只管今天」的临时状态搬进配置轻得多。
    public MainWindow(RootConfig config, Action save, Action<string, Reminder>? migrateReminderState = null,
                      Func<Reminder, bool>? isSkippedToday = null)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);   // 深色标题栏 + 消除开窗白闪（本方法自己挂 SourceInitialized/ContentRendered）
        Views.WindowSizing.FitToWorkArea(this);   // 默认高度按屏幕收放，小屏不越界、大屏不浪费
        Title = "Clockwork · " + Strings.Get("App_Subtitle");   // 副标题并入系统标题栏，去掉内容区重复的首栏
        _config = config;
        _save = save;

        _launch = new LaunchListVm(config, save);
        GridLaunch.ItemsSource = _launch.Rows;
        GridLaunch.SelectionChanged += (s, e) => { _launch.SelectedIndex = GridLaunch.SelectedIndex; LaunchRowOps.IsEnabled = GridLaunch.SelectedIndex >= 0; };
        Views.DataGridReorder.Attach(GridLaunch, (from, to) => { _launch.MoveTo(from, to); SyncSelection(); });
        AttachRowContextMenu(GridLaunch);   // 右键先选中该行，见该方法的注释

        _reminders = new ReminderListVm(config, save, migrateReminderState, isSkippedToday);
        GridRemind.ItemsSource = _reminders.Rows;
        GridRemind.SelectionChanged += (s, e) => { _reminders.SelectedIndex = GridRemind.SelectedIndex; ReminderRowOps.IsEnabled = GridRemind.SelectedIndex >= 0; };
        Views.DataGridReorder.Attach(GridRemind, (from, to) => { _reminders.MoveTo(from, to); SyncSel(GridRemind, _reminders); });
        AttachRowContextMenu(GridRemind);   // 右键先选中该行，见该方法的注释

        _groups = new GroupListVm(config, save);
        GridGroup.ItemsSource = _groups.Rows;
        GridGroup.SelectionChanged += (s, e) => { _groups.SelectedIndex = GridGroup.SelectedIndex; GroupRowOps.IsEnabled = GridGroup.SelectedIndex >= 0; };
        Views.DataGridReorder.Attach(GridGroup, (from, to) => { _groups.MoveTo(from, to); SyncSel(GridGroup, _groups); });
        AttachRowContextMenu(GridGroup);   // 右键先选中该行，见该方法的注释

        _system = new SystemStartupVm(SystemStartupReader.SetItemEnabled, ReportSystemMsg, PromptRelaunchAdmin);
        GridSystem.ItemsSource = _system.Rows;
        // 「N / M 项」：与端口页同一形状。默认隐藏只读项，被藏了多少由这个数字直接回答——
        // 这句从前是页脚里的一句话（「系统 / 策略 / 一次性等只读项默认隐藏」），没人读，还占着一行。
        _system.Changed = () => SysCount.Text = Lf("System_Count", _system.Rows.Count, _system.TotalCount);
        _system.Changed();

        var ports = new PortsVm { DevOnly = config.Settings.PortsDevOnly };
        _ports = ports;
        // 「N / M 个端口」：M 是全部在监听的端口，N 是当前档位筛出来的。挂在 Changed 上，
        // 刷新、搜索、切档位三条路径都会经过 ApplyFilter，不用在三个事件处理器里各写一遍。
        ports.Changed = () => PortsCount.Text = Lf("Ports_Count", ports.Rows.Count, ports.TotalCount);
        ports.Changed();
        _portsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _portsTimer.Tick += (_, _) => LoadPorts();
        IsVisibleChanged += (_, _) => SyncPortsTimer();   // 隐到托盘就停，重新显示再起
        DevOnlyPorts.IsChecked = config.Settings.PortsDevOnly;
        GridPorts.ItemsSource = _ports.Rows;
        // 系统启动项页首次选中时才扫描（枚举较慢）；端口页每次选中都重扫（数据易变、扫描很快）。
        Tabs.SelectionChanged += Tabs_SelectionChanged;

        // 设置页
        VersionText.Text = "v" + AppVersion();
        // **这个置位一直没人写过。** _loadingSettings 声明在下面、也被 Settings_Changed 读着，
        // 却从来没有被赋过值（编译器一直在报 CS0649），于是那道重入守卫恒为假、等于不存在——
        // 而它上面那两行注释描述的正是这里：构造期间填这几个控件会逐个触发 Settings_Changed
        // → _save，每开一次主窗口就把配置文件白写好几遍。
        // try/finally 而不是直接两句：中间任一句抛出去，标志留在 true 上会让整个设置页从此不再存盘。
        _loadingSettings = true;
        try
        {
            StartupDelayBox.Text = config.Settings.StartupDelaySeconds.ToString();
            StartMinChk.IsChecked = config.Settings.StartMinimized;
            WaitReadyChk.IsChecked = config.Settings.StartupWaitForReady;
            PanelEnabledChk.IsChecked = config.Settings.PanelEnabled;
            MiddleLongPressChk.IsChecked = config.Settings.PanelMiddleLongPress;
            LongPressMsBox.Text = config.Settings.PanelLongPressMs.ToString();
            // 手势总开关与手势管理器标题行那个勾是**同一个字段**的两个视图。
            // 这里只能填一次（主窗口关闭只是隐藏，构造函数不会再跑），所以真正要守的接缝
            // 在 GestureManager_Click：管理器关掉之后必须把这个勾读回来，否则它手里那个
            // 陈旧值会在下一次 SaveConfig 时被回写。（曾经这里写的是「不会互相看到旧值，
            // 因为管理器是模态开的、每次构造时重读」——那只能证明**管理器**看到的是新的，
            // 反方向压根没论及。）
            GesturesEnabledChk.IsChecked = config.Settings.GesturesEnabled;
            UpdatePanelRows();
            WireHotkeyBox();   // 急停键「点击即录键」（Attach 内会填入当前值）
        }
        finally { _loadingSettings = false; }
        // 急停按钮跟着运行状态走：订阅一次，窗口真正关闭（托盘退出）时摘掉——
        // 平时关窗只是隐到托盘，窗口对象还在，摘早了再打开就不会更新了。
        if (AppInstance is { } app)
        {
            app.RunStateChanged += RefreshStopButton;
            Closed += (_, _) => app.RunStateChanged -= RefreshStopButton;
        }
        RefreshStopButton();   // 建窗时可能已有东西在跑（开机清单先跑、用户随后才打开窗口）
        int langSel = 0;
        for (int i = 0; i < Languages.All.Length; i++)
        {
            var (native, code) = Languages.All[i];
            LangCombo.Items.Add(new ComboBoxItem { Content = native, Tag = code });
            if (code == config.Settings.Language) langSel = i;
        }
        LangCombo.SelectedIndex = langSel;

        int themeSel = 0;
        for (int i = 0; i < Themes.All.Length; i++)
        {
            ThemeCombo.Items.Add(new ComboBoxItem { Content = Strings.Get("Theme_" + Themes.All[i]), Tag = Themes.All[i] });
            if (Themes.All[i] == Themes.Normalize(config.Settings.Theme)) themeSel = i;
        }
        ThemeCombo.SelectedIndex = themeSel;
        UpdateAutostartLabel();
    }

    // 与换语言同样重启。原本想做成当场生效（颜色不像文案那样在构造时定死），
    // 实测不成立：只替换调色板时，切回去会剩一半旧颜色（详见 App.ApplyTheme 里的记录）。
    // 改成整份资源重建之后颜色是齐的，但已经开着的窗口抓的是旧画笔实例，仍然不会变——
    // 一个「有的窗口新主题、有的窗口旧主题」的程序比重启一次糟得多。
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_config == null) return;
        var theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "dark";
        if (theme == Themes.Normalize(_config.Settings.Theme)) return;   // 含初始化时的自赋值
        _config.Settings.Theme = theme;
        _save?.Invoke();
        (System.Windows.Application.Current as App)?.RelaunchForLanguage();
    }

    private void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_config == null) return;
        var lang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh-CN";
        if (lang == _config.Settings.Language) return;   // 含初始化时的自赋值
        _config.Settings.Language = lang;
        _save?.Invoke();
        // 语言即时应用：XAML 本地化在加载时解析、RTL 与代码构造的文本也只在启动时定，故自动重启
        // 让新语言完整生效（重启后窗口强制显示，不受「启动时最小化」影响）。
        (System.Windows.Application.Current as App)?.RelaunchForLanguage();
    }

    // 构造期间填下拉框会触发 SelectionChanged → Settings_Changed → 存盘。存的值和读到的一样，
    // 不会改坏数据，但每开一次窗口就白写一次配置文件。置位期间早退，比给每个控件都写一遍
    //「新值等于旧值就 return」省事，也不会漏掉下一个新增的控件。
    private bool _loadingSettings;

    // 面板关掉时把它下属的两行收起来：热键和中键长按此刻控制不了任何东西，
    // 留着就是「看着能填、填了不生效」——本程序在编辑器里对这件事一向的处理是收起来
    // （UpdateOnYes / UpdateMessageRows / UpdateSysRows 三处同一条立场）。
    // 只收这两行，不收「管理面板…」：关的是「用不用」，不是「删不删」，
    // 面板页那些数据还在，用户仍该进得去看和整理。
    private void UpdatePanelRows()
    {
        var vis = PanelEnabledChk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelHotkeyRow.Visibility = vis;
        PanelMiddleRow.Visibility = vis;
    }

    // —— 底部设置栏 ——
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_config == null || _loadingSettings) return;
        // 非法/越界输入不静默丢弃：合法则 clamp 到 [0,600] 存下，非法则保持旧值；
        // 两种情况都把规范化后的值回写输入框，保证「看到的 = 存下的」。
        if (int.TryParse(StartupDelayBox.Text.Trim(), out var d) && d >= 0)
            _config.Settings.StartupDelaySeconds = StepHelpers.ClampStartupDelay(d);
        StartupDelayBox.Text = _config.Settings.StartupDelaySeconds.ToString();
        _config.Settings.StartMinimized = StartMinChk.IsChecked == true;
        _config.Settings.StartupWaitForReady = WaitReadyChk.IsChecked == true;
        _config.Settings.PanelEnabled = PanelEnabledChk.IsChecked == true;
        UpdatePanelRows();
        _config.Settings.GesturesEnabled = GesturesEnabledChk.IsChecked == true;
        _config.Settings.PanelMiddleLongPress = MiddleLongPressChk.IsChecked == true;
        // 长按阈值同上：合法则 clamp 到 [150, 2000] 存下，非法保持旧值，规范化后的值回写输入框。
        // 下界 150——低于这个数，稍慢一点的普通中键点击就会被判成长按，中键从此不好点了；
        // 上界 2000，按到两秒还没反应，人早就以为坏了。
        if (int.TryParse(LongPressMsBox.Text.Trim(), out var ms))
            _config.Settings.PanelLongPressMs = StepHelpers.ClampLongPressMs(ms);
        LongPressMsBox.Text = _config.Settings.PanelLongPressMs.ToString();
        // 面板外观。下拉的值从 Tag 取（Content 是本地化文案，换语言就对不上了）；
        // 取不到就保持旧值，不静默改成默认——用户选过的东西不该因为一次读取失败被抹掉。
        _save?.Invoke();
    }

    // 急停键与面板键「点击即录键」——与组热键/发送键统一走 KeyCaptureBox（见 WireHotkeyBox，在构造末尾调用）。
    // 两个框都不做「和另一个撞了」的前置校验：撞了的那个注册失败时会点名 toast（见 RebindFunctionHotkey），
    // 而在这里再拦一道就得决定谁让谁——那是用户的事，不是表单该替他做的决定。
    private void WireHotkeyBox()
    {
        if (_config == null) return;
        Views.KeyCaptureBox.Attach(HotkeyBox, HotkeyCapture.KeyCaptureMode.Hotkey, null,
            () => _config.Settings.StopHotkey,
            // 保存→SaveConfig→按新配置重注册全部热键；急停按钮的提示里印着这个键，一并刷新
            combo => { _config.Settings.StopHotkey = combo; _save?.Invoke(); RefreshStopButton(); });
        Views.KeyCaptureBox.Attach(PanelHotkeyBox, HotkeyCapture.KeyCaptureMode.Hotkey, null,
            () => _config.Settings.PanelHotkey,
            // 面板键没有对应的界面按钮要刷（面板是热键/托盘唤出的），保存后重注册就够
            combo => { _config.Settings.PanelHotkey = combo; _save?.Invoke(); });
        // 一键直达刻意用 HotkeyBare：RunAny 式的裸 `（Oem3）也要能录——不带修饰键按下即开，
        // 代价是这个字符在全系统打不出来，急停/面板/组键不许走这一档（见 HotkeyCapture 枚举注释）。
        // 实测（探针，2026-09）：物理裸 ` 并不会被 RunAny 接走——真正让框录不到的是中文输入法，
        // 它把裸键改写成 WPF 的 ImeProcessed；KeyCaptureBox 已关 IME 并用 ResolveKey 解包真值。
        // allowTyping 退居纯兜底（奇怪钩子 / 远程桌面键事件变形时双击手输 Oem3）；
        // RegisterHotKey 裸键抢得赢 RunAny——注册后 WM_HOTKEY 实测归 Clockwork。
        Views.KeyCaptureBox.Attach(QuickOpenBox, HotkeyCapture.KeyCaptureMode.HotkeyBare,
            combo => KeyInput.ToHotkeyParams(combo) != null && !HotkeyCapture.IsReserved(combo),
            () => _config.Settings.QuickOpenHotkey,
            // 同面板键：保存即重注册，清空 = 不绑定（RebindFunctionHotkey 认空串）
            combo => { _config.Settings.QuickOpenHotkey = combo; _save?.Invoke(); },
            allowTyping: true);
    }

    // 标签条右端的急停按钮：只在真有东西在跑时存在。
    // 常驻一个永不变化的图标会被读成「正在运行」指示灯，而且按下去毫无变化，两头都在说谎；
    // 「出现＝真有东西在跑、消失＝真的停了」之后，它同时是状态也是控件。
    //
    // 这里刻意没有「已请求停止、正在收尾」的中间态：引擎里每一处不可打断的等待都很短且每轮都查急停
    // （等窗口 500ms 一轮、置前台发键 200+500ms、前台切换 120ms），那个中间态实际只活几毫秒到最多 0.7 秒，
    // 只会在一颗马上要消失的按钮上闪一下灰。「我收到了」的回执由气泡保证（每条急停路径都弹），不靠它。
    //
    // 提示与屏幕阅读器名共用一串：纯图形按钮没有可读文字，只给 ToolTip 等于对读屏用户什么都没给。
    private void RefreshStopButton()
    {
        StopAllBtn.Visibility = AppInstance?.IsRunning == true ? Visibility.Visible : Visibility.Collapsed;
        var hint = StopHint.Compose(Strings.Get("Tray_Stop"), _config?.Settings.StopHotkey);
        StopAllBtn.ToolTip = hint;
        System.Windows.Automation.AutomationProperties.SetName(StopAllBtn, hint);
    }

    // 鼠标点完不把焦点环留在急停按钮上：环是强调色，而强调色在本应用里读作「活动 / 正在跑」
    // （选中标签的刻度线、「运行这一步」都是它），一直亮在一颗红色急停按钮上会被误读成「还有东西在运行」。
    // 只对鼠标这么做：键盘激活(空格/回车)时保留焦点——那是用户自己 Tab 过来的位置，抹掉会让下一次 Tab 从头开始。
    private bool _stopClickedByMouse;

    private void StopAll_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _stopClickedByMouse = true;

    // 三个急停入口（热键 / 托盘 / 本按钮）统一走 App.RequestStop，行为与提示一致。
    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        AppInstance?.RequestStop();
        if (_stopClickedByMouse) Keyboard.ClearFocus();
        _stopClickedByMouse = false;
    }

    // —— 关于 ——
    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    private static string AppVersion()
    {
        var v = typeof(App).Assembly.GetName().Version;
        return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateChecker.RepoUrl);

    // 检查更新：拉 GitHub 最新 Release 比对版本。有新版询问是否前往下载；否则提示已最新；失败如实回。
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var old = CheckUpdateBtn.Content;
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = Strings.Get("Update_Checking");
        var info = await UpdateChecker.CheckAsync(AppVersion());
        CheckUpdateBtn.Content = old;
        CheckUpdateBtn.IsEnabled = true;

        if (info.Error != null)
        {
            Views.BrandDialog.Warn(this, "Clockwork", Lf("Update_Failed", info.Error));
            return;
        }
        if (info.HasNewer)
        {
            if (Views.BrandDialog.Confirm(this, "Clockwork", Lf("Update_Available", info.Latest)))
                OpenUrl(info.Url ?? UpdateChecker.ReleasesUrl);
        }
        else
        {
            Views.BrandDialog.Info(this, "Clockwork", Lf("Update_Latest", "v" + info.Current));
        }
    }

    private void UpdateAutostartLabel()
    {
        AutostartChk.IsEnabled = false;
        Task.Run(() => Autostart.IsRegistered()).ContinueWith(t =>
        {
            bool reg = t.IsCompletedSuccessfully && t.Result;
            AutostartChk.IsChecked = reg;
            AutostartChk.Tag = reg;          // 失败回弹用：记住"界面当前认为的真实状态"
            AutostartChk.IsEnabled = true;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // 复选框的 Click 只在用户交互时触发（程序设 IsChecked 不会触发），所以这里不会因 UpdateAutostartLabel
    // 回写 IsChecked 而递归。点击发生时 IsChecked 已被 WPF 翻到「用户想要的新状态」，currentlyReg（旧 Tag）
    // 才是操作前的真实状态，用来决定该注册还是注销。
    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool currentlyReg = AutostartChk.Tag as bool? ?? false;
        var exe = Environment.ProcessPath ?? "";
        AutostartChk.IsEnabled = false;
        Task.Run(() => currentlyReg ? Autostart.Unregister() : Autostart.Register(exe)).ContinueWith(t =>
        {
            var res = t.IsCompletedSuccessfully ? t.Result : "Error";
            if (res == "NeedsAdmin")   // 无管理员权限：直接以管理员身份重开自己完成（注销），不再只弹提示。
            {
                ElevateAutostart(exe, register: !currentlyReg);
                return;   // 状态由 ElevateAutostart 在子进程结束后经 UpdateAutostartLabel 刷新
            }
            AutostartChk.IsEnabled = true;
            if (res != "Ok")
            {
                AutostartChk.IsChecked = currentlyReg;   // 没成功就别让勾选状态撒谎
                Views.BrandDialog.Warn(this, "Clockwork", Lf("Autostart_Fail", res));
            }
            else AutostartChk.Tag = !currentlyReg;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // 以管理员身份重开自身执行自启注册/注销（触发 UAC），等子进程退出后刷新标签。
    // 子进程走 App 的 --register-autostart / --unregister-autostart 一次性模式：做完即退，不建窗口/托盘。
    private void ElevateAutostart(string exe, bool register)
    {
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = register ? "--register-autostart" : "--unregister-autostart",
                    Verb = "runas",           // 触发 UAC 提升
                    UseShellExecute = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode ?? -1;
            }
            // 仅 ERROR_CANCELLED(1223)=用户取消 UAC 才静默；其他 Win32 失败（exe 被删/被锁等）如实报错，不再一律吞成取消。
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? -2 : -1; }
            catch { return -1; }
        }).ContinueWith(t =>
        {
            AutostartChk.IsEnabled = true;
            int code = t.IsCompletedSuccessfully ? t.Result : -1;
            // -2 = 用户取消 UAC：静默不报错。其余非 0 = 提权子进程执行失败。
            if (code != 0 && code != -2)
                Views.BrandDialog.Warn(this, "Clockwork", Lf("Autostart_Fail", "exit " + code));
            UpdateAutostartLabel();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    // —— 配置导入/导出 ——
    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var cfgPath = AppInstance?.ConfigFilePath;
        if (string.IsNullOrEmpty(cfgPath) || !File.Exists(cfgPath)) return;
        var dlg = new Microsoft.Win32.SaveFileDialog   // 限定 Win32：与 WinForms 同名类消歧义（沿用 Pickers 惯例）
        {
            // 默认名不能是配置文件本名：初始目录就是配置所在目录，同名默认值=导出目标即源文件自身，
            // File.Copy(源==目标) 必抛共享冲突，「一路确认」的默认流程永远失败。
            Filter = Strings.Get("Config_Filter"),
            FileName = "clockwork.settings.backup.json",
            InitialDirectory = Path.GetDirectoryName(cfgPath),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // 用户仍可手动选中配置文件本身 → 同路径守卫，给明确指引而非裸 IOException
            if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(cfgPath), StringComparison.OrdinalIgnoreCase))
            {
                Views.BrandDialog.Warn(this, "Clockwork", Strings.Get("Config_ExportSamePath"));
                return;
            }
            File.Copy(cfgPath, dlg.FileName, overwrite: true);
            Views.BrandDialog.Info(this, "Clockwork", Lf("Config_Exported", dlg.FileName));
        }
        catch (Exception ex) { Views.BrandDialog.Warn(this, "Clockwork", Lf("Config_ExportFail", ex.Message)); }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var app = AppInstance;
        if (app == null) return;   // 直接守卫 app（而非只判 cfgPath）：末尾要调 app.RelaunchForLanguage
        var cfgPath = app.ConfigFilePath;
        if (string.IsNullOrEmpty(cfgPath)) return;
        // 导入=整份配置覆盖，全应用最重的破坏性操作，与删除同用 Warn 红轨（别让它比删一行还显得温和）
        if (!Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"), Strings.Get("Config_ImportConfirm"), Views.ToastLevel.Warn)) return;
        var dlg = new Microsoft.Win32.OpenFileDialog   // 同上：限定 Win32
        {
            Filter = Strings.Get("Config_Filter"),
            InitialDirectory = Path.GetDirectoryName(cfgPath),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // 验证 JSON 可解析为 RootConfig，防止导入无效文件后应用启动异常。
            var json = File.ReadAllText(dlg.FileName);
            var test = System.Text.Json.JsonSerializer.Deserialize<RootConfig>(json, ConfigStore.JsonOptions);
            if (test == null) throw new InvalidOperationException(Strings.Get("Err_JsonNull"));
            // 与启动读取同一套规范化管线（剔 null 元素/补重 id/OnYes 归一）：导入落盘的就是规范形，
            // 「什么算合法配置」不在此另定义一份浅版本，也不把修补推迟到重启后的 Read。
            ConfigStore.Normalize(test);
            // 覆盖前把现有配置备份到 .bak：JSON 能解析≠语义正确（选错文件/不兼容配置照样通过 null 检查），
            // 备份给用户留一条撤销路径，避免唯一一份配置被无声覆盖后无法找回。
            try { if (File.Exists(cfgPath)) File.Copy(cfgPath, cfgPath + ".bak", overwrite: true); } catch { }
            // 用 ConfigStore 原子写（写临时文件再替换），而非 File.Copy 直接覆盖——避免中途 I/O 失败
            // 把唯一一份 config 截断成半截损坏的 JSON（下次启动会被 Read 当解析失败、回落默认配置）。
            ConfigStore.Write(test, cfgPath);
            // 从这一刻起 App 内存里的 _config 已作废：它靠重开新实例重读生效，本实例不得再回写。
            app.MarkConfigSuperseded();
        }
        catch (Exception ex) { Views.BrandDialog.Warn(this, "Clockwork", Lf("Config_ImportFail", ex.Message)); return; }
        // 写盘成功之后的收尾刻意放在 try 外：这里再抛异常也绝不能落回上面的「导入失败」分支而跳过重启——
        // 那会留下一个「新配置已在盘上、旧 _config 还在内存里」的实例。下面的确认框还是模态的，其嵌套消息
        // 循环期间提醒计时器照常在走（DispatcherTimer 不因模态停摆，正是 _reminderTickBusy 存在的原因），
        // 一次 SaveConfig（如「仅一次」提醒触发完自动取消勾选）就会把旧配置写回、无声还原导入——
        // 上面那道 MarkConfigSuperseded 闸门就是为这段窗口设的。
        // 迁移的账要在**这里**报，不能指望重启后那一次。
        //
        // 上面那句 Normalize 与启动读取共用同一条管线，所以导入一份旧版配置（panelSchema < 3）
        // 时，面板页迁移就在此刻真的发生了：几个「只当页用」的动作组被从动作列表里删掉、
        // 托盘也少了几行——一次结构性改写。而 LastMigrationLog 只有 App.OnStartup 会读，
        // 等重启后再读时 panelSchema 已经是 3、迁移那道门早关了，账是空的。
        // 于是用户看到的只有「导入成功」，然后发现三个动作和两行托盘不见了——
        // 正是 MigratePanelPages 自己的文档明令不许发生的结局。
        // 与「导入成功」合成一段说：两句话都是关于这一次导入的，分两个框弹反而像出了两件事。
        var mig = Core.ConfigStore.LastMigrationLog;
        Views.BrandDialog.Info(this, "Clockwork", mig.Count == 0
            ? Strings.Get("Config_Imported")
            : Strings.Get("Config_Imported") + Environment.NewLine + Environment.NewLine
              + Strings.Get("Mig_Title") + Environment.NewLine
              + string.Join(Environment.NewLine, mig));
        app.RelaunchForLanguage();   // 复用重启逻辑：重开自身 + 退出当前实例（内部保证无论成败都退出）
    }

    // 右键菜单开之前，先把光标底下那一行选中。
    //
    // WPF 的 DataGrid 右键**不会**改变选中行（ListBox 同理），而菜单里的命令——上移/下移/复制/
    // 今天不再——全都按 SelectedIndex 走。不补这一步就会：选中第 1 行、右键第 3 行、点「复制」，
    // 复制的是第 1 行。用户看着高亮在别处、菜单从这一行弹出来，结果作用在另一条上，且毫无提示。
    // 这与侧栏按钮时代的语义差别正在于此：按钮明摆着作用于「选中项」，右键则天然指向「这一行」。
    //
    // 空白处右键：没有行可指，直接取消菜单——弹一个作用在别处的菜单比不弹更糟。
    private static void AttachRowContextMenu(System.Windows.Controls.DataGrid grid)
    {
        bool overRow = false;   // 上一次右键是不是落在某一行上（表头/空白处则为 false）
        grid.PreviewMouseRightButtonDown += (s, e) =>
        {
            var row = Views.DataGridReorder.RowFromHit(e.OriginalSource as DependencyObject);
            overRow = row != null;
            if (row != null) grid.SelectedItem = row.Item;
        };
        grid.ContextMenuOpening += (s, e) =>
        {
            // 键盘 Menu 键也会走到这里，但它不经过上面那个鼠标事件——用陈旧的 overRow 判断
            // 会把键盘那条路整个堵死。WPF 在键盘调起时把光标坐标置为 -1，据此分流：
            // 键盘按当前选中行走（那正是用户焦点所在），鼠标才要求确实点在某一行上。
            bool byKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
            if (grid.SelectedIndex < 0 || (!byKeyboard && !overRow)) e.Handled = true;
        };
    }

    // 变更(增/改/删/移)后把 VM 的选中回推到对应 DataGrid。三个列表页统一走它。
    private static void SyncSel(System.Windows.Controls.DataGrid grid, ListVmBase? vm) { if (vm != null) grid.SelectedIndex = vm.SelectedIndex; }
    private void SyncSelection() => SyncSel(GridLaunch, _launch);

    // App 在「仅一次」触发完成后自动取消勾选提醒时调用：把模型层的 Enabled 变化刷回列表复选框。
    // 只发通知不触发存盘（Refresh 不走 Enabled setter）。
    public void RefreshReminderRows()
    {
        if (_reminders == null) return;
        foreach (var row in _reminders.Rows) row.Refresh();
    }

    // 快捷面板上增删改动作后调用（见 App.EditPanelItem）：面板改的是同一批模型对象，
    // 而这一页的行是与之平行的另一份 VM——不刷的话，主窗口开着时步骤数、摘要会停在改动之前，
    // 看着像面板那次编辑没生效。与上面那条同一形状：只发通知，不触发存盘（存盘在调用方）。
    // 面板管理器：同一批动作组的另一种视图（页与格），编辑仍走本页那两个编辑器。
    // 关闭后统一刷一次列表——管理器里改的是同一批模型对象，而这一页的行是与之平行的另一份 VM。
    // 手势管理器：改的是 RootConfig.Gestures（一份独立的步骤清单），不碰动作组本身——
    // 所以这里**不需要**刷本页的行。仍然刷一次是因为手势可以引用动作组（group 步骤），
    // 而管理器里能顺手编辑被引用的那个组；不刷的话主窗口开着时那一行会停在改动之前。
    // 它自己即时存盘（画完那一刻语义就完整了），故这里不再调 _save。
    private void GestureManager_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        new Views.GestureManagerWindow(_config, () => _save?.Invoke()) { Owner = this }.ShowDialog();
        RefreshGroupRows();
        // 手势总开关在管理器里也能改（标题行那个勾），而设置页这个勾只在本窗口
        // **构造时**填过一次——主窗口关闭只是隐藏，不会重建。不在这里读回来的话，
        // 下一次随便动设置页任何一项（SaveConfig 会把整页回写）就把旧值盖了回去，
        // 双向静默回滚：在管理器里关掉的又被重新装上钩子，反之亦然。
        // 抬 IsChecked 会触发 Settings_Changed，所以要抬在 _loadingSettings 里（否则又存一次）。
        _loadingSettings = true;
        try { GesturesEnabledChk.IsChecked = _config.Settings.GesturesEnabled; }
        finally { _loadingSettings = false; }
    }

    private void PanelManager_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        new Views.PanelManagerWindow(_config, () => _save?.Invoke()) { Owner = this }.ShowDialog();
        // 管理器现在只动 _config.PanelPages，不碰动作列表（页独立成实体之后，「添加面板页」
        // 不再是「新建一个动作」）。仍走 Resync 而不是 RefreshGroupRows：它能改格子里的步骤，
        // 而那些步骤可能指向某个动作，整份拉回来最省心，也不必去分辨这次到底改没改到。
        _groups?.Resync();
        SyncSel(GridGroup, _groups);
        RefreshGroupRows();
    }

    /// <summary>动作组列表被别处改动过（重排 / 新增）之后，把 VM 拉回与配置一致。</summary>
    //
    // Models 就是 _config.ActionGroups 本身，而 Rows 与它平行、删除按下标同删。
    // 外面动过那个列表却不 Resync 的话，两者错位，在这一页按删除删掉的会是另一个组。
    public void ResyncGroups()
    {
        _groups?.Resync();
        SyncSel(GridGroup, _groups);
        RefreshGroupRows();
    }

    public void RefreshGroupRows()
    {
        if (_groups == null) return;
        foreach (var row in _groups.Rows) row.Refresh();
    }

    // 托盘「快速提醒」的增 / 删入口。必须经 VM：Models 就是 _config.Reminders 本身，Rows 是平行的另一份，
    // 绕过 VM 直接改配置会让两者错位，之后每一行都指向相邻那条提醒。两个方法内部都会存盘。
    // SyncSel 一个都不能省——本文件里每一个动列表的调用点都紧跟着它，因为 VM 的 SelectedIndex 变了
    // （Add 设成落点、RemoveWhere 做钳位）而 DataGrid 那边不会自己跟上：删掉高亮行之前的一条时，
    // WPF 保住 SelectedItem 把 grid 索引前移一位却不发 SelectionChanged，两边就此错开，
    // 之后按「删除」删掉的是另一条提醒。
    public void AddReminderRow(Reminder r) { _reminders?.Add(r); SyncSel(GridRemind, _reminders); }
    public void RemoveReminderRow(Reminder r) { _reminders?.RemoveWhere(x => ReferenceEquals(x, r)); SyncSel(GridRemind, _reminders); }

    private void LAdd_Click(object sender, RoutedEventArgs e)
    {
        // 新增 ▾：按意图分节的类型菜单（见 StepMenu）→ 打开对应编辑器 → 插入。
        // 「从开始菜单选择…」排「打开」节最前：它是零配置的那条路（勾几下就完事），
        // 而其余每一项都要先开编辑器再手填目标。最常见的需求应该排在最省事的入口上。
        var fromMenu = new MenuItem { Header = Strings.Get("Menu_FromStartMenu") };
        fromMenu.Click += (_, _) => AddFromStartMenu();
        // 开机清单不显示「常用」：那一节里的每一条问的都是「你眼下正看着什么」——
        // 复制哪段文字、最小化哪个窗口。开机那一刻没人在场，答案不存在。
        var menu = Views.StepMenu.Build((k, seed) =>
        {
            var step = Views.StepEditorWindow.Edit(this, seed, k, _config?.ActionGroups ?? new List<ActionGroup>());
            if (step != null) { _launch?.Add(step); SyncSelection(); }
        }, firstOpenItem: fromMenu, presets: false);
        menu.PlacementTarget = LAdd;
        menu.IsOpen = true;
    }

    // 从开始菜单批量加：选中的每一项都建一条「启动程序」步骤，目标就是那个 .lnk。
    // 新加的步骤默认不勾选——与首启样例同一条立场：工具不该在用户还没看过一眼时就替他动电脑。
    // 逐条 Add 而不是一次性塞：Add 内部管选中位置与落盘，重写一遍批量版本只会多出一份要维护的插入逻辑。
    private void AddFromStartMenu()
    {
        if (_launch == null) return;
        if (Views.Pickers.PickStartMenuApps(this) is not List<(string Name, string Path)> picked) return;
        foreach (var (name, path) in picked)
            _launch.Add(new LaunchStep { Kind = "app", Label = name, Target = path, Enabled = false });
        SyncSelection();
    }

    private void LEdit_Click(object sender, RoutedEventArgs e)
    {
        var sel = _launch?.SelectedStep;
        if (sel == null) return;
        var edited = Views.StepEditorWindow.Edit(this, sel, sel.Kind, _config?.ActionGroups ?? new List<ActionGroup>());
        if (edited != null) { _launch?.ReplaceSelected(edited); SyncSelection(); }
    }

    private void GridLaunch_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LEdit_Click(sender, e);

    // 删除统一先确认：口径（文案/红轨）在 BrandDialog.ConfirmDelete 一处维护。
    private bool ConfirmDelete(string label) => Views.BrandDialog.ConfirmDelete(this, label);

    private void LDel_Click(object sender, RoutedEventArgs e)
    {
        var sel = _launch?.SelectedStep;
        if (sel == null || !ConfirmDelete(StepDisplay.StepListSummary(sel))) return;
        _launch?.DeleteSelected(); SyncSelection();
    }
    private void LUp_Click(object sender, RoutedEventArgs e) { _launch?.MoveUp(); SyncSelection(); }
    private void LDown_Click(object sender, RoutedEventArgs e) { _launch?.MoveDown(); SyncSelection(); }
    private void LCopy_Click(object sender, RoutedEventArgs e) { _launch?.DuplicateSelected(); SyncSelection(); }

    private static App? AppInstance => App.Instance;   // 转发到唯一出处（App.Instance），本类内仍用短名

    private void LRun_Click(object sender, RoutedEventArgs e)
    {
        var s = _launch?.SelectedStep;
        if (s == null) return;
        if (s.Kind == "group")
        {
            var g = ActionGroupResolver.Resolve(_config?.ActionGroups, s.GroupId);
            if (g != null) AppInstance?.RunGroupAsync(g, this);
        }
        else AppInstance?.RunStepAsync(s, this);
    }

    private void GRun_Click(object sender, RoutedEventArgs e)
    {
        var g = _groups?.SelectedGroup;
        // 传 this：用户此刻正对着主窗点「运行」，这一趟里的消息/确认框该认主窗为父（居中其上、关后焦点回它）。
        // 托盘/热键/提醒那些入口照旧不传——那时用户不在主窗前。
        if (g != null) AppInstance?.RunGroupAsync(g, this);
    }

    private IReadOnlyList<ActionGroup> Groups => _config?.ActionGroups ?? new List<ActionGroup>();

    private void RAdd_Click(object sender, RoutedEventArgs e)
    {
        var r = Views.ReminderEditorWindow.Edit(this, null, Groups);
        if (r != null) { _reminders?.Add(r); SyncSel(GridRemind, _reminders); }
    }
    private void REdit_Click(object sender, RoutedEventArgs e)
    {
        var sel = _reminders?.SelectedReminder;
        if (sel == null) return;
        var edited = Views.ReminderEditorWindow.Edit(this, sel, Groups);
        if (edited != null) { _reminders?.ReplaceSelected(edited); SyncSel(GridRemind, _reminders); }
    }
    private void GridRemind_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => REdit_Click(sender, e);
    private void RPreview_Click(object sender, RoutedEventArgs e)
    {
        var sel = _reminders?.SelectedReminder;
        if (sel != null) AppInstance?.PreviewReminder(sel);
    }
    // 「今天不再」：不改配置、只改当天的运行态，所以不需要确认框也不必存配置——
    // 误点的代价是少响一天，且明天自动恢复；而取消勾选那条路才是会被忘掉的那个。
    private void RSkip_Click(object sender, RoutedEventArgs e)
    {
        var sel = _reminders?.SelectedReminder;
        if (sel != null) AppInstance?.SkipReminderToday(sel);
    }
    private void RDel_Click(object sender, RoutedEventArgs e)
    {
        var sel = _reminders?.SelectedReminder;
        // 点名用列表里显示的那句话，不用 Message 原文：**静默运行动作的提醒没有正文**，
        // 传原文会得到「确定删除「」吗？」——点名点了个寂寞（同 BlankRowTests 那条规矩）。
        if (sel == null || !ConfirmDelete(ReminderDisplay.TextSummary(sel, Groups))) return;
        _reminders?.DeleteSelected(); SyncSel(GridRemind, _reminders);
    }
    private void RUp_Click(object sender, RoutedEventArgs e) { _reminders?.MoveUp(); SyncSel(GridRemind, _reminders); }
    private void RDown_Click(object sender, RoutedEventArgs e) { _reminders?.MoveDown(); SyncSel(GridRemind, _reminders); }
    private void RCopy_Click(object sender, RoutedEventArgs e) { _reminders?.DuplicateSelected(); SyncSel(GridRemind, _reminders); }

    // 直接开一个空动作的编辑器，不先弹菜单——只有一种东西可建（同「定时任务」那一页）。
    private void GAdd_Click(object sender, RoutedEventArgs e) => AddGroupFrom(new ActionGroup { Name = "" });

    // 内置模板。每次现生成新 id，选中即开编辑器预填，按需改进程名再保存。
    // 它们此前和「空白动作」挤在同一个菜单里，于是每建一个动作都要先从 9 项里挑第 1 项。
    private void GTemplate_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var t in ActionGroupTemplates.All())
        {
            var tt = t;
            var mi = new MenuItem { Header = tt.Name };
            mi.Click += (_, _) => AddGroupFrom(tt);
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = GTemplate;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // 本程序自己占着的全局键，交给动作编辑器查重用。**加了新的功能键就往这儿添一行**——
    // 漏了不会报错，只会在某天变成一条按了没反应的热键（面板键加进来的那一轮就漏过一次）。
    private IReadOnlyList<(string Key, string Owner)> FunctionHotkeys()
        => _config == null
            ? System.Array.Empty<(string, string)>()
            : new[]
            {
                (_config.Settings.StopHotkey, Strings.Get("Settings_StopHotkey")),
                (_config.Settings.PanelHotkey, Strings.Get("Settings_PanelHotkey")),
                (_config.Settings.QuickOpenHotkey, Strings.Get("Settings_QuickOpenHotkey")),
            };

    private void AddGroupFrom(ActionGroup template)
    {
        var g = Views.GroupEditorWindow.Edit(this, template, Groups, FunctionHotkeys());
        if (g != null) { _groups?.Add(g); SyncSel(GridGroup, _groups); }
    }
    private void GEdit_Click(object sender, RoutedEventArgs e)
    {
        var sel = _groups?.SelectedGroup;
        if (sel == null) return;
        var edited = Views.GroupEditorWindow.Edit(this, sel, Groups, FunctionHotkeys());
        if (edited != null) { _groups?.ReplaceSelected(edited); SyncSel(GridGroup, _groups); }
    }
    private void GridGroup_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => GEdit_Click(sender, e);
    private void GUp_Click(object sender, RoutedEventArgs e) { _groups?.MoveUp(); SyncSel(GridGroup, _groups); }
    private void GDown_Click(object sender, RoutedEventArgs e) { _groups?.MoveDown(); SyncSel(GridGroup, _groups); }
    private void GCopy_Click(object sender, RoutedEventArgs e) { _groups?.DuplicateSelected(); SyncSel(GridGroup, _groups); }
    // 删除动作组：先扫引用（提醒的静默组/点是后、启动清单与其他组里的「动作组」步骤），有引用则确认并联动清理，
    // 防止悬空引用静默失效（旧版 $gDelGuard 的移植，并补上组内嵌套引用）。
    private void GDel_Click(object sender, RoutedEventArgs e)
    {
        var g = _groups?.SelectedGroup;
        if (g == null || _config == null) return;
        var refReminders = _config.Reminders.Where(r =>
            r.SilentGroupId == g.Id || (r.OnYes?.Type == "group" && r.OnYes.Target == g.Id)).ToList();
        bool RefsGroup(LaunchStep s) => s.Kind == "group" && s.GroupId == g.Id;
        // **四份清单都要算**：单步清单、手势、其余组里的步骤、以及面板页上的格子。
        // 漏掉任何一份，删掉一个被它指着的组时确认框都只字不提，删完那个引用永远解析成 null——
        // 用户照旧点得到 / 画得出来，什么都不发生，而管理器里它看着完好。
        //
        // 面板页这一份是最后补上的，也是**最容易撞上的一份**：RootConfig.DefaultPanelPages
        // 给每个出厂动作都摆了一个 Kind="group" 的格子，所以任何用户从第一次启动就有这类引用。
        // （这一段注释原先写着「三份」，而 PanelPages 是这一版才从 ActionGroups 里分出来的第四份。）
        int refSteps = _config.LaunchSteps.Count(RefsGroup)
                     + _config.Gestures.Count(RefsGroup)
                     + _config.ActionGroups.Where(x => x.Id != g.Id).Sum(x => x.Steps.Count(RefsGroup))
                     + _config.PanelPages.Sum(p => (p.Steps ?? new()).Count(s => s != null && RefsGroup(s)));
        if (refReminders.Count > 0 || refSteps > 0)
        {
            // 有引用走专用确认文案（说明会联动清理），无引用走通用删除确认——两条路径都必确认。
            if (!Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"),
                    Lf("Confirm_DeleteGroupRefs", g.Name, refReminders.Count, refSteps), Views.ToastLevel.Warn)) return;
            foreach (var r in refReminders)
            {
                if (r.SilentGroupId == g.Id) r.SilentGroupId = "";
                if (r.OnYes?.Type == "group" && r.OnYes.Target == g.Id) r.OnYes = new OnYes();
            }
            _launch?.RemoveWhere(RefsGroup, save: false);   // 随后的 DeleteSelected 会整体落盘，不写两次
            // **删了行就得同步选中。** 这是本文件第 505 行那条规矩唯一漏掉的调用点：RemoveWhere 只对
            // VM 的 SelectedIndex 做钳位，而删掉高亮行**之前**的一条时钳位是空操作（Math.Min(1,2)=1），
            // WPF 那边保住 SelectedItem 把 grid 索引前移一位却不发 SelectionChanged——两边就此错开。
            // 之后在启动清单页按「删除」，删掉的是另一条，而且直接落盘：静默丢数据。
            SyncSelection();
            // 手势那一份也要清。计数早就算上它了，清理却漏着——于是确认框承诺「会联动清理」，
            // 而删完那条手势仍指着一个不存在的组：画得出来、什么都不发生，管理器里看着完好。
            // 承诺了不做，比一开始就不提更糟。
            if (_config.Gestures.Any(RefsGroup))
                _config.Gestures = _config.Gestures.Where(s => !RefsGroup(s)).ToList();
            // 面板页上的格子同理。**只数不清更糟**：确认框刚承诺过「这些引用会一并清除」，
            // 而留下来的格子带着被删动作的名字和图标，点它 ActionGroupResolver 返回 null，
            // 于是既没有气泡也没有日志——出厂默认页每个动作都有这样一个格子，人人都撞得到。
            foreach (var p in _config.PanelPages)
            {
                if (p.Steps == null) continue;
                if (p.Steps.Any(s => s != null && RefsGroup(s)))
                    p.Steps = p.Steps.Where(s => s == null || !RefsGroup(s)).ToList();
            }
            // 替换整个列表而非就地 RemoveAll：后台可能正拿着旧列表引用在枚举（跑组/拍快照），
            // 引用赋值是原子的——旧引用照常枚举完旧内容，不会抛「集合已修改」。
            foreach (var other in _config.ActionGroups.Where(x => x.Id != g.Id))
                if (other.Steps.Any(RefsGroup)) other.Steps = other.Steps.Where(s => !RefsGroup(s)).ToList();
            if (_reminders != null) foreach (var row in _reminders.Rows) row.Refresh();
        }
        else if (!ConfirmDelete(g.Name)) return;
        _groups?.DeleteSelected();
        if (_groups != null) foreach (var row in _groups.Rows) row.Refresh();   // 其他组的步骤摘要可能变了（嵌套引用被联动清掉）
        SyncSel(GridGroup, _groups);
    }

    // —— 系统启动项页 ——
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.TabControl) return;
        // 按名比较，不再用魔数序号（插/删 tab 不失效）
        if (Tabs.SelectedItem == TabSystem && !_systemLoaded) LoadSystemAsync();
        else if (Tabs.SelectedItem == TabPorts) LoadPorts();
        SyncPortsTimer();
    }

    private void SRefresh_Click(object sender, RoutedEventArgs e) => LoadSystemAsync();
    private void SSearch_TextChanged(object sender, TextChangedEventArgs e) { if (_system != null) _system.Search = SSearch.Text; }
    private void ShowReadOnly_Changed(object sender, RoutedEventArgs e) { if (_system != null) _system.ShowReadOnly = ShowReadOnly.IsChecked == true; }

    private void LoadSystemAsync()
    {
        if (_system == null) return;
        _systemLoaded = true;
        SysLoading.Visibility = Visibility.Visible;
        GridSystem.Visibility = Visibility.Collapsed;
        Task.Run(() => SystemStartupReader.GetItems()).ContinueWith(t =>
        {
            _system.SetItems(t.IsCompletedSuccessfully ? t.Result : new List<SystemStartupItem>());
            SysLoading.Visibility = Visibility.Collapsed;
            GridSystem.Visibility = Visibility.Visible;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ReportSystemMsg(string msg)
        => Views.BrandDialog.Warn(this, "Clockwork", msg);

    // 系统项开关/接管遇 NeedsAdmin：询问「以管理员身份重开？」，一键提权（旧版 Show-NeedsAdminPrompt 的移植）。
    private void PromptRelaunchAdmin()
    {
        if (Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"), Strings.Get("Confirm_RelaunchAdmin")))
            AppInstance?.RelaunchElevated();
    }

    // 菜单弹出前按选中行刷新可用态：只读项（策略/系统/一次性等）禁用「接管/删除」——
    // 此前点了静默无反应，看起来像功能坏了。行内代码守卫仍保留作兜底。
    private void GridSystem_MenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 与另外三张表同一条规则：右键没落在某一行上就不弹菜单（键盘 Menu 键除外，
        // 它按当前选中行走，光标坐标为 -1 是 WPF 给出的区分方式）。
        // 不拦的话，选中一行后右键列表下方空白，菜单照弹并作用在那一行——「接管/删除」
        // 这两个操作作用错行的代价，比排序那几个大得多。
        bool byKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        if (!byKeyboard && !_sysRightClickOnRow) { e.Handled = true; return; }
        bool can = GridSystem.SelectedItem is SystemStartupRowVm row && row.CanEdit;
        SysMenuTakeover.IsEnabled = can;
        SysMenuDelete.IsEnabled = can;
    }

    // 右键先选中光标下的行，使随后的上下文菜单作用于该行。
    // 命中点→行统一走 DataGridReorder.RowFromHit：这里原先手写的那圈遍历漏了「非 Visual 的
    // DependencyObject 要走逻辑树」那道分流，直接喂给 VisualTreeHelper.GetParent 会抛。
    private bool _sysRightClickOnRow;   // 上一次右键是否落在某一行上（见 GridSystem_MenuOpening）

    private void GridSystem_RightClick(object sender, MouseButtonEventArgs e)
    {
        var row = Views.DataGridReorder.RowFromHit(e.OriginalSource as DependencyObject);
        _sysRightClickOnRow = row != null;
        if (row != null) row.IsSelected = true;
    }

    // 「接管到启动清单」：禁用原系统自启项 + 去重导入为托管 app 步骤（延迟 2s 体现接管价值）。
    // 禁用失败（只读/需管理员）会由复选框逻辑自行提示并回读，此时不导入以免自启重复。
    private void SysTakeOver_Click(object sender, RoutedEventArgs e)
    {
        if (GridSystem.SelectedItem is not SystemStartupRowVm row || _launch == null) return;
        // 只读项(策略/系统/一次性等)不可停用：SetItemEnabled 对这类项会写入无效值却仍返回 "Ok"，
        // 光靠"禁用是否成功"兜不住 → 直接前置守卫，避免"假接管"造成双份自启 + 无效注册表写入。
        // (只读项默认隐藏，仅"显示只读项"时可见；对其接管无意义，静默忽略。)
        if (!row.CanEdit) return;
        // 恢复旧版类型守卫：仅注册表 Run 键/启动文件夹可接管。计划任务的 COM 动作路径过 ParseCommandLine
        // 会丢参数/截断带空格路径（如 C:\Program Files\...），导致原任务被禁、导入的步骤又启动失败，两头落空。
        if (row.Item.Type == "ScheduledTask") { ReportSystemMsg(Strings.Get("Warn_TakeoverUnsupported")); return; }
        if (row.Enabled) row.Enabled = false;   // 禁用原项
        if (row.Enabled) return;                 // 没禁用成功(需管理员) → 放弃，避免与托管步骤双份自启
        int idx = _launch.AddIfNew(SystemStartupReader.ToImportedStep(row.Item));   // 返回新增或既有步骤的索引
        Tabs.SelectedItem = TabLaunch;           // 切到启动清单，让接管结果直接可见（按名，不用魔数序号）
        _launch.SelectedIndex = idx;
        GridLaunch.SelectedIndex = idx;
    }

    // 「从系统中删除」：彻底移除注册表值/启动文件夹快捷方式/计划任务（区别于取消勾选=仅禁用）。
    // 专用确认文案强调不可撤销；NeedsAdmin 复用「以管理员身份重开？」一键提权。
    private void SysDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GridSystem.SelectedItem is not SystemStartupRowVm row || _system == null) return;
        if (!row.CanEdit) return;   // 只读项(策略/系统/一次性等)不可删，与开关/接管同守卫
        // 启动文件夹项被接管后，导入的 app 步骤 Target 直指这个 .lnk（见 ToImportedStep）；
        // 删除会连文件一起移除、该步骤随之失效 → 换专用文案把后果讲清，决定权交还用户。
        bool takenOver = row.Item.Type == "StartupFolder" && StepRefersToFile(row.Item.LnkPath);
        if (!Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"),
                Lf(takenOver ? "Confirm_DeleteSysItemTakenOver" : "Confirm_DeleteSysItem", row.Name), Views.ToastLevel.Warn)) return;
        var res = SystemStartupReader.DeleteItem(row.Item);
        if (res == "Ok") _system.Remove(row.Item);
        else if (res == "NeedsAdmin") PromptRelaunchAdmin();
        else ReportSystemMsg(Lf("SysMsg_DeleteFail", row.Name, res));
    }

    // 启动清单或动作组里是否有 app 步骤直指该文件（接管启动文件夹项时 Target=.lnk 路径）。
    private bool StepRefersToFile(string path)
    {
        if (_config == null || string.IsNullOrEmpty(path)) return false;
        bool Hit(LaunchStep s) => s.Kind == "app" && string.Equals(s.Target, path, StringComparison.OrdinalIgnoreCase);
        return _config.LaunchSteps.Any(Hit) || _config.ActionGroups.Any(g => g.Steps.Any(Hit));
    }
    // —— 端口页 ——
    // 每次切进来都重扫，不像系统启动项页那样只扫一次：端口是分钟级变化的（终端里刚 Ctrl+C
    // 掉的服务留在列表里，点「用浏览器打开」就是一个打不开的页面），而 GetExtendedTcpTable 是毫秒级，
    // 不值得为它套一层异步 + loading 态。刷新按钮仍留着：人就停在这一页上起服务时用得着。
    // 仅当端口页处于前台且窗口可见时让 timer 跑。
    private void SyncPortsTimer()
    {
        if (_portsTimer == null) return;
        bool want = IsVisible && Tabs.SelectedItem == TabPorts;
        if (want && !_portsTimer.IsEnabled) _portsTimer.Start();
        else if (!want && _portsTimer.IsEnabled) _portsTimer.Stop();
    }

    // 扫端口整轮**实测** 3ms（本机），但**不能因此就同步跑在 UI 线程上**。
    //
    // 那 3ms 是本机 + 本地盘的数字，而这条路里有三样会随环境变慢的东西：
    //   · `Process.GetProcesses()`：本机 200 来个进程，逐个开句柄；
    //   · 逐 PID 读 PEB 拿命令行 / 工作目录；
    //   · `LooksLikeProject` 沿工作目录**往上 6 层 × 10 个标记**做文件系统探测
    //     （`Directory.Exists` / `File.Exists`）——工作目录落在网络盘、UNC 路径、
    //     或 OneDrive / Dropbox / 备份客户端挂钩的目录上时，**一次探测就能等满超时**。
    //     本仓库自己就住在 `D:\Backup\...\Documents\...` 这种典型会被同步客户端挂钩的位置。
    //
    // 而这条路上还挂着一个 **5 秒一拍**的定时器（见 _portsTimer）。UI 线程被卡过 5 秒
    // = DWM 在全屏笔迹窗上盖幽灵窗 = 整个桌面点不动（见 Core.UiStallWatch）。
    // 一次几毫秒的收益，不值得拿这个去赌；扫描放后台，结果回 UI 线程。
    //
    // `SetItems` 必须在 UI 线程上跑（它改的是 ObservableCollection，见 PortsVm）。
    private void LoadPorts()
    {
        if (_ports == null) return;
        // 单飞：5 秒一拍的定时器、用户点刷新、杀进程后重扫三条路会叠在一起，
        // 而一次扫描慢的时候叠起来只会更慢（每个都再跑一遍 Process.GetProcesses + 文件探测）。
        if (System.Threading.Interlocked.Exchange(ref _portsScanning, 1) == 1) return;
        Task.Run(PortReader.GetEntries).ContinueWith(t =>
        {
            List<PortEntry> items;
            try { items = t.Result; } catch { items = new List<PortEntry>(); }
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _portsScanning, 0);
                    _ports?.SetItems(items);
                });
            }
            // 调度器正在关闭（窗口都要没了）：把闸放开，别让它永久卡住这一页的刷新。
            catch { System.Threading.Interlocked.Exchange(ref _portsScanning, 0); }
        });
    }

    private void PRefresh_Click(object sender, RoutedEventArgs e) => LoadPorts();
    private void PSearch_TextChanged(object sender, TextChangedEventArgs e) { if (_ports != null) _ports.Search = PSearch.Text; }
    // 两个复选框共用一个处理器：它们描述的是同一个三档视图，分开写两份就得在两边各维护一遍优先级。
    // 「显示全部」勾上时把「只看 dev」置灰：两者同时勾上是自相矛盾的说法，
    // 置灰把「后者不生效」直接摄在界面上，而不是让人对着两个对勾猜为什么没反应。
    private void PortsView_Changed(object sender, RoutedEventArgs e)
    {
        if (_ports == null) return;
        bool all = ShowAllPorts.IsChecked == true;
        bool dev = DevOnlyPorts.IsChecked == true;
        DevOnlyPorts.IsEnabled = !all;
        _ports.ShowAll = all;
        _ports.DevOnly = dev;
        // 只持久化 DevOnly。「显示全部」是一次性的「让我看看全貌」，下次开程序还默认勾着反而意外。
        if (_config != null && _config.Settings.PortsDevOnly != dev)
        {
            _config.Settings.PortsDevOnly = dev;
            _save?.Invoke();
        }
    }

    private void GridPorts_DoubleClick(object sender, MouseButtonEventArgs e) => PortOpen_Click(sender, e);

    private void PortOpen_Click(object sender, RoutedEventArgs e)
    {
        if (GridPorts.SelectedItem is PortRowVm row) OpenUrl(row.Url);
    }

    // 与另外四张表同一条规则：右键没落在某一行上就不弹菜单（键盘 Menu 键除外，
    // 它按当前选中行走，光标坐标为 -1 是 WPF 给出的区分方式）。
    // 「释放端口」作用错行的代价比排序那几个大得多，不能靠「上次选中的行」蒙。
    private bool _portRightClickOnRow;

    private void GridPorts_RightClick(object sender, MouseButtonEventArgs e)
    {
        var row = Views.DataGridReorder.RowFromHit(e.OriginalSource as DependencyObject);
        _portRightClickOnRow = row != null;
        if (row != null) row.IsSelected = true;
    }

    private void GridPorts_MenuOpening(object sender, ContextMenuEventArgs e)
    {
        bool byKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        if (!byKeyboard && !_portRightClickOnRow) { e.Handled = true; return; }
        // PID 0/4 是内核占位，杀必失败 → 灰掉而不是让人点了没反应（同系统启动项页的 CanEdit 门控）。
        PortMenuKill.IsEnabled = GridPorts.SelectedItem is PortRowVm row && row.CanKill;
    }

    // 释放端口：破坏性且不可撤销（未保存的东西没了），故与「从系统中删除自启项」同级：
    // 走带警示色的确认框。一个端口可能被好几个进程同时占着，所以文案逐个点名，
    // 不能只说「释放端口」——这一下可能带走不止一个进程。
    private void PortKill_Click(object sender, RoutedEventArgs e)
    {
        if (GridPorts.SelectedItem is not PortRowVm row || !row.CanKill) return;
        // 释放端口杀的是进程，同进程的其余端口会一并没。不把它们列出来，
        // 就是把破坏半径藏起来：你以为在释放 3000，实际是停掉整个 dev server 加另外 7 个端口。
        var msg = Lf("Confirm_KillPort", row.PortText, row.OwnersText);
        if (row.SiblingPorts.Count > 0) msg += " " + Lf("Confirm_KillPortAlso", row.SiblingPortsText);
        if (!Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"), msg, Views.ToastLevel.Warn)) return;
        // **杀进程这一步也不能在 UI 线程上等。** FreePort 对**每个**占用者调 Kill，
        // 而 Kill 里是 `p.WaitForExit(2000)`——一个端口被三个进程占着，最坏就是 6 秒，
        // 正好越过幽灵化的 5 秒阈值（见 Core.UiStallWatch）。放后台，结果回 UI 线程再报。
        // item / portText 先取出来：回调回来时 row 可能已经被 LoadPorts 换掉了。
        var item = row.Item;
        var portText = row.PortText;
        Task.Run(() => PortReader.FreePort(item)).ContinueWith(t =>
        {
            string err;
            try { err = t.Result; } catch (Exception ex) { err = ex.GetBaseException().Message; }
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (err != "") Views.BrandDialog.Warn(this, "Clockwork", Lf("Ports_KillFail", portText, err));
                    LoadPorts();   // 成败都重扫：成功要让那行消失，失败要让人看见它还在
                });
            }
            catch { }   // 调度器正在关闭：窗口都要没了，没人要这份结果
        });
    }
}

