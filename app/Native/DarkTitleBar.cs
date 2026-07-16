using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Clockwork.Native;

// 让窗口的系统标题栏也转为深色（Win10 2004+/Win11），与蓝钢暗色界面一致。老系统上静默无效。
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;   // 20 适用于 Win10 20H1+/Win11；旧的 19 由系统忽略

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0) return;   // 须在 SourceInitialized 之后调用（句柄已建）
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }
        catch { }
    }
}
