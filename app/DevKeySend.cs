using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Clockwork.Native;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这几个名字两边都有（同别处那几行）。
using TextBox = System.Windows.Controls.TextBox;

namespace Clockwork;

// 发键自查：**真的发一次键，看目标程序到底收到了什么**。
//
// 事实经过（2026-10-31，用户实报「发送 enter 还是无效」）：
// 热键触发的发键，注入那一刻用户正按着热键自己的修饰键，于是发出去的
// 不是裸回车，而是 Ctrl+Shift+Enter——目标程序收到的是「带修饰键的回车」，
// 多数程序对它毫无反应，用户看到的正是「按了没反应」。
//
// 这类缺陷**代码里看不出来**：case 都在、单测也绿，按下去没反应而已。
// 源头上钉不住的东西，就得真的发一遍：目标窗是一个真的 WPF 窗，
// 键由本程序真的注入（SendInput），发完看它那边记下了什么键、带什么修饰键。
//
// **一张矩阵，不是一个回车**：回车只是用户报的那一个。修饰键这条闸管的是
// **所有**键——裸键、带修饰键的组合、置前并发送按键、发送文本，哪一条走漏了都会变成
// 「发出去的键被改了身份」。键盘用例在两种状态下各验一次：修饰键没按着（基线）与
// 修饰键还按着（用户实报的那种）；组合键/文本只在修饰键按住态验一次，鼠标伪键另见下文。
//
// **必须让目标窗在前台**：注入是发给前台窗口的，窗口不在前台时键会落到
// 用户正在用的程序上——那既是干扰，也可能正好把用户的东西弄坏。
// 抢不到前台就如实说「这条没验成」，不假装通过。
internal static class KeySendChecks
{
    // 虚拟键码（只列这份矩阵用得上的）。
    private const ushort VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;
    private const ushort VK_RETURN = 0x0D, VK_F5 = 0x74, VK_A = 0x41, VK_C = 0x43;
    private const ushort VK_SPACE = 0x20, VK_ESCAPE = 0x1B, VK_TAB = 0x09, VK_LEFT = 0x25;

    // 一个场景：按着某些修饰键 → 发一个组合 → 目标程序应当收到什么键、带什么修饰键。
    //
    // Hold 是「用户这会儿还按着」的修饰键（通用码：0x11 Ctrl / 0x10 Shift / 0x12 Alt）。
    // ExpectMods 是**目标程序看到**的修饰键：
    //   · Hold 里的键若不在这次组合里 → 目标程序应当一个都看不到（None）；
    //   · 这次组合自己带的修饰键 → 目标程序必须看得到（不能被误摘掉）。
    private sealed record Case(string Name, string Combo, ushort[] Hold, ushort ExpectVk, ModifierKeys ExpectMods);

    private static readonly Case[] Cases =
    {
        // —— 裸键：什么都不按着时发出去是什么样（基线）——
        new("裸回车", "Enter", Array.Empty<ushort>(), VK_RETURN, ModifierKeys.None),
        new("裸 F5", "F5", Array.Empty<ushort>(), VK_F5, ModifierKeys.None),
        new("Ctrl+C（基线）", "Ctrl+C", Array.Empty<ushort>(), VK_C, ModifierKeys.Control),

        // —— **修饰键还按着**：这一族才是修的那条（用户实报）——
        new("按着 Ctrl 发回车", "Enter", new[] { VK_CONTROL }, VK_RETURN, ModifierKeys.None),
        new("按着 Ctrl+Shift 发回车", "Enter", new[] { VK_CONTROL, VK_SHIFT }, VK_RETURN, ModifierKeys.None),
        new("按着 Ctrl 发 F5", "F5", new[] { VK_CONTROL }, VK_F5, ModifierKeys.None),
        new("按着 Ctrl 发空格", "Space", new[] { VK_CONTROL }, VK_SPACE, ModifierKeys.None),
        new("按着 Ctrl 发 Esc", "Esc", new[] { VK_CONTROL }, VK_ESCAPE, ModifierKeys.None),
        new("按着 Ctrl 发 Tab", "Tab", new[] { VK_CONTROL }, VK_TAB, ModifierKeys.None),

        // —— 这次组合自己带修饰键：**不能被误摘**（摘错了就是把 Ctrl+C 发成 C）——
        new("按着 Ctrl 发 Ctrl+C", "Ctrl+C", new[] { VK_CONTROL }, VK_C, ModifierKeys.Control),
        new("按着 Ctrl+Shift 发 Ctrl+Shift+A", "Ctrl+Shift+A", new[] { VK_CONTROL, VK_SHIFT },
            VK_A, ModifierKeys.Control | ModifierKeys.Shift),
    };

