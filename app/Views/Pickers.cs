using System.Diagnostics;
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

    // 日期选择（yyyy-MM-dd）。current 可解析则定位到该日期；取消 → null。
    public static string? PickDate(Window owner, string current)
    {
        var dlg = NewDialog(owner, Strings.Get("Date_Pick"), 300, 340);
        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = OkCancelRow(dlg, out var ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cal = new Calendar { HorizontalAlignment = HorizontalAlignment.Center };
        if (DateTime.TryParse(current, out var cur)) { cal.SelectedDate = cur; cal.DisplayDate = cur; }
        else cal.SelectedDate = DateTime.Today;
        ok.Click += (_, _) => { if (cal.SelectedDate != null) dlg.DialogResult = true; };
        root.Children.Add(buttons); root.Children.Add(cal);
        dlg.Content = root;
        return dlg.ShowDialog() == true && cal.SelectedDate is DateTime d ? d.ToString("yyyy-MM-dd") : null;
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
        dlg.SourceInitialized += (_, _) => DarkTitleBar.Apply(dlg);
        return dlg;
    }

    private static StackPanel OkCancelRow(Window dlg, out Button ok)
    {
        var okBtn = new Button { Content = Strings.Get("Ed_Ok"), MinWidth = 80, Height = 30, Margin = new Thickness(0, 0, 10, 0), Style = (Style)System.Windows.Application.Current.Resources["PrimaryButton"] };
        var cancel = new Button { Content = Strings.Get("Ed_Cancel"), MinWidth = 70, Height = 30, IsCancel = true };
        ok = okBtn;
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { okBtn, cancel } };
    }
}
