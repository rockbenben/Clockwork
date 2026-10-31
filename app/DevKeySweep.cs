using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Native;
using Clockwork.Views;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using TextBox = System.Windows.Controls.TextBox;

namespace Clockwork;

// 逐个按键：面板之外的那些按键处理点。
//
// 面板那批键在 DevPanelKeys 里；这一批是「别的窗上按什么键」：
//   ① 录键框（急停键 / 面板键 / 组热键 / 一键直达 / 步骤编辑器的「组合键」都走它）
//   ② 问句弹框（Esc 取消、回车确定）
//   ③ 面板管理器的就地改名（回车 = 定、Esc = 不改）
//
// **有几处按不了，说明在这里**：按键组合里带修饰键的（问句弹框的 Ctrl+Enter）
// 读的是**物理修饰键状态**，投一条消息过去它并不认为 Ctrl 按着——
// 那一条只能真的按住 Ctrl 按一次，不是这里能替用户做的。
// 同理，「动作组工具箱·回车」按下后会**弹出一扇模态的步骤编辑器**，
// 模态框从同一条线程里驱不动（会把自己锁死）——那一步由逐功能自查里的
// 「每种类型都开得出一扇编辑器」罩着。
internal static class KeySweep
{
    /// <summary>跑一遍其它按键处理点的自查。返回是否全过；<paramref name="log"/> 是给人看的那份账。</summary>
    public static bool Run(out string log)
    {
        var fails = new List<string>();
        Trace("开始");
        try { CaptureBox(fails); } catch (Exception ex) { fails.Add("录键框：" + ex.Message); }
        Trace("录键框 完");
        try { DialogKeys(fails); } catch (Exception ex) { fails.Add("问句弹框：" + ex.Message); }
        Trace("问句弹框 完");
        try { RenameKeys(fails); } catch (Exception ex) { fails.Add("面板管理器改名：" + ex.Message); }
        Trace("改名 完");
        log = fails.Count == 0 ? "其它按键处理点全对" : string.Join("；", fails);
        return fails.Count == 0;
    }