    // **Alt 那一格单独放最后跑**：Alt 一按，目标窗就进了菜单模式（Windows 的老规矩），
    // 之后那扇窗**抢不回前台**——而矩阵里每一格的开头都要重新抢一次前台。
    // 把它排在最后，它自己那格不受影响（它抢到前台时是好的），后面也不用再抢。
    private static readonly Case AltCase =
        new("按着 Alt 发方向键", "Left", new[] { VK_MENU }, VK_LEFT, ModifierKeys.None);

    // **矩阵里没有「光秃秃的字母」**（A、5 这种）：中文输入法开着时它会被输入法吃掉
    // （目标程序收到的是「输入法处理过的键」，不是那个字母），还会让输入法开始拼拼音。
    // 那是输入法的事，不是这条路的事——要打字母有「发送文本」那条路（按 Unicode 送进去，
    // 绕开输入法与键盘布局，见 Win32.SendUnicodeText 的说明）。
    // 带 Ctrl 的字母不受影响（输入法不拦组合键），所以矩阵里留着 Ctrl+C 与 Ctrl+Shift+A。

    /// <summary>跑一遍发键自查。返回是否全过；<paramref name="skipped"/> 为真表示「环境不具备，没验成」。</summary>
    //
    // skipped 与失败是两回事：抢不到前台时**不能**往用户正在用的程序上注入，
    // 那是干扰，也可能正好弄坏用户手头的东西。这种情况下这一条只能跳过——
    // 但也不能当成通过（那等于没验却说验过了），所以单开一档。
    public static bool Run(out string log, out bool skipped)
    {
        var fails = new List<string>();
        var notes = new List<string>();
        skipped = false;

        // 目标窗：一个真的 WPF 窗，记下每个键/鼠标事件到达时的**虚拟键码**与**当时的修饰键**。
        // 记虚拟键码而不是键名：WPF 那边同一个键有两个名字（Enter 与 Return 同值，
        // ToString 取其中一个），按名字断言会写出「明明收到了却说没收到」的假红。
        var seen = new List<(ushort Vk, ModifierKeys Mods, string Name)>();
        var box = new TextBox { Width = 200, Height = 24 };
        var host = new Window
        {
            Content = box,
            Width = 320,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.Manual,
            // **贴着屏幕右下角，只露一条边**——不是 -32000 那种完全离屏的位置：
            // 完全离屏的窗**抢不到前台**（Windows 不给完全不可见的窗前台资格），
            // 而这条自查的全部前提就是「目标窗在前台，键才发得到它身上」。
            // 只露 30×30 的一条边：用户那边基本看不见，却是个真正在屏上的窗。
            Left = Math.Max(0, SystemParameters.PrimaryScreenWidth - 30),
            Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - 30),
            ShowActivated = false,
            // **要有标题**：程序按进程名找窗口时会滤掉无标题的窗（那一条是挡
            // Progman 之类的无标题壳窗用的，剔的是别家的窗）——目标窗自己没标题，
            // 「置前并发送按键」那条路就找不到它，键自然发不到自己身上。
            Title = "cw-keysend",
        };
        host.PreviewKeyDown += (_, e) =>
        {
            // 带 Alt 的组合是**系统键**（WM_SYSKEYDOWN）：键在 SystemKey 上，Key 只是 Key.System。
            // 不看 SystemKey 就会把「Alt+方向键」记成一把 0——那是读法的问题，不是键没到。
            var k = e.SystemKey != Key.None ? e.SystemKey : e.Key;
            seen.Add(((ushort)KeyInterop.VirtualKeyFromKey(k), Keyboard.Modifiers, k.ToString()));
        };
        // **鼠标伪键（点击 / 滚轮）不在这张矩阵里**：它落在**指针当前所在的窗口**，
        // 要验就得把指针挪到目标窗上——那等于在用户屏幕上点一下别人正在用的东西。
        // 它与键盘走的是同一道闸（ModifierGuard），键盘那几条验过就够了。

