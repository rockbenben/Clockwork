using System.Drawing;
using System.IO;
using Clockwork.Core;
using Clockwork.I18n;
using WinForms = System.Windows.Forms;

namespace Clockwork;

// 托盘图标与右键菜单。WPF 无原生托盘，用 WinForms NotifyIcon。
// 菜单在每次打开前重建（旧版同款）：动作组增删/勿扰剩余时间/恢复项的出现与消失都即时反映。
public sealed class TrayIcon : IDisposable
{
    // 主题色（对齐 Theme.xaml）：暗底 slate、暖白 paper、悬停 steel、分隔/边框 line、禁用 faint。
    private static readonly Color Slate = ColorTranslator.FromHtml("#1A212B");
    private static readonly Color Steel = ColorTranslator.FromHtml("#232C38");
    private static readonly Color Line  = ColorTranslator.FromHtml("#2E3947");
    private static readonly Color Paper = ColorTranslator.FromHtml("#ECE6D8");
    private static readonly Color Faint = ColorTranslator.FromHtml("#5B6472");

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
            // 暗色：WinForms 默认是浅色系统主题，与应用暗色主题不符。用自定义配色表/渲染器对齐（见文件末尾）。
            Renderer = new DarkMenuRenderer(),
            BackColor = Slate,
            ForeColor = Paper,
            ShowImageMargin = false,   // 无图标项，去掉左侧留白让菜单更紧凑
        };
        menu.Opening += (s, e) => Rebuild(menu, app);
        Rebuild(menu, app);   // 初始也建一份：空菜单在部分系统上首次右键不弹
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (s, e) => app.ShowMain();
    }

    private static void Rebuild(WinForms.ContextMenuStrip menu, App app)
    {
        menu.Items.Clear();
        menu.Items.Add(Strings.Get("Tray_Show"), null, (s, e) => app.ShowMain());
        menu.Items.Add(Strings.Get("Tray_Rerun"), null, (s, e) => app.RunLaunchAsync(false));
        menu.Items.Add(Strings.Get("Tray_Stop"), null, (s, e) => StopSignal.Request());
        // Tray_LaunchWarn 的气泡文案让用户「右键托盘→查看上次启动日志」——菜单里必须真有这一项。
        menu.Items.Add(Strings.Get("Tray_ViewLog"), null, (s, e) => app.OpenRunLog());

        // 「运行：某组」——动作组的托盘触发入口（禁用的组置灰可见，与旧版一致）。
        var groups = app.Groups;
        if (groups.Count > 0)
        {
            menu.Items.Add(new WinForms.ToolStripSeparator());
            foreach (var g in groups)
            {
                var gg = g;
                var mi = new WinForms.ToolStripMenuItem(Strings.Lf("Tray_RunGroup", g.Name)) { Enabled = g.Enabled };
                mi.Click += (s, e) => app.RunGroupAsync(gg);
                menu.Items.Add(mi);
            }
        }

        // 勿扰：暂停提醒 1/2/4 小时；生效期间追加「恢复提醒（剩 N 分钟）」。
        menu.Items.Add(new WinForms.ToolStripSeparator());
        foreach (int h in new[] { 1, 2, 4 })
        {
            int hh = h;
            menu.Items.Add(Strings.Lf("Tray_DndHours", hh), null, (s, e) => app.PauseReminders(hh));
        }
        if (app.DndRemaining is TimeSpan left)
            menu.Items.Add(Strings.Lf("Tray_DndResume", (int)Math.Ceiling(left.TotalMinutes)), null, (s, e) => app.ResumeReminders());

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Strings.Get("Tray_Exit"), null, (s, e) => app.ExitApp());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    // 暗色配色表：菜单底、悬停高亮、分隔线、边框全部对齐应用主题。
    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Slate;
        public override Color ImageMarginGradientBegin => Slate;
        public override Color ImageMarginGradientMiddle => Slate;
        public override Color ImageMarginGradientEnd => Slate;
        public override Color MenuItemSelected => Steel;
        public override Color MenuItemSelectedGradientBegin => Steel;
        public override Color MenuItemSelectedGradientEnd => Steel;
        public override Color MenuItemBorder => Steel;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color MenuItemPressedGradientBegin => Steel;
        public override Color MenuItemPressedGradientEnd => Steel;
    }

    // 暗色渲染器：套用配色表 + 文字用暖白(禁用项用 faint)，去掉圆角描边。
    private sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }
        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Paper : Faint;
            base.OnRenderItemText(e);
        }
    }
}
