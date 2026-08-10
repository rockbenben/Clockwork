using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Clockwork.Views;

// 把窗口高度收进它所在那块屏幕的工作区。
//
// 为什么不能只写死一个数：三个编辑器用的是 SizeToContent="Height"，高度完全由内容决定，
// 必须封顶，否则内容一多就长到屏幕外去——首当其冲的是「确定 / 取消」那一行，跑到任务栏底下点不着。
// 而写死的封顶值要同时伺候 1366×768 的小本（工作区仅约 728 DIP）和 4K 屏：原来的 780 / 840
// 在小本上已经超出屏幕，在大屏上又白白留着一半空间不用——内容明明放得下，却还在滚。
// 改成运行时按工作区算，两头都对：小屏收住、大屏放开。
//
// 用 MonitorFromWindow 而不是 SystemParameters.WorkArea：后者给的是「主屏」的工作区。
// 本程序声明了 PerMonitorV2（见 app.manifest），不存在「整个桌面一个缩放比例」这回事，
// 而对话框是 CenterOwner——主窗口在副屏时，拿主屏的尺寸去封顶就是错的。
// DPI 换算走 VisualTreeHelper.GetDpi(窗口)：它在 PerMonitorV2 下返回的正是该窗口所在那块屏的缩放，
// 不必自己去调 GetDpiForMonitor。
public static class WindowSizing
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO mi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>在 SourceInitialized 时把窗口高度封进当前屏幕工作区（留出 margin 作为上下呼吸）。
    /// 已设了 Height 的窗口（主窗口、动作组编辑器）若比工作区还高，也一并收回来。
    /// 拿不到显示器信息就什么都不做——退回 XAML 里那个保守的 MaxHeight，不该因为量不到屏幕就开不了窗。</summary>
    public static void FitToWorkArea(Window window, double margin = 72)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == 0) return;
                var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (mon == 0) return;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(mon, ref mi)) return;

                var scale = VisualTreeHelper.GetDpi(window).DpiScaleY;
                if (scale <= 0) return;
                double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / scale;
                double cap = workHeight - margin;
                // MinHeight 优先：窗口再怎么收也不能小到自己声明的可用下限之下（那是控件排得下的底线）。
                if (cap < window.MinHeight) cap = window.MinHeight;

                window.MaxHeight = cap;
                if (!double.IsNaN(window.Height) && window.Height > cap) window.Height = cap;
            }
            catch { }
        };
    }
}
