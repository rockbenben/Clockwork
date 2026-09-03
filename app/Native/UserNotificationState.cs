using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 「此刻能不能打断人」——问 Windows 自己的那把尺子。
//
// 系统维护着一个全局的通知状态（QUERY_USER_NOTIFICATION_STATE）：全屏游戏、投影演示、
// 全屏商店应用都会把它翻到「别打断」。自己去枚举窗口判断「是不是全屏」要处理多显示器、
// 任务栏自动隐藏、无边框全屏 vs 独占全屏，还判不出「正在投影」——而这个 API 正是
// Windows 通知中心自己用的那一个，判据与系统一致，不必再造一把尺子。
public static class UserNotificationState
{
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const int Busy_ = 2;                  // QUNS_BUSY：全屏应用在跑
    private const int RunningD3DFullScreen = 3;   // 独占全屏（游戏）
    private const int PresentationMode = 4;       // 演示模式：正在投影
    private const int App = 7;                    // 全屏运行的商店应用

    // 刻意不含的两个值，都是「看着该算、其实不该算」：
    //   QUNS_NOT_PRESENT(1) —— 人不在（锁屏 / 屏保）。锁屏语义已经由 lock/unlock 两个事件触发
    //     各自管着，再让它顺带静音全部提醒，等于给锁屏偷加一层没人配过的勿扰。
    //   QUNS_QUIET_TIME(6) —— 专注助手 / 安静时段。本程序有自己的勿扰（托盘可开、有剩余时间、
    //     会自动恢复），把 Windows 那套按规则自动开关的状态也接进来，用户会发现提醒莫名不响
    //     而托盘上的勿扰明明没开——两套勿扰互相看不见对方，是最难自查的一种哑火。
    // 同理，判据写成「命中这几个值」而不是「!= QUNS_ACCEPTS_NOTIFICATIONS」：日后微软新增的
    // 状态值默认落到「可以打断」，宁可多弹一次，也别让提醒在一个没人见过的状态里永久静音。
    public static bool Busy()
    {
        try
        {
            // 非 0 = 调用失败，state 不可信，按「可以打断」处理。
            if (SHQueryUserNotificationState(out int s) != 0) return false;
            return s is Busy_ or RunningD3DFullScreen or PresentationMode or App;
        }
        catch { return false; }   // 老系统 / 受限令牌 / shell 未就绪：探测失败绝不能把提醒整体静音
    }
}
