using System.Drawing;
using Clockwork.Core;
using Clockwork.I18n;
using WinForms = System.Windows.Forms;

namespace Clockwork;

// 托盘图标与右键菜单。WPF 无原生托盘，用 WinForms NotifyIcon。
// 菜单每次打开前重建：动作组增删 / 勿扰剩余时间 / 恢复项的出现与消失即时反映。
// 外观（暗色仪表盘：字形列 + 悬停黄铜刻度 + 区段小标题）见 TrayMenuRenderer。
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(App app)
    {
        _icon = new WinForms.NotifyIcon { Visible = true, Text = "Clockwork" };
        // 从内嵌 WPF 资源读图标（不是磁盘文件）——单文件发布时 exe 旁没有 assets\logo.ico，
        // 按文件路径读会落空、托盘图标变成系统默认图，通知(Win10 把气泡渲染成 toast)也就没了应用图标。
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("logo.ico", UriKind.Relative));
            _icon.Icon = res != null ? new Icon(res.Stream) : SystemIcons.Application;
        }
        catch { _icon.Icon = SystemIcons.Application; }

        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = TrayPalette.Ink,
            // 显式设菜单字体：项自动测宽与渲染器绘制都用它（e.TextFont），两侧一致，标签不会被省略号截断。
            Font = new System.Drawing.Font("Segoe UI", 9.75f),
            ShowImageMargin = false,
            ShowCheckMargin = false,
        };
        menu.Opening += (s, e) => Rebuild(menu, app);
        Rebuild(menu, app);   // 初始也建一份：空菜单在部分系统上首次右键不弹
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (s, e) => app.ShowMain();
    }

    private static void Rebuild(WinForms.ContextMenuStrip menu, App app)
    {
        menu.Items.Clear();
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Show"), TrayGlyph.Window, (s, e) => app.ShowMain()));

        // 启动清单区（小标题复用「我的启动清单」标签页名，已多语言）
        menu.Items.Add(TrayMenu.Header(Strings.Get("Tab_Launch")));
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Rerun"), TrayGlyph.Rerun, (s, e) => app.RunLaunchAsync(false)));
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Stop"), TrayGlyph.Stop, (s, e) => StopSignal.Request()));
        // Tray_LaunchWarn 的气泡文案让用户「右键托盘→查看上次启动日志」——菜单里必须真有这一项。
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_ViewLog"), TrayGlyph.Log, (s, e) => app.OpenRunLog()));

        // 动作组区——托盘触发入口（禁用的组置灰可见）。有组才加小标题。
        var groups = app.Groups;
        if (groups.Count > 0)
        {
            menu.Items.Add(TrayMenu.Header(Strings.Get("Tab_Group")));
            foreach (var g in groups)
            {
                var gg = g;
                menu.Items.Add(TrayMenu.Item(Strings.Lf("Tray_RunGroup", g.Name), TrayGlyph.Run,
                    (s, e) => app.RunGroupAsync(gg), enabled: g.Enabled));
            }
        }

        // 提醒区——勿扰：暂停 1/2/4 小时；生效期间追加「恢复提醒（剩 N 分钟）」。
        menu.Items.Add(TrayMenu.Header(Strings.Get("Tab_Reminder")));
        foreach (int h in new[] { 1, 2, 4 })
        {
            int hh = h;
            menu.Items.Add(TrayMenu.Item(Strings.Lf("Tray_DndHours", hh), TrayGlyph.Dnd, (s, e) => app.PauseReminders(hh)));
        }
        if (app.DndRemaining is TimeSpan left)
            menu.Items.Add(TrayMenu.Item(Strings.Lf("Tray_DndResume", (int)Math.Ceiling(left.TotalMinutes)), TrayGlyph.Run,
                (s, e) => app.ResumeReminders()));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Exit"), TrayGlyph.Exit, (s, e) => app.ExitApp()));
    }

    public void Dispose()
    {
        // 只释放托盘图标。菜单/渲染器/字体是随进程存活的单例，故意不在此 Dispose——
        // 「退出」是从菜单项自身的 Click 里调 app.ExitApp()→本方法，此时该 ContextMenuStrip 仍在
        // 调用栈上派发点击；同步 Dispose 它会在点击返回后触发 ObjectDisposedException。GDI 句柄由进程结束回收。
        _icon.Visible = false;
        _icon.Dispose();
    }
}