    // 走到哪一步了：**挂住的时候这一行就是线索**。
    // 自查是在真进程里跑的，模态框那一段一旦等不到人，整个进程就停在那儿——
    // 没有这行痕迹的话，外头只看到「自查没写结果」，不知道停在哪一步。
    private static void Trace(string what)
    {
        try
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cw-keysweep.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {what}{Environment.NewLine}");
        }
        catch { }
    }

    // 录键框：① 按一个键 → 框里是那个组合；② Esc → 取消（复原）；③ 双击手输 → 回车 → 提交。
    private static void CaptureBox(List<string> fails)
    {
        string captured = "";
        var box = new TextBox { Width = 240, Height = 28 };
        var host = new Window
        {
            Content = box,
            Width = 320,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
        };
        host.Show();
        try
        {
            // 与生产代码同一套：发送模式 + 可以手输。
            KeyCaptureBox.Attach(box, HotkeyCapture.KeyCaptureMode.SendKeys, KeyInput.CanSendCombo,
                                 () => box.Text, v => captured = v, allowTyping: true);

            // ① 录一个键（F5）
            box.Focus();
            PanelKeyChecks.Pump(host, 60);
            PanelKeyChecks.Press(host, Key.F5);
            PanelKeyChecks.Pump(host, 80);
            if (captured != "F5") fails.Add($"录键框·按一个键：录到的是「{captured}」（应为 F5）");
            if (box.Text != "F5") fails.Add($"录键框·框里的字是「{box.Text}」（应为 F5）");

            // ② Esc = 取消：框里复原，什么也不录
            captured = "";
            box.Focus();
            PanelKeyChecks.Pump(host, 60);
            PanelKeyChecks.Press(host, Key.Escape);
            PanelKeyChecks.Pump(host, 80);
            if (captured != "") fails.Add($"录键框·Esc：取消却还是录进了「{captured}」");
            if (box.Text != "F5") fails.Add($"录键框·Esc 之后框里是「{box.Text}」（应复原为 F5）");

            // ③ 双击 → 手输 → 回车提交（Win+D 这类系统先吃掉的组合只能走这条路）
            box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Control.MouseDoubleClickEvent });
            box.Text = "Ctrl+K";
            box.Focus();   // 上一轮按 Esc 时清过焦点（Keyboard.ClearFocus），这里得先把焦点拿回来
            PanelKeyChecks.Pump(host, 40);
            PanelKeyChecks.Press(host, Key.Enter);
            PanelKeyChecks.Pump(host, 80);
            if (captured != "Ctrl+K") fails.Add($"录键框·手输回车：提交的是「{captured}」（应为 Ctrl+K）");
        }
        finally { try { host.Close(); } catch { } }
    }

    // 问句弹框：Esc = 取消；回车 = 确定（默认按钮）。
    private static void DialogKeys(List<string> fails)
    {
        // Esc 取消（窗自己的 KeyDown 处理器 ✓，路由事件能驱动 ✓）
        string? viaEsc = AskWith("问一句？", d => { After(d, 700, () => PressKey(d, Key.Escape)); });
        if (viaEsc != null) fails.Add($"问句弹框·Esc：没有取消（得到「{viaEsc}」）");

        // 回车确定：这一下走的是 WPF 的**默认按钮**机制，而它不在任何路由事件处理器里
        // （在输入管线的后处理阶段）——所以路由事件驱动不了它，投 Windows 消息又要求那扇框
        // 恰好是活动窗口（托盘里那个提权实例常常占着前台，非提权的自查抢不走 ✗）。
        // 于是这里只钉**接线**：确定那颗必须是默认按钮（回车才会由它接手）。
        try
        {
            bool? isDefault = null;
            AskWith("问一句？", d => After(d, 700, () => isDefault = d.YesBtn.IsDefault));
            if (isDefault != true) fails.Add($"问句弹框·回车：确定那颗不是默认按钮（IsDefault={isDefault}）");
        }
        catch (Exception ex) { fails.Add("问句弹框·回车：" + ex.Message); }
    }

    // 给一扇窗按一个键：发真的 KeyDown 路由事件。
    //
    // **这里为什么不投 Windows 消息**（面板那边投的就是消息）：模态框未必拿得到前台
    // ——托盘里那个提权实例就常在别人前面——而 WPF 的输入管线会把「不是活动窗口」那扇窗的
    // 键消息丢掉：消息投进去了，框不关，整个自查就停死（实测卡在这一段）。
    // 路由事件走的是同一批处理器（窗自己的 KeyDown），绕过的只是「消息怎么到窗上」那一段。
    private static void PressKey(Window w, Key key)
    {
        var src = PresentationSource.FromVisual(w);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, src, Environment.TickCount, key)
        { RoutedEvent = Keyboard.KeyDownEvent };
        w.RaiseEvent(args);
    }

    // 面板管理器的就地改名：回车 = 定；Esc = 不改。
    private static void RenameKeys(List<string> fails)
    {
        var cfg = RootConfig.Default();
        var page = new PanelPage { Name = "旧名字", Tab = "" };
        cfg.PanelPages.Add(page);
        var w = new PanelManagerWindow(cfg, () => { });
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -32000; w.Top = -32000; w.ShowActivated = false;
        w.Show();      // **必须显示**：没显示就没有视觉树，改名那个框自然一个都找不到
        try
        {
            // 回车 = 定
            w.BeginRenameForShots(page);
            PanelKeyChecks.Pump(w, 80);
            var box = FindRenameBox(w, page.Name);
            if (box == null)
            {
                var seen = AllTextBoxes(w).Select(t => $"「{t.Text}」").ToList();
                fails.Add($"面板管理器改名：找不到改名的那个框（这一窗里有 {seen.Count} 个输入框：{string.Join(",", seen)}）");
                return;
            }
            box.Text = "新名字";
            PanelKeyChecks.Press(w, Key.Enter);
            PanelKeyChecks.Pump(w, 80);
            if (page.Name != "新名字") fails.Add($"面板管理器改名·回车：名字是「{page.Name}」（应为「新名字」）");

            // Esc = 不改
            w.BeginRenameForShots(page);
            PanelKeyChecks.Pump(w, 80);
            var box2 = FindRenameBox(w, page.Name);
            if (box2 == null) { fails.Add("面板管理器改名：找不到改名的那个框"); return; }
            box2.Text = "不该被采纳";
            PanelKeyChecks.Press(w, Key.Escape);
            PanelKeyChecks.Pump(w, 80);
            if (page.Name != "新名字") fails.Add($"面板管理器改名·Esc：名字变成了「{page.Name}」（应保持「新名字」）");
        }
        finally { try { w.Close(); } catch { } }
    }

    // 问一句，按脚本操作那扇框。脚本在 700ms 之后跑（弹框有 600ms 的误触保护，早了不算数）。
    private static string? AskWith(string q, Action<BrandDialog> script)
    {
        BrandDialog.Rehearse = d =>
        {
            // **兜底：三秒后这扇框还没关，就把它关掉。**
            // 模态框收不到键消息时（它在后台），ShowDialog 不返回 = 整个自查停死在那儿，
            // 外头只看到「自查没写结果」。这一道闸把它变成一条失败消息，而不是一场挂起。
            var guard = new DispatcherTimer(DispatcherPriority.Normal, d.Dispatcher) { Interval = TimeSpan.FromSeconds(3) };
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                if (!d.IsVisible) return;
                try { d.DialogResult = false; } catch { }
            };
            guard.Start();
            After(d, 700, () => script(d));
        };
        try { return BrandDialog.Ask(null, "自查", q, ""); }
        finally { BrandDialog.Rehearse = null; }
    }

    // 过 ms 毫秒之后做一件事（走 DispatcherTimer 而不是 Sleep：模态循环里睡的是那个循环本身）。
    private static void After(DispatcherObject on, int ms, Action act)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, on.Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); act(); };
        t.Start();
    }

    // 改名那一个框：它是此刻唯一**带着当前页名**的输入框。
    private static TextBox? FindRenameBox(Window w, string currentName)
    {
        var all = AllTextBoxes(w);
        return all.FirstOrDefault(t => t.Text == currentName)
               ?? all.FirstOrDefault(t => t.IsVisible && t.IsEnabled);
    }

    private static List<TextBox> AllTextBoxes(Window w)
    {
        var all = new List<TextBox>();
        Collect(w, all);
        return all;
    }

    private static void Collect(DependencyObject root, List<TextBox> into)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is TextBox t) into.Add(t);
            else Collect(c, into);
        }
    }
}
