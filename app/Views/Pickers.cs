using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.I18n;
using Clockwork.Native;
// UseWindowsForms 的全局 using 会让这些控件类型与 WinForms 同名冲突，显式钉到 WPF。
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using SelectionMode = System.Windows.Controls.SelectionMode;
using TextBox = System.Windows.Controls.TextBox;

namespace Clockwork.Views;

// 编辑器辅助选择器（旧版 WpfDialogs 的移植）：文件/文件夹浏览、进程选择（带搜索）、日期选择、按键捕获。
// 小对话框全部代码构建；控件外观走 App 资源里的主题隐式样式。
public static class Pickers
{
    public static string? BrowseFile(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("Filter_Browse") };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    public static string? BrowseFolder(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        return dlg.ShowDialog(owner) == true ? dlg.FolderName : null;
    }

    // 进程选择：列出所有带主窗口的进程（进程名 — 窗口标题），顶部搜索框实时过滤，双击或「确定」选中。
    // 返回裸进程名（窗口动作/发送文本按它找窗口）；取消 → null。
    public static string? PickProcess(Window owner)
    {
        var procs = new List<(string Name, string Title)>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) procs.Add((p.ProcessName, p.MainWindowTitle)); }
            catch { }
            finally { p.Dispose(); }
        }
        procs = procs.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var dlg = NewDialog(owner, Strings.Get("Picker_Process"), 460, 420);
        var root = new DockPanel { Margin = new Thickness(14) };
        var search = new TextBox { Height = 30, ToolTip = Strings.Get("Picker_Search") };
        DockPanel.SetDock(search, Dock.Top);
        var buttons = OkCancelRow(dlg, out var ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var list = new ListBox { Margin = new Thickness(0, 8, 0, 8) };
        // 两个控件都没有可见标签（搜索框只有 placeholder 式的 tooltip），读屏软件读到的就是「编辑，空白」——
        // 补上朗读名，名字直接借用它们各自的用途文案。
        System.Windows.Automation.AutomationProperties.SetName(search, Strings.Get("Picker_Search"));
        System.Windows.Automation.AutomationProperties.SetName(list, Strings.Get("Picker_Process"));

        void Fill()
        {
            var q = search.Text.Trim();
            list.ItemsSource = procs
                .Where(x => q == "" || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(x => new ListBoxItem { Content = x.Title == "" ? x.Name : $"{x.Name} — {x.Title}", Tag = x.Name })
                .ToList();
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) dlg.DialogResult = true; };
        ok.Click += (_, _) => { if (list.SelectedItem != null) dlg.DialogResult = true; };

        root.Children.Add(search); root.Children.Add(buttons); root.Children.Add(list);
        dlg.Content = root;
        search.Focus();
        return dlg.ShowDialog() == true ? (list.SelectedItem as ListBoxItem)?.Tag as string : null;
    }

    // 递归扫开始菜单的枚举选项。这一份是承重件，别退回 SearchOption.AllDirectories 那个重载：
    // 那个重载用的是「兼容」选项——IgnoreInaccessible=false、什么属性都不跳——而两处开始菜单根目录下
    // 都躺着一个拒绝访问的旧版本地化联结（简中系统上叫「程序」，指向 Programs）。撞上它整根抛
    // UnauthorizedAccessException，下面那句 catch 再把整个根丢掉，结果就是明明有三百多个快捷方式、
    // 选择器却报「一个都没找到」。这个 bug 上线过一次，就是这么来的。
    //
    // IgnoreInaccessible=true：读不了的子目录跳过去接着走，而不是掀桌子。
    // AttributesToSkip 显式写全（一旦设了这个属性，默认的 Hidden|System 就不再自动生效）：
    //   Hidden|System   —— 与用户在开始菜单里实际看得见的东西保持一致；
    //   ReparsePoint    —— 那个联结只是 Programs 的另一个名字，跟进去等于把整棵树数两遍。
    private static readonly EnumerationOptions StartMenuScan = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    // 开始菜单里的程序（两处「程序」文件夹下的全部 .lnk，递归）。按显示名去重 + 排序。
    // 快捷方式本身就是启动目标（LaunchTarget 认 .lnk），不必解析它指向哪个 exe——
    // 解析反而更差：Store 应用、带参数的快捷方式解出来是一串没法直接跑的东西。
    public static List<(string Name, string Path)> StartMenuEntries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string Name, string Path)>();
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                 })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files;
            // 兜底保留，但它不再是主力：真正要紧的是上面那份 EnumerationOptions。
            try { files = Directory.EnumerateFiles(root, "*.lnk", StartMenuScan).ToList(); }
            catch { continue; }
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Length == 0 || !seen.Add(name)) continue;   // 公用与个人菜单常有同名项，留先见到的（公用那份）
                list.Add((name, f));
            }
        }
        // .lnk 扫完再补 AppsFolder，顺序不能反：经典程序两边都有，而快捷方式带着参数、工作目录和图标，
        // 比一串 AUMID 完整得多。反过来的话，几百个经典程序会被 AUMID 版本先占住名字。
        foreach (var app in AppsFolderEntries())
            if (seen.Add(app.Name)) list.Add(app);
        return list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // 目标前缀：拼上 AUMID 就是一条能直接交给 ShellExecute 的启动目标（实测可用，引擎侧一行没改）。
    // 它不是路径也不是 URL：Path.IsPathRooted 认不出（冒号在第 6 位不是第 2 位），
    // LaunchTarget.TargetProcessName 也推导不出进程名——于是「已在运行则激活」自动空转，正是想要的。
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    // shell:AppsFolder 这个 shell 命名空间里躺着开始菜单能看到的全部条目，其中打包 / Store 应用
    // （便笺、画图、电脑管家这一类，本机 85 个）根本不以文件形式存在，只扫 .lnk 是永远扫不到的——
    // 用户既挑不着也没法手填，因为它们没有路径可填。
    // FolderItem.Path 给的就是 AUMID：打包应用形如 Family!AppId，经典程序是它注册的那串标识。
    //
    // 整段兜底：Shell COM 不可用（ProgID 未注册、受限令牌）时退化成只有 .lnk 的列表，
    // 而不是让整个选择器打不开——这正是上一版那个「一个都没找到」的教训。
    // 线程：本方法只从 UI 线程调用，WPF 的 UI 线程是 STA，Shell COM 要的就是它。
    private static List<(string Name, string Path)> AppsFolderEntries()
    {
        var list = new List<(string Name, string Path)>();
        try
        {
            var progId = Type.GetTypeFromProgID("Shell.Application");
            if (progId == null) return list;
            dynamic? shell = Activator.CreateInstance(progId);
            dynamic? folder = shell?.NameSpace("shell:AppsFolder");
            if (folder == null) return list;
            foreach (dynamic item in folder.Items())
            {
                string name, aumid;
                // 逐条兜底：个别条目取属性会抛（正在安装 / 已损坏的包），不该带走整份列表。
                try { name = item.Name as string ?? ""; aumid = item.Path as string ?? ""; }
                catch { continue; }
                if (name.Length == 0 || aumid.Length == 0) continue;
                // AppsFolder 里混着开始菜单的 .url 条目，它们的「AUMID」本身就是一条网址
                // （本机上有一条 shell:AppsFolder\http://docs.oracle.com/...）。给网址套前缀只会做出一个
                // 打不开的目标；而裸网址本来就是已支持的目标类型，直接用它严格更好。
                list.Add((name, aumid.Contains("://", StringComparison.Ordinal) ? aumid : AppsFolderPrefix + aumid));
            }
        }
        catch { }
        return list;
    }

    // 从开始菜单挑程序（可多选）。返回选中的条目；取消或一个没选 → null。
    // 这是「省去手动填路径」那一步的入口：右键属性复制目标那套流程，在这里变成勾几下。
    public static List<(string Name, string Path)>? PickStartMenuApps(Window owner)
    {
        var entries = StartMenuEntries();
        if (entries.Count == 0)
        {
            BrandDialog.Info(owner, "Clockwork", Strings.Get("Picker_StartMenuEmpty"));
            return null;
        }

        var dlg = NewDialog(owner, Strings.Get("Picker_StartMenu"), 520, 520);
        var root = new DockPanel { Margin = new Thickness(14) };
        var search = new TextBox { Height = 30, ToolTip = Strings.Get("Picker_SearchApp") };
        DockPanel.SetDock(search, Dock.Top);
        var hint = new TextBlock
        {
            Text = Strings.Get("Picker_MultiHint"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushMuted"], FontSize = 12,
        };
        DockPanel.SetDock(hint, Dock.Top);
        var buttons = OkCancelRow(dlg, out var ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var list = new ListBox { Margin = new Thickness(0, 8, 0, 8), SelectionMode = SelectionMode.Extended };
        System.Windows.Automation.AutomationProperties.SetName(search, Strings.Get("Picker_SearchApp"));
        System.Windows.Automation.AutomationProperties.SetName(list, Strings.Get("Picker_StartMenu"));

        void Fill()
        {
            var q = search.Text.Trim();
            list.ItemsSource = entries
                .Where(x => q == "" || x.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .Select(x => new ListBoxItem { Content = x.Name, Tag = x, ToolTip = x.Path })
                .ToList();
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        // 双击 = 选这一个就走（单选是最常见的用法，不该逼人再去点确定）
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItems.Count > 0) dlg.DialogResult = true; };
        ok.Click += (_, _) => { if (list.SelectedItems.Count > 0) dlg.DialogResult = true; };

        root.Children.Add(search); root.Children.Add(hint); root.Children.Add(buttons); root.Children.Add(list);
        dlg.Content = root;
        search.Focus();
        if (dlg.ShowDialog() != true) return null;
        // 遍历 Items 而不是 SelectedItems：后者按点选先后排，Ctrl 点出来的顺序会原样变成清单里的步骤顺序，
        // 而用户脑子里的顺序是他在列表上看到的那个。
        var picked = list.Items.Cast<ListBoxItem>()
            .Where(i => list.SelectedItems.Contains(i))
            .Select(i => ((string Name, string Path))i.Tag!)
            .ToList();
        return picked.Count > 0 ? picked : null;
    }

    // 日期选择（公历 yyyy-MM-dd）。current 可解析则定位到该日期；取消 → null。
    // 读写都过 DurationText（InvariantCulture）：Calendar 控件按系统区域显示（泰历/回历也照常用），
    // 但落进配置的字符串必须是公历——不然泰历区域下选出来的是 2569 年，那条提醒永远不到期。
    public static string? PickDate(Window owner, string current)
    {
        var dlg = NewDialog(owner, Strings.Get("Date_Pick"), 300, 340);
        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = OkCancelRow(dlg, out var ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cal = new Calendar { HorizontalAlignment = HorizontalAlignment.Center };
        if (Core.DurationText.TryParseDate(current, out var cur)) { cal.SelectedDate = cur; cal.DisplayDate = cur; }
        else cal.SelectedDate = DateTime.Today;
        ok.Click += (_, _) => { if (cal.SelectedDate != null) dlg.DialogResult = true; };
        root.Children.Add(buttons); root.Children.Add(cal);
        dlg.Content = root;
        return dlg.ShowDialog() == true && cal.SelectedDate is DateTime d ? Core.DurationText.FormatDate(d) : null;
    }

    // 按键捕获：弹小窗提示「按下快捷键…」，按下即返回组合串（修饰键可选，裸 F5/Enter 也接受——发送按键不要求修饰键）。
    // Esc = 取消。accept：目的地的发送路径校验（SendInput 与 SendKeys 认的键集不同）——校验不过则忽略这次按键、
    // 继续等，避免存下执行层编码不了、运行时被当字面文本打进目标窗口的键名。
    public static string? CaptureKey(Window owner, Func<string, bool>? accept = null)
    {
        var dlg = NewDialog(owner, Strings.Get("Capture_Key"), 320, 130);
        dlg.Content = new TextBlock
        {
            Text = Strings.Get("Hotkey_PressPrompt"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 15,
        };
        string? combo = null;
        dlg.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HotkeyCapture.IsModifierKey(key)) return;                    // 只按了修饰键：等主键
            if (key == Key.Escape) { dlg.DialogResult = false; return; }    // Esc = 取消
            var tok = HotkeyCapture.KeyToken(key);
            if (tok == null) return;
            var parts = new List<string>();
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(tok);
            var candidate = string.Join("+", parts);
            if (accept != null && !accept(candidate)) return;   // 该目的地发不了这个键：忽略，继续等
            combo = candidate;
            dlg.DialogResult = true;
        };
        // 录键期间挂起全部全局热键（急停 + 组）：e.Handled 拦不住 OS 级 WM_HOTKEY，
        // 不挂起的话，按到某组已绑的组合会当场把整组跑起来。
        var app = App.Instance;
        app?.SuspendHotkeys();
        try { return dlg.ShowDialog() == true ? combo : null; }
        finally { app?.ResumeHotkeys(); }
    }

    private static Window NewDialog(Window owner, string title, double w, double h)
    {
        var dlg = new Window
        {
            Title = title, Owner = owner, Width = w, Height = h,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushInk"],
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
        };
        DarkWindow.Apply(dlg);
        WindowSizing.FitToWorkArea(dlg);   // 小屏上这些定高小窗（最高 520）也别越界
        return dlg;
    }

    private static StackPanel OkCancelRow(Window dlg, out Button ok)
    {
        // 尺寸走 DialogButton / DialogPrimaryButton（Theme.xaml 一处定义），与三个编辑器的页脚同高同宽
        var okBtn = new Button { Content = Strings.Get("Ed_Ok"), Margin = new Thickness(0, 0, 10, 0), Style = (Style)System.Windows.Application.Current.Resources["DialogPrimaryButton"] };
        var cancel = new Button { Content = Strings.Get("Ed_Cancel"), IsCancel = true, Style = (Style)System.Windows.Application.Current.Resources["DialogButton"] };
        ok = okBtn;
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { okBtn, cancel } };
    }
}
