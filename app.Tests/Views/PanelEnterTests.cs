using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using Clockwork.Views;
using Xunit;
// 测工程同时引用 WPF 与 WinForms（两边的 Button 同名），这里要的是 WPF 那颗。
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

// 面板上的回车。
//
// **WPF 的普通按钮不吃回车**：回车只激活 IsDefault="True" 的那颗，
// 而一块面板上有几十颗按钮，不可能有一颗是「默认按钮」（空格才有效）。
// 于是「方向键挑中一颗、回车跑它」这条流程整条落空——挑中的那颗高亮着，回车什么也不发生，
// 而面板其余部分照常工作，看上去像「这一格坏了」。
// 用户报的就是这个：「发送按键」那一格按回车完全无效（所有动作都一样，格子不止那一颗）。
//
// 焦点流转要真窗口 + 消息泵，单测跑不了，所以分两半钉：
// 判据本身（EnterTarget）用纯对象测，接线用源码钉（与 PanelFocusLandingTests 同一套纪律）。
public class PanelEnterTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(new[] { d!.FullName, "app" }.Concat(parts).ToArray()));
    }

    // WPF 控件必须在 STA 线程上构造（InputManager 那层直接查线程套间，不给就抛）。
    // xunit 的测试线程是 MTA，所以自己起一条 STA 的来跑，跑完把异常原样带回来。
    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    // 判据：焦点落在挂着动作的那颗按钮上 → 找得到它的动作，回车跑的就是它。
    [Fact]
    public void The_focused_panel_button_is_the_one_enter_runs()
    {
        OnSta(() =>
        {
            bool ran = false;
            var action = new PanelAction(() => ran = true);
            var b = new Button { Tag = action };
            var found = QuickPanelWindow.EnterTarget(b);
            Assert.Same(action, found);
            found!.Run();
            Assert.True(ran);
        });
    }

    // 判据的反面：焦点不在面板按钮上时没有动作可跑——回车另有去处，不能被这一条抢走。
    [Fact]
    public void A_focus_that_is_not_a_panel_button_has_nothing_to_run()
    {
        OnSta(() =>
        {
            Assert.Null(QuickPanelWindow.EnterTarget(null));
            Assert.Null(QuickPanelWindow.EnterTarget(new Button()));      // 没有挂动作的按钮
            Assert.Null(QuickPanelWindow.EnterTarget(new TextBlock()));    // 压根不是按钮
        });
    }

    // 停用的格子：鼠标点它不动，回车也不该跑它（点不动的东西不能被键盘偷偷跑起来）。
    [Fact]
    public void A_disabled_panel_button_is_found_but_marked_disabled()
    {
        OnSta(() =>
        {
            var action = new PanelAction(() => { }, enabled: false);
            var b = new Button { Tag = action, IsEnabled = false };
            var found = QuickPanelWindow.EnterTarget(b);
            Assert.NotNull(found);
            Assert.False(found!.Enabled);
        });
    }

    // 接线：面板上每一颗按钮都要挂上动作。
    // 少挂一处就有一颗按回车没反应——而那一颗在鼠标下完全正常，看着像「只有键盘坏了」。
    // 六处：格子 / 夹板上那排 / 表圈那几颗 / 顶栏分类 / 左栏页签 / 空面板那颗行动按钮。
    [Fact]
    public void Every_panel_button_carries_its_action_for_enter()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        foreach (var maker in new[] { "MakeTile", "MakeRailButton", "MakeWaistButton",
                                       "BuildCategoryBar", "BuildPageRail", "BuildChrome" })
        {
            int at = src.IndexOf($"private Button {maker}(");
            if (at < 0) at = src.IndexOf($"private void {maker}(");
            Assert.True(at >= 0, $"找不到 {maker}：这段守卫要跟着它走");
            int end = src.IndexOf("\n    }", at);
            var body = src.Substring(at, end - at);
            Assert.Contains("new PanelAction(", body);
        }
    }

    // 两条导航栏的身份跟着动作一起挂在 Tag 上（PanelAction.Identity）——
    // Tag 只有一格：原来拿它放身份，动作就没地方放了。
    // 画选中态读的是 Identity，不是 Tag 本身：读错了那一条就是「栏不亮当前页」。
    [Fact]
    public void The_rails_keep_their_identity_alongside_the_action()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        Assert.Contains("b.Tag is PanelAction { Identity: int i }", src);
        Assert.Contains("b.Tag is PanelAction { Identity: string c }", src);
    }

    // 接线：窗口级回车按焦点那一颗跑（而不是搜索结果的第一个——那样会出现
    // 「眼睛看着 A 高亮、跑起来却是另一个动作」，而出厂页里就有锁屏与清空回收站）。
    [Fact]
    public void Enter_runs_the_focused_tile_and_not_the_first_hit()
    {
        var src = Src("Views", "QuickPanelWindow.xaml.cs");
        // 焦点认的是**事件自己的来源**（focused），不是全局的 Keyboard.FocusedElement：
        // 后者是「当前活动窗口里的焦点元素」，面板被别的程序抢了前台时它会答 null——
        // 于是一颗明明高亮着的格子按回车什么也不发生（用户报的正是这个）。
        Assert.Contains("var focused = e.OriginalSource;", src);
        Assert.Contains("case Key.Enter when EnterTarget(focused) is { } act:", src);
        Assert.Contains("act.Run();", src);
        // 焦点还在搜索框时那条快路仍按名次跑第一个——两条并存，各管各的。
        Assert.Contains("case Key.Enter when _searching && _hits.Count > 0", src);
    }
}
