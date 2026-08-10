using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Clockwork.Engine;

// 系统命令派发。破坏性命令（注销/重启/关机）经注入的 confirmDestructive 回调门控。
public static class SystemCommands
{
    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    private static void Start(string file, string? args = null, bool useShell = false)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = useShell };
        if (args != null) psi.Arguments = args;
        Process.Start(psi);
    }

    // text/level：只有少数命令带参数（setClipboard 用 text、brightness 用 level），
    // 从 LaunchStep 的既有字段直接借过来，不为两条命令给模型再加两个字段。
    public static void Invoke(string command, Func<string, bool> confirmDestructive, string text = "", int level = 0)
    {
        switch (command)
        {
            case "showDesktop":
            {
                // 原生 Shell COM（等价 Win+D，不注入按键、结果可信）；COM 不可用或失败都退回模拟按键。
                // ShellApp() 在 ProgID 未注册时返回 null——?. 会短路成「什么都不做」且不抛，故不能只靠 catch 兜底。
                bool toggled = false;
                try { var sh = ShellApp(); if (sh != null) { sh.ToggleDesktop(); toggled = true; } } catch { }
                if (!toggled) KeyInput.SendKeyCombo("Win+D");
                break;
            }
            case "lockScreen": Start("rundll32.exe", "user32.dll,LockWorkStation"); break;
            case "taskManager": Start("taskmgr.exe"); break;
            case "clearClipboard":
                // WinForms Clipboard 要求 STA 线程，而所有执行路径（开机序列/单步/动作组）都在 MTA 线程池上——
                // 直接调必抛 ThreadStateException，故挪到专用 STA 线程同步执行。
                try { RunSta(() => WinForms.Clipboard.Clear()); }
                catch { try { RunSta(() => WinForms.Clipboard.SetText(" ")); } catch (Exception ex) { throw new InvalidOperationException(Strings.Lf("Err_ClearClipboard", ex.Message)); } }
                break;
            case "monitorOff":
                // HWND_BROADCAST(0xFFFF) WM_SYSCOMMAND(0x0112) SC_MONITORPOWER(0xF170) 2=关。
                Win32.PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2);
                break;
            case "hibernate": Start("shutdown.exe", "/h"); break;
            case "signOut": if (confirmDestructive(Strings.Get("Sys_signOut"))) Start("shutdown.exe", "/l"); break;
            case "restart": if (confirmDestructive(Strings.Get("Sys_restart"))) Start("shutdown.exe", "/r /t 0"); break;
            case "shutdown": if (confirmDestructive(Strings.Get("Sys_shutdown"))) Start("shutdown.exe", "/s /t 0"); break;
            case "emptyRecycleBin":
                // 先数条目：查询成功且为空→静默跳过（本就无事，避免「已空」误报）；非空→清。
                // 但查询失败（某些盘符/权限下返回非零 HRESULT）时不能假装成功——照旧尝试清（对空桶清也是无害 no-op）。
                try
                {
                    var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                    bool queriedEmpty = SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems <= 0;
                    if (!queriedEmpty)
                        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                }
                catch (Exception ex) { throw new InvalidOperationException(Strings.Lf("Err_EmptyRecycleBin", ex.Message)); }
                break;
            case "openSettings": Start("ms-settings:", useShell: true); break;
            case "screenshot":
                // 原生截图协议（Win10 1809+/Win11），不注入按键；协议缺失才退回 Win+Shift+S。
                try { Start("ms-screenclip:", useShell: true); } catch { KeyInput.SendKeyCombo("Win+Shift+S"); }
                break;
            case "sleep":
                // rundll32 无法传类型化参数，会在开启休眠的机器上误休眠——用 .NET 明确指定 Suspend；失败才退回旧方式。
                try { WinForms.Application.SetSuspendState(WinForms.PowerState.Suspend, false, false); }
                catch { Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); }
                break;
            // 显示器模式：系统自带的 DisplaySwitch.exe，和按 Win+P 选一项完全等价，不注入按键。
            case "displayInternal": Start("DisplaySwitch.exe", "/internal"); break;
            case "displayClone": Start("DisplaySwitch.exe", "/clone"); break;
            case "displayExtend": Start("DisplaySwitch.exe", "/extend"); break;
            case "displayExternal": Start("DisplaySwitch.exe", "/external"); break;
            case "setClipboard": SetClipboard(text ?? ""); break;
            case "notificationsOff": SetNotifications(false); break;
            case "notificationsOn": SetNotifications(true); break;
            case "brightness": SetBrightness(level); break;
            default: throw new InvalidOperationException(Strings.Lf("Err_UnknownSysCmd", command));
        }
    }

    // 往剪贴板放一段固定文本（常用地址 / 话术 / 模板）。空文本=清空，与 clearClipboard 同效——
    // 把空串写进剪贴板会留下一个「空但存在」的项，不如直接清掉。
    // Clipboard 与 clearClipboard 一样要 STA 线程（所有执行路径都在线程池的 MTA 上），走同一个 RunSta。
    private static void SetClipboard(string text)
    {
        try { RunSta(() => { if (text.Length == 0) WinForms.Clipboard.Clear(); else WinForms.Clipboard.SetText(text); }); }
        catch (Exception ex) { throw Fail("setClipboard", ex.Message); }
    }

    // 通知总开关。注册表有两处，两处都写：
    //   PushNotifications\ToastEnabled                      —— 设置里「从应用和其他发送者获取通知」，决定 toast 弹不弹；
    //   Notifications\Settings\NOC_GLOBAL_SETTING_TOASTS_ENABLED —— 通知中心那一层的全局开关。
    // 只写其一在部分 Windows 版本上不生效（而且哪一处生效随版本变），两处都写才稳；两处都是当前用户的键，
    // 不需要管理员。立即对之后弹出的通知生效，已在屏上的那条不会被收回。
    //
    // 这不是 Win11 的「专注 / 勿扰」——那套没有公开可写的开关（WNF 状态，未文档化）。此处关的是通知本身，
    // 效果更彻底：勿扰只是攒起来，这个是根本不弹。别忘了配一条 notificationsOn 收尾，否则会一直静着。
    private static void SetNotifications(bool on)
    {
        int v = on ? 1 : 0;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
                k?.SetValue("ToastEnabled", v, RegistryValueKind.DWord);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                k?.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", v, RegistryValueKind.DWord);
        }
        catch (Exception ex) { throw Fail(on ? "notificationsOn" : "notificationsOff", ex.Message); }
    }

    // 屏幕亮度。唯一的路是 WMI 的 WmiMonitorBrightnessMethods，而 System.Management 在 .NET 上是个要单独装的包——
    // 为一个滑杆让自包含发布多背一个依赖不划算，改由系统自带的 powershell.exe 现调。
    // ponytail: 起一次 powershell ≈ 0.5–1 秒；真嫌慢再换 System.Management。
    // 必须走 Invoke-CimMethod：第一版写的是 (Get-CimInstance ...).WmiSetBrightness(1,p)，
    // 而 CimInstance 根本不携带 WMI 方法，那一版在所有机器上（含支持的笔记本）都必然失败（评审 #1 实跑证实）。
    // 两段都挂 -ErrorAction Stop：Get-CimInstance 的「不支持」是非终止错误，不升级的话
    // 管道空转、退出码 0——台式机上会把「什么都没调」谎报成功。
    // 只对「由系统驱动的显示器」有效：笔记本内屏、部分一体机。外接显示器走 DDC/CI，这条路够不着——
    // 那种机器上会如实报错，不假装成功。
    private static void SetBrightness(int percent)
    {
        int p = Math.Clamp(percent, 0, 100);
        try
        {
            string cmd = "$i = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop; " +
                         "$i | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{Timeout=1;Brightness=" + p + "} -ErrorAction Stop | Out-Null";
            var psi = new ProcessStartInfo
            {
                FileName = LaunchTarget.PowerShellExe,
                Arguments = "-NoProfile -NonInteractive -Command \"" + cmd + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("powershell");
            // 等一小会儿只为拿到退出码：不支持的显示器上 WMI 类是空的，PowerShell 会以非 0 退出，
            // 那正是要如实报给用户的那条。超时不算失败（慢机器上别把成功报成失败），进程留着自己跑完。
            if (proc.WaitForExit(4000) && proc.ExitCode != 0) throw new InvalidOperationException("exit " + proc.ExitCode);
        }
        catch (Exception ex) { throw Fail("brightness", ex.Message); }
    }

    // 命令失败的统一措辞：命令名用本地化标签（用户在下拉里看到的那个），后面接系统给的原文。
    private static InvalidOperationException Fail(string command, string detail)
        => new(Strings.Lf("Err_SysCommand", Strings.Get("Sys_" + command), detail));

    private static dynamic? ShellApp()
    {
        var t = Type.GetTypeFromProgID("Shell.Application");
        return t == null ? null : Activator.CreateInstance(t);
    }

    // 在专用 STA 线程上同步跑 action（剪贴板等 OLE 依赖 STA）；异常原栈重抛给调用方。
    private static void RunSta(Action action)
    {
        Exception? err = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (err != null) ExceptionDispatchInfo.Capture(err).Throw();
    }
}
