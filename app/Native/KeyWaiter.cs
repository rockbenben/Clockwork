using System.Diagnostics;

namespace Clockwork.Native;

/// <summary>
/// 等用户按下某个键。轮询全局键态，**不装钩子**。
///
/// 为什么轮询而不是 SetWindowsHookEx(WH_KEYBOARD_LL)：
///
/// · **沙箱/降权下照样能读。** 钩子要注入到别的进程里读键盘流，提权与否差别很大；
///   GetAsyncKeyState 只是**读**一个全局状态位，不注入任何东西，受限令牌读得到
///   （读不到时按「没按」处理，与 Win32 里 ShiftHeld 同一口径）。
/// · **不抢钩子链。** 本程序已经有鼠标钩子在链上（MouseHook），再加一个键盘钩子意味着
///   别人的软件（WGestures / StrokesPlus / Quicker）可能把事件先吃掉，我们根本收不到。
///   轮询没有这个问题——状态位是全系统共用的，谁都改不了。
/// · **回调不跨进程、不会因为目标窗口挂起而卡住。** 钩子回调里一旦抛异常，
///   低级钩子会被系统摘掉，而摘掉之后**没有任何迹象**——表现就是「手势偶尔不灵了」。
///
/// 代价是最多一个轮询间隔的延迟（15ms，人感知不到），以及**等不到自己刚发出去的那一下**
/// ——所以每次开始等之前会先把当前按着的键等松开（见 Wait 开头那段）。
/// </summary>
public static class KeyWaiter
{
    private const int PollMs = 15;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// 等到 <paramref name="vks"/> 里的任意一个被按下（或到点/被取消）。
    /// 返回按下的那个虚拟键码；超时或取消返回 null。
    /// </summary>
    /// <param name="vks">可接受的虚拟键码。</param>
    /// <param name="timeoutMs">0 = 不限时；其余按毫秒。</param>
    /// <param name="cancel">取消闸；为 null 只认全局急停。</param>
    public static int? Wait(int[] vks, int timeoutMs, Core.RunCancel? cancel)
    {
        if (vks.Length == 0) return null;

        // **先等松开。** 触发这一步的那个键（热键、手势组合）此刻多半还按着，
        // 而状态位只要「按着」就一直为真 —— 不先松开的话，等到的第一下永远是触发键本身，
        // 不是用户想按的那个。这一步同时消掉了「刚 SendInput 发出去的键被自己等回来」。
        var deadline = timeoutMs > 0 ? Stopwatch.StartNew() : null;
        var limit = timeoutMs > 0 ? timeoutMs : int.MaxValue;
        var allDown = true;
        while (allDown)
        {
            allDown = false;
            foreach (var vk in vks)
            {
                if (!Down(vk)) { allDown = false; break; }
            }
            if (!allDown) break;
            if (HasCancelled(cancel) || (deadline != null && deadline.ElapsedMilliseconds > 300)) break;
            System.Threading.Thread.Sleep(PollMs);
        }

        deadline = timeoutMs > 0 ? Stopwatch.StartNew() : null;
        while (true)
        {
            foreach (var vk in vks) if (Down(vk)) return vk;
            if (HasCancelled(cancel)) return null;
            if (deadline != null && deadline.ElapsedMilliseconds > limit) return null;
            System.Threading.Thread.Sleep(PollMs);
        }
    }

    private static bool HasCancelled(Core.RunCancel? cancel)
        => (cancel?.IsStopped ?? false) || Core.StopSignal.IsRequested;

    private static bool Down(int vk)
    {
        try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        catch { return false; }   // 取不到按「没按」：逃生口探测失败不该让这一步永远挂着
    }
}