        host.Show();
        var hwnd = new WindowInteropHelper(host).Handle;

        // 原始消息钩子：**WPF 的事件看不见系统键**（Alt 组合走 WM_SYSKEYDOWN，
        // 而 WPF 在菜单模式下根本不派发 KeyDown）。要分清「键没到」与「键到了但被吃掉」，
        // 就得看窗口过程真的收到了什么。
        // **WM_CHAR 也在内**：SendKeys 那条路（置前并发送按键）发的是字符消息，
        // WPF 那边只当文本输入、不派发 KeyDown——只听 KeyDown 会以为「键没到」。
        var raw = new List<string>();
        HwndSource.FromHwnd(hwnd)?.AddHook((h, msg, w, l, ref handled) =>
        {
            string? kind = msg switch
            {
                0x0100 => "down", 0x0101 => "up",
                0x0102 => "char", 0x0104 => "SYS-down", 0x0105 => "SYS-up",
                _ => null,
            };
            if (kind != null) raw.Add($"{kind}:0x{w.ToInt64():X2}");
            return IntPtr.Zero;
        });
        try
        {
            Trace("开窗");
            if (!TakeForeground(host, hwnd))
            {
                // 抢不到就**不跑**：注入会打到用户正在用的程序上。
                log = "抢不到前台，不敢往用户正在用的程序上注入";
                skipped = true;
                return false;
            }
            box.Focus();
            Pump(host, 60);
            Trace("焦点落在输入框");

            // 跑一个场景：按着修饰键 → 发 → 看目标程序收到了什么。
            //
            // **前台被抢走了要重来一次**：发键之前有一小段等待（等用户松开修饰键，
            // 见 ModifierGuard），等待期间前台可能被别人拿走——那一下键就发去了
            // 别的窗口，目标窗自然什么都没收到。这种情况重跑一次，而不是记成失败。
            bool CaseOk(Case c, out string detail)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!TakeForeground(host, hwnd)) { detail = "抢不到前台"; continue; }
                    seen.Clear();
                    raw.Clear();
                    HoldKeys(c.Hold);
                    try
                    {
                        KeyInput.SendKeyCombo(c.Combo);
                        // **立刻抬起**：靶子还是我们的时候抬。抬到别的窗口（用户正在用的程序）
                        // 是一次误伤——那边会收到一个凭空而来的「修饰键抬起」。
                        ReleaseKeys(c.Hold);
                    }
                    finally { ReleaseKeys(c.Hold); }   // 幂等兜底：发键抛了也得把键抬起来
                    Pump(host, 250);

