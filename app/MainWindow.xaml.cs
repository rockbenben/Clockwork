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

    // 设计器/兜底无参构造。
    public MainWindow()
    {
        InitializeComponent();
        RefreshStopButton();   // 无配置也先摆正：默认隐藏，别让设计器/兜底路径漏出一颗常驻按钮
    }

    public MainWindow(RootConfig config, Action save, Action<string, Reminder>? migrateReminderState = null)
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

        _reminders = new ReminderListVm(config, save, migrateReminderState);
        GridRemind.ItemsSource = _reminders.Rows;
        GridRemind.SelectionChanged += (s, e) => { _reminders.SelectedIndex = GridRemind.SelectedIndex; ReminderRowOps.IsEnabled = GridRemind.SelectedIndex >= 0; };
        Views.DataGridReorder.Attach(GridRemind, (from, to) => { _reminders.MoveTo(from, to); SyncSel(GridRemind, _reminders); });

        _groups = new GroupListVm(config, save);
        GridGroup.ItemsSource = _groups.Rows;
        GridGroup.SelectionChanged += (s, e) => { _groups.SelectedIndex = GridGroup.SelectedIndex; GroupRowOps.IsEnabled = GridGroup.SelectedIndex >= 0; };
        Views.DataGridReorder.Attach(GridGroup, (from, to) => { _groups.MoveTo(from, to); SyncSel(GridGroup, _groups); });

        _system = new SystemStartupVm(SystemStartupReader.SetItemEnabled, ReportSystemMsg, PromptRelaunchAdmin);
        GridSystem.ItemsSource = _system.Rows;
        Tabs.SelectionChanged += Tabs_SelectionChanged;   // 系统启动项页首次选中时才扫描（枚举较慢）

        // 设置页
        VersionText.Text = "v" + AppVersion();
        StartupDelayBox.Text = config.Settings.StartupDelaySeconds.ToString();
        StartMinChk.IsChecked = config.Settings.StartMinimized;
        WaitReadyChk.IsChecked = config.Settings.StartupWaitForReady;
        WireHotkeyBox();   // 急停键「点击即录键」（Attach 内会填入当前值）
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
        UpdateAutostartLabel();
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

    // —— 底部设置栏 ——
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        // 非法/越界输入不静默丢弃：合法则 clamp 到 [0,600] 存下，非法则保持旧值；
        // 两种情况都把规范化后的值回写输入框，保证「看到的 = 存下的」。
        if (int.TryParse(StartupDelayBox.Text.Trim(), out var d) && d >= 0)
            _config.Settings.StartupDelaySeconds = StepHelpers.ClampStartupDelay(d);
        StartupDelayBox.Text = _config.Settings.StartupDelaySeconds.ToString();
        _config.Settings.StartMinimized = StartMinChk.IsChecked == true;
        _config.Settings.StartupWaitForReady = WaitReadyChk.IsChecked == true;
        _save?.Invoke();
    }

    // 急停键「点击即录键」——与组热键/发送键统一走 KeyCaptureBox（见 WireHotkeyBox，在构造末尾调用）。
    private void WireHotkeyBox()
    {
        if (_config == null) return;
        Views.KeyCaptureBox.Attach(HotkeyBox, HotkeyCapture.KeyCaptureMode.Hotkey, null,
            () => _config.Settings.StopHotkey,
            // 保存→SaveConfig→按新配置重注册全部热键；急停按钮的提示里印着这个键，一并刷新
            combo => { _config.Settings.StopHotkey = combo; _save?.Invoke(); RefreshStopButton(); });
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

    // 鼠标点完不把焦点环留在急停按钮上：环是黄铜色，而黄铜在本应用里读作「活动 / 正在跑」
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
                var p = Process.Start(psi);
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
            if (test == null) throw new InvalidOperationException("JSON 解析结果为 null");
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
        Views.BrandDialog.Info(this, "Clockwork", Strings.Get("Config_Imported"));
        app.RelaunchForLanguage();   // 复用重启逻辑：重开自身 + 退出当前实例（内部保证无论成败都退出）
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

    private void LAdd_Click(object sender, RoutedEventArgs e)
    {
        // 新增 ▾：按意图分节的类型菜单（见 StepMenu）→ 打开对应编辑器 → 插入。
        // 「从开始菜单选择…」排「打开」节最前：它是零配置的那条路（勾几下就完事），
        // 而其余每一项都要先开编辑器再手填目标。最常见的需求应该排在最省事的入口上。
        var fromMenu = new MenuItem { Header = Strings.Get("Menu_FromStartMenu") };
        fromMenu.Click += (_, _) => AddFromStartMenu();
        var menu = Views.StepMenu.Build(k =>
        {
            var step = Views.StepEditorWindow.Edit(this, null, k, _config?.ActionGroups ?? new List<ActionGroup>());
            if (step != null) { _launch?.Add(step); SyncSelection(); }
        }, firstOpenItem: fromMenu);
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
            if (g != null) AppInstance?.RunGroupAsync(g);
        }
        else AppInstance?.RunStepAsync(s);
    }

    private void GRun_Click(object sender, RoutedEventArgs e)
    {
        var g = _groups?.SelectedGroup;
        if (g != null) AppInstance?.RunGroupAsync(g);
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
    private void RDel_Click(object sender, RoutedEventArgs e)
    {
        var sel = _reminders?.SelectedReminder;
        if (sel == null || !ConfirmDelete(sel.Message)) return;
        _reminders?.DeleteSelected(); SyncSel(GridRemind, _reminders);
    }
    private void RUp_Click(object sender, RoutedEventArgs e) { _reminders?.MoveUp(); SyncSel(GridRemind, _reminders); }
    private void RDown_Click(object sender, RoutedEventArgs e) { _reminders?.MoveDown(); SyncSel(GridRemind, _reminders); }
    private void RCopy_Click(object sender, RoutedEventArgs e) { _reminders?.DuplicateSelected(); SyncSel(GridRemind, _reminders); }

    private void GAdd_Click(object sender, RoutedEventArgs e)
    {
        // 新增 ▾：空白组 + 内置模板（专注/会议/收工/睡前/离开/截图/久坐，旧版 Get-ActionGroupTemplates 的移植）。
        // 模板每次现生成新 id，选中即开编辑器预填，按需改进程名再保存。
        var menu = new ContextMenu();
        var blank = new MenuItem { Header = Strings.Get("Menu_BlankGroup") };
        blank.Click += (_, _) => AddGroupFrom(new ActionGroup { Name = "" });
        menu.Items.Add(blank);
        menu.Items.Add(new Separator());
        foreach (var t in ActionGroupTemplates.All())
        {
            var tt = t;
            var mi = new MenuItem { Header = tt.Name };
            mi.Click += (_, _) => AddGroupFrom(tt);
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = GAdd;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void AddGroupFrom(ActionGroup template)
    {
        var g = Views.GroupEditorWindow.Edit(this, template, Groups, _config?.Settings.StopHotkey ?? "");
        if (g != null) { _groups?.Add(g); SyncSel(GridGroup, _groups); }
    }
    private void GEdit_Click(object sender, RoutedEventArgs e)
    {
        var sel = _groups?.SelectedGroup;
        if (sel == null) return;
        var edited = Views.GroupEditorWindow.Edit(this, sel, Groups, _config?.Settings.StopHotkey ?? "");
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
        int refSteps = _config.LaunchSteps.Count(RefsGroup)
                     + _config.ActionGroups.Where(x => x.Id != g.Id).Sum(x => x.Steps.Count(RefsGroup));
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
        if (e.Source is System.Windows.Controls.TabControl && Tabs.SelectedItem == TabSystem && !_systemLoaded) LoadSystemAsync();   // 按名比较，不再用魔数序号（插/删 tab 不失效）
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
        bool can = GridSystem.SelectedItem is SystemStartupRowVm row && row.CanEdit;
        SysMenuTakeover.IsEnabled = can;
        SysMenuDelete.IsEnabled = can;
    }

    // 右键先选中光标下的行，使随后的上下文菜单作用于该行。
    private void GridSystem_RightClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as System.Windows.DependencyObject;
        while (dep != null && dep is not DataGridRow) dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow row) row.IsSelected = true;
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
}

