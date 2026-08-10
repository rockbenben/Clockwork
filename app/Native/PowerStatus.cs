using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 交流电 / 电池状态（GetSystemPowerStatus）。步骤条件「仅接电源 / 仅用电池」与
// 「插上电源 / 拔掉电源 / 电量偏低」三个事件触发共用这一处读数，别在两边各写一份口径。
public static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0=电池 1=接电源 255=未知
        public byte BatteryFlag;
        public byte BatteryLifePercent;  // 0..100，255=未知
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // (是否接着电源, 电量百分比)。读不到或未知一律按「接着电源、电量未知(-1)」：
    // 台式机根本没有电池，把未知当成「用电池」会让「仅接电源时」的步骤在台式机上永远不执行——
    // 那是最容易被当成「功能坏了」的失败方向。电量 -1 则让「电量偏低」触发永不成立（同理，宁可不触发）。
    public static (bool OnAc, int Percent) Read()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s)) return (true, -1);
            bool onAc = s.ACLineStatus != 0;   // 1=在线、255=未知都算接着电源
            int pct = s.BatteryLifePercent > 100 ? -1 : s.BatteryLifePercent;
            return (onAc, pct);
        }
        catch { return (true, -1); }
    }

    public static bool OnAc() => Read().OnAc;
}
