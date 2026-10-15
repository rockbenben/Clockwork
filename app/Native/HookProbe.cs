using System.Collections.Concurrent;

namespace Clockwork.Native;

// 钩子回调里那两个会卡住**整条鼠标链**的跨进程查询，搬到这里一条专用后台线程上算，
// 回调只读这里发布的快照。
//
// 两个查询都是 OpenProcess 打底：
//   · ProcessNameForPid   —— OpenProcess + QueryFullProcessImageName（手势的「这是哪个程序」）
//   · InputNotBlockedNow  —— OpenProcess + GetTokenInformation（UIPI 那一问）
//
// 平时它们是微秒级的，出事时不是。挂起的进程、加载器吊死的进程、注册了内核回调的杀软，
// 都会把 OpenProcess 推进一条要跟对方内核对象较劲的路。本机两份日志各记了一次：
//     hook-proc-name  took 3187ms
//     hook-callback   took 6469ms chain=0ms ours=6469ms
// chain=0 排除了 CallNextHookEx，也排除了注入——那 6.5 秒整个落在这两句上。
//
// 而 WH_MOUSE_LL 是**全局串行**的：回调慢过 LowLevelHooksTimeout（300ms）系统就把钩子
// 静默摘掉，此刻正被 return 1 扣着的那次右键按下再没人补发（见 MouseHook 类头第 2 条）。
// 用户看到的就是「按右键的时候整机鼠标顿一下」。
//
// 代价是「窗口刚出现、还没算过」的那一瞬答不出来。那一档按**送不进去**处理，于是那一笔
// 手势整笔放行：用户拿到一个正常的右键菜单，而不是一个被吞掉的右键。取向与
// Win32.IntegrityBlocksInjection 一致（读不到就当会拦），两处是同一个决定的两半。
public sealed class HookProbe : IDisposable
{
    // ── 发布的快照 ──
    //
    // 两个字段必须**一起**换掉，不能是两个 volatile 各写一次：回调读到的组合必须是某一次
    // 真实算出来的（pid 与 injectable 出自同一次 InputNotBlockedNow），不能是两次的拼接。
    // 引用换引用是原子的，所以整份快照用一个不可变对象发布、读一个字段。
    private sealed class Snapshot
    {
        public Snapshot(uint pid, bool injectable) { Pid = pid; Injectable = injectable; }
        public uint Pid { get; }
        public bool Injectable { get; }
    }

    private volatile Snapshot _snap = new(0, false);

    // 进程名按 pid 缓存。一笔手势只落在少数几个 pid 上，命中率接近 100%，
    // 而命中时回调里那一次跨进程查询整个消失。
    private readonly ConcurrentDictionary<uint, string> _names = new();

    // 单槽的解析请求队列：满了就**丢掉这一次**，绝不阻塞回调。丢掉的下一次补解析补上，
    // 而这一格永远是「没名字 → per-app 手势不命中、全局手势照常」，不是错误。
    private readonly BlockingCollection<uint> _want = new(64);
    private Thread? _thread;
    private int _disposed;

    /// <summary>前台窗口送不进去的快照。语义同 <see cref="Win32.InputNotBlockedNow"/>。</summary>
    public bool Injectable => _snap.Injectable;

    /// <summary>这一份快照是给哪个前台窗口算的。</summary>
    public uint SnapshotPid => _snap.Pid;

    /// <summary>
    /// 此刻按在这个前台窗口上的右键，能不能接管成手势。
    /// </summary>
    /// <param name="foregroundPid">按下那一刻的前台窗口 pid（<see cref="Win32.ForegroundPid"/>，
    /// 只是一次 user32 调用，不碰别的进程）。</param>
    /// <returns>
    /// true = 可以接管；false = **不要碰这一下**，原样放行让系统出一个正常的右键。
    ///
    /// 答 false 只有两种情形，都是同一个取向（宁可这次没有手势，也不要把用户的右键吞了）：
    ///   · 快照说送不进去——补发注定被 UIPI 丢掉，而抬起已经吞了就再也还不回来；
    ///   · 快照不是给这个窗口算的（刚切窗、或还没刷新过）——答不出来时按答不出来处理。
    /// </returns>
    public bool MayEngage(uint foregroundPid)
        => foregroundPid != 0 && _snap.Pid == foregroundPid && _snap.Injectable;

    /// <summary>
    /// pid → 进程名（缓存读）。未命中时排一次解析并返回空串。
    /// </summary>
    /// <remarks>
    /// 返回空串不是错：per-app 绑定的手势这一次不命中，全局的照常命中。宁可少一个手势，
    /// 也不在钩子线程上等一次可能卡几秒的 OpenProcess。
    /// </remarks>
    public string ProcessName(uint pid)
    {
        if (pid == 0) return "";
        if (_names.TryGetValue(pid, out var cached)) return cached;
        Request(pid);
        return "";
    }

