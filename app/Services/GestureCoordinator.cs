using System.Windows;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;

namespace Clockwork.Services;

// 鼠标钩子与手势：中键长按/右键笔画的装、卸、自愈、重钩，笔迹覆盖窗，光标巡检与 UI 卡顿仪表。
// 自 App 拆出（纯结构重构，线程模型一字未改）。对面板域的三处依赖由 App 接线：
// OnTogglePanel（中键到点唤出面板）、DismissPanel（手势起笔/开管理器前先收面板）、
// RunGestureStep（命中后走单步执行的手势档——`RunStep(step, by: StepTrigger.Gesture` 那行
// 留在 App 的接线上，GestureReceiptTests 按它钉「手势必须走 Gesture 档」）。
// OffUiThread / TrailGhosting / UiStallWatch / HookSuspects 四组按源码文本钉住的纪律
// （不在 UI 线程等安装、幽灵化先于建窗、仪表先于建窗、摘钩前抄账）全部跟着本文件走。
internal sealed class GestureCoordinator
{
    private readonly Func<RootConfig> _cfg;
    private readonly Action _saveConfig;
    private readonly AppDiagnostics _diag;

    public GestureCoordinator(Func<RootConfig> cfg, Action saveConfig, AppDiagnostics diag)
    {
        _cfg = cfg; _saveConfig = saveConfig; _diag = diag;
    }

    // App 装配的出站接线（面板窗与主窗在 BuildShell 之后才存在）。
    public Action? OnTogglePanel;
    public Action? DismissPanel;
    public Action<LaunchStep, IntPtr>? RunGestureStep;
    public Func<Window?>? DialogOwner;      // 主窗可见时作对话框 Owner，否则 null

    private static System.Windows.Threading.Dispatcher Disp => System.Windows.Application.Current.Dispatcher;
    private RootConfig _config => _cfg();
    private void WarnToast(string msg) => _diag.WarnToast(msg);
    private void AppendErrorLog(string line) => _diag.AppendErrorLog(line);

