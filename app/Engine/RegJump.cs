using System.Diagnostics;
using System.Linq;
using System.Threading;
using Clockwork.I18n;
using Microsoft.Win32;

namespace Clockwork.Engine;

// 注册表一键定位：写好 LastKey 再拉起 regedit，让它打开时直接定位到目标键。
// Sysinternals RegJump 同款机制——regedit 启动时读
// HKCU\Software\Microsoft\Windows\CurrentVersion\Applets\RegEdit\LastKey 还原位置。
public static class RegJump
{
    private const string RegEditSubKey = @"Software\Microsoft\Windows\CurrentVersion\Applets\RegEdit";

    // normalizedKey：SmartOpen.NormalizeRegistryKey 产出的长名键（HKEY_LOCAL_MACHINE\…）。
    public static void Jump(string normalizedKey)
    {
        // regedit 是**单实例**。实测（Win11 26200，2026-09）：
        //   · 冷启动——LastKey 写裸 HKEY_ 形式，启动即定位，关闭时它把键回写成带「计算机\」前缀；
        //   · 已有实例——再启动只激活旧窗、**不会重新定位**；更糟的是旧实例退出时会用它自己的当前
        //     位置回写 LastKey，把我们写的值盖掉。
        // 所以顺序是承重的：先关旧实例并**等到真的退出**，再写值，再启动。先写后关等于没写。
        if (!TryCloseExisting())
        {
            // 关不掉：旧窗可能开着键名改名框（WM_CLOSE 被拦），或本进程完整性级别低于 regedit、
            // UIPI 把 WM_CLOSE 丢了（regedit 的清单是 requireAdministrator）。
            // 不杀进程——用户可能正在编辑键名。只拉起一次把旧窗带到前面，这次定位不了，如实让
            // 上面的命令报成警告总比一声不响强——故这里不写 LastKey（反正会被旧实例退出时盖掉）。
            StartRegEdit();
            throw new InvalidOperationException(Strings.Get("Err_RegEditBusy"));
        }

        using (var k = Registry.CurrentUser.CreateSubKey(RegEditSubKey))
            k.SetValue("LastKey", normalizedKey, RegistryValueKind.String);
        StartRegEdit();
    }

    // 温和关闭所有 regedit 实例（发 WM_CLOSE，不是杀进程）并等它们退出。
    // 没有实例（含关闭成功）返回 true；有实例但等了 2 秒还在返回 false。
    private static bool TryCloseExisting()
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName("regedit"); }
        catch { return true; }   // 连枚举都不让（极少见）：按冷启动走，真撞上单实例再说
        if (procs.Length == 0) return true;
        try
        {
            foreach (var p in procs)
            {
                try { _ = p.CloseMainWindow(); } catch { }   // 拒绝访问（低完整性→提权窗）等：下面按超时处理
            }
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(100);
                if (procs.All(p => { try { return p.HasExited; } catch { return true; } })) return true;
            }
            return false;
        }
        finally
        {
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { }
            }
        }
    }

    private static void StartRegEdit()
    {
        using var p = Process.Start(new ProcessStartInfo { FileName = "regedit.exe", UseShellExecute = true });
    }
}
