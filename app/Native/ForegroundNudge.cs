using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clockwork.Native;

// 「把某个窗口抢到前台」的专用泵线程。
//
// **为什么不能就在调用线程上做。** Win32.ForceForeground 被前台锁拒绝之后，补救手段是
// AttachThreadInput —— 把**调用线程**的输入队列挂到当前前台线程上。挂上之后
// SetForegroundWindow 内部投递的那些同步消息（WM_ACTIVATE / WM_KILLFOCUS 等）要由调用线程
// 取消息才能消化，所以调用线程**必须有消息泵**。
//
// **为什么也不能在 UI 线程上做。** 原先的做法正是把它切到 UI 线程（UI 线程当然有泵），但那是
// **跨进程同步**调用：目标程序一忙（在加载、在渲染、或自己卡着），UI 线程就跟着一起等。
// 更坏的一档是 SetForegroundWindow 干脆不返回——那时 detach 那句永远不会执行，UI 线程就
// **永久挂在别人的输入队列上**，两边一起不泵。WindowManager 那边的注释里写着这条路曾经
// 「前台线程跟着一起卡死，DWM 重启才能解开」，说的就是它。
// 而 UI 线程卡过 5 秒，DWM 就会在全屏笔迹窗上盖一块点得中的幽灵窗（见 Win32.DisableProcessWindowsGhosting）。
//
// **真正的需求是「一条有消息泵的线程」，不是「UI 线程」。** 所以给它一条专属线程：
//
//   · 它只做这一件事。卡住也只卡它自己，UI 线程照常跑（笔迹窗该藏就藏，幽灵化不成立）。
//   · 调用方**带超时**地等（默认 1200ms，低于 Core.UiStallWatch 的 1500ms 卡顿阈值，
//     也远低于幽灵化的 5 秒）。超时就算没抢到。
//   · 抢不到是可恢复的：所有调用方本来就处理 false，而且窗口管理那条路还会在 120ms 后
//     自己复核一次前台（见 WindowManager.SetForeground）。
//   · 于是「永久挂接」这件事最多只发生在这一条牺牲线程上，不会再拖住整个程序。
//
// 不把它做成「一次一条线程」：抢前台是个高频动作（每个窗口动作都走），起线程比干活还贵。
// 也不复用钩子线程：那条线程有 300ms 的 LowLevelHooksTimeout 预算，绝不能被跨进程调用占住。
public static class ForegroundNudge
{
    /// <summary>调用方最多等这么久。**必须低于 Core.UiStallWatch 的 1500ms 卡顿阈值**——
    /// 否则这条路上的等待会自己变成一次「UI 线程卡顿」的告警来源，那就本末倒置了。</summary>
    public const int DefaultTimeoutMs = 1200;

    // 线程消息号。线程消息只投给本线程，与 MouseHook 那条线程上的号不会撞。
    private const int WM_NUDGE = 0x8000 + 0x51;   // WM_APP + 0x51
    private const uint PM_NOREMOVE = 0;

    private sealed class Request
    {
        public IntPtr Hwnd;
        public bool Result;
        // 不 Dispose：调用方超时走人之后，泵线程仍会 Set 它。多留一个句柄到 GC 为止，
        // 换掉一整类「谁先走」的竞态——而抢前台是低频动作，这点句柄压力可以忽略。
        public readonly ManualResetEventSlim Done = new(false);
    }

    private static readonly ConcurrentQueue<Request> _queue = new();
    private static readonly ManualResetEventSlim _ready = new(false);
    private static readonly object _startLock = new();
    // **这一格就是「泵还活着吗」**，由泵线程自己在 finally 里清零（见 PumpThread）。
    // 不另设一个 `_pump` 引用去记：那个引用得由 EnsurePump 在线程起来之后再赋值，
    // 于是「线程刚 Set 就死了」和「赋值晚于 finally」之间又多一扇错位的窗。
    private static uint _pumpThreadId;

