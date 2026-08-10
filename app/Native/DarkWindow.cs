using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Clockwork.Native;

// 窗口外观的两件事，在构造函数里调一次 Apply(this) 即可（本类自己挂事件）：
//   1. 系统标题栏转深色（DWM，Win10 2004+/Win11）；
//   2. 消灭开窗时的白闪。
//
// 白闪的根因（走过两条弯路，写在这儿免得再走一遍）：
//   ✗ 不是「窗口出现得太早」。ShowWindow 与 WPF 首帧之间那段空档消不掉，开机自启走 --boot 时窗口
//     从没渲染过，那一下尤其长（要现场解析整份 Theme.xaml 并实例化 DataGrid）。
//   ✗ 也不是「系统拿白刷子擦了客户区」。实测 WPF 给每个窗口注册独立的类 HwndWrapper[Clockwork;;<guid>]，
//     它的 hbrBackground 是库存 NULL_BRUSH（GetObject 出来 lbStyle=BS_NULL），系统压根不擦。
//     给类换一把 Ink 实心刷试过——白照旧。同项目 023-QuickText 里 WM_ERASEBKGND 钩子也被证伪过，
//     原因相同：白是 DWM 对这块 surface 的首次合成，发生在任何擦除周期之前，两种做法都够不着。
//   ✓ 真正管用的是把窗口从合成里摘出去：DWMWA_CLOAK。这正是 DWM 自己用来藏「非当前虚拟桌面上的窗口」
//     的那个标志。SourceInitialized 时置上，ContentRendered（首帧已提交）时摘掉，
//     于是白的那一段变成「什么都没有」，窗口一出现就是画好的。
//
// 不额外增加延迟：空档的长度没变，只是不再拿白色去填它。
public static class DarkWindow
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;            // Win10 20H1 (build 18985)+/Win11
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1 = 19;    // 20H1 之前同一开关的旧编号——那些系统上 20 会被拒绝（返回错误码，不抛），不兜就是深色应用配白标题栏
    private const int DWMWA_CLOAK = 13;

    public static void Apply(Window window)
    {
        nint Handle() => new WindowInteropHelper(window).Handle;
        void Cloak(int on) { var h = Handle(); if (h != 0) Set(h, DWMWA_CLOAK, on); }

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = Handle();
            if (hwnd == 0) return;
            if (Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1) != 0)
                Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1, 1);
            Set(hwnd, DWMWA_CLOAK, 1);
        };

        // 每次「隐藏之后」重新 cloak，为下一次显示做准备。
        // 主窗口是单例：关窗只是隐到托盘，对象一直活着。只在 SourceInitialized cloak 一次的话，
        // 只有开机后第一次显示不闪，之后每一次「托盘双击 → Show()」都要重新渲染一遍，白闪照旧回来。
        // （QuickText 那边每个窗口都是现开现建，走不到这个分支，所以它只 cloak 一次就够。）
        // 隐藏态的窗口不参与合成，此时 cloak 不会有任何可见效果。
        window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) { Cloak(1); return; }
            // 兜底：正常情况下由下面的 ContentRendered 摘掉 cloak；万一某次显示它没触发，
            // 一个卡在 cloak 里的窗口就是「点了托盘没反应」的隐形应用——那比早摘一帧、闪一下糟得多。
            // ContextIdle 排在 Render / Loaded 之后，正常路径上它跑到时 ContentRendered 早已把 cloak 摘了，
            // 这一次就是空操作。
            window.Dispatcher.BeginInvoke(() => Cloak(0), DispatcherPriority.ContextIdle);
        };

        // 首帧已提交，交还给 DWM。对没被 cloak 的窗口取消 cloak 是空操作。
        window.ContentRendered += (_, _) => Cloak(0);
    }

    // 老系统 / 受限令牌下静默无效：外观降级可以接受，开窗失败不行。
    // cloak 尤其要兜住——它设不上的后果只是白闪照旧（回到修之前），不是窗口消失。
    // 返回 HRESULT 让调用方能对「编号不被认识」做降级（深色标题栏 20 → 19）；异常按失败报。
    private static int Set(nint hwnd, int attr, int value)
    {
        try { return DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); } catch { return -1; }
    }
}
