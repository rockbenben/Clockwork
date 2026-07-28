using System.Drawing;
using System.Windows.Forms;
using Clockwork;
using Xunit;

public class TraySubMenuTests
{
    // WinForms 的 DropDown 是独立的 ToolStripDropDownMenu，不继承父菜单的 Renderer/BackColor/Font。
    // 不显式设的话子菜单会以系统浅色主题绘制，在暗色托盘菜单里是一道刺眼的接缝。
    [Fact]
    public void SubMenu_dropdown_inherits_the_dark_chrome()
    {
        var renderer = new TrayMenuRenderer();
        using var font = new Font("Segoe UI", 9.75f);

        var item = TrayMenu.SubMenu("测试", TrayGlyph.Dnd, renderer, font);

        Assert.Same(renderer, item.DropDown.Renderer);
        Assert.Equal(TrayPalette.Ink, item.DropDown.BackColor);
        Assert.Equal(font, item.DropDown.Font);

        var dd = Assert.IsType<ToolStripDropDownMenu>(item.DropDown);
        Assert.False(dd.ShowImageMargin);
        Assert.False(dd.ShowCheckMargin);
    }

    [Fact]
    public void SubMenu_carries_glyph_metadata_like_a_normal_item()
    {
        var renderer = new TrayMenuRenderer();
        using var font = new Font("Segoe UI", 9.75f);

        var item = TrayMenu.SubMenu("测试", TrayGlyph.Log, renderer, font);

        var meta = Assert.IsType<TrayMeta>(item.Tag);
        Assert.Equal(TrayGlyph.Log, meta.Glyph);
        Assert.False(meta.Header);
    }

    // 动作组名里的 & 要当字面量显示（与 TrayMenu.Item 同一条转义契约）。
    [Fact]
    public void SubMenu_escapes_ampersand()
    {
        var renderer = new TrayMenuRenderer();
        using var font = new Font("Segoe UI", 9.75f);

        Assert.Equal("R&&D", TrayMenu.SubMenu("R&D", "", renderer, font).Text);
    }
}
