using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Native;
using Clockwork.ViewModels;

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
    }

    public MainWindow(RootConfig config, Action save, Action<string, string>? migrateReminderState = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.DarkTitleBar.Apply(this);
        Title = "Clockwork · " + Strings.Get("App_Subtitle");   // 副标题并入系统标题栏，去掉内容区重复的首栏
        _config = config;
        _save = save;

        _launch = new LaunchListVm(config, save);
        GridLaunch.ItemsSource = _launch.Rows;
        GridLaunch.SelectionChanged += (s, e) => _launch.SelectedIndex = GridLaunch.SelectedIndex;

        _reminders = new ReminderListVm(config, save, migrateReminderState);
        GridRemind.ItemsSource = _reminders.Rows;
        GridRemind.SelectionChanged += (s, e) => _reminders.SelectedIndex = GridRemind.SelectedIndex;

        _groups = new GroupListVm(config, save);
        GridGroup.ItemsSource = _groups.Rows;
        GridGroup.SelectionChanged += (s, e) => _groups.SelectedIndex = GridGroup.SelectedIndex;

        _system = new SystemStartupVm(SystemStartupReader.SetItemEnabled, ReportSystemMsg, PromptRelaunchAdmin);
        GridSystem.ItemsSource = _system.Rows;
        Tabs.SelectionChanged += Tabs_SelectionChanged;   // 系统启动项页首次选中时才扫描（枚举较慢）

        // 设置页
        VersionText.Text = "v" + AppVersion();
        StartupDelayBox.Text = config.Settings.StartupDelaySeconds.ToString();
        StartMinChk.IsChecked = config.Settings.StartMinimized;
        HotkeyBox.Text = config.Settings.StopHotkey;
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
        _save?.Invoke();
    }

    // —— 急停键：按键捕捉 ——（点击后直接按下快捷键即录入，只接受可注册的组合）
    private string _hotkeyBefore = "";

    private void Hotkey_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hotkeyBefore = _config?.Settings.StopHotkey ?? "";
        HotkeyBox.Text = Strings.Get("Hotkey_PressPrompt");   // 进入捕捉态：提示「按下快捷键…」
        AppInstance?.SuspendHotkeys();                        // 捕捉期间注销全部全局热键，避免按到已注册组合触发急停/跑组
    }

    private void Hotkey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 未捕捉就离开：恢复显示。无论如何都按当前配置重新注册全部全局热键（改键即时生效、取消则复原）。
        if (HotkeyBox.Text == Strings.Get("Hotkey_PressPrompt")) HotkeyBox.Text = _hotkeyBefore;
        AppInstance?.ResumeHotkeys();
    }

    private void Hotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;   // 捕捉一切按键，不让文本框/焦点处理（PassThrough 分支除外）
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (HotkeyCapture.ProcessCaptureKey(key, Keyboard.Modifiers, out var combo))
        {
            case HotkeyCapture.CaptureAction.PassThrough:            // 裸 Tab/Enter：放行给焦点导航/默认按钮，键盘用户不被困在框里
                e.Handled = false; return;
            case HotkeyCapture.CaptureAction.Cancel:                 // Esc = 取消：恢复原值、不改配置（LostFocus 会复原注册）
                HotkeyBox.Text = _hotkeyBefore; Keyboard.ClearFocus(); return;
            case HotkeyCapture.CaptureAction.Clear:                  // Delete/Backspace = 清空停用急停键
                SaveHotkey(""); HotkeyBox.Text = ""; Keyboard.ClearFocus(); return;
            case HotkeyCapture.CaptureAction.Captured:
                SaveHotkey(combo!); HotkeyBox.Text = combo; Keyboard.ClearFocus(); return;   // → LostFocus 按新配置重注册
            default: return;                                         // Ignore：只按修饰键/组不出可注册组合，继续等
        }
    }

    private void SaveHotkey(string combo)
    {
        if (_config == null) return;
        _config.Settings.StopHotkey = combo;
        _hotkeyBefore = combo;
        _save?.Invoke();
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
        Task.Run(() => Autostart.IsRegistered()).ContinueWith(t =>
        {
            bool reg = t.IsCompletedSuccessfully && t.Result;
            AutostartBtn.Content = Strings.Get(reg ? "Autostart_On" : "Autostart_Off");
            AutostartBtn.Tag = reg;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool currentlyReg = AutostartBtn.Tag as bool? ?? false;
        var exe = Environment.ProcessPath ?? "";
        AutostartBtn.IsEnabled = false;
        Task.Run(() => currentlyReg ? Autostart.Unregister() : Autostart.Register(exe)).ContinueWith(t =>
        {
            var res = t.IsCompletedSuccessfully ? t.Result : "Error";
            if (res == "NeedsAdmin")   // 无管理员权限：直接以管理员身份重开自己完成（注销），不再只弹提示。
            {
                ElevateAutostart(exe, register: !currentlyReg);
                return;   // 标签由 ElevateAutostart 在子进程结束后刷新
            }
            AutostartBtn.IsEnabled = true;
            if (res != "Ok") Views.BrandDialog.Warn(this, "Clockwork", Lf("Autostart_Fail", res));
            UpdateAutostartLabel();
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
            AutostartBtn.IsEnabled = true;
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

    // 变更(增/改/删/移)后把 VM 的选中回推到对应 DataGrid。三个列表页统一走它。
    private static void SyncSel(System.Windows.Controls.DataGrid grid, ListVmBase? vm) { if (vm != null) grid.SelectedIndex = vm.SelectedIndex; }
    private void SyncSelection() => SyncSel(GridLaunch, _launch);

    private void LAdd_Click(object sender, RoutedEventArgs e)
    {
        // 新增 ▾：弹类型菜单 → 打开对应编辑器 → 插入。
        var menu = new ContextMenu();
        foreach (var kind in StepDisplay.StepKinds)
        {
            var k = kind;
            var mi = new MenuItem { Header = StepDisplay.StepKindLabel(k) };
            mi.Click += (s, _) =>
            {
                var step = Views.StepEditorWindow.Edit(this, null, k, _config?.ActionGroups ?? new List<ActionGroup>());
                if (step != null) { _launch?.Add(step); SyncSelection(); }
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = LAdd;
        menu.IsOpen = true;
    }

    private void LEdit_Click(object sender, RoutedEventArgs e)
    {
        var sel = _launch?.SelectedStep;
        if (sel == null) return;
        var edited = Views.StepEditorWindow.Edit(this, sel, sel.Kind, _config?.ActionGroups ?? new List<ActionGroup>());
        if (edited != null) { _launch?.ReplaceSelected(edited); SyncSelection(); }
    }

    private void GridLaunch_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LEdit_Click(sender, e);

    private void LDel_Click(object sender, RoutedEventArgs e) { _launch?.DeleteSelected(); SyncSelection(); }
    private void LUp_Click(object sender, RoutedEventArgs e) { _launch?.MoveUp(); SyncSelection(); }
    private void LDown_Click(object sender, RoutedEventArgs e) { _launch?.MoveDown(); SyncSelection(); }

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
    private void RDel_Click(object sender, RoutedEventArgs e) { _reminders?.DeleteSelected(); SyncSel(GridRemind, _reminders); }

    private void GAdd_Click(object sender, RoutedEventArgs e)
    {
        // 新增 ▾：空白组 + 内置模板（专注/会议/收工/睡前/离开/截图，旧版 Get-ActionGroupTemplates 的移植）。
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
            if (!Views.BrandDialog.Confirm(this, Strings.Get("Confirm_Title"),
                    Lf("Confirm_DeleteGroupRefs", g.Name, refReminders.Count, refSteps))) return;
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
        _groups?.DeleteSelected();
        if (_groups != null) foreach (var row in _groups.Rows) row.Refresh();   // 其他组的步骤数可能变了
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
}