    // 退出收尾（App.ExitApp 调）：全局钩子必须显式摘掉——留着不摘，退出过程中任何一次
    // 鼠标移动都会回调进一个正在拆的对象。收笔也要在这里做：退出时手可能正按着，
    // 不收笔那条线会跟着桌面一直留到重启。
    public void DisposeOnExit()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        CloseTrail();
    }

    private Native.MouseHook? _mouseHook;
    private int _mouseHookMs;          // 当前钩子是按哪个阈值装的：只有阈值真变了才重装（0=中键关着）
    private bool _mouseHookGesture;    // 当前钩子装没装手势那半
    // 当前那个 GestureGate 是按哪个最小笔画长度造的。GestureGate 不可变，所以换主屏 / 换分辨率
    // 之后必须整个重建——这一格就是「要不要重建」的判据（见 ApplyMouseHook 里那段）。
    private int _mouseHookMinLeg;
    // 当前中键拖拽容差（物理像素）。按主屏 DPI 换算，缩放变了要跟着重装，理由同 _mouseHookMinLeg。
    private int _mouseHookTolerance;
    // 画手势时屏幕上那条看得见的线。只在监听右键期间存在——没配手势的人不该多出一扇窗。
    private Views.GestureTrailWindow? _trail;
    private bool _mouseHookFailed;     // 装失败已经报过一次：别在每次保存配置时反复弹同一条
    // 每次起一次安装就 +1，用来丢弃「已经在飞的那次」的结果（见 OnHookInstalled）。
    // 只增不减、不做回绕——它是比较相等的号，不是计数器，int 溢出要 21 亿次重装。
    private int _hookInstallToken;

    /// <summary>右键监听此刻是什么状态。手势管理器把它显示出来。</summary>
    //
    // 这个功能所有的失败模式都是**不可见**的：钩子装不上（受限令牌 / 组策略 / 安全软件拦）、
    // 装完被 Windows 静默卸掉（UI 线程占满 LowLevelHooksTimeout 就会，而且不通知任何人）、
    // 没提权因此看不到提权窗口上的输入。三件事在界面上一个字都没有，
    // 用户能看到的只有「按了没反应」——而那与「我这一笔画歪了」长得一模一样。
    // 报出来之后，「功能是不是活着」就不必再靠猜。
    public GestureWatchState GestureWatch
    {
        get
        {
            if (_mouseHookFailed) return GestureWatchState.Failed;
            if (_mouseHook == null || !_mouseHookGesture) return GestureWatchState.Off;
            // **看心跳，不看「我们装没装」。** 这两个字段记的是当初的打算：Windows 把低级钩子
            // 静默卸掉之后它们原样不动，于是状态会在最需要说真话的那一次显示「监听中」。
            // 打开这扇窗必然刚动过鼠标，所以「几秒内没有回调」就是「它已经不在了」。
            // 「收得到抬起、从没收到按下」优先于其余判断：那时心跳是正常的（移动照收），
            // 状态会显示「监听中」，而手势永远起不了头——这正是最难查的那一种，
            // 它必须自己说出来，否则用户看到的又是一个「一切正常但没反应」。
            if (_mouseHook.RightDownSwallowed) return GestureWatchState.Blocked;
            if (_mouseHook.SinceBeatMs <= StaleBeatMs) return GestureWatchState.Live;
            // 「一次都没收到过」与「收到过、后来停了」分成两档：成因和补救完全不同，
            // 混成一句话的话，用户面对的又是一个「按了没反应」。
            return _mouseHook.EverBeat ? GestureWatchState.Stale : GestureWatchState.Deaf;
        }
    }

    // 多久没有回调就算它已经掉了。**这格是给「状态行」看的**：鼠标一动就有 WM_MOUSEMOVE，
    // 而人从动鼠标到看这一行，中间隔不了这么久，4 秒足够判「已经不在了」。
    private const long StaleBeatMs = 4000;

    // 自愈**没有**「沉默多少秒算死」这种固定阈值了：判据改成在同一个 500ms 采样区间里对齐
    // 「光标动了吗」与「钩子心跳新吗」两格信号（见 CursorWatchTick 与 HookWatch.ShouldHeal）。
    // 这一格只留对齐用的余量：活着的钩子在「区间内有移动」时心跳必然落在区间内
    //（SinceBeatMs ≤ 实测区间长度），余量只需盖住 DispatcherTimer 抖动与 TickCount64
    // 约 15.6ms 的时钟粒度。**不盖系统卡顿**——卡顿时实测区间自己就变长，界跟着长，
    // 不会把卡顿误判成死亡（HookWatchTests 里 A_long_sample_window_extends_the_freshness_bound
    // 钉着这一条）。
    //
    // 旧版在这儿放过一个 1500ms 的固定沉默阈值，配上「光标 2000ms 内动过」的活动窗，
    // 两扇窗错位，用户每次移动后停手 1.5~2 秒必被当成钩子死了。实测 clockwork.error.log，
    // 2026-09-15 一天 518 行 reinstall、silentFor 有 512 行落在 1515–2000ms 贴着阈值成簇，
    // prevEverFired 全是 yes——钩子一次都没死过。
    private const long HealBeatSkewMs = 250;

    // 两次自愈之间最短间隔。同区间对齐已经消掉了误报（停手的区间根本不检测），这条下限只是
    // 保险：真碰上「装上几秒就被 Windows 摘掉」的环境，别让循环摘装的频率高于此——
    // 每次摘装之间有个输入空窗，越频繁越可能正好压掉一次中键按下。
    // 这条上限治不了病（钩子该死还是死），只保证病发时不额外推高摘装频率。
    private const long HealMinGapMs = 3000;

    /// <summary>钩子掉了就重新装上。</summary>
    //
    // **Windows 会把低级鼠标钩子静默卸掉**：回调占用超过 LowLevelHooksTimeout（默认 300ms）
    // 就会被摘，不通知任何人，而本进程这边句柄原样不动——于是「装着」这个状态一直是真的，
    // 手势却从此再也不响应，直到重启程序。这个项目里到处是为那 300ms 写的防线
    //（IconLoader 不阻塞、PathProbe 不阻塞、动作一律 post 出回调再跑），但防线不是保证：
    // 别的进程拖慢整机、调试器断下、系统卡顿都能踩到它。
    //
    // 所以补一条退路：心跳停了就重装。摘掉再装是廉价的（一次 SetWindowsHookEx），
    // 而代价是零——真没掉的时候这个判断根本不会成立。
    //
    // ponytail: 靠鼠标动才有心跳，所以「久坐不动」在心跳这一格上与「钩子死了」无法区分。
    //   现在的解法见 CursorWatchTick：只在**光标本采样区间真的动过**的区间里问心跳，
    //   停手的区间不检测——久坐不再白重装，也不需要另设一条旁路。
    private (int X, int Y) _lastCursor;
    private bool _cursorSeen;   // 采过第一次没有——第一次只定基线，见 CursorWatchTick
    // 上一个钩子的账，在摘掉它之前抄下来。**只有自愈、手动「重新挂钩」、开机后抢链首这三条路会填它**，
    // 而 ApplyMouseHook 也正是拿「它有没有值」当作「这次重装值不值得记一行」的判据。
    private string? _prevBeat, _prevRight, _prevMiddle, _prevSource;
    private long _prevSince;
    private int _prevRejected;
    // 上一个钩子装上以来，事件最长迟到了多久（见 MouseHook.UpstreamMaxMs）。与 _prevSince 一起读：
    //   死寂大 + 迟到也大 → 钩子链被排在我们前面的工具堵住了（重装治不了，只能抢链首）；
    //   死寂大 + 迟到不大 → 我们自己的钩子被系统摘了（LowLevelHooksTimeout，重装正是解药）。
    private uint _prevUpstream;
    // 上一个钩子**回调自身耗时**的峰值（见 MouseHook.CallbackMaxMs）。这一格和 _prevUpstream 是一对，
    // 回答的是两个相反的问题：
    //   upstreamMax 大 → **别人**堵了链（重装治不了）；
    //   callbackMax 接近 300 → **我们自己**正在被摘（LowLevelHooksTimeout 是回调的预算，重装只是解药）。
    //
    // 为什么非要有这一格：`hook-callback` 那行 slow 日志的阈值是 500ms，而预算是 300ms ——
    // 300~500ms 这一段正是「我们把自己搞死」的区间，却一声不响。摘掉之后 upstreamMax 还会
    // 停在最后那个小值上，看着一切正常；只有这一格会把那段历史带出来。
    private uint _prevCallbackMax;
    // 自愈那一刻**在跑的那些「会装低级鼠标钩子」的程序**（见 Core.HookSuspects）。
    //
    // 上游延迟只能证明「链被堵了」，证明不了「谁堵的」——Windows 不给枚举别人钩子的 API，
    // 唯一的旁证就是谁在场。这一格是整条诊断链的最后一环：`silentFor` 说死了多久、
    // `upstreamMax` 说是被堵还是被摘、`suspects` 说当时谁在场（一家一家关掉去试）。
    //
    // **只在摘钩那一刻采一次**，而且它证明的是「在不在场」不是「有没有罪」：
    // 在场不等于作案，名单外的工具堵了链这里一个字也不会有（理由全写在 Core.HookSuspects）。
    private string? _prevSuspects;
    // 上一次摘钩重装的时刻（TickCount64）：自愈和手动「重新挂钩」都写它。只供 HealMinGapMs 那条
    // 下限用，不参与日志。0 表示「从没摘过」——TickCount64 从开机起算不会撞到 0，拿它当哨兵安全。
    private long _lastHealAt;

    // 摘钩前抄一份中键的账，格式与右键那段对称。fired 是到点唤出次数，
    // dragEscapes 非零 = 「按住很久也不弹」其实是抖动容差把长按判成了拖拽。
    private static string MiddleAccount(Native.MouseHook h)
        => "middle " + (h.EverMiddleDown ? "down+" : "down-")
          + (h.EverMiddleUp ? " up+" : " up-")
          + $" fired={h.MiddleFireCount} dragEscapes={h.MiddleDragEscapeCount}";

    // 上一次采样的时刻（TickCount64）。量的是「本采样区间实测有多长」，
    // 与心跳年龄同一条时间轴——同区间对齐判据的一半，见 CursorWatchTick。
    private long _lastSampleAt;
    private System.Windows.Threading.DispatcherTimer? _cursorWatch;

    /// <summary>按「此刻装没装鼠标钩子」开关那个每半秒采样光标的计时器（手势与中键面板任一在听就要开）。</summary>
    //
    // 只在真的装了钩子时才跑：两样都关的人一次 GetCursorPos 都不会多花。
    private void ApplyCursorWatch(bool on)
    {
        if (on)
        {
            if (_cursorWatch != null) return;
            _cursorWatch = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(500) };
            _cursorWatch.Tick += (_, _) => CursorWatchTick();
            _cursorWatch.Start();
            _lastSampleAt = 0;    // 刚开的这一轮不带上一轮的旧账
            _cursorSeen = false;  // 基线也要重新定，否则用的是上一轮关掉时的那个位置
            return;
        }
        _cursorWatch?.Stop();
        _cursorWatch = null;
    }

    // UI 线程卡顿仪表的拍长。250ms 而不是 500ms：幽灵化的阈值是 5 秒，而这里要的是
    // 「卡了多久」这个数本身——拍得越密，解卡后第一拍测出来的时长越准（误差上限就是一拍）。
    // 代价只是一次减法，比 _cursorWatch 那次 GetCursorPos 还便宜。
    private const int StallTickMs = 250;
    private System.Windows.Threading.DispatcherTimer? _stallWatch;

    /// <summary>开关 UI 线程卡顿仪表。跟着全屏笔迹窗一起开——只有那时卡顿才不只是「本程序慢」。</summary>
    //
    // **只在会摆出全屏覆盖窗的那条路上开**，与 Win32.DisableWindowGhosting 同一格理由、同一份代价观：
    // 没开手势的人一点没变。仪表本身不改任何行为，它只回答「卡了多久」，以及（配 Probe）
    // 「卡的时候在做什么」——这两格正是前两次故障事后追查时唯一缺的东西（见 Core.UiStallWatch）。
    //
    // 不用另起线程去戳 UI 线程：DispatcherTimer 在 UI 线程被卡住时**自己不会走**，
    // 所以解卡后第一拍的「实际间隔 − 名义间隔」就是卡顿时长。少一条线程、少一处竞态。
    private void ApplyStallWatch(bool on)
    {
        if (on)
        {
            if (_stallWatch != null) return;
            // 基线先定下来，否则第一次卡顿报出来的是「从开机到现在的全部 GC / 全部分配」，
            // 那串数字很大、看着很像元凶，其实跟这一次卡顿没有关系。
            _stallGc2 = GC.CollectionCount(2);
            _stallAllocMb = GC.GetTotalAllocatedBytes(precise: false) / (1024 * 1024);
            Core.UiStallWatch.Start(AppendErrorLog, Environment.TickCount64,
                                    Environment.CurrentManagedThreadId,
                                    context: StallContext);
            _stallWatch = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(StallTickMs) };
            _stallWatch.Tick += (_, _) => Core.UiStallWatch.Tick(Environment.TickCount64, StallTickMs);
            _stallWatch.Start();
            return;
        }
        _stallWatch?.Stop();
        _stallWatch = null;
        Core.UiStallWatch.Stop();
    }

    private int _stallGc2;
    private long _stallAllocMb;

    // 「自上一行卡顿以来，笔迹窗有没有亮过」——**锁存，不是现读**。
    //
    // 为什么不能现读：卡顿行是在**解卡之后**才落笔的，而一笔手势只有几百毫秒、一次卡顿却有
    // 4.6 秒（2026-09-18 那行就是），解卡那一刻窗早被 HideSoon 藏掉了，现读 IsVisible 必然
    // 得到 hidden。于是**恰恰在「整屏覆盖窗就是元凶」的那种复现里，这一格会报成 hidden**，
    // 把人往「与覆盖层无关」上引——一个只在关键场合说谎的字段比没有这个字段更糟。
    // 锁存之后它答的是「这段时间里覆盖窗在不在屏幕上」，那才是要问的问题。
    private bool _trailUpSinceStall;

    /// <summary>卡顿那一行尾巴上的补充事实：这一段时间里整机在干什么。</summary>
    //
    // 为什么必须有：2026-09-18 那次复现里，日志只写下了 `ui thread stalled 4594ms`——
    // **时长有了，名字没有**。而「我们自己的代码等在某处」和「一次阻塞式 GC 停了整个世界」
    // 在只有时长时**长得一模一样**，却要走两条完全不同的修法。前者的指纹是某个 Probe 行，
    // 后者没有名字，只能在整机计数上现形，所以这两格必须一起报。
    //
    // gc2 = 第 2 代回收次数增量（阻塞式、会停掉所有线程，包括钩子线程——而钩子回调超
    // LowLevelHooksTimeout 会被 Windows 静默摘掉，正是那次日志里 `silentFor=937ms` 的来源）。
    // alloc = 分配增量（MB）。整屏分层窗每帧要重新合成一遍 2560×1440 ≈ 14MB，
    // 这个数暴涨就说明渲染压力是元凶。
    // sinceBeat = 钩子距上次回调多久（- 表示没装钩子，never 表示装了但从没来过事件——
    // 刚装上的钩子在第一次鼠标移动前就是这个状态，别把 MaxValue 当数读）——
    // 它和卡顿同时发生，才谈得上「同一次事件」。
    //
    // 这两个 API 都是常数级读取，不会给「刚解卡」的 UI 线程再添负担。
    private string StallContext()
    {
        int gc2 = GC.CollectionCount(2);
        long allocMb = GC.GetTotalAllocatedBytes(precise: false) / (1024 * 1024);
        int dGc2 = gc2 - _stallGc2;
        long dAllocMb = allocMb - _stallAllocMb;
        _stallGc2 = gc2;
        _stallAllocMb = allocMb;
        long beat = _mouseHook?.SinceBeatMs ?? -1;
        // 上游延迟与 sinceBeat 必须**同时**出现在这一行：单看任何一个都分不出
        // 「链被排在前面的工具堵住」和「我们自己的钩子被系统摘掉」——而这台机器上
        // Logi Options+ / PowerToys / 热键助手 三家都装着 WH_MOUSE_LL（见 MouseHook._upstreamMax）。
        uint upstream = _mouseHook?.UpstreamMaxMs ?? 0;
        // 与上一格成对：那个量别人堵了我们多久，这个量我们自己离被摘还有多远（见 MouseHook.CallbackMaxMs）。
        // 卡顿行里两格一起报，才分得开「一次阻塞式 GC 停掉了整个世界（两个都会大）」
        // 和「上游某个工具堵了链（只有 upstreamMax 大）」。
        uint cbMax = _mouseHook?.CallbackMaxMs ?? 0;
        // trailUp 用**锁存值**（见 _trailUpSinceStall）：问的是「这段时间里覆盖窗在不在屏幕上」，
        // 不是「此刻在不在」——解卡那一刻它必然已经藏了，现读只会给出一个误导的 hidden。
        // 名字从 trail= 改成 trailUp= 是承重的：语义变了，沿用旧名字会让读过旧日志的人
        // 以为两者可比（旧的那一半几乎恒为 hidden）。
        string trail = _trail == null ? "none" : (_trailUpSinceStall ? "up" : "never");
        _trailUpSinceStall = false;   // 这一份账只用一次，同 _stallGc2 / _stallAllocMb
        return $"gc2=+{dGc2} alloc=+{dAllocMb}MB sinceBeat={(beat < 0 ? "-" : beat == long.MaxValue ? "never" : beat + "ms")} "
             + $"upstreamMax={(_mouseHook == null ? "-" : upstream + "ms")} "
             + $"callbackMax={(_mouseHook == null ? "-" : cbMax + "ms")} trailUp={trail}";
    }

    /// <summary>每半秒看一眼光标。装了钩子（手势或中键面板）期间跑。</summary>
    //
    // 判据在这儿采，而且必须**采得够密、且两格信号在同一区间对齐**。
    // 一度是搭在 30 秒的提醒计时器上：那时「光标动过」比的是相隔 30 秒的两次位置，
    // 于是「10 秒前动过鼠标、之后停下来看屏幕」——一个再常见不过的状态——
    // 会被判成「钩子死了」，白摘白装一次，还把日志刷满、让状态行一直挂着「已失效」。
    // 后来改成独立计时器，却又用过两扇各自计时的窗（活动 2000ms / 沉默 1500ms），
    // 用户移动后停手 1.5~2 秒必落进重叠带：一天 518 行 reinstall 就是这么来的。
    //
    // 现在每拍只回答一个问题：**这一个区间里光标动了吗？动了，那同一区间里心跳新不新？**
    // 不保留跨区间的「活动窗」，停手的区间直接不检测，重叠带在数学上不存在。
    //
    // 半秒而不是 1 秒：钩子真在连续使用中被摘掉时，最坏要到第二个移动区间才认得出
    //（死亡发生的那个区间里可能还留着死亡前的心跳），检测上界 ≈ 两个采样间隔。
    // 代价仍是一次 GetCursorPos，而且只在真的装了钩子（手势或中键面板）时才开——
    // 两样都关的人一次都不会跑到这儿。
    private void CursorWatchTick()
    {
        // 包一圈计时：这个 tick 里跑着自愈（摘钩 + 重装），而摘钩要 Join 钩子线程——
        // **这是 UI 线程上唯一一条能卡满 2 秒、之前又完全没计时的路**。
        // 2026-09-18 那次复现只留下 `ui thread stalled 4594ms`，没有名字；两种成因都落在这一带，
        // 而 Probe 行是唯一能把它们分开的东西（见 Core.UiStallWatch 的注释）。
        long t0 = Core.UiStallWatch.Begin();
        try { CursorWatchTickCore(); }
        finally { Core.UiStallWatch.End("cursor-watch", t0); }
    }

    private void CursorWatchTickCore()
    {
        // 时刻先取、只取这一次：区间长度和心跳年龄都得站在同一个瞬间上，否则又造出一扇错位的窗。
        long nowMs = Environment.TickCount64;
        var pos = Native.Win32.CursorPos();
        // **第一次采样只用来定基线，不构成区间。**
        // _lastCursor 的初值是 (0,0)，而真实光标几乎不可能正好在那儿——不特判的话，
        // 开启监视后的第一跳必然「不等于上次」，于是即使用户根本没碰鼠标也会被记成刚动过。
        if (!_cursorSeen) { _cursorSeen = true; _lastCursor = pos; _lastSampleAt = nowMs; return; }
        bool moved = pos != _lastCursor;
        long intervalMs = nowMs - _lastSampleAt;   // 实测长度：计时器迟到时它自己变长，新鲜度的界跟着长
        _lastCursor = pos;
        _lastSampleAt = nowMs;
        HealMouseHookIfDead(moved, intervalMs);
    }

    // movedInSample / intervalMs 是**同一个** 500ms 采样区间的两面（见 CursorWatchTick）：
    // 区间里光标确实动了、同一区间的心跳却老于区间长度，才是「移动发生了、回调没来」。
    /// <summary>摘掉鼠标钩子，并给这次等待计时。<paramref name="why"/> 进日志名（heal / manual / off / reapply）。</summary>
    //
    // 为什么值得单独封一层：`MouseHook.Dispose()` 内部有**两次 `Join(1000)`**（等钩子线程退出），
    // 而它是在 UI 线程上被调的。钩子线程正卡在回调里时（上游钩子链被堵住、或一次阻塞式 GC
    // 停掉了所有线程），这两次 Join 会**双双等满 = UI 线程卡 2 秒**——而这恰恰发生在用户
    // 已经在出问题的时候（自愈就是被钩子沉默触发的），于是「有点卡」被放大成「点不动」。
    //
    // 此前这里一个计时都没有，所以 2026-09-18 的日志里只有一句没有名字的
    // `ui thread stalled 4594ms`。名字是承重的：它和 `hook-install-wait`（在后台线程上）
    // 是**完全不同**的两条路，修法也不同，混在一起只能靠猜。
    private void DisposeMouseHook(string why)
    {
        if (_mouseHook == null) return;
        long t0 = Core.UiStallWatch.Begin();
        try { _mouseHook.Dispose(); }
        finally { Core.UiStallWatch.End("hook-dispose:" + why, t0); }
        _mouseHook = null;
    }

    /// <summary>笔迹采样点到达（UI 线程，由钩子线程经 Dispatcher 派来）。包一圈计时。</summary>
    //
    // 为什么这两个也要计时：笔迹窗是 `AllowsTransparency=true` 的分层窗，而 WPF 对分层窗
    // **走软件渲染**——每重画一帧要重新合成整块屏（2560×1440 ≈ 14MB）。所以「UI 线程被自己的
    // 渲染按住」是一条完全有可能的路，而它此前和「自愈时 Join 钩子线程」一样，是**没有名字**的。
    //
    // **但「钩子每个采样点都会让它重画一次、一笔能来几百个点」是错的，别再照着估。**
    // 采样点在 `MouseHook.ScheduleTrail` 里已经**合帧**（`Interlocked.Exchange(ref _trailQueued, 1)`，
    // 至多排一个 drain），所以一笔的点数 ≈ 这一笔跨了多少个 UI 帧，而不是钩子收到多少个鼠标事件：
    // 500ms 的一笔在 60Hz 上约 30 个点。14MB/帧 × 30 帧 ≈ 420MB/笔，摊到每帧约 1.4ms
    //（按 10GB/s 的拷贝带宽），**够不上「把 UI 线程按住数秒」的量级**——渲染压力真有那么大时，
    // 证据在卡顿行的 alloc= 增量里，不在这条注释里。
    private void TrailPoint(int x, int y, bool armed)
    {
        // **先锁存再干活**：卡顿可能就发生在下面那句调用里面，而卡顿行是解卡之后才写的
        //（见 _trailUpSinceStall）。放在前面，这一笔开头那一下就已经把「窗亮着」记下了。
        _trailUpSinceStall |= _trail?.IsVisible == true;
        long t0 = Core.UiStallWatch.Begin();
        try { _trail?.Point(x, y, armed); }
        finally { Core.UiStallWatch.End("trail-point", t0); }
        // 再锁一次：第一个采样点不显窗（第 2 个才 Show），所以「本句把它点亮」也要算进来。
        _trailUpSinceStall |= _trail?.IsVisible == true;
    }

    private void TrailEnd()
    {
        // 收笔这一刻窗还亮着（Finish 走的是推迟藏，见 GestureTrailWindow.HideSoon）——
        // 这是「一笔确实把覆盖窗摆上过屏」最便宜的取证点。
        _trailUpSinceStall |= _trail?.IsVisible == true;
        long t0 = Core.UiStallWatch.Begin();
        try { _trail?.Finish(); }
        finally { Core.UiStallWatch.End("trail-finish", t0); }
    }

    private void HealMouseHookIfDead(bool movedInSample, long intervalMs)
    {
        // 手势与中键面板共用同一个钩子：只开中键面板（没配手势）时钩子一样会被静默摘掉，
        // 这一档不能只给手势用户兜底。
        if (_mouseHook == null) return;
        // **从没跳过一次的钩子不算「死了」，压根别自愈。**
        //
        // SinceBeatMs 在从未收到输入时返回 long.MaxValue，于是一个**刚装上、还没被用过**的钩子
        // 和一个真死了的钩子长得一模一样：用户第一次移动鼠标的那个区间，moved=true、心跳却是
        // MaxValue，不挡这一格每次启动都会稳定地空转重装——实测日志里那 3 行一簇（yes → no → no）
        // 就是它，装到那个「试三次就放弃」的计数器封顶才停。85 行日志里 78 行是这么来的。
        //
        // 而这个方向本来就是错的：「掉了就重装」只对**装对了、事后被摘掉**那一种有效
        //（Windows 因 LowLevelHooksTimeout 把它静默摘走）。一次输入都没收到过则是另一回事——
        // 那是装的那一刻就不成立（进程没提权、有人排在输入链前面），重装一百次也一样。
        // 界面上那一档本来就叫 Deaf，与 Stale 分开报，说的正是「这条路重装治不了」。
        //
        // 于是判据从「试三次再放弃」改成「根本不试」：少了三次无谓的装卸，日志里那 3 行一簇没了，
        // 而真正的自愈（收到过输入、后来停了）一次都没少——那一档 EverBeat 恒为真。
        if (!_mouseHook.EverBeat) return;
        // 三条守卫合起来才动手（区间内有移动、心跳老于本区间、距上次摘钩够久）：
        // 抽成 HookWatch.ShouldHeal 就是要让每条都有断言盯着（挪错一条阈值、或把下限删掉，
        // 都有测试变红）。状态行用的 StaleBeatMs 与此无关：那格取宽是给人看的，这里只要
        // 同一个采样区间里的事实。
        // _lastHealAt == 0 表示「从没自愈过」。TickCount64 从开机起算，实际不会撞到 0，拿它当哨兵是安全的；
        // 不做这一步的话，开机不到 HealMinGapMs 就启动的程序，第一次真故障会被那条下限误挡住。
        long sinceLastHeal = _lastHealAt == 0 ? long.MaxValue : Environment.TickCount64 - _lastHealAt;
        if (!HookWatch.ShouldHeal(movedInSample, _mouseHook.SinceBeatMs, intervalMs,
                                  sinceLastHeal, HealBeatSkewMs, HealMinGapMs))
            return;
        // **先把上一个钩子的账记下来，再摘掉它。** 这两格（收到过输入吗、收到过右键吗）
        // 是判断「为什么没工作」的全部依据，而摘掉之后就再也问不到了——
        // 此前这里先置空再走 ApplyMouseHook，于是自愈这条路上日志永远是「-」：
        // 恰恰在唯一反复触发的那条路上，诊断是瞎的。
        _prevSource = "heal";
        _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
        // 按下与抬起**分开记**。合成一格（或）会把这个功能唯一能自证的信号抹掉：
        // 「收到过抬起、从没收到按下」只有一种成因——有人排在前面把按下吞了。
        _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                   + (_mouseHook.EverRightUp ? " up+" : " up-");
        _prevMiddle = MiddleAccount(_mouseHook);
        _prevSince = _mouseHook.SinceBeatMs;
        _prevRejected = _mouseHook.InjectRejected;
        _prevUpstream = _mouseHook.UpstreamMaxMs;
        // 与上一格成对：那个量别人堵了我们多久，这个量我们自己离被摘还有多远（见 _prevCallbackMax）。
        _prevCallbackMax = _mouseHook.CallbackMaxMs;
        // 顺手采一次在场名单（见 _prevSuspects）。放在**摘钩之前**与上面那几格同一理由：
        // 这一格记的是「出事那一刻谁在跑」，而不是「我们查日志那一刻谁在跑」——
        // 用户关掉某个工具之后再来看日志，两者就不是一回事了。
        _prevSuspects = HookSuspects.Describe(HookSuspects.RunningProcessNames());
        var wasGesture = _mouseHookGesture;
        _lastHealAt = Environment.TickCount64;   // 记在真动手之前：万一重装又失败，下限仍得拦住立刻再摘一次
        DisposeMouseHook("heal");
        _mouseHookMs = -1;                 // 逼 ApplyMouseHook 走完整的重装，别被那句「已经是想要的状态」挡回去
        _mouseHookGesture = !wasGesture;
        ApplyMouseHook();
    }

    /// <summary>把鼠标钩子摘掉再装一次，插到钩子链最前面。</summary>
    //
    // **钩子链里后装的排在前面**，而排在前面的那个若吞掉右键按下，后面的人根本收不到。
    // 所以「另一个程序抢走了右键」这件事，唯一的解法就是重装一次抢回队首——
    // 同类工具（Quicker）为此专门有一条「重新挂钩键鼠」的命令，理由一模一样。
    //
    // 自愈够不着这一格：被抢在前面时我们的钩子**活得好好的**（鼠标移动照收、心跳正常），
    // 只是右键先被人吞了，于是「掉了就重装」永远不触发，也就永远排在后面。
    // 那台机器上什么时候会有人插到前面，我们无从预知，所以这件事只能留给用户按。
    /// <summary>鼠标钩子此刻装着没有（不区分手势还是中键面板）。托盘菜单据此决定要不要给「重新挂钩」入口。</summary>
    public bool MouseHookActive => _mouseHook != null;

    // source 写进 error.log 的 source= 字段：自愈与手动摘钩的**表现完全一样**（都是一行
    // reinstalled + 一份前任账），但成因与处理方式不同——手动那几行通常只是用户在排障时点的，
    // 不该被当成故障。分不清的话，日志里每一次摘钩都要去问用户「你当时点了吗」。
    public void RehookMouse(string source = "manual")
    {
        if (!Disp.CheckAccess()) { Disp.Invoke(() => RehookMouse(source)); return; }
        // 这条路也要记账。此前不记，于是日志里连按几下「重新挂钩」全是「上一个 = -」——
        // 而那几下恰恰是用户在排障时按的，最需要知道上一个钩子到底收到过什么。
        if (_mouseHook != null)
        {
            _prevSource = source;
            _prevBeat = _mouseHook.EverBeat ? "yes" : "no";
            _prevRight = (_mouseHook.EverRightDown ? "down+" : "down-")
                       + (_mouseHook.EverRightUp ? " up+" : " up-");
            _prevMiddle = MiddleAccount(_mouseHook);
            _prevSince = _mouseHook.SinceBeatMs;
            _prevRejected = _mouseHook.InjectRejected;
            _prevUpstream = _mouseHook.UpstreamMaxMs;
            _prevCallbackMax = _mouseHook.CallbackMaxMs;   // 同上：与上一格成对
            // 手动「重新挂钩」也记这一格：那几下恰恰是用户排障时按的，
            // 而「他按的时候链上都有谁」正是他下一步要问的（见 _prevSuspects）。
            _prevSuspects = HookSuspects.Describe(HookSuspects.RunningProcessNames());
        }
        DisposeMouseHook("manual");
        _mouseHookMs = -1;               // 逼 ApplyMouseHook 走完整重装，别被「已经是想要的状态」挡回去
        _lastHealAt = Environment.TickCount64;  // 手动摘钩也算摘钩：否则紧接着的真故障会立刻再摘一次
        _mouseHookFailed = false;        // 上一次装失败不该让这一次连报错都不报
        ApplyMouseHook();
    }

    /// <summary>开机清单跑完后重挂一次鼠标钩子抢回链首。</summary>
    //
    // 钩子链**后装的排在前面**：开机清单里延迟启动的 AHK / 启动器类工具在 Clockwork 之后装钩，
    // 它们若吞中键，排在后面的我们连 MBUTTONDOWN 都收不到——开机首次唤出无效、之后又时好时坏，
    // 这是主要嫌疑之一。自愈治不了这一种：中键被单独吞掉时移动心跳照常，不满足「全钩死寂」。
    // 清单跑完意味着那些工具都已启动并装完钩子，此刻重装一次就把队首抢回来（同类工具 Quicker
    // 专门有一条「重新挂钩键鼠」的命令，理由相同；我们把它自动化在唯一知道「开机启动结束了」的时刻）。
    // 手动「重跑清单」（boot=false）不调：那时用户正在用机器，没必要白抖一次钩子。
    public void RehookMouseAfterBoot()
    {
        if (!Disp.CheckAccess()) { Disp.Invoke(RehookMouseAfterBoot); return; }
        // 没装（面板和手势都关着）或上一次装失败：别在开机后制造一次新的失败弹窗。
        if (_mouseHook == null || _mouseHookFailed) return;
        RehookMouse("boot");
    }

    /// <summary>打开手势管理器。面板表圈上那颗按钮与主界面那颗走同一条路。</summary>
    public void OpenGestureManager()
    {
        DismissPanel?.Invoke();
        var owner = DialogOwner?.Invoke();
        var w = new Views.GestureManagerWindow(_config, _saveConfig) { Owner = owner };
        // 没有 Owner 时（主窗口还在托盘里）自己置顶，否则这扇窗会开在所有窗口后面——
        // 同 OpenPanelManager 那条，理由一模一样。
        if (owner == null) { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true; }
        w.ShowDialog();
    }

    // 按当前设置装 / 卸中键长按的钩子。启动时与每次保存配置后都会调，故必须是幂等的。
    //
    // 阈值改了要重装：LongPressGate 的阈值在构造时就定了（那是它保持纯粹、可测的代价），
    // 所以调阈值等于换一个 gate。比较 _mouseHookMs 而不是无条件重装——每次保存配置都摘一次
    // 再装一次全局钩子是没必要的抖动，而钩子重装的瞬间正按着的中键会丢掉状态。
    public void ApplyMouseHook()
    {
        // **必须在 UI 线程上跑这个方法**——但原因已经不是钩子本身：MouseHook 现在自带专用
        // 钩子线程装钩（见 MouseHook 类头第 1 条，回调存活不再受 WPF/DWM 卡顿影响）。真正要求
        // UI 线程的是下面那扇 WPF 笔迹窗（_trail）的创建。这个方法还从 SaveConfig 末尾被调，
        // 而 SaveConfig 有若干调用点（跑完动作、提醒到点自行停用），并不都在 UI 线程上。
        // 与其逐个去审那些调用点、并指望以后加的每一个也记得，不如在入口处一次夹住。
        if (!Disp.CheckAccess()) { Disp.Invoke(ApplyMouseHook); return; }

        // 面板总开关是**前置条件**：关了就没有「长按中键唤出面板」这件事可谈，
        // 那半个钩子（以及它必须先吞掉每一次中键按下的那套机制）也就不该装。
        bool wantPress = LongPressGate.ShouldWatch(_config.Settings.PanelEnabled, _config.Settings.PanelMiddleLongPress);
        // 有任何启用的组绑了手势就要监听右键。gate 每次都对着**当前**配置现查（闭包捕获 _config），
        // 所以改手势串不用重装钩子——只有「要不要装 / 中键阈值」这两件事变了才重装。
        // 总开关关掉就完全不监听右键，哪怕底下还留着一堆启用中的手势——
        // 「临时让右键恢复原样」正是这个开关存在的理由，逐条取消勾选再逐条勾回来不是办法。
        // 判据在 GestureGate.ShouldWatch（纯函数、有测试）：这儿碰 Win32，断言够不着。
        bool wantGesture = GestureGate.ShouldWatch(_config.Settings.GesturesEnabled, _config.Gestures);
        // 夹在这里而不是只夹在设置页：这个值能从手改 / 导入的 json 直接进来，
        // 而太小的阀值会让每一次普通中键点击都开面板、中键从此全系统失效（见 ClampLongPressMs）。
        int ms = wantPress ? StepHelpers.ClampLongPressMs(_config.Settings.PanelLongPressMs) : 0;   // 0 = 钩子里不管中键
        if (!wantPress && !wantGesture)
        {
            DisposeMouseHook("off");
            // 号也要动：此刻可能有一次安装飞在半空（见 OnHookInstalled）。不动号的话，
            // 它回来时会以为自己还算数，于是拿一个已经 Dispose 过的钩子去报成功/失败，
            // 并把 `_mouseHook` 重新置回非 null——「关了手势却还挂着钩子」。
            _hookInstallToken++;
            CloseTrail();
            ApplyCursorWatch(false);
            _mouseHookFailed = false;   // 关掉再开时允许重新报错
            return;
        }
        // 最小笔画长度跟着主屏宽度走（见 GestureGate.MinLegForScreen）——固定 40px 在大屏上小得离谱，
        // 右键点一下的手抖就够格成一条腿，把菜单吞了还弹一句没匹配。
        //
        // **它也要进「已经是想要的状态」那个判据。** GestureGate 是不可变的，这个数在构造时就烧进去了；
        // 而下面那个早退只比中键阈值和总开关，于是换分辨率、或者插拔外屏让主屏换了一块之后，
        // 钩子根本不会重建：4K 上装好的 96px 门槛留在 1080p 上（等于要求画满屏宽的 5%，短腿一律被
        // Analyze 判成 ""，手势全部失配、右键菜单照常弹，看起来就是「手势忽然不灵了」，重启才好），
        // 反过来 1080p 装好的 40px 留在 4K 上，正是这个函数存在的意义——过于灵敏——的原样重现。
        // 已知局限（取舍，不是疏漏）：这一个阀值给**所有**屏幕用。上面那段说的是
        // 「主屏换了要重建钩子」，而 4K 主屏 + 1080p 副屏 **同时存在**时，副屏上用的仍是
        // 按主屏算出的 96px（占 1920 宽的 5%，而在 4K 上只占 2.5%）——副屏上手势会难画一些。
        // 没改成逐屏查：那要在右键按下那一刻拿光标去 MonitorFromPoint，而那是钩子回调里的
        // 300ms 硬预算上又多一次 P/Invoke，换来的是副屏上略微好一点的手感。让用户按主屏
        // 调一次更划得来。（选 SM_CXSCREEN 而不是 SM_CXVIRTUALSCREEN 的理由见 PrimaryScreenWidth。）
        int minLeg = wantGesture ? GestureGate.MinLegForScreen(Win32.PrimaryScreenWidth(), _config.Settings.GestureSensitivity) : 0;
        // 中键拖拽的抖动容差按主屏缩放换算成物理像素：低级钩子坐标是物理像素，gate 里那个 6
        // 是逻辑像素的手感。不换算的话 150% 屏上只剩 4 物理像素（约 1mm），按下去那一下抖动
        // 就把长按判成了拖拽，面板永远不弹（见 MouseHook._dragEscapes 注释）。
        // 下界 6：100% 缩放时与旧行为一字不差。
        int middleTolerance = wantPress
            ? Math.Max(6, (int)Math.Round(6 * Win32.PrimaryScale()))
            : 0;
        if (wantGesture && _trail != null) _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        if (_mouseHook != null && _mouseHookMs == ms && _mouseHookGesture == wantGesture
            && _mouseHookMinLeg == minLeg && _mouseHookTolerance == middleTolerance) return;   // 已经是想要的状态
        DisposeMouseHook("reapply");
        var gesture = wantGesture
            // liveEveryPoints: 8 —— LiveMatch 只为笔迹变色服务，按新采样点节流（判据与理由见
            // GestureGate.LiveMatch）。测试里的 gate 默认不节流，语义保持「每点即答」。
            ? new GestureGate((p, proc) => GestureGate.Match(_config.Gestures, p, proc) != null, minLeg, liveEveryPoints: 8)
            : null;
        // 覆盖窗跟着 gate 走：装了手势才建，卸了就关——没配手势的人不该多出一扇窗，哪怕它是隐藏的。
        if (wantGesture)
        {
            // **建这扇窗之前，先关掉本进程的窗口幽灵化。**
            // 它铺满整块屏、又置顶：UI 线程一旦卡顿超过 5 秒被系统判成「未响应」，DWM 就会在它
            // 上面盖一块自己的幽灵窗，而那块窗**点得中**——于是整个桌面点不动，唯一的解法是去杀
            // dwm.exe（用户报的正是这个症状，来龙去脉见 Win32.DisableWindowGhosting 那段）。
            // 放在这里而不是启动处：只有真会摆出全屏覆盖窗的人需要付那份代价（UI 线程真卡住时
            // 不再有「未响应」的壳可拖可关），没开手势的人一点没变。不可逆，所以它自己保证只发一次。
            Win32.DisableWindowGhosting();
            // 卡顿仪表与覆盖窗同时开、同时关（见 ApplyStallWatch）：从这一刻起，UI 线程卡顿
            // 才可能变成「整个桌面点不动」，也就从这一刻起值得记下卡了多久。
            ApplyStallWatch(true);
            _trail ??= new Views.GestureTrailWindow();
            _trail.ApplyStyle(_config.Settings.GestureTrailWidth);
        }
        else CloseTrail();
        _mouseHook = new Native.MouseHook(ms, () => OnTogglePanel?.Invoke(), a => Disp.BeginInvoke(a), gesture, RunByGesture,
            trailPoint: TrailPoint, trailEnd: TrailEnd,
            unmatched: GestureUnmatched, moveTolerancePhysical: middleTolerance);
        _mouseHookMs = ms;
        _mouseHookGesture = wantGesture;
        _mouseHookMinLeg = minLeg;
        _mouseHookTolerance = middleTolerance;
        // 手势与中键面板任一在监听，就要判「钩子还活着吗」：只勾中键面板的用户同样会被静默摘钩。
        ApplyCursorWatch(wantGesture || wantPress);
        // **安装这一步挪到后台线程去等，绝不在 UI 线程上等。**
        //
        // 原先这里是直接 `if (_mouseHook.Install())`，而 Install 内部是 `_installed.Wait(5000)`。
        // 那个 5000 与系统的 `HungWindowTimeout` **一模一样**：一旦真等满，这一轮结束时 UI 线程
        // 恰好被卡在幽灵化阈值上，同一轮里再多做一点点事就越线——于是 DWM 在全屏笔迹窗上盖一块
        // 点得中的幽灵窗，整个桌面点不动（来龙去脉见 Core.UiStallWatch 与 MouseHook.Install 的注释）。
        //
        // 而 ApplyMouseHook 之所以被钉在 UI 线程上，理由是**建那扇 WPF 笔迹窗**（见本方法开头那段），
        // 不是安装钩子。窗已经在上面建好了，安装没有任何理由再占着这条线程——它是纯粹的等待。
        //
        // 号（token）解决「结果回来时已经换人了」：改配置 / 自愈 / 手动重钩都可能在一次安装还在飞的
        // 时候再起一次。那时旧结果必须整个丢掉——不丢的话，旧那次若报了失败，会拿新钩子当失败收尾
        //（Dispose 掉一个已经装好的钩子、关掉笔迹窗、还弹一句「装不上」）。
        //
        // 用裸 Thread 而不是 Task.Run：这里要阻塞最多 5 秒，占着一个线程池线程不值当
        //（线程池被占满时其他 Task 会排队），而安装本来就是个低频动作，一条专属线程最省事。
        // 同 MouseHook 的钩子线程、ReminderActions 的播报线程：都是「一条线程只为一件慢事」。
        var hook = _mouseHook;
        int token = ++_hookInstallToken;
        new System.Threading.Thread(() =>
        {
            bool ok;
            try { ok = hook.Install(); } catch { ok = false; }
            // 调度器正在关闭时 BeginInvoke 会抛：那是退出路径，本来也不需要这份结果。
            try { Disp.BeginInvoke(() => OnHookInstalled(hook, token, ok)); } catch { }
        })
        { IsBackground = true, Name = "Clockwork.HookInstall" }.Start();
    }

    /// <summary>安装结果回到 UI 线程才处理：成功只记日志，失败要收尾（Dispose / 关窗 / 弹气泡）。</summary>
    //
    // 两格都要对：号必须还是最新那一个，且 `_mouseHook` 必须还是本次安装的那个对象。
    // 号是主判据（每一次改动 `_mouseHook` 的路都会动它）；引用那格是兜底——
    // 号只增不减、又是 int，万一哪天有人加了条改 `_mouseHook` 的路却忘了动号，
    // 这一格还能拦住「结果落在一个已经不存在的对象上」。两格都便宜，没必要只留一个。
    private void OnHookInstalled(Native.MouseHook hook, int token, bool ok)
    {
        if (token != _hookInstallToken || !ReferenceEquals(hook, _mouseHook)) return;
        if (ok)
        {
            _mouseHookFailed = false;
            // **只在有「前任的账」时记一行，也就是只在自愈、手动「重新挂钩」、开机后抢链首之后。**
            //
            // 此前这里对**每一次**装上都记一行成功，于是：每次启动一行、每次改中键阈值或手势开关
            // （SaveConfig 末尾会调到这儿）再一行。它们全是「一切正常」，而这个文件是唯一的事后线索、
            // 又只有 128KB——用成功刷掉真故障的历史，恰恰废掉了它存在的理由。
            // 「装上了」这件事状态行已经在报（Deaf / Stale 两档），不必再往错误日志里记一笔。
            //
            // 剩下这几条路值钱，因为它们各自在摘掉旧钩子**之前**抄了一份账（_prevBeat/_prevRight）：
            //   收到过输入吗：一次都没有 = 装的那一刻就不对；有过、停了 = 被 Windows 摘的。两者修法完全不同。
            //   收到过右键吗（按下与抬起分开）：前者 yes 而后者 no，说明右键在到达钩子链之前就被
            //   别人拿走了（鼠标驱动改键、或别的程序的低级钩子吞了它）——那时本程序一切正常，
            //   光看前一格永远查不出来。
            // 而这两条路上 _mouseHook 都已被调用方置空，所以这里不必再自己抄一份 prev：
            // 那个捕获只在「改设置触发的重装」上非空，而那一条正是现在不记的。
            if (_prevBeat == null) return;
            // 英文常量，不走 resx：这个文件是拿去贴 issue 的，读它的人未必读得懂用户那门语言，
            // 而 18 份译文里同一条线索会长出 18 种写法，grep 不到一起。用户要读的那份日志是
            // clockwork.run.log（托盘「查看上次启动日志」），那一份仍然全本地化。
            // source 放最前：它是唯一的分支点，决定后面这些数字该往哪个方向读。
            // heal = 看门狗判死了（可能真的被摘，也可能只是上游钩子卡了一下）；
            // manual = 用户在排障时点的；boot = 开机抢链首的正常一次。
            // 三个数值取自 `_mouseHook*` 那组字段而不是局部变量：安装结果现在是**异步**回来的，
            // 局部变量早就不在作用域里了；而号与引用那两道闸保证了字段里此刻装的就是本次那套参数。
            AppendErrorLog($"mouse hook reinstalled: source={_prevSource} middleHoldMs={_mouseHookMs} "
                           + $"middleTolerance={_mouseHookTolerance} gestures={_mouseHookGesture} "
                           + $"prevEverFired={_prevBeat} prevRightButton={_prevRight ?? "-"} "
                           + $"prev{_prevMiddle ?? "-"} "
                           + $"silentFor={(_prevSince == long.MaxValue ? "never" : _prevSince + "ms")} "
                           // 发不出去 vs 收不到，是两种表现一样、修法完全相反的故障（见 MouseHook.InjectRejected）。
                           + $"injectRejected={_prevRejected} "
                           // 与 silentFor 合看才分得开「链被上游堵」和「钩子被系统摘」（见 _prevUpstream）。
                           // 只报峰值，所以它是**这一任钩子**的账，不是这一刻的瞬时值。
                           + $"upstreamMax={_prevUpstream}ms "
                           // 与 upstreamMax 成对，回答的是**相反**的那个问题：别人堵了我们多久 / 我们自己
                           // 离被摘还有多远（LowLevelHooksTimeout 是回调的预算，本机实测 300ms）。
                           // 这一格非零且接近 300，就说明「钩子被摘」是我们自己造成的，别再去查上游。
                           + $"callbackMax={_prevCallbackMax}ms "
                           // 链被堵时唯一拿得到的人证：那一刻谁在场（见 _prevSuspects）。
                           // 只有 `upstreamMax` 大才有意义——那时是别人堵的，这一格是唯一的线索；
                           // 而它只是候选，不是判决，所以写 `suspects=` 而不是 `culprit=`。
                           + $"suspects={_prevSuspects ?? "-"}");
            _prevBeat = _prevRight = _prevMiddle = _prevSource = null;   // 这份账只用一次，别让它污染下一条非自愈的记录
            _prevSuspects = null;
            return;
        }
        // 装不上（受限令牌、组策略、被安全软件拦）：如实说。静默失败的话，用户会以为
        // 自己按得不够久，一直加长按时间去试一个根本没装上的钩子。
        //
        // **这一类才是这个文件该记的。** 改之前刚好相反：每次装上都记一行成功，装不上却只弹一句气泡——
        // 气泡关掉就没了，于是唯一真正需要事后追查的那种故障，日志里一个字都没有。
        // 和气泡同受 _mouseHookFailed 夹一次：ApplyMouseHook 每次保存配置都会走到，
        // 不夹的话一个装不上的钩子会把日志按保存次数刷满——那正是这轮要修的毛病。
        var win32 = hook.LastError;   // 先取错码，Dispose 之后就没人能问了
        // **按不上也要按关掉那条路收尾**，与上面 `!wantPress && !wantGesture` 那个分支同一套。
        // 漏了不是漏一次：ApplyMouseHook 在每次 SaveConfig 末尾都会走到，而 `_mouseHook` 已经是 null
        // 让上面那个「已经是想要的状态」早退永远不成立——于是每保存一次就新建一个 MouseHook，
        // 每个都带两个 System.Threading.Timer（_alarm、_clickUp），上一个连 Dispose 都没调就被丢了。
        // 轨迹窗与 2 Hz 的「钩子还活着吗」同理：前面刚建完 / 刚上弦，而自愈那一句一看 `_mouseHook == null`
        // 就立即返回，于是那个计时器永远转着、每半秒一次 GetCursorPos，什么也不做。
        hook.Dispose();
        _mouseHook = null;
        CloseTrail();
        ApplyCursorWatch(false);
        if (!_mouseHookFailed)
        {
            AppendErrorLog($"mouse hook install failed: middleHoldMs={_mouseHookMs} gestures={_mouseHookGesture} win32={win32}");
            WarnToast(Strings.Get("Warn_MouseHookFail"));
        }
        _mouseHookFailed = true;
    }

    private void CloseTrail()
    {
        // 仪表跟着覆盖窗一起收。放在 null 早退**之前**：两者由 ApplyMouseHook 同时置位，
        // 但收尾这条路有多个入口（关手势、装钩失败、退出），漏关一格就会留下一个空转的计时器。
        ApplyStallWatch(false);
        if (_trail == null) return;
        _trail.Finish();
        _trail.Close();
        _trail = null;
    }

    // 手势命中：按方向串找**步骤**来跑。串是 Fire 时刻从 gate 拿的快照，
    // 这里再查一遍配置——手势可能刚被改掉，查空就算了。
    //
    // group 类型单拎出来走 ToggleGroupByHotkey 而不是 RunStepAsync：整组运行有「再触发即停」的语义，
    // 热键那边就是这么做的，18 种语言的说明也都是这么写的。走单步执行会丢掉这个停法，
    // 而一个跑了一半停不下来的组，正是急停键存在的理由——不该让手势制造这种局面。
    // 画出来了却没绑任何东西：说一句，别沉默地吞掉。
    //
    // 沉默把三件完全不同的事混成一件——「手势总开关关着」「这条笔画没绑动作」「动作跑失败了」，
    // 在用户眼里都是「按了没反应」，而这三件事要做的补救完全不同。报出**画出来的那一串**，
    // 用户当场就能对照管理器里的列表：形状对不上就再画一次，形状对得上就是别处出了问题。
    // **就地说，别弹通知卡。** 从前这里走 ShowToast：一张带标题带图标、从屏幕角落滑进来的卡片，
    // 只为说一句「↑↓ 没绑动作」。份量和事情完全不匹配——手势是一划而过的操作，反馈却比操作本身还重，
    // 而且出现在离你的手一整个屏幕远的地方，眼睛得先找到它。
    // 现在把这一串画在笔迹那扇覆盖窗上、收笔点旁边，亮 0.9 秒自己消失：你在哪收的笔，答案就出在哪。
    // 要报的信息一个字没少（画出来的那一串），只是不再打断你。
    private void GestureUnmatched(string path)
        => _trail?.Note(GestureGate.Arrows(path));

    private void RunByGesture(string path, IntPtr origin)
    {
        var proc = Win32.ProcessNameForWindow(origin);
        var step = GestureGate.Match(_config.Gestures, path, proc);
        if (step == null) return;
        // 面板若还开着，先关掉。中键面板是靠 ForceForeground 抢过前台的，而右键手势的按下被钩子吞掉、
        // 不会激活光标下的窗口——于是面板会一直占着前台，动作里凡读前台的（「当前窗口」、发键、发文本）
        // 全都落到 Clockwork 自己头上，被自家进程守卫拒掉，看起来就是「手势丢了焦点、没反应」。
        // 每个面板格子在跑之前也是先 Dismiss()（见 QuickPanelWindow），手势这条路得照做——经 App 接出的 DismissPanel。
        DismissPanel?.Invoke();
        // origin 是起笔点所在的窗口（MouseHook 在按下那一刻解析）：窗口管理类「当前窗口」打它，
        // 而不是前台窗——在后台窗口上起笔时这两者不是同一个（见 Win32.WindowAtPoint）。
        // 手势这一档：成功不弹回执、也不设防连点闸（两条理由都在 RunStepAsync 的 by 参数上）。
        RunGestureStep!(step, origin);
    }
}
