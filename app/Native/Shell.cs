using System.Runtime.InteropServices;

namespace Clockwork.Native;

// Shell 相关 P/Invoke（AUMID 声明）。
public static class Shell
{
    // 声明进程显式 AppUserModelID：通知平台按此值缓存「应用归属」显示名，须稳定唯一。
    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);
}