    /// <summary>排一次「重算注入可行性」的请求（幂等，队列满时静默跳过）。</summary>
    //
    // **只在真正需要时才去问那一句。** 那一句要 OpenProcess，而本机实测它能到秒级（见类头）：
    // 无条件每 500ms 问一次，等于把这个已知会卡的操作放大到**每分钟 120 次**——比修复之前
    // （只在每次右键抬起时问）还频繁，只是从钩子线程搬到了后台。搬走的是「卡住输入链」，
    // 不是「凭空多出一倍的跨进程查询」，所以频率必须一起收回来。
    //
    // 判据是**前台窗口换没换**（GetForegroundWindow 是纯 user32，纳秒级）：而「送不送得进去」
    // 只取决于前台那个窗口，窗口没换答案就不变。快照另有一个上限（SnapshotMaxAgeMs），
    // 用来兜住「窗口没换但桌面换了」的那一档——UAC 安全桌面就是：它不会让前台窗口句柄改变，
    // 可 `GetShellWindow()` 会答 0，而那正是补发必定送不进去的情形。
    public void Refresh()
    {
        IntPtr fg = Win32.ForegroundWindow();
        long now = Environment.TickCount64;
        // 这两个字段只由 Refresh() 写，而它只从 UI 线程那一个计时器来 —— 普通字段即可。
        if (fg == _lastForeground && now - _lastProbeAt < SnapshotMaxAgeMs) return;
        _lastForeground = fg;
        _lastProbeAt = now;
        Request(0);
    }

    // 快照的最长保鲜期。前台窗口没换时的兜底重算间隔，5 秒足够短（切到安全桌面后一次
    // 右键最多晚 5 秒才不接管），又足够长到不会变成「每秒问一次」。
    private const long SnapshotMaxAgeMs = 5000;
    private IntPtr _lastForeground = IntPtr.Zero;
    private long _lastProbeAt;

    private void Request(uint pid)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _want.TryAdd(pid, 0); } catch { }   // CompleteAdding 之后会抛：正在拆除，不必管
    }

    /// <summary>起那条后台线程。**不在构造函数里起**（与 MouseHook 同一条纪律，见其字段注释）。</summary>
    public void Start()
    {
        if (_thread != null) return;
        var t = new Thread(Run) { Name = "Clockwork.HookProbe", IsBackground = true };
        _thread = t;
        t.Start();
    }

    private void Run()
    {
        // **整个枚举包在 try 里**（与 MouseHook.ReplayLoop 同一格纪律）：`Dispose` 只能让它
        // 停止收新活，管不到这一刻正睡在 `GetConsumingEnumerable` 里的这条线程。
        // 没有这一圈，那条线程会带着 ObjectDisposedException 冲出线程顶——而线程顶没人接，
        // 进程当场就没了。宁可解析停在一半（快照停在最后一次算出的值），也不能带崩输入。
        try
        {
            // 两个 pid 各占一格：0 = 刷新注入可行性，非 0 = 解析那个 pid 的进程名。
            foreach (var pid in _want.GetConsumingEnumerable())
            {
                try
                {
                    if (pid == 0)
                    {
                        uint fg = Win32.ForegroundPid();
                        _snap = new Snapshot(fg, Win32.InputNotBlockedNow());
                    }
                    else
                    {
                        string name = Win32.ProcessNameForPid(pid);
                        // 查不到就不写：把空串钉进缓存会让它**永远**答不出来（进程名不会凭空出现），
                        // 而一个后来才启动的同名 pid 会一直吃这一格空答案。
                        if (!string.IsNullOrEmpty(name)) _names[pid] = name;
                    }
                }
                catch { /* 探针自己坏掉不许连累输入：最坏退回「不接管手势」 */ }
            }
        }
        catch { /* 队列被 Dispose（见上面的说明）：这条线程到此为止 */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 只 CompleteAdding，**不 Dispose 那个队列**——同一理由：那条线程此刻正睡在里面，
        // Dispose 掉它的内部信号量会让对方抛 ObjectDisposedException（已由 Run 的整圈 try 接住，
        // 但那只是兜底；正解是根本不制造那一格）。队列本身是纯托管对象，随本对象一起回收。
        try { _want.CompleteAdding(); } catch { }
        _thread = null;   // 后台线程，不 Join：CompleteAdding 之后它自己就退了
    }
}