    /// <summary>把窗口抢到前台。任何线程都可以调，永不无界阻塞。</summary>
    //
    // 返回值是「抢到了吗」。注意它可能因为**超时**而为 false——那时泵线程还在干活，
    // 结果只是没人要了。这与「被前台锁拒绝」在调用方眼里是同一件事，处理方式也一样。
    public static bool Activate(IntPtr hwnd, int timeoutMs = DefaultTimeoutMs)
    {
        if (hwnd == IntPtr.Zero) return false;
        // 已经在泵线程上：就地做。派给自己会等成死锁（那个 Wait 永远等不到 Set）。
        // 生产里到不了这儿，但留一条正确的退路比留一个隐含假设便宜。
        if (GetCurrentThreadId() == _pumpThreadId) return Win32.ForceForeground(hwnd);
        if (!EnsurePump()) return false;

        var req = new Request { Hwnd = hwnd };
        _queue.Enqueue(req);
        // 投递失败（线程刚好没了）时不清理队列：那一条会被下一次投递顺手做掉（Done 照 Set，
        // 只是没人等），最坏是留一个小对象到进程结束。为此加一层「按对象出队」的机制不值当。
        if (!PostThreadMessage(_pumpThreadId, WM_NUDGE, IntPtr.Zero, IntPtr.Zero)) return false;
        // 计时归到调用方这一侧：这条等待**正是**调用方（可能是 UI 线程）被按住的那一段，
        // 而里面那次 ForceForeground 由它自己那条探针记（会标成 thread=bg）。
        // 两条一起看才能分清「是目标程序慢」还是「是我们的调度慢」。
        long t0 = Core.UiStallWatch.Begin();
        bool signalled = req.Done.Wait(timeoutMs);
        Core.UiStallWatch.End("foreground-wait", t0);
        return signalled && req.Result;
    }

    private static bool EnsurePump()
    {
        if (_pumpThreadId != 0) return true;
        lock (_startLock)
        {
            if (_pumpThreadId != 0) return true;
            _ready.Reset();
            new Thread(PumpThread) { IsBackground = true, Name = "Clockwork.Foreground" }.Start();
            // 等它把消息队列建出来（PostThreadMessage 要求目标线程已有队列，见 PumpThread）。
            // 起一条线程没有任何正当理由超过 2 秒；等不到就当这条路这次不可用，下次再试。
            // 之后再看一次 id：线程可能 Set 完立刻就退了，那一格由它的 finally 清零。
            return _ready.Wait(2000) && _pumpThreadId != 0;
        }
    }

    // 泵线程的一生：建队列 → 报就绪 → 取消息 → 把攒下的请求逐个做掉。
    private static void PumpThread()
    {
        try
        {
            // 先碰一次消息 API，把本线程的消息队列**建出来**。没有队列时 PostThreadMessage 会失败，
            // 而队列是惰性创建的——不先戳一下，EnsurePump 报就绪之后紧接着的那次投递就可能落空。
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _pumpThreadId = GetCurrentThreadId();
            _ready.Set();

            while (true)
            {
                MSG msg;
                int r;
                try { r = GetMessage(out msg, IntPtr.Zero, 0, 0); }
                catch { return; }                 // 取消息本身出错：这条线程没救了，让它退（finally 会清账）
                if (r <= 0) return;               // WM_QUIT → 0；出错 → -1，都收摊
                if (msg.message != WM_NUDGE) continue;   // 本线程只认这一个号
                // 一次消息把当时攒下的都做掉：多个调用方几乎同时来的时候每个请求各投一次消息，
                // 这里不必为每条消息只做一件。
                while (_queue.TryDequeue(out var req))
                {
                    try { req.Result = Win32.ForceForeground(req.Hwnd); }
                    catch { req.Result = false; }
                    // 调用方可能已经超时走了，而这个事件不会被 Dispose（见 Request 的注释）。
                    try { req.Done.Set(); } catch { }
                }
            }
        }
        finally
        {
            // 线程退出了就把账清掉，让下一次调用重新起一条——而不是继续往一个死线程上投消息，
            // 那样表现只是「面板偶尔抢不到焦点」，查起来会非常远。
            _pumpThreadId = 0;
            _ready.Reset();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
