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
        // 清空前先释放上一轮的项：TrayMenu.SubMenu 会实体化 DropDown（一个 ToolStripDropDownMenu，是 Control），
        // 展开过一次就持有窗口句柄——只 Items.Clear() 不 Dispose 的话每次开菜单都漏一批 USER 对象，
        // GC/终结器都收不回（每进程默认上限 10000，而本程序设计成常驻托盘数周）。
        // 这里 Dispose 与 Dispose() 里「故意不释放菜单」并不矛盾：本方法由 Opening 触发、在菜单显示之前跑，
        // 释放的是上一轮的旧项，没有任何一项在派发栈上；而「退出」是从菜单项自身的 Click 里回调过来的，
        // 那时 ContextMenuStrip 仍在派发点击，释放它才会重入崩溃。别为了「一致」把这里也删掉。
        DisposeItems(menu.Items);
        menu.Items.Clear();
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Show"), TrayGlyph.Window, (s, e) => app.ShowMain()));

        // 启动清单区（小标题复用「我的启动清单」标签页名，已多语言）
        menu.Items.Add(TrayMenu.Header(Strings.Get("Tab_Launch")));
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Rerun"), TrayGlyph.Rerun, (s, e) => app.RunLaunchAsync(false)));
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Stop"), TrayGlyph.Stop, (s, e) => app.RequestStop()));
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

        // 提醒区——勿扰折成子菜单：1/2/4 小时是低频操作，占 3 行不值当（动作组段则保持扁平，
        // 「运行：某组」是托盘最高频的动作，不能埋进 hover）。生效期间追加「恢复提醒（剩 N 分钟）」。
        menu.Items.Add(TrayMenu.Header(Strings.Get("Tab_Reminder")));
        var dnd = TrayMenu.SubMenu(Strings.Get("Tray_DndMenu"), TrayGlyph.Dnd, menu.Renderer, menu.Font);
        foreach (int h in new[] { 1, 2, 4 })
        {
            int hh = h;
            dnd.DropDownItems.Add(TrayMenu.Item(Strings.Lf("Tray_Hours", hh), TrayGlyph.Dnd, (s, e) => app.PauseReminders(hh)));
        }
        menu.Items.Add(dnd);
        if (app.DndRemaining is TimeSpan left)
            menu.Items.Add(TrayMenu.Item(Strings.Lf("Tray_DndResume", (int)Math.Ceiling(left.TotalMinutes)), TrayGlyph.Run,
                (s, e) => app.ResumeReminders()));

        // 最近通知区——回看被点掉 / 被挤掉 / 已自动消失的卡片；点一条把它重新弹出来。
        // 会话级：重启即空，所以没通知时整区不出现（不留一个常年空着的小标题）。
        var recent = app.RecentNotifications;
        if (recent.Count > 0)
        {
            var hist = TrayMenu.SubMenu(Strings.Get("Tray_History"), TrayGlyph.Log, menu.Renderer, menu.Font);
            foreach (var n in recent)
            {
                var nn = n;
                hist.DropDownItems.Add(TrayMenu.Item($"{n.At:HH:mm}  {StepHelpers.Ellipsis(OneLine(n.Message), 36)}", TrayGlyph.Log,
                    (s, e) => app.ReplayNotification(nn)));
            }
            menu.Items.Add(hist);
        }

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(TrayMenu.Item(Strings.Get("Tray_Exit"), TrayGlyph.Exit, (s, e) => app.ExitApp()));
    }

    // 递归释放菜单项。必须连子项一起：漏的句柄挂在父项的 DropDown 上，子项本身也是 IDisposable，
    // 只释放顶层项收不干净。倒序按下标取——ToolStripItem.Dispose 会把自己从 Owner.Items 里摘掉，
    // 正序 foreach 会在集合被就地改动时抛「集合已修改」。
    private static void DisposeItems(WinForms.ToolStripItemCollection items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            if (it is WinForms.ToolStripMenuItem { HasDropDownItems: true } sub) DisposeItems(sub.DropDownItems);
            it.Dispose();
        }
    }

    // 摘要压成单行：提醒/消息步骤的文本框允许多行，而 ToolStrip 菜单按单行测绘，
    // 硬换行会把自绘菜单画花（第二行被裁掉/压住相邻项）。与 ReminderDisplay.TextSummary 同口径。
    private static string OneLine(string s)
        => string.Join(" ", s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    public void Dispose()
    {
        // 只释放托盘图标。菜单/渲染器/字体是随进程存活的单例，故意不在此 Dispose——
        // 「退出」是从菜单项自身的 Click 里调 app.ExitApp()→本方法，此时该 ContextMenuStrip 仍在
        // 调用栈上派发点击；同步 Dispose 它会在点击返回后触发 ObjectDisposedException。GDI 句柄由进程结束回收。
        _icon.Visible = false;
        _icon.Dispose();
    }
}
