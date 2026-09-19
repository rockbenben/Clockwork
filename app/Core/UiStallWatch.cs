namespace Clockwork.Core;

// UI 线程卡顿的仪表：**卡了多久**，以及**卡的时候正在做什么**。
//
// 为什么需要它：本程序已经栽在这件事上两次了——
//
//   ① 手势笔迹窗铺满整块屏又置顶，UI 线程一卡超过 5 秒，DWM 就在它上面盖一块点得中的幽灵窗，
//      整个桌面点不动（用户报的「偶尔手势操作会造成无法点击，关闭桌面窗口管理器才正常」）；
//   ② 同一扇窗是分层窗，收笔时藏得太急，上一笔的位图被下一次 Show 原样贴回来
//      （用户报的「画手势为什么还出现上一次的手势轨迹」）。
//
// 两次都是**先修现象、后查原因**，而查原因时手里只有 clockwork.error.log，里面记的是结果，
// **一个字都没写「卡了多久」**。事后回头看，最该有的那一格恰恰是缺的：
// 5 秒是幽灵化的阈值，而「我们自己的某段代码等了 5 秒」和「整机忙/GC/别的进程堵了输入队列」
// 在日志里长得一模一样，只有时长能分开。
//
// 所以这里做两件事，都不改任何行为：
//
//   · **卡顿探测**：App 那边挂一个 250ms 的 DispatcherTimer。UI 线程被卡住时表不会走，
//     于是「这一拍迟到了多久」就是「UI 线程被卡了多久」——不需要另起线程去戳它。
//   · **可疑调用计时**：已知的两处 UI 线程阻塞点（前台抢占、钩子安装等待）用 Probe 包一圈，
//     超过 500ms 就自己报一行。它们**在卡顿结束后才落笔**，所以那条日志里带的是自己的名字。
//
// 为什么两个都要：探测回答「卡了多久」，Probe 回答「是谁」。只有探测的话，日志会写
// 「ui thread stalled 4300ms」而不知道是谁；只有 Probe 的话，卡在别处（GC、别的进程）
// 就一声不响。两条合起来才能把下一次复现钉到具体某一行。
//
// 不碰 WPF：Core 保持纯的（同 HookWatch / GestureGate 的纪律），计时器由 App 那边提供。
// 判据全是纯函数，阈值与边界都有断言盯着（UiStallWatchTests）。
public static class UiStallWatch
{
    /// <summary>迟到超过这么久就算「卡顿」。默认 1.5 秒——离幽灵化的 5 秒阈值还差得远，
    /// 于是真正危险的那一档会先在这里留下一条，而不是等到系统盖了幽灵窗才知道。</summary>
    public const long DefaultStallMs = 1500;

    /// <summary>单次调用超过这么久就记一行。默认 500ms——低于它的慢调用在正常负载下很常见
    ///（前台抢占本来就要等目标窗口响应），记下来只会把 128KB 的日志刷满。</summary>
    public const long DefaultSlowMs = 500;

    // ── 判据（纯函数） ──
    //
    // 「迟到多少」= 本拍的实际间隔 − 名义间隔。UI 线程被卡住时表不走，本拍的实际间隔自己就变长，
    // 所以这一个减法就是全部机制，不需要去问系统「你卡了没有」。
    // 夹在 0 上：计时器早到（TickCount64 粒度约 15.6ms）会算出负数，那不是「提前卡了一下」。
    public static long LatenessMs(long nowMs, long lastTickMs, long intervalMs)
        => Math.Max(0, nowMs - lastTickMs - intervalMs);

    public static bool IsStall(long latenessMs, long thresholdMs) => latenessMs >= thresholdMs;

    public static bool IsSlow(long elapsedMs, long slowMs) => elapsedMs >= slowMs;

    // ── 开关 ──
    //
    // sink 为空 = 关着（没装笔迹窗的人不付这份代价，同 DisableWindowGhosting 的口径）。
    // 只给「会摆出全屏覆盖窗」的那条路开：那时 UI 线程卡顿才不只是「本程序反应慢」，
    // 而是「整个桌面点不动」。
    private static volatile Action<string>? _sink;
    private static volatile Func<string>? _context;
    private static long _stallMs = DefaultStallMs;
    private static long _slowMs = DefaultSlowMs;
    private static long _lastTickMs;
    private static long _lastReportMs;
    private static int _uiThreadId;

    public static bool Enabled => _sink != null;

