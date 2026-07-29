using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 全局热键注册（急停键）。fsModifiers: Alt=1 Ctrl=2 Shift=4 Win=8。
public static class HotKey
{
    public const int WM_HOTKEY = 0x0312;

    // 按住不放时不重复投递 WM_HOTKEY（Win7+）。组热键是开关（按一次跑、再按一次取消），没有它的话
    // 键盘自动重复会以重复速率来回翻转：跑起来→取消→跑起来…，一次长按能把「收工」这类组跑上两三遍
    // （Alt+F4、锁屏各来一轮），松手时停在哪一态还是随机的。急停键也加：重复投递只会刷屏气泡。
    // 只在 RegisterHotKey 调用点按位或上去，绝不能并进 KeyInput.ToHotkeyParams——HotkeyCapture.IsReserved
    // 拿 Modifiers 与裸掩码（Alt=1/Ctrl=2/Shift=4）比对判系统保留组合，混进 0x4000 会让那道拦截全部失效。
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 句柄是否仍是有效窗口。退出时主窗 HWND 已销毁但缓存的句柄仍非零——注册路径据此跳过，
    // 避免在死句柄上 RegisterHotKey 失败、又弹「注册失败」气泡。
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
}
