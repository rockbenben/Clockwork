using Microsoft.Win32;

namespace Clockwork.Native;

// 系统主题偏好。单独一个文件是因为它要读注册表，而 Core 不碰 Win32
// （CONTRIBUTING：`app/Core/` = Pure logic — no Win32, no UI）。
// 判据本身在 Core.Themes.IsDark 里，那边收一个 bool，因而测得动。
public static class SystemTheme
{
    /// <summary>Windows 的「应用模式」是不是浅色。读不到就当深色——
    /// 这个程序的本色是深色，拿不准时回到本色，而不是把界面翻成白的。</summary>
    public static bool PrefersLight()
    {
        try
        {
            // AppsUseLightTheme 管的是应用界面，SystemUsesLightTheme 管的是任务栏/开始菜单。
            // 我们画的是应用界面，所以读前者——两者可以不一致，用户完全可能要「浅色应用 + 深色任务栏」。
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }
}
