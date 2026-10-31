using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Views;
using Xunit;
// 测工程同时引用 WPF 与 WinForms（两边的 MenuItem 同名），这里要的是 WPF 那颗。
using MenuItem = System.Windows.Controls.MenuItem;

// 「新增 ▾」菜单的形状。
//
// 用户报的是「发送按键 是非常常用的动作，却在很多路径都没有入口」——
// 入口少这件事得从**菜单本身**看：它排在第几层、在不在首层。
// 所以这里直接把菜单造出来数，而不是读源码：源码只说得出「有这一项」，
// 说不出「用户要点几下才看得见它」。
//
// 这一轮还把菜单**重新排过**（用户：「整个菜单重新思考调整」）：
//   · 「常用」那一节拍平到首层 —— 首层摆的是动作本身，下面几节摆的是别的对象；
//   · 每种类型在菜单里**只出现一次** —— 之前「用户输入」既在「常用」又在「界面交互」，
//     同一件事在同一列里露两次面，菜单长了一截而信息没多一分；
//   · 「常用预设」改名「预置动作」 —— 它和「常用」只差两个字，说的却是两张表。
public class StepMenuShapeTests
{
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

    private static string?[] TopLevel()
    {
        var menu = StepMenu.Build((_, _) => { });
        return menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToArray();
    }

    // 首层 = 「常用」那一节拍平：启动程序 / 打开网址 / 发送按键 / 发送文本 / 窗口动作 / 延时。
    //
    // 首层是「不用先猜分类」的那一层——用户心里想的是「我要发个键 / 关掉这个窗口」，
    // 不是「系统那一类里的第二项」。多放一件就多占一行，
    // 而菜单不像窗口，长过屏幕不会报错，只会悄悄长出上下滚动箭头把末尾几项藏起来；
    // 冒烟自查量着它（DevChecks.CheckStepMenuFits），量不过就不该加。
    [Fact]
    public void The_top_level_is_the_daily_actions_flattened_out_of_the_common_section()
    {
        OnSta(() =>
        {
            var top = TopLevel();
            var want = StepDisplay.DailyKinds.Select(k => Strings.Get("Kind_" + k)).ToArray();
            Assert.Equal(want, top.Take(want.Length).Select(h => h!));
        });
    }

    // 「发送按键」在首层——它在每个「新增」菜单里都是一格直达
    // （启动清单、鼠标手势、面板管理器、快捷面板，四处共用这一份菜单）。
    [Fact]
    public void Send_keys_sits_at_the_top_level_of_the_add_menu()
    {
        OnSta(() => Assert.Contains(Strings.Get("Kind_keys"), TopLevel()));
    }

    // **每种类型在菜单里只出现一次。**
    //
    // 之前「用户输入」既在「常用」又在「界面交互」、「获取选中的文字」既在「常用」又在「剪贴板」——
    // 同一件事在同一列里露两次面。而「常用」那一节已经被拍平到首层，所以这里数的是整棵菜单。
    //
    // 数的范围是**单步能用的那些类型**：块内那一族（每个 / 跳出 / 正则 / 拆行 / 列表…）
    // 只在复杂动作里才说得通，这个菜单不放它们（见 StepDisplay.BlockOnlyKinds），
    // 它们在动作组工具箱里。
    [Fact]
    public void Every_step_type_appears_exactly_once_in_the_menu()
    {
        OnSta(() =>
        {
            var menu = StepMenu.Build((_, _) => { });
            var seen = new List<string>();
            void Walk(MenuItem mi)
            {
                // 叶子才是类型；带孩子的那个是节（节名是本地化过的）。
                if (mi.Items.Count == 0) { seen.Add(mi.Header as string ?? ""); return; }
                foreach (var child in mi.Items.OfType<MenuItem>()) Walk(child);
            }
            foreach (var mi in menu.Items.OfType<MenuItem>()) Walk(mi);

            foreach (var k in StepDisplay.StepKinds.Where(StepDisplay.IsAtomic))
            {
                string label = Strings.Get("Kind_" + k);
                Assert.Equal(1, seen.Count(s => s == label));   // 一次，一次都不多
            }
            // 块内那一族在这个菜单里一次都不该出现（它们在复杂动作的工具箱里）。
            foreach (var k in StepDisplay.BlockOnlyKinds)
                Assert.Equal(0, seen.Count(s => s == Strings.Get("Kind_" + k)));
        });
    }

    // 「常用」在菜单里**不再**作为一个子菜单出现（它拍平到首层了）——
    // 它旁边曾有一节叫「常用预设」，两个名字只差两个字，用户分不出哪一节是哪一节。
    [Fact]
    public void The_common_section_is_flattened_and_the_presets_are_named_apart()
    {
        OnSta(() =>
        {
            var top = TopLevel();
            Assert.DoesNotContain(Strings.Get("Menu_SecCommon"), top);
            // 预置动作那一节还在（它是填好参数的成品），但名字不再和「常用」只差两个字。
            Assert.Contains(Strings.Get("Menu_SecPreset"), top);
            Assert.NotEqual(Strings.Get("Menu_SecPreset"), Strings.Get("Menu_SecCommon"));
        });
    }
}
