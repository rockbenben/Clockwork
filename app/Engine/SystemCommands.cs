using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Clockwork.I18n;
using Clockwork.Native;
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

    public static void Invoke(string command, Func<string, bool> confirmDestructive)
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
            default: throw new InvalidOperationException(Strings.Lf("Err_UnknownSysCmd", command));
        }
    }

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
