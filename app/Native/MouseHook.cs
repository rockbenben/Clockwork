using System.Runtime.InteropServices;
using Clockwork.Core;

namespace Clockwork.Native;

// 全局低级鼠标钩子，管两件事：长按中键唤出快捷面板；按住右键画手势跑动作组。
// 判定逻辑全在 Core.LongPressGate / Core.GestureGate（纯状态机、有测试），这里只负责
// Win32 那一半——装钩子、认事件、按裁决吞掉或补发。两个 gate 各管各的键，互不知道对方。
//
// 三条硬约束，写在最前面，因为每一条踩下去都是很难自查的故障：
//
// 1) **必须装在有消息循环的线程上**。WH_MOUSE_LL 的回调是靠给安装线程投消息驱动的，
//    装在没有泵的线程上会「装得上、永不回调」。本类**自带一条专用钩子线程**（Install 时启动，
//    里面就是一个 GetMessage 泵），不挂在 WPF UI 线程上——这是刻意的：UI 线程同时负责
//    全屏笔迹分层窗的合成提交，DWM/GPU 一旦卡顿（黑屏看门狗那类故障），WPF 派发随之停摆，
//    钩子若装在那条线程上，回调必超 LowLevelHooksTimeout、被 Windows 静默摘掉，已被吞掉的
//    右键/中键再没人补发，于是「显示抖一下」被放大成「全系统鼠标死掉」。独立泵让钩子的存活
//    与显示管线彻底脱钩。gate 的读写因此只发生在钩子线程上；要碰 UI 一律经 _post。
//
// 2) **回调里绝不做慢活**。Windows 有个 LowLevelHooksTimeout（默认 300 毫秒）：回调超时
//    系统就**静默**把这个钩子踢掉，之后既没有回调也没有报错，表现为「用了一会儿突然失灵」。
//    开窗口远远不止 300 毫秒（首次要展开整份模板），所以唤出面板一律 post 给 UI 队列，
//    回调本身立刻返回。SendInput 是微秒级的，留在回调里安全。
//
// 3) **必须认出自己补发的事件**。短按放行要补发一次真中键，那次注入会再次进本回调；
//    不认就会被自己吞掉，于是中键在全系统失灵——正是本功能最该避免的后果。
//    认的办法是 dwExtraInfo 上的 Win32.InjectTag 签名。
public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr DwExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int PtX, PtY;
    }

    // 投给钩子线程泵的私有消息：中键长按闹钟到点，回钩子线程问 gate 要不要弹面板。
    private const uint WM_POLLDUE = 0x8001;   // WM_APP + 1
    private const uint WM_QUIT = 0x0012;

    private readonly LongPressGate? _gate;
    private readonly GestureGate? _gesture;
    private readonly Action<string> _unmatched;
    private long _beat;        // 回调最后一次被叫到的时刻（Environment.TickCount64）
    // 右键的按下与抬起**分开记**。这不是记账癖：只收到抬起、从没收到按下，是「有人排在我们
    // 前面把按下吞了」的确凿指纹——手势类工具正是这么干的（吞掉按下去判断是不是手势，
    // 抬起时再放行或补发）。合成一格的话，这个唯一能自证的信号就没了。
    private long _beatRDown, _beatRUp;
    // 中键同样分着记：中键是面板的触发键，「移动心跳正常、中键一次都没到」就是上游钩子吞键
    // 或前台 UIPI 拦截的指纹，与全钩死寂不是一种病。
    private long _beatMDown, _beatMUp;
    // **上游延迟的峰值**：这条事件在系统里生成（MSLLHOOKSTRUCT.time）到我们的回调被叫到，
    // 中间隔了多久，取装上以来的最大值。
    //
    // 为什么这一格值得单独留：低级钩子是**按安装顺序串成一条链**的，系统要挨个叫过去才轮到我们。
    // 于是「链上排在我们前面的那个堵住了」会表现成一串自相矛盾的现象——
    //   · 光标照动（位置由 win32k 直接更新，根本不走钩子链）；
    //   · 按钮却点不动（按钮事件必须走完整条链才投递到窗口）；
    //   · 我们自己的钩子**活着**（心跳最终还是会来，只是迟到），于是「掉了就重装」那套自愈
    //     看着像好了、其实什么都没治。
    // 本机同时跑着 Logi Options+ / PowerToys / 热键助手，三家都装 WH_MOUSE_LL —— 这条路不是假想。
    //
    // 与 SinceBeatMs 分工：那一格量「多久没被叫到」（死寂），这一格量「叫到了、但迟到了多久」。
    // 死寂 + 迟到都大 = 链被堵；死寂大而迟到不大 = 钩子真被系统摘掉了（LowLevelHooksTimeout）。
    private uint _upstreamMax;
    // **我们自己回调耗时的峰值**（进回调到出去，取装上以来的最大值）。
    //
    // 为什么必须与 _upstreamMax 成对：上游延迟回答「是不是**别人**堵了链」，这一格回答
    // 「是不是**我们自己**要被摘了」。Windows 的 LowLevelHooksTimeout（本机实测 300ms，
    // HKCU\Control Panel\Desktop）是**回调**的预算，超了就把钩子**静默摘掉**；
    // 而摘掉之后上游延迟永远停在最后那个小值上，看着一切正常。
    //
    // 为什么不靠 `hook-callback` 那行 slow 日志就够：它的阈值是全局的 500ms，
    // 而预算是 300ms —— **300~500ms 这一段恰好就是「我们正在把自己搞死」的区间，
    // 却一声不响**。这一格没有阈值、也不会刷屏（一任钩子只有一个数），
    // 只在自愈/重钩那一行报出来；那时它接近 300 就是铁证。
    private uint _callbackMax;
    // 面板到点唤出过几次（闹钟与回调内追赶轮询共用，见 PollFireAndPost）。
    private int _middleFired;
    // 长按被改判成「中键拖拽」的次数：按住后位移超出容差就归还按下、撤防闹钟、面板不再弹。
    // 这格是症状「按住很久、松手也不弹」在生产里唯一能自证的指纹——它非零就说明物理事件
    // 全都到了、是我们自己的抖动容差把这次按判成了拖拽，而不是被谁吞了。
    private int _dragEscapes;
    // 一笔手势按住不放的上限。到点还没收到抬起，就当那条抬起丢了（安全桌面）并收笔。
    // 取 5 秒是「宽到不会误伤真手势、又短到不至于让笔迹挂很久」：真手势一两秒到头，
    // 而没有这道兜底的话，丢失的抬起要挂到下一次右键才收得掉。
    private const long GestureStaleMs = 5000;
    private readonly Action _fire;
    private readonly Action<string, IntPtr> _fireGesture;
    // 这一笔手势**起笔点**所在的顶层窗口（右键按下那一刻解析）。手势的「当前窗口」动作要打的是它，
    // 不是 GetForegroundWindow——按下被本钩子吞掉、系统不会激活光标下的窗，在后台窗口上起笔时
    // 前台仍是另一个窗（见 Win32.WindowAtPoint 的注释）。一笔手势串行，单字段即可。
    private IntPtr _gestureOrigin;
    // 这一笔手势起笔时的**前台窗口 pid**（纯 user32 查的，不碰别的进程）。
    // 单独留一格是因为进程名在按下那一刻多半还没算出来，抬起前要按同一个 pid 再问一次
    // （见 CallbackCore 里那段「抬起那一刻再问一次」）。
    private uint _gesturePid;
    private readonly Action<int, int, bool> _trailPoint;   // 第三个参数：画到此刻已经命中了吗（笔迹据此变色）
    private readonly Action _trailEnd;
    private readonly Action<Action> _post;
    private readonly Func<bool> _modifierHeld;

    // 「起笔窗是谁家的程序」与「这一下能不能接管成手势」两句话的答案，由 HookProbe 在**后台线程**
    // 上算好在这里；回调只做两次读。生产走 HookProbe，单测注入假实现。
    //
    // **为什么必须搬走**（理由与实测数字见 HookProbe 类头）：这两句都要 OpenProcess，
    // 而低级鼠标钩子回调是全局串行的，那一路上的 300ms 预算超了系统会**静默**摘掉钩子。
    // 本机日志里 `hook-proc-name took 3187ms` 与 `hook-callback took 6469ms chain=0ms`
    // 就是这么来的——chain=0 排除了别人也排除了注入，整整几秒全是这两句。
    // 用户看到的是「按右键时整机鼠标顿一下」，而 Windows 不会为它留下任何一行日志。
    private readonly Func<uint, string> _procNameForPid;
    private readonly Func<uint, bool> _mayEngage;

    // ── 笔迹回调合帧 ──
    // 不把每个采样点都 post 一条重绘：high 灵敏度 + 高刷屏一笔能产生几百个采样点，每个都让
    // UI 线程对一块**整屏大小**的分层窗重合成一次（4K ≈ 33MB/帧）。只记最新一点，且 UI 队列里
    // 同一时刻最多挂着一个 drain；drain 跑起来先放号再画，期间的新点会再排一个。于是空闲时
    // 每点照常一画，爆发时自动塌缩成「每轮只画最新位置」，队列永不被重绘塞满。
    private readonly object _trailLock = new();
    private int _trailX, _trailY;
    private bool _trailArmed;
    private int _trailQueued;

    // 委托必须由本对象持有：SetWindowsHookEx 只存了个裸函数指针，GC 不知道它被引用着。
    // 只传一个 lambda 进去、不留字段的话，下一次 GC 之后回调地址就是野指针——进程直接崩，
    // 而且崩在任意一次鼠标移动上，看不出和这里有关。
    private readonly HookProc _proc;
    private IntPtr _hook;

    // ── 专用钩子线程（见类头第 1 条）──
    // Install 之前一直是 null：单测反射驱动 Callback、不调 Install，构造函数绝不能偷偷开线程。
    private Thread? _thread;
    private uint _threadId;                                  // 原生线程 ID（PostThreadMessage 要的不是托管 ID）
    private readonly ManualResetEventSlim _installed = new(false);
    private int _installResult;                              // 1=成功 0=SetWindowsHookEx 失败 -1=抛异常
    private int _disposePosted;                              // 防重复拆除（Dispose 可能从多条路进来）
    // 欠系统的那次「按下/抬起」已经还过了没有（见 SettleOwedKeys）：泵线程与工作线程抢这一格。
    private int _downsSettled;
    // **还挂着、没退干净的泵线程数**——进程级，因为钩子链是全进程的，而每一次重装用的是一个新对象。
    // 这条计数是自愈唯一能知道「上一次重装把线程卡住了」的途径，而它正是下一次该不该动手的判据
    //（见 GestureCoordinator.HealMouseHookIfDead）：那条线程卡在链里，新装的那个多半卡在同一处，
    // 而每一次摘装之间都有一段输入空窗。
    // 这条计数在**放弃等待**时一并减掉（30 秒到了还不知道它死没死）：那时「不再自愈」的代价
    // 比「白重装一次」大，宁可放行。
    private static int _strandedThreads;
    internal static int StrandedTeardowns => Volatile.Read(ref _strandedThreads);

    // 到点的闹钟：按满时长就弹面板，不等抬起。按下时上弦，抬起 / 判成拖拽 / 卸载时收掉。
    // 回调在线程池线程上来，但它只做一件事——往钩子线程投一条 WM_POLLDUE（见 PollDue），
    // gate 的所有读写只发生在钩子线程上，不必加锁。
    private readonly System.Threading.Timer _alarm;
    private readonly int _holdMs;

    private readonly System.Collections.Concurrent.BlockingCollection<int>? _replay;
    private Thread? _replayThread;
    // 补发间隔与「注入绝不在回调里」是同一条决定的两半，见 ReplayClick / ReplayLoop。
    //
    // **不能是 0，而同一批 SendInput 里的 down+up 就是 0**：两条事件的时间戳完全相同，
    // 于是补发出去的是一次「零长度」的点击。不少程序据此把它当噪声丢掉（自己做按下-抬起
    // 配对判定的、以及 Chromium 系那种异步处理输入的），表现就是**快速点一下右键没有菜单**，
    // 而按住久一点反而正常——因为那条路走的是「把按下还给系统」，时序是真实的。
    //
    // 15ms 是 WGestures / StrokesPlus 等主流工具验证过的甜蜜点：
    //   · 低于 Chromium 等过滤零长度点击的阈值（~10ms），不会被当噪声丢掉；
    //   · 低于人眼视觉暂留（~16ms）和绝大多数程序一帧绘制的周期，
    //     目标程序来不及在屏幕上产生可见变化——于是没有「闪一下再出菜单」的中间态。
    // 曾经试过拿用户真实按住时长当间隔：菜单时机倒是跟松手对齐了，
    // 但 100–200ms 的间隔让目标程序有充足时间处理 DOWN 并改变状态（选中文件、移光标），
    // 那才是「闪」的真正来源。间隔短了，闪就没了。
    private const int ClickGapMs = 15;
    // 补发队列的容量：连点右键时排在后面的几笔。满了就当场报「投不进去」，让调用方放行真事件——
    // **绝不静默丢**（那是 NextGestures 的 `let _ = tx.try_send(ev)` 留的洞：丢掉的那一次
    // 就是用户「被吞掉的一下右键」）。1024 是它选的数，64 对我们够用且更早开始让路。
    private const int ReplayQueueCapacity = 64;

    // 摘钩时 UI 线程最多陪钩子线程走多久（见 Dispose）。正常拆除是亚毫秒级——WM_QUIT 排在
    // 那条线程自己的队列里，它下一条 GetMessage 就返回。所以要 200ms 只是为了不误伤慢机器，
    // 而**不能拿它当「等它退干净」的预算**：等满就是钩子线程正卡在回调里，那时 UI 线程跟着
    // 冻住，屏幕上那扇整屏覆盖窗没人收（实测旧写法冻 2015ms，见 Dispose 里那段）。
    // 上限由 MouseHookThreadTests 钉着，改回 1000 会让那条路原样回来。
    private const int UiJoinBudgetMs = 200;
    // 后台替系统保住 _proc 那个托管根的时长。放弃得比旧写法（2 秒）晚一个量级，
    // 因为这里多等不痛——它不占 UI 线程，而少等一次的代价是随机崩进程。
    private const int BackgroundJoinBudgetMs = 30_000;

    // 补发的按下已经发出、抬起还欠着——欠的是哪个键：0=没有，1=右键，2=中键。
    // 卸载时要补上，否则那个键会在全系统卡在按下态，是这个类最不能出的一种事故。
    //
    // 写这一格的现在只有补发线程（`ReplayLoop` 在按下与抬起之间那段 15ms 里挂着它），
    // 结清的有两条路：那条线程自己，以及它没能跑完时 `SettleOwedKeys`（Exchange 保证只发一次）。
    // 从前这里由 `_clickUp` 闹钟结清，而闹钟与回调抢同一格——补发整体上移到那条线程之后，
    // 闹钟和它的跨线程竞态一起没了（见 ReplayClick）。
    private int _clickUpDue;
    // 注入被系统拒掉的次数（UIPI：前台是提权窗口时，非提权进程的 SendInput 会被静默丢弃）。
    //
    // 这一格是**诊断**，不是状态。补发线程发现被拒时已经没有「放行真事件」这条路可选——
    // 那次真抬起早被吞掉了。可做的只有两件事：抬起前用 `_canInject` 先问一句（Win32
    // .InputNotBlockedNow，取向是「读不准就当送不进去」，所以宁可放行也不吃掉），
    // 以及把真被拒的次数留着可查。不记的话，「右键拖拽在某些窗口上就是没反应」永远只能靠猜。
    private int _injectRejected;
    // 队列满了 / 补发线程还没起来而**投不进去**的次数。每一次都当场让调用方放行真事件，
    // 所以它不影响正确性；留着是防止这条路变成静默的（NextGestures 的 `let _ = tx.try_send`
    // 就是把丢事件藏起来了）。
    private int _replayDropped;
    // 补发线程上**一次注入**的峰值耗时（不含那 15ms 间隔——间隔是我们自己定的，不是被谁按住）。
    // 从前这一格长在回调的 `inject=` 里，与 `chain=` 各半；注入移出回调之后，回调那一格
    // 就该恒为 0，而耗时并没有消失，它换了名字待在这里。验收口径就写在这两个数的对比上。
    private int _replayMaxMs;
    // 排队排进去过多少次（右键/中键合在一起）：证明「补发确实在跑」，而不是静默地被跳过。
    private int _replayQueued;
    // 「这次注入送不送得进去」的探子。单测注入用，生产走 `Win32.InputNotBlockedNow`。
    private readonly Func<bool> _canInject;

    /// <param name="holdMs">按住多久算长按；&lt;=0 表示不管中键（只做手势）。</param>
    /// <param name="fire">判定为长按时要做的事（唤出面板）。会经 <paramref name="post"/> 派发，不在回调里直接跑。</param>
    /// <param name="post">把动作投到 UI 队列的方式（App 传 Dispatcher.BeginInvoke）。</param>
    /// <param name="gesture">右键手势的判定器；null 表示不监听右键——没配手势时右键必须一根毫毛都不动。</param>
    /// <param name="fireGesture">手势命中时要做的事，参数是方向串与起笔点所在的顶层窗口句柄（取不到为 Zero）。
    /// 同 fire，经 post 派发；句柄在按下那一刻解析，动作里的「当前窗口」打它而不是前台窗口。</param>
    /// <param name="trailPoint">屏幕上那条笔迹又画到了哪（物理像素），以及**画到此刻是否已经命中**。
    /// 同样经 post——低级钩子回调里有 LowLevelHooksTimeout 的预算，绝不在这里碰 UI。
    /// 命中与否在回调线程上算完（GestureGate.LiveMatch，纯计算、微秒级）再把结论捎过去：
    /// 把 gate 的内部状态交给 UI 线程去读，会撞上「钩子线程正往里加采样点」的竞态。</param>
    /// <param name="trailEnd">笔迹收笔（抬起、或钩子卸载）。</param>
    /// <param name="unmatched">画出来了却没绑任何东西时报一声，参数是画出来的方向串。</param>
    /// <param name="modifierHeld">修饰键按住判定（单测注入用，生产留空查物理键态）。</param>
    /// <param name="canInject">抬起前问一句「这次注入送得进去吗」（单测注入用，生产留空查 UIPI/安全桌面）。</param>
    /// <param name="moveTolerancePhysical">按住期间位移超过多少**物理像素**判为拖拽（抖动容差）。
    /// 0 = 用 gate 默认值 6——那是 100% 缩放下的手感；缩放屏必须由调用方按 DPI 换算后传进来
    ///（见 GestureCoordinator.ApplyMouseHook / Win32.PrimaryScale）。</param>
    /// <param name="probe">把「这一下能不能接管」「起笔窗是谁家的程序」两句交给后台线程算的探针
    ///（生产传 <see cref="HookProbe"/>；为 null 时退回同步实现，只在单测里成立）。
    /// 传对象而不是两个委托，是因为它同时管着生命周期与「答案是哪一次算出来的」这件事。</param>
    public MouseHook(int holdMs, Action fire, Action<Action> post,
                     GestureGate? gesture = null, Action<string, IntPtr>? fireGesture = null,
                     Action<int, int, bool>? trailPoint = null, Action? trailEnd = null,
                     Action<string>? unmatched = null, Func<bool>? modifierHeld = null,
                     int moveTolerancePhysical = 0, Func<bool>? canInject = null,
                     HookProbe? probe = null,
                     Func<uint, string>? procNameForPid = null, Func<uint, bool>? mayEngage = null)
    {
        _trailPoint = trailPoint ?? ((_, _, _) => { });
        _trailEnd = trailEnd ?? (() => { });
        _gate = holdMs > 0
            ? new LongPressGate(holdMs, moveTolerancePhysical <= 0 ? 6 : moveTolerancePhysical)
            : null;
        _gesture = gesture;
        _unmatched = unmatched ?? (_ => { });
        _holdMs = holdMs;
        _fire = fire;
        _fireGesture = fireGesture ?? ((_, _) => { });
        _post = post;
        _proc = Callback;
        _modifierHeld = modifierHeld ?? Win32.ModifierHeld;
        // 建成停着的（Infinite）：按下时才上弦。
        //
        // 闹钟到点只往钩子线程投一条 WM_POLLDUE：PollFire 改 gate 状态，必须与 Callback 同线程
        //（旧实现装在 UI 线程上时它是 post 回 UI 的）；真要弹面板再由钩子线程 _post 给 UI。
        //
        // **回调要把投递兜住。** 调度器关闭 / 钩子线程已退出时 PostThreadMessage 会失败，
        // 而这里是线程池线程——逃出去的异常没人接，直接终结进程，且 clockwork.error.log 里一个字都没有。
        // Timer.Dispose 挡不住这一下：它不取消**已经派发出去**的那次回调，本类的 Set() 里那句
        // `catch (ObjectDisposedException)` 承认的正是同一个竞态。
        // 触发窗口很窄但很日常：中键按住（上了 _alarm 的弦）的那一瞬从托盘退出。
        _alarm = new System.Threading.Timer(_ => PollDue(), null,
                                            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        // 补发点击的队列在这里建，**消费者线程要等 Install 才起**（构造函数不许偷开线程，
        // 与钩子线程同一条纪律，见 MouseHookThreadTests.Constructor_does_not_start_any_thread）。
        // 没 Install 时队列只进不出，测试因此可以驱动补发路径而不往开发机桌面上注入真按键。
        _replay = new System.Collections.Concurrent.BlockingCollection<int>(ReplayQueueCapacity);
        _canInject = canInject ?? Win32.InputNotBlockedNow;
        // 探针在场就用它的答案（后台线程算好的快照），不在场退回同步实现——生产永远传探针，
        // 所以「回调里做跨进程查询」这条路在生产里已经不成立（理由见字段处与 HookProbe 类头）。
        // 两个都**显式取方法组**：写成 `probe?.ProcessName ?? other` 会让编译器报「方法组不可为 null」
        //（方法组不是值类型，没法用 ?? 判空）。
        _procNameForPid = probe != null ? probe.ProcessName
                        : procNameForPid ?? Win32.ProcessNameForPid;
        _mayEngage = probe != null ? probe.MayEngage
                   : mayEngage ?? (_ => true);
    }

    // 补发一次点击：投给补发线程，回调**立刻返回**。返回 false = 这一次注定送不进去（探针答
    // 「被拦」）或排不进去（队列满 / 线程没起来），调用方必须放行真事件，绝不能吞。
    //
    // 为什么非搬不可（2026-10-06 本机读数，不是推测）：一整任钩子（19:06 装、两小时真实使用）
    // 的账是 `callbackMax=313ms chainMax=125ms injectMax=313ms`——回调里最慢的一件事是我们自己的
    // `SendInput`，比链上七家在场的工具加起来还长，而 313ms 已经越过系统摘钩的预算
    //（`LowLevelHooksTimeout`，本机 300ms，见类头第 2 条）。越过的那一刻钩子被**静默**卸掉，
    // 而此刻正被 `return 1` 扣着的那次按下没人补发——用户报的「右键多点几下就迟缓、而且经常被吞」
    // 就是这两件事的合起来的样子。类头第 2 条那句「SendInput 是微秒级的，留在回调里安全」被这组
    // 读数推翻，改这条纪律由 ReplayOffCallbackTests 钉着。
    //
    // 同类的两家 Windows 实现都不在回调里注入：WGestures 回调只 `Post(WM.GESTBTN_*)`、
    // `SimulateMouseEvent` 在队列消费侧；NextGestures 回调只 `tx.try_send`（hook.rs:203 →
    // engine.rs:109）。五家里只有 MouseInc 在回调里 `::SendInput`，而它的形状比我们还重。
    //
    // 搬走时不能丢掉的老半件事：旧写法拿 `SendInput` 的返回值**当场**决定「被拒就放行真抬起」，
    // 而抬起一吞就再也还不回去（那一下右键彻底消失，等于把用户报的症状换个成因）。所以这里先
    // 同步问一句「送得进去吗」，答「问不准」也当送不进去（取向见 Win32.IntegrityBlocksInjection），
    // 只有能送的那一档才排队。探针答能、注入仍被拒是剩下的窄缝（这几毫秒里前台恰好换成提权窗），
    // 计进 `_injectRejected`。
    private bool ReplayClick(int which)
    {
        if (!_canInject()) return false;
        return EnqueueReplay(which);
    }

    // 投一件补发进队列。投不进去当场报 false，并计一笔 `_replayDropped`——**绝不静默丢**：
    // NextGestures 的 `let _ = tx.try_send(ev)` 把丢事件藏起来了，而每丢一次就是用户的一下右键
    // 凭空消失，那正是我们这次要治的病，不能治病的方子里自带一遍。
    private bool EnqueueReplay(int which)
    {
        var q = _replay;
        if (q == null || q.IsAddingCompleted || !q.TryAdd(which, 0))
        {
            Interlocked.Increment(ref _replayDropped);
            return false;
        }
        Interlocked.Increment(ref _replayQueued);
        return true;
    }

    // 补发线程本体：一条一件，按下 → 隔 ClickGapMs → 抬起，全在同一条线程上，**顺序天然串行**。
    // 从前这件事靠 `_clickUpDue` 那格跨线程欠账台账保证（闹钟、Dispose、下一次补发前的清账三方
    // 抢同一格，还键只能还一次要用 Interlocked.Exchange）——队列把它替掉了，那套状态机跟着
    // `_clickUp` 闹钟一起没。
    //
    // 为什么还留着 15ms 而不是学 NextGestures 把 down+up 收进同一次 `SendInput`：同批两条事件
    // 时间戳完全相同，等于一次「零长度点击」，会被下游当噪声丢掉（见 ClickGapMs 那段本地实测口径）。
    // 补上补发点击欠着的那次抬起。发过就不再发（Exchange 保证只发一次）：
    // 补发线程自己结清，它没能跑完时由 `SettleOwedKeys` 结清——多发一次抬起会打断用户随后
    // 真按下去的那一下，少发一次就是那个键在全系统卡在按下态。
    private void ReplayUp()
    {
        switch (Interlocked.Exchange(ref _clickUpDue, 0))
        {
            case 1: Win32.SendRightUpTagged(); break;
            case 2: Win32.SendMiddleUpTagged(); break;
        }
    }

    private void ReplayLoop()
    {
        var q = _replay;
        if (q == null) return;
        try
        {
            foreach (int item in q.GetConsumingEnumerable())
            {
                switch (item)
                {
                    case 1:   // 一次完整的右键点击（抬起被改判成普通点击）
                        if (SendTimed(() => Win32.SendRightDownTagged()))
                        {
                            Volatile.Write(ref _clickUpDue, 1);
                            Thread.Sleep(ClickGapMs);
                            ReplayUp();
                        }
                        break;
                    case 2:   // 一次完整的中键点击（短按）
                        if (SendTimed(() => Win32.SendMiddleDownTagged()))
                        {
                            Volatile.Write(ref _clickUpDue, 2);
                            Thread.Sleep(ClickGapMs);
                            ReplayUp();
                        }
                        break;
                    case 3:   // 只还一次按下：扣着的按下还给系统，随后那条真事件自己配对
                        SendTimed(() => Win32.SendRightDownTagged());
                        break;
                }
            }
        }
        catch
        {
            // 队列被 Dispose / 线程被中止：补发就此停住。不会留下卡住的按键——摘钩那一路
            // 由 SettleOwedKeys 无条件还键并结清欠着的抬起，与这条线程活不活无关。
        }
    }

    // 一次注入：耗时记进 `_replayMaxMs`，被 UIPI 丢掉就计 `_injectRejected`。只在补发线程上跑。
    // 返回「送出去了没有」——按下被丢时不该再补一次无配对的抬起（见上面的台账）。
    private bool SendTimed(Func<uint> send)
    {
        long t = Environment.TickCount64;
        uint sent;
        try { sent = send(); }
        finally
        {
            long ms = Environment.TickCount64 - t;
            if (ms > Volatile.Read(ref _replayMaxMs)) Volatile.Write(ref _replayMaxMs, (int)ms);
        }
        if (sent == 0) { Interlocked.Increment(ref _injectRejected); return false; }
        return true;
    }

    // 起补发线程。与钩子线程同起同收（见 Install / Teardown）。
    // 必须 `IsBackground`：否则用户点了托盘退出，进程还要陪一条睡在队列上的线程赖着不走
    //（本仓库为写盘线程钉过同一件事，见 OffUiThreadTests）。名字是排障时认出它的唯一办法。
    private void StartReplayWorker()
    {
        if (_replayThread != null) return;
        _replayThread = new Thread(ReplayLoop) { Name = "Clockwork.Replay", IsBackground = true };
        _replayThread.Start();
    }

    // 超时兜底丢掉一笔手势后，**还欠系统一次右键按下**（它已经被我们 return 1 吃掉了）。
    //
    // 不在兜底那一刻就补发，因为那一刻分不清两种原因，而它们要的处理恰好相反：
    //   · 抬起真的丢了（UAC 安全桌面吃掉）——手指已经松了，补一个按下就是一个永不释放的卡死按键。
    //   · 真的按过了五秒——手指还在，不补则稍后那次真抬起无配对按下，
    //     DefWindowProc 凭它合成一个莫名的右键菜单，自己配对按下-抬起的程序（资源管理器
    //     右键拖拽、3D 视口）落入错的拖拽状态。
    //
    // 于是把这笔账记下来，等**真抬起到了**再还：手指还在就正好配成一次普通右键（按了
    // 五秒才松，给他右键菜单是对的）；抬起真的丢了就永远没人来领，一下也不会注入。
    // 还的时候走 ReplayClick(1)——按下、吞掉当前这条真抬起、抬起由补发线程隔 ClickGapMs 补上，
    // 顺序由那条线程的队列保证，否则注入的按下会排到真抬起后面去。
    private bool _owedRightClick;

    // 线程池闹钟到点：把询问投回钩子线程。PollFire 改 gate 状态，与 Callback 争用不得，
    // 而定时器回调跑在线程池线程上。钩子线程还没起来（单测直接驱动 Callback 的场景）时
    // 退回旧路：经 _post 执行——测试里的假 post 同步跑，生产里没有 Install 就不会有闹钟上弦。
    private void PollDue()
    {
        uint id = _threadId;
        if (id != 0) { try { PostThreadMessage(id, WM_POLLDUE, IntPtr.Zero, IntPtr.Zero); } catch { } }
        else { try { _post(FireIfDue); } catch { } }
    }

    // 闹钟响了：问 gate 是不是真该弹（中途抬起 / 拖走的话它会说不该）。只在钩子线程上跑；
    // 要弹的动作本身仍经 _post 交给 UI——回调/泵线程绝不直接碰 UI（类头第 2 条）。
    private void FireIfDue() => PollFireAndPost(Environment.TickCount64);

    // 到点询问 + 唤出的唯一入口，闹钟（WM_POLLDUE）与回调里的追赶轮询共用。
    //
    // **回调里也必须问一遍（追赶轮询）。** 闹钟那一跳是「线程池 Timer → PostThreadMessage →
    // 钩子线程 GetMessage → Dispatcher」四级接力，任一级排队（开机线程池饥饿、按住时密集的
    // 鼠标事件流）都会让「到点弹」迟到，手感就是「按很久不弹、松手才弹」——松手那条
    // WM_MBUTTONUP 里还有 OnMiddleUp 的 Fire 裁决兜底，所以它总是恰好在松手时弹。
    // 而按住期间 WM_MOUSEMOVE 每秒上百条，每条都在这里问一句（纯状态判定、微秒级、
    // Pending 时才真算），闹钟投递再慢，面板也会在下一条输入事件上准点弹。
    // 闹钟由此降级为「手完全不动、一条输入都没有」时的兜底。PollFire 只说一次 yes，两条路幂等。
    private void PollFireAndPost(long nowMs)
    {
        if (_gate == null || !_gate.Pending || !_gate.PollFire(nowMs)) return;
        Interlocked.Increment(ref _middleFired);
        try { _post(_fire); } catch { }
    }

    // 上弦 / 收弦。收弦不必等待回调结束——最坏是一次已经在路上的询问照常发生，
    // 而 PollFire 本身会因为 gate 已不在 pending 而什么都不做。
    private static void Set(System.Threading.Timer t, int dueMs)
    {
        try { t.Change(dueMs, System.Threading.Timeout.Infinite); }
        catch (ObjectDisposedException) { }   // 正在卸载：闹钟已释放，不必再管
    }

    private void Arm(bool on) => Set(_alarm, on ? _holdMs : System.Threading.Timeout.Infinite);

    // 笔迹采样点合帧（见字段处注释）。Callback（钩子线程）调用，只更新最新值、至多排一个 drain。
    private void ScheduleTrail(int x, int y, bool armed)
    {
        lock (_trailLock) { _trailX = x; _trailY = y; _trailArmed = armed; }
        if (Interlocked.Exchange(ref _trailQueued, 1) == 0)
        {
            // _post 在调度器关闭时会抛：把号放回去，免得标志永远卡在 1、下一笔一笔都画不出来。
            try { _post(DrainTrail); }
            catch { Volatile.Write(ref _trailQueued, 0); }
        }
    }

    // UI 线程执行：先放号再取快照——放号后到取数之间进来的新点会再排一个 drain，最新坐标丢不了。
    private void DrainTrail()
    {
        Volatile.Write(ref _trailQueued, 0);
        int x, y;
        bool armed;
        lock (_trailLock) { x = _trailX; y = _trailY; armed = _trailArmed; }
        _trailPoint(x, y, armed);
    }

    /// <summary>距离回调最后一次被叫到过了多久（毫秒）。从没被叫到过时返回 long.MaxValue。</summary>
    //
    // **判「钩子还活着吗」只能靠它。** Windows 没有「查一个钩子还在不在」的 API，
    // 而被它静默卸掉时，本进程这边 _hook 句柄原样不动、UnhookWindowsHookEx 也照样成功——
    // 于是「装没装」这个字段会一直说「装着」，正是在最需要它说真话的那一次。
    // 鼠标一动就有 WM_MOUSEMOVE，所以「刚才动过鼠标却没有戳」就等于「钩子没了」。
    /// <summary>这个钩子自装上以来，回调**有没有被叫到过哪怕一次**。</summary>
    //
    // 与 SinceBeatMs 分开报：这两种失败的成因完全不同。
    //   一次都没有  → 装的那一刻就不对（最常见的是装在了没有消息泵的线程上：
    //                 低级钩子的回调是投递到装它那个线程的消息队列上的，句柄却照样有效）。
    //   有过、停了  → 装对了，是事后被 Windows 摘掉的（LowLevelHooksTimeout）。
    public bool EverBeat => Volatile.Read(ref _beat) != 0;

    /// <summary>这个钩子有没有收到过**右键**消息（区别于「收到过任何鼠标消息」）。</summary>
    //
    // 这一格把「手势为什么不响应」里最难查的一种分了出来：**收得到移动、收不到右键**，
    // 意味着右键在到达钩子链之前就被别人拿走了——鼠标厂商的驱动把它指派成了别的功能，
    // 或者另一个装了低级钩子的程序（AutoHotkey 之类）把它吞了。
    // 那种情况下 Clockwork 这边一切正常：钩子活着、配置没错、状态显示监听中，
    // 而右键永远不会来。分不开这一格的话，只能看到「一切正常但没反应」。
    public bool EverRightBeat => EverRightDown || EverRightUp;

    /// <summary>收到过右键**按下**没有。手势全靠它起头，没有它就一个采样点都攒不下来。</summary>
    public bool EverRightDown => Volatile.Read(ref _beatRDown) != 0;

    /// <summary>收到过右键**抬起**没有。</summary>
    public bool EverRightUp => Volatile.Read(ref _beatRUp) != 0;

    /// <summary>把扣住的那次右键按下还给系统时，被 UIPI 拒掉了多少次。</summary>
    //
    // 与上面那一格（按下被上游吞了）恰好是**相反方向**的同一类故障：那一格是「我们收不到」，
    // 这一格是「我们发不出去」。两者的表现一样（右键拖拽没反应），修法完全不同：
    // 前者要去查是谁排在前面，后者只需要把本程序也提权。分不开就只能猜。
    public int InjectRejected => Volatile.Read(ref _injectRejected);

    /// <summary>这条钩子装上以来，事件从生成到被我们回调收到，最长迟到了多少毫秒。</summary>
    //
    // 迟到 = 链上排在我们前面的钩子（Logi Options+ / PowerToys / 热键助手 之流）拖住了链。
    // 与 SinceBeatMs 合看才分得开两种病，见 _upstreamMax 字段处的注释。
    // 0 表示「一次都没量到」——包括 time 字段为 0 的兜底情形（那时不记，免得报一个假的天文数字）。
    public uint UpstreamMaxMs => Volatile.Read(ref _upstreamMax);

    /// <summary>这任钩子**回调自身耗时**的峰值（毫秒）。与 <see cref="UpstreamMaxMs"/> 成对看：
    /// 那个量别人堵了我们多久，这个量我们自己离被摘还有多远（见 _callbackMax 字段处的注释）。</summary>
    public uint CallbackMaxMs => Volatile.Read(ref _callbackMax);

    /// <summary>右键按下被上游吞了：抬起收得到，按下一次都没有。</summary>
    //
    // 只有「有人排在我们前面拦截」会造成这个组合——鼠标本身不可能只发抬起不发按下。
    // 实测（--hookprobe 注入 5 次按下+5 次抬起）：按下 0、抬起 5。
    // 这是这个功能唯一能自证的失败信号，值得单独有个名字。
    public bool RightDownSwallowed => EverRightUp && !EverRightDown;

    /// <summary>收到过中键**按下**没有。中键是面板触发键：移动心跳正常但它一直不来，
    /// 就是中键在钩子链上游被吞的指纹（与右键那一格同理）。</summary>
    public bool EverMiddleDown => Volatile.Read(ref _beatMDown) != 0;

    /// <summary>收到过中键**抬起**没有。</summary>
    public bool EverMiddleUp => Volatile.Read(ref _beatMUp) != 0;

    /// <summary>按满时长唤出面板的次数（闹钟与追赶轮询合计，一次长按只计一次）。</summary>
    public int MiddleFireCount => Volatile.Read(ref _middleFired);

    /// <summary>长按被改判成中键拖拽的次数（按住后位移超容差）。它非零就是「按住很久、
    /// 松手也不弹」的直接指纹：物理事件全到了，是我们自己的抖动容差把它判成了拖拽。</summary>
    public int MiddleDragEscapeCount => Volatile.Read(ref _dragEscapes);

    public long SinceBeatMs => Volatile.Read(ref _beat) == 0
        ? long.MaxValue
        : Environment.TickCount64 - Volatile.Read(ref _beat);

    /// <summary>装钩子（在专用钩子线程上装并返回结果）。返回是否成功——失败时调用方该如实告知用户，
    /// 而不是让功能静默不存在。可从任意线程调（见类头注释第 1 条）。</summary>
    // ── 只给 --hookprobe 用的裸接口 ──
    // 探针要的是「一句 SetWindowsHookEx 加一个计数器」，不能带上本类的任何状态机；
    // 而那三个 P/Invoke 声明就在这个文件里，再抄一份到别处只会多一处会漂移的声明。
    public delegate IntPtr RawProc(int code, IntPtr wParam, IntPtr lParam);

    // 交给系统的那个**包装**委托也必须有人持有，理由与实例路径的 _proc 一字不差（见上面 _proc 那段）：
    // SetWindowsHookEx 只存了个裸函数指针。调用方持有的是 RawProc，而真正被 marshal 出去的是
    // 这里 new 出来的 HookProc 包装——没有字段引用它，下一次 GC 之后回调地址就是野指针。
    // 探针那 12 秒里旁边还跑着一个真 MouseHook 在不停 BeginInvoke，Gen0 几乎必然发生一次。
    // 探针整个进程只装一次、装完就退，所以一个静态字段够用，不必按句柄记账。
    private static HookProc? _rawProc;

    public static IntPtr InstallRaw(RawProc proc)
    {
        try
        {
            _rawProc = new HookProc(proc.Invoke);   // 先落到字段上，再交给系统
            return SetWindowsHookEx(WH_MOUSE_LL, _rawProc, IntPtr.Zero, 0);
        }
        catch { return IntPtr.Zero; }
    }

    public static void UninstallRaw(IntPtr h)
    {
        if (h != IntPtr.Zero) try { UnhookWindowsHookEx(h); } catch { }
        _rawProc = null;   // 摘完才放手：顺序反了就又回到「系统还握着指针、委托已可回收」
    }

    public static IntPtr CallNext(IntPtr h, int code, IntPtr w, IntPtr l) => CallNextHookEx(h, code, w, l);

    /// <summary>上一次 Install 失败时的 Win32 错误码（成功为 0，抛异常为 -1）。</summary>
    //
    // 装不上有三种成因——受限令牌、组策略、被安全软件拦——修法各不相同，而它们只有在错误码上
    // 分得开。不留这一格的话，日志里只剩一句「装不上」，等于什么都没说。
    // 必须**紧跟**在 P/Invoke 之后取：GetLastWin32Error 拿的是本线程最近一次调用的结果，
    // 中间隔任何一句托管代码都可能把它冲掉。
    public int LastError { get; private set; }

    public bool Install()
    {
        // **这两处 `Wait(5000)` 是 UI 线程上最可疑的阻塞点，所以各包了一圈计时
        //（见 Core.UiStallWatch；调用方 GestureCoordinator.ApplyMouseHook 被 Dispatcher.Invoke 钉在 UI 线程上）。**
        //
        // 设计气味在**这个数本身**：5000ms 与系统默认的 `HungWindowTimeout` 一模一样。
        // 也就是说「我们自己的超时」正好落在「系统判你未响应」那条线上——一旦真的等满，
        // 这一轮结束时 UI 线程恰好被卡在阈值边界上，同一轮里再多做一点点事就越线，
        // 于是 DWM 在全屏笔迹窗上盖一块点得中的幽灵窗（见 Win32.DisableWindowGhosting 那段）。
        // 钩子线程一返回 SetWindowsHookEx 就发信号，所以常态下这里几乎立刻返回；
        // 但全局 WH_MOUSE_LL 的 SetWindowsHookEx 恰恰是安全软件 / 组策略会拖住的那一个
        //（本类 LastError 的注释就列了「被安全软件拦」这一档），而那条线程还要先付整条手势
        // 路径的首次 JIT。日志里 `ui thread slow: hook-install-wait took ...ms (thread=ui)`
        // 就是它真等过 5 秒的指纹。
        //
        // 幂等：线程已经起来了就等它的安装报告，别起第二个泵抢同一套状态。
        // 失败的尝试会收掉线程并把 _thread 置空，允许重试。
        var existing = _thread;
        if (existing != null)
        {
            long t0 = Core.UiStallWatch.Begin();
            bool ok0 = WaitInstalled(5000);
            Core.UiStallWatch.End("hook-install-wait", t0);
            return ok0 && _installResult == 1;
        }
        _installResult = 0;
        LastError = 0;
        _installed.Reset();
        var t = new Thread(HookThreadRun) { Name = "Clockwork.MouseHook", IsBackground = true };
        _thread = t;
        t.Start();
        // 补发线程与钩子线程同起（收在 Teardown 的 CompleteAdding）。它不在这里等安装结果：
        // 没有钩子就没有补发要排，多一条睡在队列上的后台线程不痛。
        StartReplayWorker();
        long tw = Core.UiStallWatch.Begin();
        if (!WaitInstalled(5000))
        {
            Core.UiStallWatch.End("hook-install-wait", tw);
            // 起线程 / 装钩没有任何正当理由超过 5 秒；等不到也别把上层吊在这儿，按失败报。
            try { PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); } catch { }
            t.Join(1000);
            _thread = null;
            LastError = -1;
            return false;
        }
        Core.UiStallWatch.End("hook-install-wait", tw);
        if (_installResult != 1) { t.Join(1000); _thread = null; }
        return _installResult == 1;
    }

    // 等安装报告，**并且接住「等的时候有人把钩子拆了」**。
    //
    // `Teardown()` 里有 `_installed.Dispose()`（钩子线程退出的必经之路），而 `ManualResetEventSlim`
    // 被 Dispose 之后再 Wait 会抛 `ObjectDisposedException`。这条竞态原先不存在：Install 与 Dispose
    // 都由 UI 线程串行调用，两者不可能重叠。**安装改成在后台线程上等之后（见 GestureCoordinator.ApplyMouseHook）
    // 它就成了必然而不是偶然**：改配置 / 自愈 / 手动重钩都可能在一次安装还在飞的时候
    // `Dispose()` 掉那个对象。那时正确的答案是「这次安装没成」，而不是让异常冒到后台线程顶上。
    private bool WaitInstalled(int ms)
    {
        try { return _installed.Wait(ms); }
        catch (ObjectDisposedException) { return false; }
    }

    // 专用钩子线程的一生：在本线程装钩（LL 回调只认装它那个线程的消息泵）→ 报告安装结果 →
    // 泵消息，WM_POLLDUE 问长按 gate，WM_QUIT 退出 → 在本线程 Teardown。
    private void HookThreadRun()
    {
        try
        {
            _threadId = GetCurrentThreadId();   // PostThreadMessage 要的是原生 ID，不是托管线程 ID
            // 模块句柄传 0：WH_MOUSE_LL 是全局低级钩子，回调跑在本进程内，不需要注入 DLL，
            // 因此也不需要真实的模块句柄（这一点与需要注入的 WH_MOUSE 不同）。
            try
            {
                Volatile.Write(ref _hook, SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0));
                LastError = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                _installResult = _hook != IntPtr.Zero ? 1 : 0;
            }
            catch { Volatile.Write(ref _hook, IntPtr.Zero); LastError = -1; _installResult = -1; }
            _installed.Set();
            if (_installResult != 1) { return; }
            // LL 钩子的回调由系统在本线程取消息时**同步**派发；不需要 DispatchMessage。
            while (true)
            {
                int r = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
                if (r <= 0) break;   // WM_QUIT → 0；出错 → -1，都走统一拆除
                if (msg.Message == WM_POLLDUE) FireIfDue();
            }
        }
        catch
        {
            // 泵线程绝不能把异常漏到线程顶：Install 那边还在等安装信号。
            _installResult = -1;
            LastError = -1;
            Volatile.Write(ref _hook, IntPtr.Zero);
            try { _installed.Set(); } catch { }
        }
        finally
        {
            try { Teardown(); } catch { }
            _threadId = 0;
        }
    }

    /// <summary>拆钩（自愈 / 换配置 / 手动重钩）：先摘钩，再在这条线程上等设备退出来，最多 200ms。</summary>
    public void Dispose() => DisposeCore(joinOnCallingThread: true);

    /// <summary>退出路径上的拆钩：摘完钩就走，绝不在调用线程上等那条泵线程。</summary>
    //
    // **这里有一次公开更正。** 上一轮（1a49657）我写的判据是「UI 线程停在 `UnhookWindowsHookEx`
    // 里出不来」，据此把摘钩挪给了工作线程。拿着日志重看一遍，那句不成立：整份
    // clockwork.error.log 里 `hook-unhook` 与 `hook-teardown-inject` **一条 slow 都没有**
    //（阈值 500ms），慢的只有一种写法——`hook-join-background took 30000ms`，十七条。
    // 一直卡着的是那条泵线程本身（它正替整条链走 CallNextHookEx），与谁去调 unhook 无关。
    //
    // 于是顺序反过来，理由两条：
    //   · **摘钩要趁调用线程还走得动、尽早做**：它一返回，系统就不会再叫 _proc，链上也就没有
    //     我们那套「吞掉右键按下」的逻辑了。2026-10-06 那次「点了退出，进程还在，而且右键一直卡」
    //     的真正来源就是这个——留着一条钩子的壳进程还在吃按下。摘掉之后壳进程只是难看，不再有害。
    //   · **等它 = 一行都不等**：那条线程什么时候解卡不由我们决定，等它就是把退出交给别人。
    public void DisposeForExit() => DisposeCore(joinOnCallingThread: false);

    private void DisposeCore(bool joinOnCallingThread)
    {
        var t = _thread;
        // 没装过钩子（单测反射驱动 Callback、或 Install 失败后 App 的收尾）：没有泵可通知，
        // 就地做同一份托管/注入清理。
        if (t == null) { Teardown(); return; }
        // 正常路径只走一次：自愈 / 手动重钩 / 退出可能先后碰到同一对象。
        if (Interlocked.Exchange(ref _disposePosted, 1) == 1) return;
        // 摘钩排在通知泵线程之前（见 DisposeForExit 的那段更正）。清零也一样在先：
        // 泵线程随后的 Teardown 读到的就是这个 0，不会重复摘。
        var h = Volatile.Read(ref _hook);
        Volatile.Write(ref _hook, IntPtr.Zero);
        UnhookNow(h);
        try { PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); } catch { }
        // 退出这一路到此为止：钩子已经不在链上了，剩下那些事（还键、保住托管根、等它退）
        // 全部交给下面那个线程，调用线程一句都不等。
        if (!joinOnCallingThread) { HandOff(t); return; }
        // UI 线程只付它该付的那一点。正常路径上泵线程亚毫秒就退（WM_QUIT 排在它自己的队列里），
        // 等满这一格说明它正卡在某个回调里出不来——那时继续在 UI 线程上等，就是把「一次跨进程
        // 查询慢了三秒」放大成「UI 线程冻两秒、屏幕上那条笔迹没人收」。实测（2026-10-03）：
        // hook-join-first 1000ms + hook-join-second 1015ms = hook-dispose:heal 2015ms，
        // 而同一行 trailUp=up —— 那两秒里整屏覆盖窗就在屏幕上，正是用户报的「轨迹卡住」。
        long j0 = Core.UiStallWatch.Begin();
        bool gone = t.Join(UiJoinBudgetMs);
        Core.UiStallWatch.End("hook-join-first", j0);
        if (gone) { _thread = null; return; }
        HandOff(t);
    }

    // 泵线程没能在这条线程的预算里退出来：还有两件事必须有人接着做。
    //   1. **把欠系统的那次按下还回去**（见 SettleOwedKeys）。它那份 Teardown 不知道几秒后才跑、
    //      或者永远不跑，而用户手指底下那一记右键就扣在这次注入上——这正是「有些右键不出现」。
    //   2. **替系统保住 _proc 这个托管根**直到它真退出：本对象一旦被丢掉而回调还可能再来，
    //      打到的就是已回收的桩（旧注释里那句「野指针回调会随机崩进程」）。闭包捕获 this 就是这根；
    //      30 秒的上限也比旧写法放弃前的 2 秒长一个量级——放弃得更晚，不是更早。
    private void HandOff(Thread t)
    {
        Interlocked.Increment(ref _strandedThreads);
        System.Threading.Tasks.Task.Run(() =>
        {
            SettleOwedKeys();
            long b0 = Core.UiStallWatch.Begin();
            bool exited = t.Join(BackgroundJoinBudgetMs);
            Core.UiStallWatch.End("hook-join-background", b0);
            GC.KeepAlive(this);      // 让 this（连同 _proc）确实活到这一次 Join 之后
            if (exited) _thread = null;
            Interlocked.Decrement(ref _strandedThreads);
        });
        _thread = null;
    }

    // 摘钩，带计时。h 为 0 时什么都不做（泵线程自己已经摘过了）。
    // 单独成一句：自愈与退出**都在调用线程上调它**（它在整份日志里从来没慢过），
    // 两条路的分别在「之后等不等那条泵线程」，见 DisposeCore。
    private void UnhookNow(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        long u0 = Core.UiStallWatch.Begin();
        try { UnhookWindowsHookEx(h); } catch { }
        Core.UiStallWatch.End("hook-unhook", u0);
    }

    /// <summary>把欠系统的键还回去——**每个钩子只还一次**，谁先跑到谁还。</summary>
    //
    // 为什么这件事不能只在泵线程上做（它原先正是这样）：手势路径会把每一条 WM_RBUTTONDOWN
    // 吞掉（return 1），那次按下此后只存在于我们手里，直到这条路径把它注入回去。
    // 而泵线程恰恰是**可能永远回不来**的那条——它此刻正卡在 CallNextHookEx 或一次 SendInput 上
    //（链上有人不回话；2026-10-06 06:16–06:18 一晚四条 `hook-join-background took 30000ms`）。
    // 那时旧写法的结果是：用户的那一下右键**再也没有出现**（正是他报的「有些右键不出现」），
    // 而还键的那三句代码躺在一个不会执行的 finally 里。
    //
    // 于是 DisposeCore 的 stranded 分支（泵线程没在预算内退出）也调这一个方法。两边抢同一格
    // `_downsSettled`：Exchange 保证只有一边真发得出注入，不会出现「泵线程解开之后点两下菜单」。
    //
    // **这里破了一条本类的纪律**：gate 的读写原先只在钩子线程上（见 _alarm 那处注释）。
    // 从工作线程读 `Pending` 是一次故意的例外，理由与代价摆在这儿：
    //   · 走进这一格的前提是那条线程 200ms 没回来，所以读到的是「它还没来得及改」的旧值；
    //   · 最坏的交错是它恰好在读之后、注入之前解开了 pending 并唤出了手势——
    //     那一下会多出一个右键菜单，而这仍好过把用户本来那次右键永久吞掉；
    //   · 反过来不做，就是「按下去石沉大海」，且没有任何日志能自证。
    private void SettleOwedKeys()
    {
        if (Interlocked.Exchange(ref _downsSettled, 1) == 1) return;
        // 三次注入各是一次 SendInput，它们排在钩子线程退出的路上（Teardown 是泵线程 finally
        // 里的最后一步），所以它们慢 = 上面 Dispose 的 Join 等满。包一格名字，是为了把
        // 「Join 等满」这条果倒着查到这一句因上。
        long inj0 = Core.UiStallWatch.Begin();
        try
        {
            ReplayUp();   // 欠着的抬起先补上，否则右键卡在按下态
            // **扣着的那次真按下要还回去，不是清掉就算完。**
            // 两个 gate 处于 pending，意思就是「用户按下去了，而那一下被我们 return 1 吞掉了」。
            // 只 Reset 的话那次按下永远不存在，而用户的手指还在键上——松手时真正的抬起会原样流下去
            //（新 gate 不在 pending，不拦），于是光标底下那个程序收到一记没有配对按下的抬起：
            // DefWindowProc 凭它合成一个莫名其妙的右键菜单，自己做按下-抬起配对的程序则拖拽状态错乱。
            // 走到这里的路很日常：Windows 静默摘掉钩子 → 自愈重装（HealMouseHookIfDead），
            // 而用户此刻正按着右键画手势；托盘退出、每一次带键重装同理。
            if (_gesture?.Pending == true) Win32.SendRightDownTagged();
            // 中键只在 Pending 时还，**`_fired` 那一档故意不还**，与右键不对称是有理由的：
            // · `_fired` 意思是面板已经弹出来了，此刻注入一个中键按下很可能把它当场点掉
            //   （LongPressGate 那边为同一个理由把随后的抬起吞掉：「否则面板刚弹出来就被点掉了」）。
            // · 不还的代价又比右键小一个量级：孤立的中键抬起在绝大多数程序里是惰性的，
            //   而孤立的右键抬起会被 DefWindowProc 合成一个 WM_CONTEXTMENU（那才是看得见的毛病）。
            // 已经被当成「漏了 _fired 一半」提过一次，留这段免得反复。
            if (_gate?.Pending == true) Win32.SendMiddleDownTagged();
        }
        finally { Core.UiStallWatch.End("hook-teardown-inject", inj0); }
    }

    // 有序拆除。生产里只在钩子线程上跑（与 Callback 天然串行——旧实现装在 UI 线程时靠
    // 「都在 UI 线程」白拿这个保证）；没装过钩子时由 Dispose 在调用线程直接跑。两条路互斥。
    private void Teardown()
    {
        // 卸载前收弦并清掉扣着的那次按下：留着它没有意义，而且万一钩子又被装回来，
        // 那个陈旧的按下时刻会让下一次抬起被算成一次超长的长按。
        Arm(false);
        try { _alarm.Dispose(); } catch { }
        // 三句「把欠系统的键还回去」各是一次 SendInput，现在收在 SettleOwedKeys 里：
        // 泵线程可能永远回不来（见 DisposeCore 的 stranded 那一格），那时由工作线程来还同一笔账。
        SettleOwedKeys();
        // 补发队列到这里就收： CompleteAdding 让那条线程把手上排着的补发跑完自己退，
        // 不再接受新的。它不必被 Join——线程是后台的，而欠着的键由上面 SettleOwedKeys 结清。
        try { _replay?.CompleteAdding(); } catch { }
        _gate?.Reset();
        _gesture?.Reset();
        _owedRightClick = false;   // 这笔账随钩子一起作废：上面已经无条件还过了
        // 钩子卸载时手可能还按着：那条线不收笔就会一直挂在屏幕上。
        // **必须兜住**：_post 在调度器关闭时会抛，抛出去会打断后面的摘钩；本对象紧接着会被
        // 置空，而 _proc 是系统那个裸函数指针**唯一的**托管根，野指针回调会死在随机的鼠标移动上。
        try { _post(_trailEnd); } catch { }
        var h = Volatile.Read(ref _hook);
        if (h != IntPtr.Zero) try { UnhookWindowsHookEx(h); } catch { }
        Volatile.Write(ref _hook, IntPtr.Zero);
        try { _installed.Dispose(); } catch { }
    }

    /// <summary>回调本身也包一圈计时，再转给 <see cref="CallbackCore"/>。</summary>
    //
    // **这一格是唯一能把「钩子为什么沉默」分成两种病的东西。**
    // 2026-09-18 的日志里有一行 `mouse hook reinstalled: source=heal ... silentFor=937ms`——
    // 用户正在画手势、鼠标一直在动，而回调 937ms 一次都没被叫到。那有两种成因，修法相反：
    //
    //   · **我们的回调被堵了**（一次阻塞式 GC 停掉整个世界、或回调里某句 P/Invoke 卡住）：
    //     这里会留下一行 `ui thread slow: hook-callback took ...ms (thread=bg) chain=..ms ours=..ms`。
    //     thread=bg 是**对的**——回调跑在专用钩子线程上，不是 UI 线程（见类头第 1 条）。
    //     chain/ours 的拆分见 Chain()：2026-09-27 那晚 157 条 slow、中位 4 秒最大 45 秒，
    //     而我们自己的计算是微秒级——不拆开的话，整条钩子链的账都记在我们头上。
    //   · **回调压根没被叫到**（上游某条低级钩子链被堵住、或 Windows 已按 LowLevelHooksTimeout
    //     把它静默摘掉）：一行都不会有，只有沉默。那时要查的是排在我们前面的那些工具。
    //
    // 两种病此前在日志里长得一模一样（都只有「沉默 937ms」），只能靠猜。
    // 代价是每个鼠标事件多两次 TickCount64——常数级读取，与回调里那些 P/Invoke 比可以忽略。
    private int _cbDepth;    // 重入深度：回调内补发的注入事件会同步重走钩子链，可能重入本回调
    // 本次回调（最外层）唯一一类**外部**耗时：
    //   · `_chainMs` = `CallNextHookEx` —— 同步跑完排在我们后面的所有低级钩子，它们不回头话就全算在这格。
    // 曾经这里还有第二格 `_injectMs`（我们自己的 `SendInput`），因为那两件事的修法相反：链堵了只能
    // 让路/抢链首，注入堵了要把注入挪出回调。**2026-10-06 补发改到补发线程之后，回调里一句注入都不剩**
    //（ReplayOffCallbackTests 第 1 条钉着），留着那一格就是一部永远答 0 的仪表——比没有仪表更坏，
    // 因为它看起来像在读数。注入的耗时没有消失，它换了地方待着，见 ReplayMaxMs / ReplayQueued。
    // 只有最外层记账：注入引起的重入回调（_cbDepth>1）落在外层的窗口之内，再累加一次就是算两遍。
    private long _chainMs;
    private uint _chainMax;    // 历史峰值，读法见 CallbackMaxMs 那条

    /// <summary>这任钩子里 `CallNextHookEx`（别人的链）的峰值耗时。</summary>
    public uint ChainMaxMs => Volatile.Read(ref _chainMax);

    /// <summary>补发线程上**一次注入**的峰值耗时。与 `ChainMaxMs` 成对读：
    /// 那一格大 = 链堵了；这一格大 = 输入队列堵了，但它现在只拖慢补发，不再按住整条链。</summary>
    public int ReplayMaxMs => Volatile.Read(ref _replayMaxMs);

    /// <summary>排进补发队列的次数（证明这条路真在跑，而不是静默被跳过）。</summary>
    public int ReplayQueued => Volatile.Read(ref _replayQueued);

    /// <summary>投不进去的次数（队列满 / 线程没起来）。每一次都当场让调用方放行真事件，
    /// 所以它不吃掉按键，但必须看得见——见 EnqueueReplay。</summary>
    public int ReplayDropped => Volatile.Read(ref _replayDropped);

    /// <summary>把一次回调的时长拆成「链 / 剩下才是我们自己的计算」。</summary>
    // 纯函数，为了这一句能被断言钉住（日志归因写错方向比没有归因更糟）。
    // TickCount64 是毫秒粒度，取整会让 chain 略大于 total，ours 兜到 0，不代表算错。
    internal static string Attribution(long totalMs, long chainMs)
        => $"chain={chainMs}ms ours={Math.Max(0, totalMs - chainMs)}ms";

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        _cbDepth++;
        if (_cbDepth == 1) _chainMs = 0;   // 只有最外层清账，理由见字段处
        long t0 = Core.UiStallWatch.Begin();
        try { return CallbackCore(code, wParam, lParam); }
        finally
        {
            // 先记峰值再交给 slow 日志：那行的阈值是 500ms，而钩子的预算是 300ms，
            // 中间那一段只有这一格抓得住（见 _callbackMax）。
            uint ms = (uint)(Environment.TickCount64 - t0);
            if (ms > _callbackMax) Volatile.Write(ref _callbackMax, ms);
            _cbDepth--;
            // 归因尾巴只在最外层给：内层与外层是同一份活的两次记账，报两遍会误导。
            if (_cbDepth == 0)
            {
                if (_chainMs > _chainMax) Volatile.Write(ref _chainMax, (uint)_chainMs);
            }
            Core.UiStallWatch.End("hook-callback", t0,
                _cbDepth == 0 ? Attribution(ms, _chainMs) : null);
        }
    }

    // 放行整条链的耗时：CallNextHookEx 会**同步**跑完排在我们后面的所有低级钩子，
    // 它们卡在自家消息泵上是几十秒级的事。这一格里一个字节都不是我们的计算。
    // 只跑在钩子线程上，无并发，普通字段即可。
    private long ChainedMs(Func<long> f)
    {
        if (_cbDepth != 1) return f();      // 重入：那段时间在祖先的窗口里，算两遍就错了
        long t = Environment.TickCount64;
        try { return f(); }
        finally { _chainMs += Environment.TickCount64 - t; }
    }

    // **同步注入的唯一出口**——只准留给下面那一处（拖拽救援），别的注入一律走补发线程。
    //
    // 为什么那一份要留在回调里：它在「长按被改判成拖拽」的第一条 WM_MOUSEMOVE 上，必须抢在
    // 这条移动被放行下去**之前**让下游看到中键按下——浏览器/IDE 的中键自动滚动按事件顺序判定
    // 「按住时有没有移动」，按下晚一步整个功能就没了。排到另一条线程上等于把它推到移动后面去。
    //
    // 代价照实说：这仍是一次能在回调里等住的 SendInput（每次拖拽一次，不是每次点击一次），
    // 所以它必须自己报名——包一层具名段，慢过阈值时在 slow 行里出现的是 `hook-drag-rescue`，
    // 而不是混进 `ours=` 里让人重新猜。这条例外由 ReplayOffCallbackTests 数着：回调里只许有这一处。
    // 拖拽救援：长按被改判成拖拽时，把中键按下补发给下游。
    //
    // 它为什么可以留在回调线程上而补发点击不行：这一档必须**抢在眼前这条 WM_MOUSEMOVE 被放行
    // 下去之前**让下游看到按下——浏览器/IDE 的中键自动滚动按事件顺序判「按住之后有没有移动」，
    // 按下排到补发线程上就落到移动后面，功能整个消失。代价照实说：这里仍可能被输入队列按住
    // 一次（每次拖拽一次，不是每次点击一次），所以它自己报名 `hook-drag-rescue`，慢的时候
    // 出现在 slow 行里的是这个名字，而不是重新混进 `ours=`。
    private void RescueDragDown() => SendSync(() => Win32.SendMiddleDownTagged());

    private uint SendSync(Func<uint> send)
    {
        long t = Core.UiStallWatch.Begin();
        try
        {
            uint sent = send();
            if (sent == 0) Interlocked.Increment(ref _injectRejected);
            return sent;
        }
        finally
        {
            long ms = Environment.TickCount64 - t;
            if (ms > Volatile.Read(ref _replayMaxMs)) Volatile.Write(ref _replayMaxMs, (int)ms);
            Core.UiStallWatch.End("hook-drag-rescue", t);
        }
    }

    private IntPtr Chain(int code, IntPtr wParam, IntPtr lParam)
        => (IntPtr)ChainedMs(() => CallNextHookEx(_hook, code, wParam, lParam).ToInt64());

    private IntPtr CallbackCore(int code, IntPtr wParam, IntPtr lParam)
    {
        // code < 0 是系统要求「别处理、直接往下传」的约定，必须照办。
        // 整个回调包一层 try：这里抛出去的异常会穿到系统的消息派发里，后果不可控；
        // 出错时按「放行」收场——最坏是这次长按不生效，而不是鼠标失灵。
        try
        {
            // 盖在最前面（连 code < 0 那一档也算）：这一戳要回答的是「系统还在叫我吗」，
            // 而不是「这一条消息我处理了吗」。Windows 把低级钩子静默卸掉之后，
            // 回调就再也不会被叫到——那时这个戳会停住，而它是唯一能看出来的迹象。
            System.Threading.Volatile.Write(ref _beat, Environment.TickCount64);
            if (code < 0) return Chain(code, wParam, lParam);
            int msg = wParam.ToInt32();
            if (msg is not (WM_MOUSEMOVE or WM_MBUTTONDOWN or WM_MBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP))
                return Chain(code, wParam, lParam);

            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // 自己补发的那些：原样放行（见类头注释第 3 条）。
            if (data.DwExtraInfo == Win32.InjectTag) return Chain(code, wParam, lParam);

            // 上游延迟：这条事件在系统里生成的时刻（MSLLHOOKSTRUCT.time）到此刻隔了多久。
            // TickCount 与 time 都是 32 位、同样 49.7 天回绕，uint 减法天然正确，不必处理回绕。
            // 只量物理事件（自己补发的那批上面已经放行掉了，量它会永远得到 0）；
            // time==0 时作罢——那说明系统没填这一格，照算会得到一个假的天文数字。
            // 为什么值得在每个回调里都算一次：见 _upstreamMax 字段处的注释。
            uint upstream = unchecked((uint)Environment.TickCount - data.Time);
            if (data.Time != 0 && upstream > _upstreamMax) Volatile.Write(ref _upstreamMax, upstream);

            // 中键按下/抬起分开记账（理由同右键的 _beatRDown/_beatRUp）：
            // 移动心跳照常、中键却一次都没来，是中键在钩子链上游被吞的指纹。
            if (msg == WM_MBUTTONDOWN) Volatile.Write(ref _beatMDown, Environment.TickCount64);
            else if (msg == WM_MBUTTONUP) Volatile.Write(ref _beatMUp, Environment.TickCount64);

            // 到点追赶：必须排在本事件 verdict 之前（见 PollFireAndPost 注释）。
            PollFireAndPost(Environment.TickCount64);

            // —— 右键：手势 ——（gate 词汇与中键共用，但这边永远用不到 ReplayDownThenPass）
            if (msg is WM_RBUTTONDOWN or WM_RBUTTONUP)
            {
                if (msg == WM_RBUTTONDOWN) Volatile.Write(ref _beatRDown, Environment.TickCount64);
                else Volatile.Write(ref _beatRUp, Environment.TickCount64);
                if (_gesture == null) return Chain(code, wParam, lParam);
                // 修饰键（Ctrl/Shift/Alt/Win）按着的右键是**别人的组合手势**——WindowShuffle 的
                // Ctrl+右键拖窗、Explorer 的 Shift+右键扩展菜单那一类。按下这一刻就整笔放行：
                // 低级钩子后装先调、return 1 截断整条链，扣下这个按下，排在后面的工具连按下都收不到，
                // 而我们补发的是注入事件，对方按防回环会把它丢掉——它的手势就此消失。
                // 只问按下这一刻：起笔之后才按修饰键救不回已吞的按下，也没有工具这么起手；
                // gate 没起笔，后续的 move/up 天然全部 Pass。
                // 问的是键盘物理态，我们从不吞键盘——不犯「吞了消息还拿系统键态当判据」那条（见 Win32.ModifierHeld）。
                if (msg == WM_RBUTTONDOWN && _modifierHeld())
                {
                    // 旧欠账不在组合手势里还：超时兜底留下的那笔补发会在这里注入一个按下，
                    // 而 Ctrl 还按着——它会变成一记 Ctrl+右键，正是我们要让路的那类东西。
                    // 这笔账的安全结局本来就是「没人来领就永远不注入」（见 _owedRightClick 注释），作废即可。
                    _owedRightClick = false;
                    return Chain(code, wParam, lParam);
                }
                // **补发送不进去的窗口上，整笔右键都不接管。**
                //
                // 旧设计在这里无条件吞下按下，指望抬起来时补发一次真右键。可补发是**注入**：
                // 前台窗口比本进程提权时（UIPI）SendInput 被静默丢弃，投不进去；而按下已经吞了。
                // 那条路上的后果是「吞下去之后才发现送不回来」——用户那一下右键彻底消失，
                // 而这正是他报的「右键经常被吞」。判据由 HookProbe 在后台线程算好（见该类类头：
                // 这句话本身要 OpenProcess，在回调里会卡住整条鼠标链）。
                //
                // 问的是**前台窗口**而不是起笔窗口：被吞掉的右键要补发到前台去，用户看见的菜单
                // 也开在前台上。起笔窗口是后台时两者不同，那一档仍然整笔放行——宁可这一下没手势。
                //
                // 代价：刚切过去的窗口、快照还没算出来的那一瞬不接管（MayEngage 对 pid 不符答 false）。
                // 这是刻意选的错：一次没画成手势 vs 一次右键凭空消失。
                if (msg == WM_RBUTTONDOWN && !_mayEngage(Win32.ForegroundPid()))
                {
                    _owedRightClick = false;
                    return Chain(code, wParam, lParam);
                }
                // 起笔窗口在**按下那一刻**解析：此刻光标压着的窗就是这笔手势划在谁身上，
                // 与轨迹起点同一坐标。回调只做纯查询，不碰 UI。
                // 「微秒级」这句此前是断言、没量过；而日志里有一条 `hook-callback took 6469ms
                // chain=0ms ours=6469ms`——chain 排除了 CallNextHookEx 与注入，那么秒级就只可能落在
                // 这两句跨进程查询或 Disp.BeginInvoke 上，所以三处各包一格计时，让它下次自己报名。
                if (msg == WM_RBUTTONDOWN)
                {
                    long w0 = Core.UiStallWatch.Begin();
                    _gestureOrigin = Win32.WindowAtPoint(data.X, data.Y);
                    Core.UiStallWatch.End("hook-window-at", w0);
                }
                // 抬起那一刻**在判定之前**再问一次进程名。按下的那一次多半还没算出来（缓存未命中），
                // 而一笔真手势要画几百毫秒——这段时间足够后台线程把 OpenProcess 做完。
                // 不补这一句的后果很具体：某个窗口里的**第一次**右键，绑定了该程序的手势一律
                // 不命中（ContextProcess 是空串，Match 只走全局那一档），第二次才正常。
                //
                // **必须排在 OnRightUp 之前**：匹配是在 OnRightUp 内部按当时的 ContextProcess
                // 做的，补写在它后面等于给下一次用——那一笔仍然按空串判（而 Gate.Pending 已被
                // 清掉，这条补写连「本笔」都认不出来了）。
                // 读缓存仍是纯字典查，不把跨进程查询放回回调。
                if (msg == WM_RBUTTONUP && string.IsNullOrEmpty(_gesture.ContextProcess) && _gesturePid != 0)
                    _gesture.ContextProcess = _procNameForPid(_gesturePid);
                var gv = msg == WM_RBUTTONDOWN ? _gesture.OnRightDown(data.X, data.Y) : _gesture.OnRightUp();
                if (msg == WM_RBUTTONDOWN)
                {
                    // 只查一次 pid（纯 user32），进程名交给 HookProbe 的缓存——那一句要 OpenProcess，
                    // 在这条回调路径上实测能到秒级（见 HookProbe 类头）。
                    long p0 = Core.UiStallWatch.Begin();
                    _gesturePid = Win32.ForegroundPid();
                    _gesture.ContextProcess = _procNameForPid(_gesturePid);
                    Core.UiStallWatch.End("hook-proc-name", p0);
                }
                // 屏幕上那条看得见的笔迹，起止都在这里派（一律走 _post，绝不在回调里碰 UI）。
                //
                // 按下：先收上一笔再落起点。多数时候上一笔早收了，那一下什么也不做；它堵的是
                //   **收不了笔**那条路——抬起发生在安全桌面上时（UAC 提示弹出来那一瞬间）
                //   低级钩子根本收不到那个 up，那条线就会一直挂在屏幕上直到重启。
                //   下一次右键顺手清掉它，代价是零。落起点顺带让覆盖窗摆到起点那块屏上，
                //   而它要到第二个点才现身，所以只是右键点一下不会闪出一条线。
                // 先结掉那笔“欠的按下”（超时兜底留下的）。放在 gate 之前：此刻 gate 已经 Reset 过，
                // 问它只会得到 Pass，那条真抬起就无配对地流到下游去了。
                if (msg == WM_RBUTTONUP && _owedRightClick)
                {
                    _owedRightClick = false;
                    _post(_trailEnd);
                    // 补发被 UIPI 拒了（前台是提权进程）：放行这条真抬起。按下救不回来了，
                    // 但 DefWindowProc 光凭 WM_RBUTTONUP 就会发 WM_CONTEXTMENU，多数程序的菜单靠这一下能出来
                    // （与下面 ReplayClick 失败时同一个口径）。
                    if (!ReplayClick(1)) return Chain(code, wParam, lParam);
                    return 1;
                }
                if (msg == WM_RBUTTONDOWN) _owedRightClick = false;   // 新的一按作废旧账
                // 抬起：先收笔，再让下面的 switch 去跑动作——收笔排在 Fire 前面，
                //   免得动作已经开跑了，屏幕上那条线还挂着。
                // 这两句要包 try：它们是本类里仅剩的无守卫 `_post`。gate 已经把裁决记下了
                // （上面 gv 那一句），而这里一抛就会被最外层的 catch 接走、走到 CallNextHookEx——
                // 于是一条已经记成 Swallow 的事件实际上 Pass 了下去，gate 的状态与真实发生的事分家。
                // 笔迹画不画得出来是小事，裁决对不上号是大事。（定时器里那两处已经是这么写的。）
                // 起点单独排一次 Finish 在前、起点 drain 在后：FIFO 保证旧笔先收、新笔才落第一点。
                if (msg == WM_RBUTTONDOWN) { try { _post(_trailEnd); } catch { } ScheduleTrail(data.X, data.Y, false); }
                else { try { _post(_trailEnd); } catch { } }
                switch (gv)
                {
                    case PressVerdict.Swallow:
                        // 只有「按下要吞掉」这一档还会走到这里。画完没匹配上不再吞——
                        // 它改成 ReplayClick 把右键还给用户（判据见 GestureGate.OnRightUp），
                        // 那句「你画的是 R，它没绑动作」的提示跟着搬到那里去报。
                        return 1;
                    case PressVerdict.ReplayClick:
                        // 这次抬起被判成「普通点击」（Analyze 没画出方向串），要补发右键。
                        // 补发间隔是固定的 ClickGapMs（15ms）——短到目标程序来不及在屏幕上产生可见变化，
                        // 于是没有「闪一下再出菜单」的中间态。菜单在松手后 15ms 内弹出，用户无感。
                        //
                        // 补发投不进去（探针答被拦 / 队列满）就放行这次真抬起：按下已经吞了救不回来，
                        // 但 DefWindowProc 光凭 WM_RBUTTONUP 就会发 WM_CONTEXTMENU，多数程序的菜单
                        // 靠这一下能出来。原先不看返回值直接 return 1，那一下右键在提权窗口里就彻底没了。
                        //
                        // 「画出来却没绑动作」那句提示在这一档报（Silently 吞掉是最难自证的状态）。
                        // **只在抬起报。** 按下不补发、也不读 Path：按下那一刻 Path 已经是空的，
                        // 而从前正是「按下也读一次」让右键点一下弹出上一笔的形状。
                        var drawn = msg == WM_RBUTTONUP ? _gesture.Path : "";
                        if (drawn.Length > 0) _post(() => _unmatched(drawn));
                        if (!ReplayClick(1)) break;
                        return 1;
                    case PressVerdict.Fire:
                        var path = _gesture.Path;
                        var origin = _gestureOrigin;   // 快照进闭包：下一笔按下会覆写这个字段
                        _post(() => _fireGesture(path, origin));   // 绝不在回调里跑动作（见类头注释第 2 条）
                        return 1;
                }
                return Chain(code, wParam, lParam);
            }

            // 移动喂两个 gate：手势那份只记轨迹、永远放行，先喂不影响中键的裁决。
            if (msg == WM_MOUSEMOVE && _gesture != null)
            {
                // 手抬起来了，可我们没收到那条抬起消息（抬手那一刻弹了 UAC 安全桌面，
                // 低级钩子收不到安全桌面上的输入）。不兜这一下的话，状态机会一直以为你还在画：
                // 之后每一次普通的鼠标移动都被当作采样点，那条笔迹就跟着**没按键的光标**满屏跑。
                //
                // 曾经这里查的是物理键态（GetAsyncKeyState(VK_RBUTTON)），那是错的，而且错得彻底：
                // 我们自己刚把那条 WM_RBUTTONDOWN 吞掉（return 1），它就再没进入系统的输入处理，
                // 键态表里右键从头到尾没按下过——于是这一问永远答「没按」，**每一笔手势都在第一次
                // 移动时被自己作废**，一个采样点都攒不下，屏幕上连轨迹都不会出现。
                // 实测：吞掉之后的 486 次移动里，答「按着」0 次。
                // 教训一句话——吞了别人的消息，就不能再拿系统状态当自己的判据。
                //
                // 改用我们自己收到那条按下的时刻。_beatRDown 正是在上面那个分支里盖的戳，
                // 不依赖任何被我们改动过的系统状态。超过 GestureStaleMs 还没抬起，按丢了那条抬起处理：
                // 真手势撑死一两秒，而丢失的抬起原本要挂到下一次右键（或重启）才收得掉。
                if (_gesture.Pending
                    && Environment.TickCount64 - Volatile.Read(ref _beatRDown) > GestureStaleMs)
                {
                    _gesture.Reset();
                    _owedRightClick = true;   // 还欠系统一次按下，等真抬起来领（见字段注释）
                    _post(_trailEnd);
                    return Chain(code, wParam, lParam);
                }
                int before = _gesture.PointCount;
                _gesture.OnMove(data.X, data.Y);
                // 只有真记下了新采样点才画。不比这一下的话，每个 WM_MOUSEMOVE 都要 post 一次——
                // 划一道手势能有上千个移动消息，UI 队列会被这些重绘塞满。
                if (_gesture.PointCount != before)
                {
                    // 命中与否在**钩子线程**上算完再捎过去：LiveMatch 是纯计算，且按新采样点节流
                    // gate 状态只在这条线程上读写，不存在竞态（旧实现里这条线程就是 UI 线程）。
                    // 投递走合帧：一笔几百个采样点不会往 UI 队列塞几百次整屏分层窗重合成。
                    ScheduleTrail(data.X, data.Y, _gesture.LiveMatch());
                }
            }
            if (_gate == null) return Chain(code, wParam, lParam);

            // 中键长按同一条规矩：修饰键按着的中键是别人的组合（浏览器 Ctrl/Shift+中键开标签、
            // 别的工具拿中键做的手势）。按下这一刻整笔放行——gate 不起笔、闹钟不上弦，
            // 随后的移动/抬起天然全部 Pass，修饰键+中键拖拽也走原生路径。
            if (msg == WM_MBUTTONDOWN && _modifierHeld())
                return Chain(code, wParam, lParam);

            var verdict = msg switch
            {
                WM_MBUTTONDOWN => _gate.OnMiddleDown(Environment.TickCount64, data.X, data.Y),
                WM_MBUTTONUP => _gate.OnMiddleUp(Environment.TickCount64),
                _ => _gate.OnMove(data.X, data.Y),
            };

            // 闹钟只在「正扣着一次按下」期间需要走：按下上弦，其余出口一律收弦。
            if (msg == WM_MBUTTONDOWN) Arm(true);
            else if (msg == WM_MBUTTONUP || verdict == PressVerdict.ReplayDownThenPass) Arm(false);

            switch (verdict)
            {
                case PressVerdict.Swallow:
                    return 1;   // 非 0 = 吞掉，下游收不到
                case PressVerdict.ReplayClick:
                    // 与右键同一条路：按下现在发、抬起隔 ClickGapMs 再发（零长度的点击会被下游当噪声丢掉），
                    // 补发被拒就放行真抬起。
                    if (!ReplayClick(2)) break;
                    return 1;   // 真事件吞掉，补发的那次代替它往下走
                case PressVerdict.Fire:
                    // 保留分支：弹面板走的是回调开头的追赶轮询与闹钟（PollFireAndPost），
                    // 同一条 UP 被算 verdict 之前已经到点弹过、gate 转为 _fired，这里不会再命中。
                    // 留着是因为 PressVerdict 是公开枚举，漏一个分支等于默默放行。
                    _post(_fire);   // 绝不在回调里开窗口（见类头注释第 2 条）
                    return 1;
                case PressVerdict.ReplayDownThenPass:
                    // 这次长按被改判成了拖拽：记一笔——它非零就是「按住很久、松手也不弹面板」
                    // 的自证指纹（见 _dragEscapes 字段注释）。
                    Interlocked.Increment(ref _dragEscapes);
                    // 全场**唯一**留在回调线程上的注入（理由见 RescueDragDown / SendSync）。
                    RescueDragDown();
                    break;      // 补发按下后放行本次移动，拖拽继续
            }
            return Chain(code, wParam, lParam);
        }
        catch
        {
            try { return Chain(code, wParam, lParam); } catch { return IntPtr.Zero; }
        }
    }
}
