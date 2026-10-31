using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Clockwork.Views;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这几个名字两边都有（同别处那几行）。
using Button = System.Windows.Controls.Button;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
namespace Clockwork;

// 面板的按键：**面板认领的每一个键都真按一遍，按完看效果**。
//
// 事实经过（2026-10-30，用户报「发送按键 用 enter 根本就无效的，你把所有动作都检查下」）：
// WPF 的普通按钮不吃回车（回车只激活 IsDefault 的那颗，而一块面板上有几十颗），
// 于是「方向键挑中一颗、回车跑它」整条流程是空的——挑中的那颗高亮着，回车什么也不发生，
// 其余部分照常工作，看上去像「这一格坏了」。
//
// 那类缺陷**代码里看不出来**：case 都在、单测也绿，按下去没反应而已。
// 源头上钉不住的东西，就得真的按一遍：这一节只信效果——按完看哪颗跑了、焦点去了哪、窗关了没。
//
// 投递方式：把 WM_KEYDOWN / WM_KEYUP 投给面板那扇窗的 HWND，走 Windows 消息 → WPF 输入管线
// 那条真路（HwndSource → InputManager → KeyboardDevice → 焦点元素 → 窗口的 PreviewKeyDown）。
// 自己 RaiseEvent 造一个假事件也行，但那样验的是「处理器写没写对」，
// 不是「这个键到底会不会到达面板、焦点在不在它身上」——而用户报的正是后者。
internal static class PanelKeyChecks
{
    /// <summary>跑一遍面板的按键自查。返回是否全过；<paramref name="log"/> 是给人看的那份账。</summary>
    public static bool Run(out string log)
    {
        var fails = new List<string>();
        var hits = new List<string>();

        // 一个场景一块**新的**面板：格子跑起来会顺手把面板收掉（真面板就是这样——
        // 跑之前先关窗，好让按键打到用户原本在用的那个程序上），所以不能拿同一块连着按。
        void Scene(string name, Func<QuickPanelWindow, List<Button>, (bool ok, string got)> body,
                  int tileCount = 5, PanelLook? look = null)
        {
            var w = MakePanel(hits, tileCount, look);
            // 诊断：这一扇窗到底收到了哪些键。（挂在窗上的实例处理器与面板那个
            // OnPreviewKeyDown 是同一个事件的两拨处理器，谁先谁后不影响「收没收到」这件事。）
            var seen = new List<Key>();
            w.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => seen.Add(e.Key)));
            try
            {
                w.Show();
                // **不抢前台**：自查不该把用户的桌面抢走（托盘里那个正在用的实例多半是提权的，
                // 前台锁也不会让本进程把前台拿回来——实测那一等就是十几秒）。
                // 面板的按键处理**不需要**前台：它认的是事件自己的来源（见 OnPreviewKeyDown），
                // 所以窗口没在前台时按键照样送得到、焦点照样落得下。
                Pump(w, 80);
                var all = ButtonsOf(w).ToList();
                var tiles = all.Where(b => b.IsVisible && b.Tag is PanelAction).ToList();
                if (tiles.Count == 0)
                    fails.Add($"{name}：一颗格子都没有（窗={w.IsVisible} 按钮={all.Count} 挂动作的={all.Count(b => b.Tag is PanelAction)} 可见的={all.Count(b => b.IsVisible)}）");
                var (ok, got) = body(w, tiles);
                if (!ok) fails.Add($"{name}：{got}（收到的键=[{string.Join(",", seen)}]）");
            }
            catch (Exception ex) { fails.Add($"{name}：自查抛了 {ex.Message}"); }
            finally { try { w.Close(); } catch { } }
        }

        // ① 回车跑焦点所在的那一颗 —— 用户报的就是这一条。
        Scene("回车·跑焦点那一颗", (w, tiles) =>
        {
            if (tiles.Count < 2) return (false, $"第一页只有 {tiles.Count} 颗格子");
            hits.Clear();
            FocusTile(w, tiles[0]);
            Press(w, Key.Enter);
            Pump(w, 80);
            return (hits.Count == 1 && hits[0] == "1", $"跑了 [{string.Join(",", hits)}]，焦点在 {Where()}");
        });

        // ② 空格也还跑（那是 WPF 原生那条路，别被上面那条挡掉）。
        Scene("空格·跑焦点那一颗", (w, tiles) =>
        {
            if (tiles.Count < 2) return (false, $"第一页只有 {tiles.Count} 颗格子");
            hits.Clear();
            FocusTile(w, tiles[1]);
            Press(w, Key.Space);
            Pump(w, 80);
            return (hits.Count == 1 && hits[0] == "2", $"跑了 [{string.Join(",", hits)}]，焦点在 {Where()}");
        });

        // ③ 方向键在格子之间挪焦点：左右在行内，上下跨行。
        Scene("方向键·往右挪一格", (w, tiles) =>
        {
            if (tiles.Count < 2) return (false, $"第一页只有 {tiles.Count} 颗格子");
            hits.Clear();
            FocusTile(w, tiles[0]);
            Press(w, Key.Right);
            Pump(w, 80);
            return (tiles[1].IsFocused, $"焦点在 {Where()}");
        });

        Scene("方向键·往下挪一行", (w, tiles) =>
        {
            if (tiles.Count < 5) return (false, $"第一页只有 {tiles.Count} 颗格子（要排得下两行才验得了上下）");
            hits.Clear();
            if (!FocusTile(w, tiles[0])) return (false, "焦点没落到第一颗");
            Press(w, Key.Down);
            Pump(w, 120);
            return (tiles[4].IsFocused, $"焦点在第 {IndexOf(tiles)} 颗");
        });

        // ④ 焦点挪到哪颗，回车就跑哪颗——不是别的（曾经是搜索结果里的第一个：
        //    眼睛看着 A 高亮、跑起来却是另一个动作，而出厂页里就有锁屏与清空回收站）。
        Scene("回车·跑挪过去的那颗", (w, tiles) =>
        {
            if (tiles.Count < 2) return (false, $"第一页只有 {tiles.Count} 颗格子");
            hits.Clear();
            FocusTile(w, tiles[0]);
            Press(w, Key.Right);
            Pump(w, 80);
            Press(w, Key.Enter);
            Pump(w, 80);
            return (hits.Count == 1 && hits[0] == "2", $"跑了 [{string.Join(",", hits)}]，焦点在 {Where()}");
        });

        // ⑤ 翻页键换页（多页时），换完焦点落在新那页的第一颗。
        Scene("翻页·换到下一页", (w, tiles) =>
        {
            if (tiles.Count == 0) return (false, "第一页没有格子");
            hits.Clear();
            FocusTile(w, tiles[0]);
            Press(w, Key.PageDown);
            Pump(w, 80);
            var onPage2 = ButtonsOf(w).Where(b => b.IsVisible && b.Tag is PanelAction).ToList();
            if (onPage2.Count != 1) return (false, $"下一页有 {onPage2.Count} 颗（应为 1），焦点在 {Where()}");
            Press(w, Key.Enter);
            Pump(w, 80);
            return (hits.Count == 1 && hits[0] == "末", $"跑了 [{string.Join(",", hits)}]");
        });

        // ⑥ Esc 关掉面板（这是它唯一的退出保证）。
        Scene("Esc·关掉面板", (w, tiles) =>
        {
            if (tiles.Count == 0) return (false, "第一页没有格子");
            FocusTile(w, tiles[0]);
            Press(w, Key.Escape);
            Pump(w, 80);
            return (!w.IsVisible, "面板还开着");
        });

        // ⑦ 跑完格子，面板自己收掉（跑之前先关窗，好让按键打到用户原本在用的那个程序上）。
        Scene("跑完·面板收掉", (w, tiles) =>
        {
            if (tiles.Count == 0) return (false, "第一页没有格子");
            FocusTile(w, tiles[0]);
            Press(w, Key.Enter);
            Pump(w, 80);
            return (!w.IsVisible, "面板还开着");
        });

        // ⑧ 跨带：上带最后一行往下 = 下带第一行。两条带是两个 UniformGrid，
        //    WPF 的方向键导航在一条带里循环，跨带这一步得面板自己接。
        Scene("方向键·跨到下带", (w, tiles) =>
        {
            if (tiles.Count < 14) return (false, $"第一页只有 {tiles.Count} 颗（要 14 颗才排得下上带三行 + 下带）");
            FocusTile(w, tiles[11]);
            Press(w, Key.Down);
            Pump(w, 80);
            return (tiles[12].IsFocused, $"焦点在第 {IndexOf(tiles)} 颗");
        }, tileCount: 14);

        // ⑨ 搜索态：打字 → 回车跑第一个命中；方向键能往下挑，回车跑挑中的那颗。
        //
        // 搜索只在「动作多到一屏装不下」时才装（面板自己的规矩），所以这里把版面调小到一屏八颗——
        // 十四颗装不下，搜索才会出现。
        var tight = new PanelLook(Columns: 4, TopRows: 1, BottomRows: 1, ShowOps: false, TopTabs: false, LeftTabs: false);
        Scene("搜索·回车跑命中", (w, tiles) =>
        {
            hits.Clear();
            FocusTile(w, tiles[0]);
            Type(w, '1');
            Pump(w, 80);
            var shown = ButtonsOf(w).Count(b => b.IsVisible && b.Tag is PanelAction);
            if (shown == 0) return (false, "打字之后没有出结果");
            Press(w, Key.Enter);
            Pump(w, 80);
            return (hits.Count == 1, $"跑了 [{string.Join(",", hits)}]，结果 {shown} 颗");
        }, tileCount: 14, look: tight);

        Scene("搜索·方向键往下挑", (w, tiles) =>
        {
            hits.Clear();
            FocusTile(w, tiles[0]);
            Type(w, '1');
            Pump(w, 80);
            Press(w, Key.Down);      // 进结果区：落在第一颗命中
            Pump(w, 80);
            Press(w, Key.Down);      // 再往下：第二颗命中（旧写法每次都跳回第一颗）
            Pump(w, 80);
            Press(w, Key.Enter);     // 跑挑中的那颗
            Pump(w, 80);
            return (hits.Count == 1 && hits[0] != "1", $"跑了 [{string.Join(",", hits)}]（应当是第二颗命中，不是第一颗）");
        }, tileCount: 14, look: tight);

        log = fails.Count == 0 ? "面板按键全对" : string.Join("；", fails);
        return fails.Count == 0;
    }

    // 造一块面板：两页。甲页若干颗（回车 / 空格 / 方向键拿它们试），
    // 乙页一颗（翻页键拿它试）。
    //
    // 甲页默认五颗：面板默认四列，五颗才排得下两行——两行才验得到「上下」那个方向
    // （只有一颗时上下左右都没有落点，方向键按了等于没按）。
    private static QuickPanelWindow MakePanel(List<string> hits, int tileCount = 5, PanelLook? look = null)
    {
        var names = Enumerable.Range(1, tileCount).Select(i => i.ToString()).ToArray();
        var pages = new[]
        {
            new PanelTilePage("甲", names.Select(n => new PanelTile(n, "1", () => hits.Add(n))).ToArray()),
            new PanelTilePage("乙", new[] { new PanelTile("末", "2", () => hits.Add("末")) }),
        };
        var w = new QuickPanelWindow(pages, look: look ?? new PanelLook(ShowOps: false, TopTabs: false, LeftTabs: false));
        // 挪到屏幕外：面板认得 ShowActivated=false（见 PlaceAtCursor 那段），
        // 于是它不会追着光标跑回屏幕中央——自查期间桌面不受打扰。
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -32000; w.Top = -32000; w.ShowActivated = false;
        return w;
    }

    // 真的按一个键：把 WM_KEYDOWN / WM_KEYUP 投给面板那扇窗的 HWND。
    internal static void Press(Window w, Key key)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint scan = Native.Win32.ScanCode(vk);
        // lParam：bit0-15 扫描码，bit16-23 扩展键标志，bit24-31 按下次数。
        uint down = 1 | (scan << 16);
        uint up = down | (1u << 30) | (1u << 31);
        Native.Win32.PostKey(hwnd, Native.Win32.WM_KEYDOWN, (IntPtr)vk, (IntPtr)down);
        Native.Win32.PostKey(hwnd, Native.Win32.WM_KEYUP, (IntPtr)vk, (IntPtr)up);
    }

    // 真的打一个字：把 WM_CHAR 投给面板那扇窗的 HWND。
    // 打字进搜索框走的是这条（面板的 OnPreviewTextInput 靠它起搜索）。
    private static void Type(Window w, char ch)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        Native.Win32.PostKey(hwnd, WM_CHAR, (IntPtr)ch, (IntPtr)1);
    }

    private const uint WM_CHAR = 0x0102;

    // 把焦点真的落到某颗格子上，然后确认它落上了。
    //
    // **必须等**：`Focus()` 是排进队列的，窗口还没布局完时它可能落空——
    // 焦点没有收件人，按键就一个都送不到（实测偶发：整场一个键都没收到）。
    // 所以这里反复「聚焦 + 泵」，直到那颗真的持有焦点为止。
    private static bool FocusTile(Window w, Button b)
    {
        for (int i = 0; i < 20; i++)
        {
            b.Focus();
            Pump(w, 25);
            if (b.IsFocused) return true;
        }
        return b.IsFocused;
    }

    // 把消息泵转一会儿，让投进去的按键消息真的被处理掉。
    //
    // **必须走 DispatcherFrame 而不是 Sleep**：键消息排在窗口的消息队列里，
    // 消息循环不转它就永远躺在那儿——这正是「按了没反应」的那副样子。
    internal static void Pump(DispatcherObject o, int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, o.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    // 焦点此刻在第几颗（1 起；一颗都不在就是 0）。
    private static int IndexOf(List<Button> tiles)
    {
        for (int i = 0; i < tiles.Count; i++) if (tiles[i].IsFocused) return i + 1;
        return 0;
    }

    // 焦点此刻在谁身上（给人看的那份账：说清「没动」是「没动」还是「跑到别处去了」）。
    private static string Where()
    {
        var f = Keyboard.FocusedElement;
        if (f == null) return "(无)";
        if (f is Button b) return $"按钮「{LabelOf(b)}」";
        return $"{f.GetType().Name}";
    }

    // 按钮上的字：格子的内容是「图标 + 标签」两段，标签是后加的那个。
    private static string LabelOf(Button b)
    {
        var texts = new List<TextBlock>();
        CollectTexts(b, texts);
        return texts.Count > 0 ? texts[^1].Text : b.Content?.ToString() ?? "";
    }

    private static void CollectTexts(DependencyObject root, List<TextBlock> into)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock t) into.Add(t);
            else CollectTexts(c, into);
        }
    }

    // 视觉树里所有的按钮（窗格里那些控件要自己找：面板的格子区不是公开的）。
    private static IEnumerable<Button> ButtonsOf(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Button b) yield return b;
            foreach (var x in ButtonsOf(c)) yield return x;
        }
    }
}