                    var got = seen.Where(s => s.Vk == c.ExpectVk).ToList();
                    string rawText = string.Join(",", raw);
                    if (got.Count == 0)
                    {
                        detail = $"目标程序没收到 0x{c.ExpectVk:X2}（收到 [{Show(seen)}]；原始消息 {rawText}）";
                        if (GetForegroundWindow() != hwnd) continue;   // 前台被抢走了 → 再来一次
                        return false;
                    }
                    if (got.Any(s => s.Mods != c.ExpectMods))
                    {
                        detail = $"目标程序收到的是 [{Show(got)}]，修饰键应当是 {c.ExpectMods}";
                        return false;
                    }
                    detail = $"0x{c.ExpectVk:X2}/{c.ExpectMods}";
                    Trace($"{c.Name}: {Show(seen)} | 原始消息: {rawText}");
                    return true;
                }
                // 两轮都没成：前台已经不在我们这儿 → 这一格是**环境**抢的，不是这一格坏了。
                detail = GetForegroundWindow() == hwnd ? "没收到键" : "前台被抢走";
                return false;
            }

            foreach (var c in Cases)
            {
                if (!CaseOk(c, out string detail))
                {
                    // **「前台被抢走」不是这一格的失败**：键确实发出去了，只是发到了
                    // 别的窗口（多半是用户在切窗口，或输入法弹了什么）。
                    // 记成「没验成」，不算失败——否则这条自查会隔三差五地红。
                    if (detail is "抢不到前台" or "前台被抢走" or "前台一直被抢走")
                    { notes.Add($"{c.Name} 没验成（{detail}）"); continue; }
                    fails.Add($"{c.Name}：{detail}");
                }
                else notes.Add($"{c.Name} → {detail}");
            }

            // —— 置前并发送按键（SendKeys 序列那条路）——
            // 这条路发的是 **WM_CHAR**（SendKeys 的做法），WPF 那边只当文本输入、
            // 不派发 KeyDown——所以这里听**原始消息**，不听 WPF 的键事件。
            if (!TakeForeground(host, hwnd)) { log = "中途失去前台"; skipped = true; return false; }
            seen.Clear();
            raw.Clear();
            HoldKeys(new[] { VK_CONTROL });
            try
            {
                // 目标进程就是本进程：目标窗是唯一可见的那扇窗（标题也在，见上面那扇窗）。
                WindowManager.WindowLogin("Clockwork", "{ENTER}", 8);
                ReleaseKeys(new[] { VK_CONTROL });
            }
            finally { ReleaseKeys(new[] { VK_CONTROL }); }
            Pump(host, 250);
            bool winGot = raw.Any(r => r.EndsWith(":0x0D", StringComparison.OrdinalIgnoreCase))
                          || seen.Any(s => s.Vk == VK_RETURN);
            if (!winGot) fails.Add($"置前并发送按键：目标程序没收到回车（原始消息 {string.Join(",", raw)}）");
            else notes.Add("置前并发送按键（{ENTER}）→ 回车（原始消息 " + string.Join(",", raw) + "）");

            // —— 发送文本：字符按 Unicode 进去（不进键日志），末尾那个换行是真键 ——
            if (!TakeForeground(host, hwnd)) { log = "中途失去前台"; skipped = true; return false; }
            seen.Clear();
            box.Clear();
            HoldKeys(new[] { VK_CONTROL });
            try
            {
                WindowManager.SendText("ab\n");
                ReleaseKeys(new[] { VK_CONTROL });
            }
            finally { ReleaseKeys(new[] { VK_CONTROL }); }
            Pump(host, 250);
            if (box.Text != "ab") fails.Add($"发送文本：输入框里是「{box.Text}」（应当是 ab）");
            var textEnter = seen.Where(s => s.Vk == VK_RETURN).ToList();
            if (textEnter.Count == 0) fails.Add($"发送文本：没收到那个换行回车 [{Show(seen)}]");
            else if (textEnter.Any(s => s.Mods != ModifierKeys.None))
                fails.Add($"发送文本：修饰键跟着换行回车走了 [{Show(textEnter)}]");
            else notes.Add("发送文本（ab\\n）→ 文本 ab + 换行/无修饰键");

            // —— Alt 那一格放最后：Alt 一按，目标窗就进了菜单模式、抢不回前台，
            //    放最后它自己那格才抢得到（它抢到时是好的，见 AltCase 的说明）——
            if (!CaseOk(AltCase, out string altDetail))
            {
                if (altDetail == "抢不到前台" || altDetail == "前台一直被抢走")
                    notes.Add($"Alt 那一格没验成（{altDetail}）：Alt 会把窗推进菜单模式，抢不回前台");
                else fails.Add($"{AltCase.Name}：{altDetail}");
            }
            else notes.Add($"{AltCase.Name} → {altDetail}");

            log = fails.Count == 0
                ? $"发键全对（{notes.Count} 种场景，目标程序收到的就是用户写下的那个键）"
                : string.Join("；", fails);
            return fails.Count == 0;
        }
        catch (Exception ex) { log = "发键自查抛了：" + ex.Message; return false; }
        finally { try { host.Close(); } catch { } }
    }

    // 按住一组修饰键（模拟「用户正按着」）：只按下，不抬起。
    private static void HoldKeys(ushort[] vks)
    {
        foreach (var v in vks) Win32.SendKeyDown(v);
    }

    // 抬起一组修饰键：**通用码与左右具体码都要抬**——
    // 抬起一个不会连带另一个（系统里它们是各自的状态位），漏一个就会留下一个
    // 「还按着」的修饰键，下一场景就被它带偏。
    private static void ReleaseKeys(ushort[] vks)
    {
        var list = new List<ushort>(vks);
        foreach (var v in vks)
        {
            if (v == VK_CONTROL) { list.Add(0xA2); list.Add(0xA3); }
            else if (v == VK_SHIFT) { list.Add(0xA0); list.Add(0xA1); }
            else if (v == VK_MENU) { list.Add(0xA4); list.Add(0xA5); }
        }
        Win32.ReleaseKeys(list.Distinct().ToArray());
    }

    // 目标窗必须在前台，注入才会落到它身上而不是用户正在用的程序上。
    // 每发一次之前都复核一遍：这条自查要跑好几秒，别的程序随时可能把前台抢走。
    //
    // **抢不到要重试几次**：前台锁认的是「谁刚被用户碰过」，别的程序随时可能
    // 把这个资格拿走——实测头一次常常抢不到，隔一下再抢就成了。
    private static bool TakeForeground(Window host, IntPtr hwnd)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (GetForegroundWindow() != hwnd)
            {
                ForegroundNudge.Activate(hwnd);
                host.Activate();
                Pump(host, 150);
            }
            if (GetForegroundWindow() == hwnd)
            {
                Trace("前台 是目标窗（第 " + (attempt + 1) + " 次）");
                return true;
            }
            Trace("前台 抢不到（第 " + (attempt + 1) + " 次）");
            Thread.Sleep(250);
        }
        Trace("前台 不是目标窗");
        return false;
    }

    // 给人看的那份账：键码 + 当时的修饰键 + 键名。
    private static string Show(IEnumerable<(ushort Vk, ModifierKeys Mods, string Name)> seen)
        => string.Join(",", seen.Select(s => $"0x{s.Vk:X2}/{s.Mods}({s.Name})"));

    // 走到哪一步了：**挂住的时候这一行就是线索**。
    // 这一条要抢前台、要往系统里注入键——挂在哪一步只有日志说了算。
    private static void Trace(string what)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "cw-keysend.log"),
                               $"{DateTime.Now:HH:mm:ss} {what}{Environment.NewLine}");
        }
        catch { }
    }

    // 单独跑这一条（--keysend）：结果写进 %TEMP%\cw-keysend.txt。
    public static void RunStandalone()
    {
        bool ok = Run(out var log, out bool skipped);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cw-keysend.txt"),
                           (skipped ? "SKIP " : ok ? "OK " : "FAIL ") + log);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // 把消息泵转一会儿：键消息排在窗口的队列里，泵不转它就永远躺在那儿。
    //
    // **不用 DispatcherTimer 定时器**：它排在 Background 档，而这条自查跑在
    // 自查链的后头——前面的检查（导航栏那族要建真窗口）会把调度器占上一阵子，
    // 定时器那一档排不进去，实测迟了 16 秒才响（前台早被别的东西抢走了）。
    // 改成后台线程到点往调度器里塞一条 **Normal 档**的操作：它比 Background 高一档，
    // 一定排得进去，顺带把等待中的调度器叫醒。
    private static void Pump(DispatcherObject o, int ms)
    {
        var frame = new DispatcherFrame();
        var dispatcher = o.Dispatcher;
        var t = new Thread(() =>
        {
            Thread.Sleep(ms);
            try { dispatcher.BeginInvoke(new Action(() => frame.Continue = false)); } catch { }
        }) { IsBackground = true, Name = "cw-keysend-pump" };
        t.Start();
        Dispatcher.PushFrame(frame);
    }
}
