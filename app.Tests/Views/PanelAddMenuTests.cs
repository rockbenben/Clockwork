using System;
using System.IO;
using System.Linq;
using Xunit;

// 面板表圈右端那颗「+」开的「新增」菜单。
//
// 事实经过（2026-10-30，用户报「快捷面板 点击添加后，当焦点失去，菜单依然停在那了」）：
// 那条路是四个「新增」入口里**唯一一个菜单没有归属窗口**的——它先把面板收掉，
// 而主窗通常缩在托盘里（DialogOwner 给的就是 null），于是 PlacementTarget 是空的。
// 没有归属的 ContextMenu 是一扇没有归属的浮窗：它收不到「本程序失去前台」这件事，
// 切到别的程序之后它就留在屏幕上不走。
//
// 修法有两条，缺一条就复发：
//   ① 菜单挂在**面板**上（面板因此要晚一点收——它就是那扇浮窗的归属）；
//   ② 失焦即关的闸：程序退到后台时把菜单收掉（判据问 OS「现在谁在前台」）。
public class PanelAddMenuTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // 菜单必须挂在面板上：挂在「已经关掉的窗口」上、或一个都不挂，都是无归属的浮窗。
    [Fact]
    public void The_add_menu_is_anchored_to_the_panel()
    {
        var src = Src("Services", "PanelCoordinator.cs");
        int at = src.IndexOf("private Action? AddStepTo(");
        Assert.True(at >= 0, "找不到 AddStepTo：这段守卫要跟着它走");
        var body = src.Substring(at, src.IndexOf("\n    }", at) - at);
        Assert.Contains("anchor?.HoldMenu(menu);", body);
        Assert.Contains("Views.StepMenu.OpenAt(anchor, menu)", body);
        // 锚点是面板自己，不是主窗：主窗通常缩在托盘里，给的就是 null。
        Assert.Contains("var anchor = _panel;", body);
    }

    // 菜单先收、面板再收、编辑器最后开：面板先没的话，菜单那扇浮窗跟着一起没，
    // 菜单项点下去像是没反应。
    [Fact]
    public void The_menu_closes_before_the_panel_goes_away()
    {
        var src = Src("Services", "PanelCoordinator.cs");
        int at = src.IndexOf("private Action? AddStepTo(");
        var body = src.Substring(at, src.IndexOf("\n    }", at) - at);
        int menuClose = body.IndexOf("menu!.IsOpen = false;");
        int panelGone = body.IndexOf("_panel?.Dismiss();");
        Assert.True(menuClose >= 0, "菜单必须先收：面板先没，菜单那扇浮窗跟着一起没");
        Assert.True(panelGone > menuClose, "顺序反了：菜单还没收，面板先没了");
    }

    // 面板在菜单开着的时候不许自己关掉（失焦即关）——否则菜单刚出来面板就没了，
    // 菜单项点下去指向的是一个已经关掉的窗口。与右键菜单同一条理由。
    [Fact]
    public void The_panel_suspends_its_dismiss_rule_while_the_menu_is_open()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        Assert.Contains("internal void HoldMenu(ContextMenu menu)", src);
        Assert.Contains("private bool AnyMenuOpen => _menuOpen || _heldMenu?.IsOpen == true;", src);
        // 关窗的那道门要看「有没有菜单开着」。
        Assert.Contains("if (!AnyMenuOpen && _everActivated) Dismiss();", src);
    }

    // 失焦即关：程序退到后台时把菜单收掉。判据问 OS「现在谁在前台」——
    // 菜单弹出的那一瞬锚点窗口会失焦，而那一记失焦不是「程序没了前台」。
    [Fact]
    public void The_menu_closes_when_the_app_leaves_the_foreground()
    {
        var src = Src("Views", "StepMenu.cs");
        Assert.Contains("app.Deactivated += onDeactivated;", src);
        Assert.Contains("!Native.Win32.ForegroundIsOurs()", src);
        Assert.Contains("menu.IsOpen = false;", src);
        // 退订也要有：菜单关了就别再挂着那个处理器（每次开菜单都加一个的话会越挂越多）。
        Assert.Contains("app.Deactivated -= onDeactivated;", src);
    }
}