    /// <summary>打开仪表。幂等：重复调只是把阈值与 sink 刷新一遍。</summary>
    //
    // 调用方负责喂 Tick（见 App 那个 DispatcherTimer），这里不持有计时器——
    // Core 不碰 WPF，测试也不必造一个 Dispatcher 出来。
    //
    // <paramref name="context"/>（可选）给卡顿那一行补上「卡的时候整机在干什么」。
    // **只加在卡顿行上，不加在慢调用行上**：慢调用行自己已经带着名字，再挂一串 GC 计数
    // 只会把 128KB 的日志刷满，而卡顿行恰恰是唯一没有名字的那种——它现在只有时长，
    // 而「我们自己的代码等了 4 秒」和「一次阻塞式 GC 停了整个世界」在只有时长时长得一模一样。
    public static void Start(Action<string> log, long nowMs, int uiThreadId,
                             long stallMs = DefaultStallMs, long slowMs = DefaultSlowMs,
                             Func<string>? context = null)
    {
        _stallMs = stallMs;
        _slowMs = slowMs;
        _uiThreadId = uiThreadId;
        _lastTickMs = nowMs;
        _lastReportMs = 0;      // 新开的一轮不带上一轮的节流账
        _context = context;
        _sink = log;
    }

    public static void Stop()
    {
        _sink = null;
        _context = null;
    }

    /// <summary>卡顿行的拼装（纯函数，有测试盯着后缀到底加没加、加了会不会丢字段）。</summary>
    //
    // context 为 null/空时**一个多余空格都不留**：那一行的格式是既定的，
    // 只加一段尾巴不会让它变形，这也是现有断言（Contains "stalled" / "4000ms"）还成立的原因。
    public static string FormatStall(long lateMs, long thresholdMs, string? context)
        => string.IsNullOrEmpty(context)
            ? $"ui thread stalled {lateMs}ms (threshold {thresholdMs}ms)"
            : $"ui thread stalled {lateMs}ms (threshold {thresholdMs}ms) {context}";

    /// <summary>每个计时器拍子调一次。<paramref name="intervalMs"/> 是名义间隔。</summary>
    //
    // 被卡住期间这个方法是**跑不到**的——这正是它能量的原因：解卡之后第一拍带着一个很大的
    // 实际间隔回来，那一下就是卡顿的时长。
    public static void Tick(long nowMs, long intervalMs)
    {
        var log = _sink;
        long late = LatenessMs(nowMs, _lastTickMs, intervalMs);
        _lastTickMs = nowMs;
        if (log == null || !IsStall(late, _stallMs)) return;
        // 节流：持续性的慢（每拍都迟到）会每 250ms 报一行，把日志刷满。
        // 一个卡顿区间只该留下一条——时长本身就写在里面。
        if (_lastReportMs != 0 && nowMs - _lastReportMs < _stallMs) return;
        _lastReportMs = nowMs;
        // context 自己也可能抛（它要去读 _mouseHook / _trail 那些字段），别让它带走这条卡顿行——
        // 卡顿行本身比那串补充信息重要得多。Report 只包得住 sink，包不住这里，所以就地兜住。
        string? ctx = null;
        try { ctx = _context?.Invoke(); } catch { }
        Report(log, FormatStall(late, _stallMs, ctx));
    }

    /// <summary>取计时起点，配 <see cref="End"/> 包住一段可疑调用。</summary>
    public static long Begin() => Environment.TickCount64;

    /// <summary>可疑调用结束：超过阈值就报一行，带上自己的名字与所在线程。</summary>
    public static void End(string what, long t0) => Note(what, Environment.TickCount64 - t0);

    /// <summary>同 <see cref="End"/>，但时长由调用方给（测试用，也便于跨时钟复用）。</summary>
    //
    // isUi 这一格是承重的：同一个名字出现在后台线程上（比如动作线程等窗口）是无害的，
    // 出现在 UI 线程上才是幽灵化的点火源。不区分的话，日志里两者长得一样。
    public static void Note(string what, long elapsedMs)
    {
        var log = _sink;
        if (log == null || !IsSlow(elapsedMs, _slowMs)) return;
        bool isUi = Environment.CurrentManagedThreadId == _uiThreadId;
        Report(log, $"ui thread slow: {what} took {elapsedMs}ms (thread={(isUi ? "ui" : "bg")})");
    }

    // 日志本身绝不能成为故障源：sink 是 AppendErrorLog，它自己会碰文件系统，
    // 在一条「UI 线程已经被卡住」的路上再抛出来，正好落在最不该崩的地方。
    private static void Report(Action<string> log, string line)
    {
        try { log(line); } catch { }
    }
}
