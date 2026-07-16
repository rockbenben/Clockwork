namespace Clockwork.Core;

// 系统开机至今的分钟数（供「登录时」提醒门控用），异常回退 0。
// TickCount64 是 64 位毫秒、从不回绕：早期版本沿用 PS5.1 的「取低 32 位」写法，反把它掩成 ~49.7 天就回绕，
// 导致开机超 49.7 天后中途重启程序被误判成「刚登录」。改为直接换算，不再掩位。
public static class SystemInfo
{
    // 纯函数，便于测试：毫秒 / 60000。负/越界防御回退（正常开机时长远在 int 分钟范围内）。
    public static int UptimeMinutesFromTicks(long tickCount) => tickCount <= 0 ? 0 : (int)Math.Min(tickCount / 60000L, int.MaxValue);

    public static int UptimeMinutes()
    {
        try { return UptimeMinutesFromTicks(Environment.TickCount64); }
        catch { return 0; }
    }
}
