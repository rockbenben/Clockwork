using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 全局热键注册（急停键）。fsModifiers: Alt=1 Ctrl=2 Shift=4 Win=8。
public static class HotKey
{
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 句柄是否仍是有效窗口。退出时主窗 HWND 已销毁但缓存的句柄仍非零——注册路径据此跳过，
    // 避免在死句柄上 RegisterHotKey 失败、又弹「注册失败」气泡。
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
}
