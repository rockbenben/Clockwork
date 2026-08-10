using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 距上次键鼠输入过了多久（GetLastInputInfo），供「空闲 N 分钟」触发用。
public static class IdleTime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // 纯函数便于测试：两个都是「开机以来的毫秒数」低 32 位，约 49.7 天回绕一次。
    // 必须用 uint 相减——回绕点两侧的差值在无符号算术下依然正确，换成有符号会算出一个巨大的负数。
    public static int MinutesFrom(uint nowTick, uint lastInputTick) => (int)((nowTick - lastInputTick) / 60000u);

    // 空闲分钟数；取不到返回 0（当作「刚刚还有人在操作」——宁可不触发，也不凭空触发一次）。
    // 注意：锁屏 / 切到别的用户会话时本函数只反映当前会话，别指望它能代表「这台机器没人用」。
    public static int Minutes()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref lii)) return 0;
            return MinutesFrom((uint)Environment.TickCount, lii.dwTime);
        }
        catch { return 0; }
    }
}
