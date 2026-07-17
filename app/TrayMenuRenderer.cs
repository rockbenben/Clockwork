using System.Drawing;
using System.Windows.Forms;

namespace Clockwork;

// 托盘暗色仪表盘配色（对齐 Theme.xaml）。渲染器与 TrayIcon 共用同一份，避免两处各写 #12161D 等值日后漂移。
internal static class TrayPalette
{
    public static readonly Color Ink   = ColorTranslator.FromHtml("#12161D");
    public static readonly Color Steel = ColorTranslator.FromHtml("#232C38");
    public static readonly Color Line  = ColorTranslator.FromHtml("#2E3947");
    public static readonly Color Paper = ColorTranslator.FromHtml("#ECE6D8");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8B95A3");
    public static readonly Color Faint = ColorTranslator.FromHtml("#5B6472");
    public static readonly Color Brass = ColorTranslator.FromHtml("#E0A23C");
}

// 托盘菜单字形（Segoe MDL2 Assets 码位）：统一的线性图标族，笔画一致。
internal static class TrayGlyph
{
    public static readonly string Window = char.ConvertFromUtf32(0xE8A7);  // 打开窗口
    public static readonly string Rerun  = char.ConvertFromUtf32(0xE72C);  // 刷新＝重新运行
    public static readonly string Stop   = char.ConvertFromUtf32(0xE71A);  // 停止
    public static readonly string Log    = char.ConvertFromUtf32(0xE81C);  // 历史时钟＝上次日志（呼应「Clockwork」）
    public static readonly string Run    = char.ConvertFromUtf32(0xE768);  // 播放＝运行/恢复
    public static readonly string Dnd    = char.ConvertFromUtf32(0xE708);  // 月亮＝勿扰/暂停提醒
    public static readonly string Exit   = char.ConvertFromUtf32(0xE711);  // ×＝退出
}

// 挂在 ToolStripItem.Tag 上的托盘菜单元数据：字形、是否区段小标题。
internal sealed class TrayMeta
{
    public string Glyph = "";
    public bool Header;
}

// 构造带元数据的托盘菜单项，让 TrayIcon 与截图 harness 共用同一套外观口径。
internal static class TrayMenu
{
    public const int GlyphCol = 30;   // 字形列宽
    public const int PadRight = 16;
    public static readonly Font HeaderFont = new("Segoe UI", 7.75f, FontStyle.Bold);   // 进程级单例，随进程退出释放

    public static ToolStripMenuItem Item(string text, string glyph, EventHandler? onClick, bool enabled = true)
    {
        var it = new ToolStripMenuItem(Escape(text))
        {
            Enabled = enabled,
            Tag = new TrayMeta { Glyph = glyph },
            Padding = new Padding(GlyphCol, 7, PadRight, 7),
        };
        if (onClick != null) it.Click += onClick;
        return it;
    }

    // 区段小标题：不可选、无悬停。字距在此就地加好——「测宽」与「绘制」用同一串文本，不会因绘制时才加字距、box 却按原文测宽而裁掉尾字。
    public static ToolStripMenuItem Header(string text)
        => new(Escape(Spaced(text))) { Enabled = false, Tag = new TrayMeta { Header = true }, Font = HeaderFont, Padding = new Padding(14, 10, 10, 3) };

    // 转义 & → &&：ToolStrip 按「助记符」规则测宽（吃掉单个 &），我们要把 & 当字面量显示，
    // 转义后测宽=绘制都得到字面 &（如动作组名「R&D」），不会因宽度对不上被省略号截断。
    private static string Escape(string s) => s.Replace("&", "&&");

    // 只给「纯拉丁」标题加字距（作刻字面板感）。任何 ≥0x0250 的字符（西里尔/希腊/阿拉伯/泰/天城/越南带变音/CJK…）
    // 一律原样返回——否则会把非拉丁文字排成「М о й…」甚至破坏 RTL 塑形。
    private static string Spaced(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        foreach (var ch in s) if (ch >= 0x0250) return s;
        return string.Join(" ", s.ToCharArray());
    }
}

// 托盘右键菜单渲染器：把系统浅色菜单改造成 Clockwork 暗色仪表盘——
// 字形列 + 悬停「黄铜刻度条」(signature) + 刻字式区段小标题。
internal sealed class TrayMenuRenderer : ToolStripRenderer
{
    // 图标字体：进程级单例（渲染器随托盘存活到退出），GDI 句柄由进程结束时回收，不在此显式释放——
    // 见 TrayIcon.Dispose 的说明：从 Exit 项自身的 Click 里同步 Dispose 菜单/字体有重入崩溃风险。
    private readonly Font _glyph = new("Segoe MDL2 Assets", 10f, FontStyle.Regular);
    private readonly bool _hasGlyphFont = HasFont("Segoe MDL2 Assets");

    // 精简 SKU（Server Core 等）可能没有图标字体：查不到就不画字形（留空的字形列），退化为纯文字菜单而非满屏豆腐块。
    private static bool HasFont(string family)
    {
        try { using var ff = new FontFamily(family); return true; }
        catch { return false; }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        => e.Graphics.Clear(TrayPalette.Ink);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(TrayPalette.Line);
        var r = e.AffectedBounds; r.Width -= 1; r.Height -= 1;
        e.Graphics.DrawRectangle(pen, r);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var b = e.Item.Bounds;
        int y = b.Top + b.Height / 2;
        using var pen = new Pen(TrayPalette.Line);
        e.Graphics.DrawLine(pen, b.Left + 12, y, b.Right - 12, y);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Tag is TrayMeta { Header: true }) return;
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var b = e.Item.Bounds;
        using (var br = new SolidBrush(TrayPalette.Steel))
            e.Graphics.FillRectangle(br, new Rectangle(b.Left + 3, b.Top + 1, b.Width - 6, b.Height - 2));
        using var bar = new SolidBrush(TrayPalette.Brass);   // signature：左侧黄铜刻度条
        e.Graphics.FillRectangle(bar, new Rectangle(b.Left + 3, b.Top + 5, 2, b.Height - 10));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var b = e.Item.Bounds;
        // 标签/小标题都用 e.TextFont——即 ToolStrip 自动测宽所用的那支字体，绘制=测量，绝不因字体不一致被省略号截断。
        var font = e.TextFont;

        if (e.Item.Tag is TrayMeta { Header: true })
        {
            var hr = new Rectangle(b.Left + 14, b.Top, b.Width - 24, b.Height);
            // 不加 NoPrefix：与测宽口径一致地处理助记符（&& → 字面 &），文本已转义故无游离下划线。
            TextRenderer.DrawText(g, e.Text, font, hr, TrayPalette.Muted,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            return;
        }

        bool on = e.Item.Enabled;
        if (_hasGlyphFont && e.Item.Tag is TrayMeta { Glyph.Length: > 0 } meta)
        {
            var glyphColor = e.Item.Selected && on ? TrayPalette.Brass : (on ? TrayPalette.Muted : TrayPalette.Faint);
            var gr = new Rectangle(b.Left + 3, b.Top, TrayMenu.GlyphCol - 3, b.Height);
            TextRenderer.DrawText(g, meta.Glyph, _glyph, gr, glyphColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        var lr = new Rectangle(b.Left + TrayMenu.GlyphCol, b.Top, b.Width - TrayMenu.GlyphCol - TrayMenu.PadRight, b.Height);
        // 同上不加 NoPrefix：与 ToolStrip 测宽一致地按助记符处理，动作组名里的 & 才不会被裁。
        TextRenderer.DrawText(g, e.Text, font, lr, on ? TrayPalette.Paper : TrayPalette.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
