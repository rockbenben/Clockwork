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
    // 回调超时后由回调自己投给泵线程：在**钩子线程**上摘钩再装，不经过 UI。
    private const uint WM_HOOK_RECOVER = 0x8002;   // WM_APP + 2
    // 让路状态切换：只放行右键手势，不碰中键面板。
    private const uint WM_YIELD = 0x8003;   // WM_APP + 3
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
    // 与上面那格同样的读数，但只覆盖「最近一个让路采样窗口」：让路判据要的是一个**可复位**的
    // 峰值，否则一次 313ms 会让状态永久锁在让路中。lifetime 那格继续供重装日志读。
    private uint _callbackWindowMax;
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
    private readonly Action<int, int, bool, LaunchStep?> _trailPoint;
    // 第三个参数：画到此刻已经命中了吗（笔迹据此变色）；第四个：命中的是**哪一条**（笔迹据此报「这将是哪件事」）。
    // 第四个只在用了解析版构造器时非空——只答「匹不匹配」的那一档本来就知道不了是哪一条，如实答 null。
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
    // 只读缓存，不排队：笔迹中途每个采样点都会问一次（见 CallbackCore 的 A1 修正）。
    private readonly Func<uint, string> _cachedProcNameForPid;
    private readonly Func<uint, bool> _mayEngage;

    // ── 笔迹回调合帧 ──
    // 不把每个采样点都 post 一条重绘：high 灵敏度 + 高刷屏一笔能产生几百个采样点，每个都让
    // UI 线程对一块**整屏大小**的分层窗重合成一次（4K ≈ 33MB/帧）。只记最新一点，且 UI 队列里
    // 同一时刻最多挂着一个 drain；drain 跑起来先放号再画，期间的新点会再排一个。于是空闲时
    // 每点照常一画，爆发时自动塌缩成「每轮只画最新位置」，队列永不被重绘塞满。
    private readonly object _trailLock = new();
    private int _trailX, _trailY;
    private bool _trailArmed;
    // 命中的是哪一条。合帧时它跟着坐标一起换掉——它是**这一串**的属性：
    // 继续画下去可能换成另一条，笔迹旁那句「这将是哪件事」也得跟着换。
    private LaunchStep? _trailHit;
    private int _trailQueued;

    // 委托必须由本对象持有：SetWindowsHookEx 只存了个裸函数指针，GC 不知道它被引用着。
    // 只传一个 lambda 进去、不留字段的话，下一次 GC 之后回调地址就是野指针——进程直接崩，
    // 而且崩在任意一次鼠标移动上，看不出和这里有关。
    private readonly HookProc _proc;
    private IntPtr _hook;
    // 自救请求已投出（防止一次超时在泵线程还没处理时又投一条）。
    private int _recoverPosted;
    // 自救阈值：LowLevelHooksTimeout + 余量，构造时读一次。与让路的预算同源（同一个注册表读数），见 HookWatch。
    private readonly int _recoveryThresholdMs;
    private readonly Action<MouseHook, HookRecoveryReport>? _selfRecovered;
    // 让路中：右键按下/抬起一律放行，只影响手势；中键面板照常。
    private int _yielding;

    // 一代钩子的账。自救重装前抄一份交给 GestureCoordinator，否则下一次查日志时
    // 「上一任钩子到底收到了什么」已经被新钩子覆盖。字段与 _prev* 那组一一对应。
    public readonly record struct HookAccount(
        bool EverBeat, bool RightDown, bool RightUp, bool MiddleDown, bool MiddleUp,
        int MiddleFired, int DragEscapes, long SilentForMs, int InjectRejected,
        uint UpstreamMaxMs, uint CallbackMaxMs, uint ChainMaxMs,
        int ReplayMaxMs, int ReplayQueued, int ReplayDropped, int RescueSkipped);

    public readonly record struct HookRecoveryReport(bool Ok, HookAccount Previous);

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

    private readonly object _replayLock = new();
    private System.Collections.Concurrent.BlockingCollection<ReplayItem>? _replay;
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

    // 欠着的抬起账本（显式状态机，见 app/Core/MouseUpLedger.cs）。
    //
    // 账目的形状全在**那一个类**里：谁欠谁、欠几笔、有几笔在途、退避到第几次、
    // 配对期到什么时候、那一笔交还给程序的按下还在不在。
    // 这一侧只留**单一发送出口**（见 SendUpUnit）与四个驱动源：
    // 补发线程（`ReplayLoop`）、看门狗（`UpWatchdogTick`）、让路时结账（`ApplyYield`）、
    // 拆卸时的结清（`SettleOwedKeys`）。
    //
    // 为什么把它收成一个状态机而不是几个计数器：账目的形状散在各处时，
    // 它们之间的不变式只能靠注释维持——而注释不会跟着代码一起被改。
    // 收成状态机之后，一笔账有编号、在途的一定还挂在某一笔上、转段的地方只有一处：
    // 「认领之后那一笔在途」不再需要单独一个计数器兜着——看门狗看着账清零就把自己销毁，
    // 而那一笔在途的账一失败就没人还了，那种洞正是状态机要消灭的那一类。
    //
    // 欠着的抬起必须在卸载时补上，否则那个键会在全系统卡在按下态，是这个类最不能出的一种事故：
    // 卡住的右键会让**所有左键点击**都被当成「按住右键时的左键」。
    // 2026-10-28 17:50 那次整机左键失效就是它（`GetAsyncKeyState(VK_RBUTTON)=0x8000`；
    // 注入一次抬起即恢复）。
    private readonly MouseUpLedger _upLedger = new();
    // 欠着的抬起原本是冲着哪个窗口去的（句柄）——**只用来诊断**，现在记在账本里每一笔上：
    //
    // SendInput 只能送到「当前前台窗口」，所以这个句柄**不能**用来改投——
    // 退避重试时前台已经换人了，那一次抬起会落在用户没按过的
    // 窗口上（DefWindowProc 会凭它合成一个莫名菜单）。记下来是为了事后能自证是哪一笔。
    //
    // 让路时**交还给程序**的那次按下（`ApplyYield` 里注入的）等的是用户自己的抬起：
    // 那条真抬起会原样流过钩子，系统因此拿到配对的抬起。它也记在账本里。
    //
    // 抬起真丢了的那一档（抬手那一刻弹了 UAC 安全桌面，低级钩子收不到安全桌面上的输入）
    // 由**下一次右键按下**认领：用户要再按一次右键，说明上一次的手已经离开了，
    // 而系统那边那个按下还按着——这时才补一个抬起（见 ClaimLostHandedDown）。
    //
    // 看门狗自己的一拍。
    private const int UpWatchdogPeriodMs = 500;
    // 看门狗一拍最多发几个抬起：它在**线程池**上（不占钩子线程的预算），
    // 但 SendInput 走的是同一条系统输入队列——发太多会把补发线程的用户可见点击堵在后面。
    private const int UpWatchdogMaxSends = 2;
    // 拆卸时那两拍：最多多花 200ms，把「送不进去」的那笔账再试一次。
    private const int SettleRetryCount = 2;
    private const int SettleRetryDelayMs = 100;
    // 钩子已经拆完了：看门狗在欠账还清之后就该退休（否则它会一直把 this 钉住）。
    private int _tornDown;
    private int _upWatchdogGen;
    private int _upWatchdogSeq;
    // 看门狗：只负责把欠着的抬起按退避重试还上（见 MouseUpLedger.ScheduleRetry）。
    private System.Threading.Timer? _upWatchdog;
    // 只护 `_upWatchdog` 这一个指针：武装（读后写的 ??=）与退休必须互斥，理由见 UpWatchdogTick。
    // 它不跨 SendInput、也不在账本锁里去拿——本类所有锁都守着「不许挡住钩子线程」这条纪律。
    private readonly object _watchdogArmLock = new();
    // 看门狗重试/收尾这两件事要能从日志里看见（它们只在「本该正常」的路径出问题时才发生）。
    // 写盘在 AppDiagnostics 的后台线程上，这里只负责把一行文本交出去。
    private readonly Action<string> _diag;
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
    // 同上，供让路判据按窗口读后清零；`_replayMaxMs` 保留为这一任钩子的 lifetime 峰值。
    private int _replayWindowMax;
    // 排队排进去过多少次（右键/中键合在一起）：证明「补发确实在跑」，而不是静默地被跳过。
    private int _replayQueued;
    // 拖拽救援因为**已经在重入里**而被跳过注入的次数（见 RescueDragDown 的限深闸）。
    // 与 `_replayDropped` 同一个理由：这一档是主动退让，不是失败，但它必须看得见——
    // 否则「中键拖拽偶尔不自动滚动」与「压根没触发过救援」在日志里长得一模一样，
    // 而这两种病修法完全不同（前者是我们主动让的，后者是判定没走到）。
    private int _rescueSkipped;
    // 「这次注入送不送得进去」的探子。单测注入用，生产走 `Win32.InputNotBlockedNow`。
    private readonly Func<bool> _canInject;

    // **把一次「抬起」注入系统**。生产走 Win32 的两个 Send*Tagged；单测注入假的实现。
    //
    // 为什么留这道口子：抬起的注入是整个类里**唯一**会造成「键卡在全系统按下态」的动作，
    // 而它又**绝不能**在单测里真的来一次（那会在开发者的桌面上注入一个真的右键抬起）。
    // 于是「发失败要不要销账」「认领之后有没有人发」「看门狗退休判据」这些接线不变式
    // 以前只能钉字符串——而钉字符串拦不住「条件反了」那种改法。
    // 有了这道口子，它们就能**行为级**地验：假的发送口说失败，账就必须还在。
    private readonly Func<MouseUpButton, bool>? _sendUp;

    // **问一句「那个键此刻在系统里是按下的吗」**。生产走 Win32 的键态探针；单测注入假的实现。
    //
    // 它是这套账目唯一的外部真相：账按「笔」记，而系统那边一个键只有一个按下态。
    // 发抬起之前先问一次，问出「已经不按了」就当众销账而不再注入——多发的那一个抬起
    // 是没有配对按下的抬起（DefWindowProc 会凭它合成一个莫名的右键菜单）。
    // 这一问不犯「吞了别人的消息就别拿系统键态当判据」那条：账本里每一笔都来自
    // **我们自己注入成功的按下**，那一次真的进了输入流（理由见 Win32.RightButtonDown 处）。
    private readonly Func<MouseUpButton, bool>? _keyDown;

    // 测试宿主进程里绝不允许走真注入那一条路（哪怕某个测试忘了注入接缝）。
    // 认不出来的一律按生产放行：生产进程名不是这个，所以这道闸对用户端是空转。
    private static readonly bool TestHostProcess =
        Environment.ProcessPath?.EndsWith("Clockwork.Tests.exe", StringComparison.OrdinalIgnoreCase) == true;
    private static int _refusedTestInjections;

    // 「这个程序里不接管手势吗」。名单判据在 GestureGate.IsIgnoredApp（纯函数、有测试），
    // 这里只负责在按下那一刻把进程名问出来（读后台探针的缓存，不做跨进程查询）。
    private readonly Func<string, bool> _ignoredApp;

    /// <param name="holdMs">按住多久算长按；&lt;=0 表示不管中键（只做手势）。</param>
    /// <param name="fire">判定为长按时要做的事（唤出面板）。会经 <paramref name="post"/> 派发，不在回调里直接跑。</param>
    /// <param name="post">把动作投到 UI 队列的方式（App 传 Dispatcher.BeginInvoke）。</param>
    /// <param name="gesture">右键手势的判定器；null 表示不监听右键——没配手势时右键必须一根毫毛都不动。</param>
    /// <param name="fireGesture">手势命中时要做的事，参数是方向串与起笔点所在的顶层窗口句柄（取不到为 Zero）。
    /// 同 fire，经 post 派发；句柄在按下那一刻解析，动作里的「当前窗口」打它而不是前台窗口。</param>
    /// <param name="trailPoint">屏幕上那条笔迹又画到了哪（物理像素），**画到此刻是否已经命中**，
    /// 以及命中的是**哪一条**（没命中为 null）。
    /// 同样经 post——低级钩子回调里有 LowLevelHooksTimeout 的预算，绝不在这里碰 UI。
    /// 命中与否在回调线程上算完（GestureGate.LiveMatch，纯计算、微秒级）再把结论捎过去：
    /// 把 gate 的内部状态交给 UI 线程去读，会撞上「钩子线程正往里加采样点」的竞态。
    /// 第四个参数是那一条**步骤**，不是它的文案：文案要查 i18n 资源，而首次加载卫星程序集
    /// 可能要几毫秒——那几毫秒落在钩子回调里，是 300ms 预算上最不该花的那笔。</param>
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
    /// <param name="ignoredApp">这个程序里**不接管手势**吗（参数是进程名）。
    /// 名单判据在 <see cref="GestureGate.IsIgnoredApp"/>（纯函数、有测试）。
    /// 命中名单时整笔放行：右键完全交给程序自己，一个字节都不碰。</param>
    public MouseHook(int holdMs, Action fire, Action<Action> post,
                     GestureGate? gesture = null, Action<string, IntPtr>? fireGesture = null,
                     Action<int, int, bool, LaunchStep?>? trailPoint = null, Action? trailEnd = null,
                     Action<string>? unmatched = null, Func<bool>? modifierHeld = null,
                     int moveTolerancePhysical = 0, Func<bool>? canInject = null,
                     HookProbe? probe = null,
                     Func<uint, string>? procNameForPid = null, Func<uint, bool>? mayEngage = null,
                     Func<string, bool>? ignoredApp = null,
                     Action<MouseHook, HookRecoveryReport>? selfRecovered = null,
                     Action<string>? diag = null,
                     Func<MouseUpButton, bool>? sendUp = null,
                     Func<MouseUpButton, bool>? keyDown = null)
    {
        _selfRecovered = selfRecovered;
        _sendUp = sendUp;
        _keyDown = keyDown;
        // 诊断通道**绝不许把异常抛出去**：它会被看门狗的 catch 调用，也会被拆卸路径调用——
        // 一个抛出去的异常会终止线程池上的那条线程（进程当场没了），
        // 或者打断拆卸（钩子摘不掉 = 机器上留一条全局鼠标钩子）。
        // 生产那条 AppendErrorLog 自己包了 catch，但这一层不能指望每个调用方都这么小心。
        _diag = diag == null ? (_ => { }) : s => { try { diag(s); } catch { } };
        _recoveryThresholdMs = HookWatch.RecoveryThresholdMs(Win32.LowLevelHooksTimeoutMs());
        _trailPoint = trailPoint ?? ((_, _, _, _) => { });
        _ignoredApp = ignoredApp ?? (_ => false);
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
        _replay = new System.Collections.Concurrent.BlockingCollection<ReplayItem>(ReplayQueueCapacity);
        // **补发前那道「送得进去吗」也必须走探针的快照** —— 它同样要 OpenProcess + 读 token，
        // 而 `ReplayClick` 是被 `CallbackCore` **同步**调的，也就是每一次右键抬起都在回调里做一遍。
        // 漏掉它的话，这一条与 `mayEngage`／进程名那两句是同一个病，只是频率更高（每次点击都做）。
        //
        // 用的是快照里的 `Injectable` 而不是 `MayEngage(pid)`：按下那一刻已经按 pid 校验过一道了，
        // 而这里若再拿 pid 比一次，快照比前台旧半秒就会答 false —— 那一刻真抬起已经被吞，
        // 于是又变成「吞了却还不回去」的那一档，正是本次要治的病。快照答「上次问是能送的」，
        // 是此刻手上最好的知识；真被拒的窄缝照旧计进 `_injectRejected`（见 ReplayClick 的注释）。
        // 配了手势就必须说清「这一笔的起笔窗是谁家的程序」怎么查：
        // 生产走探针（后台线程算好的缓存），单测传假实现。
        //
        // **不传就抛**，而不是悄悄退回 Win32.ProcessNameForPid——那条路每次鼠标移动
        // 都要 OpenProcess + 读镜像路径（本机实测能到秒级，见 HookProbe 类头），
        // 而它正落在低级钩子回调里：超预算 = Windows 静默摘钩（手势就此失效）。
        if (gesture != null && probe == null && procNameForPid == null)
            throw new ArgumentException(
                "配了手势就必须传 probe 或 procNameForPid：默认那条 Win32.ProcessNameForPid "
                + "每次鼠标移动都要 OpenProcess，会拖过 LowLevelHooksTimeout。", nameof(probe));
        if (probe != null) _canInject = () => probe.Injectable;
        else _canInject = canInject ?? Win32.InputNotBlockedNow;
        // 探针在场就用它的答案（后台线程算好的快照），不在场退回同步实现——生产永远传探针，
        // 所以「回调里做跨进程查询」这条路在生产里已经不成立（理由见字段处与 HookProbe 类头）。
        // 两个都**显式取方法组**：写成 `probe?.ProcessName ?? other` 会让编译器报「方法组不可为 null」
        //（方法组不是值类型，没法用 ?? 判空）。
        _procNameForPid = probe != null ? probe.ProcessName
                        : procNameForPid ?? Win32.ProcessNameForPid;
        // 生产走探针的只读缓存；单测注入的假实现本身不会排队，直接复用。
        _cachedProcNameForPid = probe != null ? probe.CachedProcessName : _procNameForPid;
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
    private bool ReplayClick(ReplayKind kind)
    {
        if (!_canInject()) return false;
        return EnqueueReplay(kind);
    }

    // 投一件补发进队列。投不进去当场报 false，并计一笔 `_replayDropped`——**绝不静默丢**：
    // NextGestures 的 `let _ = tx.try_send(ev)` 把丢事件藏起来了，而每丢一次就是用户的一下右键
    // 凭空消失，那正是我们这次要治的病，不能治病的方子里自带一遍。
    //
    // token 只对 <see cref="ReplayKind.ReleaseOwed"/> 有意义：它要还的是**那一笔**欠账，
    // 而不是一个泛泛的「一笔右键抬起」。
    private bool EnqueueReplay(ReplayKind kind, long token = 0)
    {
        var q = ReplayQueue;
        if (q == null || q.IsAddingCompleted || !q.TryAdd(new ReplayItem(kind, token), 0))
        {
            Interlocked.Increment(ref _replayDropped);
            return false;
        }
        Interlocked.Increment(ref _replayQueued);
        return true;
    }

    // 把欠着的抬起补上：每成功发一次抬起销一笔账，**发失败不销账**——清早了就等于把那次
    // 抬起丢了，而丢一次就是那个键在全系统卡在按下态。（旧写法先 Exchange 清零再发，
    // 发失败也照样清零；2026-10-28 17:50 的右键卡死就落在这条缝上。）
    //
    // 只在三条路上被调：看门狗（重试）、ApplyYield（让路时结账）、SettleOwedKeys（拆卸/自救）。
    //
    // **不跨 SendInput 持锁**：SendInput 可能被输入队列堵住（本类实测到 313ms），
    // 而钩子线程也会走这里（ApplyYield / SettleOwedKeys）——它在锁上等着就越过了
    // LowLevelHooksTimeout，Windows 会静默摘掉这个钩子（钩子一摘，手势就没了）。
    // 所以「认领一笔账」与「把它发出去」分开：认领在账本的锁里（谁先到谁领走），
    // 发送在认领之后、锁外。认领与发送分开之后，多线程也只会各领各的一笔，
    // 不会把同一笔账发两次（多出来的那次是一个没有配对的抬起，会合成一个莫名菜单）。
    //
    // **maxSends 是给「钩子还活着」的那条路用的**：钩子线程正是低级钩子回调被派发的地方，
    // 而 SendInput 可能被输入队列堵住——连发几个就越过 LowLevelHooksTimeout，
    // Windows 会静默摘掉钩子。钩子还活着时只发一个（见 ApplyYield 与 SettleOwedKeys），
    // 剩下的账交给补发线程与看门狗（它们不占钩子线程）。
    internal void SettleOwedUps(int maxSends)
    {
        try
        {
            int sent = 0;
            while (sent < maxSends)
            {
                // 认领最老的那一笔（受配对闸保护）：补发线程正在配对（按下→15ms→抬起）时，
                // 抢走那一笔会把点击变成 0~15ms 的零长度点击。
                if (_upLedger.TryClaimOldest(Environment.TickCount64, out var unit) != MouseUpClaim.Claimed)
                    break;
                // 认领之后**再核一次闸**：认领与核之间那几条指令里，补发线程可能刚好
                // 开始配对（闸门刚设上）。这时这一笔属于它，不该由我们发——
                // 发出去就成了 0ms 的零长度点击。把账还回去，下一拍再说。
                if (_upLedger.PairingActive(Environment.TickCount64))
                {
                    _upLedger.ReOwe(unit);
                    break;
                }
                sent++;
                SendUpUnit(unit);
            }
        }
        finally
        {
            // 还欠着就排下一次重试；全部结清就把重试状态清掉。
            long now = Environment.TickCount64;
            if (_upLedger.HasDebt) ScheduleUpRetry(now);
            else _upLedger.ClearRetry();
        }
    }

    // 排下一次退避重试，并把该记的那一行日志记下来。
    //
    // **「第一次失败」那一行必须在这里，而不是在某一个调用点上**：补发线程先失败的时候，
    // 计数已经是 1 了，看门狗那一拍再排就是 2 —— 那行「明知会落在别的窗口上也要发」的
    // 证据就永远不出现了，而它正是事后能自证是哪一笔的唯一线索。
    private void ScheduleUpRetry(long now)
    {
        _upLedger.ScheduleRetry(now);
        if (_upLedger.RetryAttempt == 1)
        {
            // **明知会落在别的窗口上也要发。** 那一笔抬起欠的是「系统里真按下去的那个键」：
            // 不发 → 那个键卡在全系统按下态（所有左键点击都被当成「按住右键时的左键」，
            // 键盘不受影响——这是这个类最不能出的一种事故）；
            // 发了 → 那个窗口收到一条没有配对按下的抬起，DefWindowProc 凭它合成一个莫名菜单。
            // 前者是整机瘫，后者是一次多余的菜单：所以照发。
            //
            // SendInput 只能送到**当前**前台窗口，所以「欠着的那一笔冲着哪个窗口」
            // 只能记下来供事后自证，没法改投（改投要抢前台，而前台锁不认我们）。
            long fg = Win32.ForegroundWindow().ToInt64();
            _diag($"replay up retry: {_upLedger.Describe(now)} rejected={InjectRejected} "
                + $"foreground={(fg == _upLedger.HeadForegroundWindow ? "unchanged" : "moved")}");
        }
    }

    /// <summary>
    /// **整个类里唯一把「抬起」注入系统的地方**（单一发送出口）。
    /// </summary>
    /// <remarks>
    /// 成功 → 那笔账结清；失败 → 账还回去（销账 = 把那次抬起丢了 = 那个键卡在全系统按下态）。
    /// 认领在账本的锁里、发在锁外——理由见 <see cref="SettleOwedUps"/>。
    /// </remarks>
    private bool SendUpUnit(MouseUpUnit unit)
    {
        // **先与真实键态对账**：那个键已经不按着了，说明这一笔早被别人结清了（用户自己的抬起，
        // 或同键积压里我们先前发出去的那一个）。这时再发一次就是没有配对按下的抬起——
        // 目标程序凭它合成一个莫名的右键菜单（用户报的「凭空多出一次右键」）。就地销账、不发；
        // 同键的其余几笔也一样：下一笔认领时问到的还是「不按」，一路销账到底。
        //
        // 这一问为什么不犯「吞了别人的消息就别拿系统键态当自己的判据」那条（见 Win32 处）：
        // 账本里每一笔都来自**我们自己注入成功的按下**，那一次真的进了输入流、在键态表里落过账。
        // 而 SendInput 报成功却压根没落地的那一档（UIPI 在投递侧过滤）也一并兜住：
        // 那个键从头到尾没按下过，问出来是「不按」，销账正是对的结局。
        if (!ButtonDown(unit.Button))
        {
            _upLedger.Settle(unit);
            _diag($"up for {unit.Button} was already discharged; settled without injecting");
            return true;
        }
        bool ok = false;
        try
        {
            if (_sendUp != null) ok = _sendUp(unit.Button);
            else ok = SendTimed(() => unit.Button == MouseUpButton.Right
                    ? Win32.SendRightUpTagged()
                    : Win32.SendMiddleUpTagged());
        }
        finally
        {
            if (ok) _upLedger.Settle(unit);
            else _upLedger.ReOwe(unit);
        }
        return ok;
    }

    /// <summary>
    /// 那个键此刻在系统里是按下的吗。单测注入探针；生产问 Win32 的键态（读一个全局状态位，
    /// 不注入任何东西，受限令牌也读得到）。
    /// </summary>
    private bool ButtonDown(MouseUpButton button)
        => _keyDown != null
            ? _keyDown(button)
            : button == MouseUpButton.Right ? Win32.RightButtonDown() : Win32.MiddleButtonDown();

    // 补发队列里的一件：要么是一次完整的点击，要么是「还某笔欠着的抬起」（带 token）。
    private enum ReplayKind
    {
        /// <summary>一次完整的右键点击（按下 → 15ms → 抬起）。</summary>
        RightClick,
        /// <summary>一次完整的中键点击（短按）。</summary>
        MiddleClick,
        /// <summary>还一笔欠着的抬起：上一次交还给程序的那次按下，它的抬起丢了
        ///（抬手那一刻弹了 UAC 安全桌面，低级钩子收不到安全桌面上的输入）。
        /// 走补发线程，不在回调里注入。</summary>
        ReleaseOwed,
    }

    /// <summary>补发队列里的一件。<see cref="ReplayKind.ReleaseOwed"/> 要带 token——
    /// 它要还的是**那一笔**欠账，不是「随便一笔右键抬起」。</summary>
    private readonly record struct ReplayItem(ReplayKind Kind, long Token);

    // 补发队列。**不是 readonly**：拆过之后同一个对象再装一次时必须换一条队列——
    // 老队列在 Teardown 里被 CompleteAdding 收掉了，不再接受新件；
    // 只换线程不换队列的话，新件会永远排队（那条线程起来也只是空转）。
    //
    // 换队列与读队列用同一把锁。换的动作只在 Install 那一路（不与钩子线程并发）；
    // 真会并发的是钩子线程上的 EnqueueReplay——它读到老队列的那一档，
    // 后果是这一件补发投不进去、调用方放行真事件：用户照样拿到那次点击。
    private System.Collections.Concurrent.BlockingCollection<ReplayItem>? ReplayQueue
    {
        get { lock (_replayLock) return _replay; }
    }
    // 补发线程本体：一条一件，按下 → 隔 ClickGapMs → 抬起，全在同一条线程上，**顺序天然串行**。
    // 从前这件事靠一个跨线程欠账槽保证（闹钟、Dispose、下一次补发前的清账三方抢同一格），
    // 队列把它替掉了；现在「欠着的抬起」记在账本上，由看门狗与结清那两条路还（见 _upLedger）。
    //
    // 为什么还留着 15ms 而不是学 NextGestures 把 down+up 收进同一次 `SendInput`：同批两条事件
    // 时间戳完全相同，等于一次「零长度点击」，会被下游当噪声丢掉（见 ClickGapMs 那段本地实测口径）。
    private void ReplayLoop()
    {
        var q = ReplayQueue;
        if (q == null) return;
        try
        {
            foreach (var item in q.GetConsumingEnumerable())
            {
                switch (item.Kind)
                {
                    case ReplayKind.RightClick:   // 一次完整的右键点击（抬起被改判成普通点击）
                        if (SendTimed(() => Win32.SendRightDownTagged()))
                        {
                            // 按下已经送出去了，抬起从这里起就是欠着的——**先记账再睡觉**：
                            // 这 15ms 里看门狗要是正好跑到（另一条路刚把上一笔的抬起重试完），
                            // 它必须看得见这一笔。
                            // 配对期：看门狗这一拍不许抢这一笔（抢走就成了零长度点击）。
                            // **闸门先设、账后记**：反过来的话，中间那几条指令里看门狗正好赶到，
                            // 就会抢走一笔还没进配对期的账。
                            long now = Environment.TickCount64;
                            _upLedger.ReservePairing(now, MouseUpLedger.PairingGuardMs);
                            var unit = _upLedger.Owe(MouseUpButton.Right, now, Win32.ForegroundWindow().ToInt64());
                            Thread.Sleep(ClickGapMs);
                            // 抬起**自己发**，而且只认领**自己那一笔**：
                            // 认领「最老的那一笔」会把别人的账领走，于是这一笔的抬起反而没人发。
                            // 而看门狗若已经替我们发过了，这一笔就不在账上了——那正好说明抬起已经出去了，
                            // 不再发第二次（多发一次是一个没有配对的抬起，会合成一个莫名菜单）。
                            if (_upLedger.TryClaimToken(unit.Token, out var mine) && !SendUpUnit(mine))
                                ScheduleUpRetry(Environment.TickCount64);
                            _upLedger.ReleasePairing();
                        }
                        break;
                    case ReplayKind.MiddleClick:   // 一次完整的中键点击（短按）
                        if (SendTimed(() => Win32.SendMiddleDownTagged()))
                        {
                            long now = Environment.TickCount64;
                            _upLedger.ReservePairing(now, MouseUpLedger.PairingGuardMs);
                            var unit = _upLedger.Owe(MouseUpButton.Middle, now, Win32.ForegroundWindow().ToInt64());
                            Thread.Sleep(ClickGapMs);
                            if (_upLedger.TryClaimToken(unit.Token, out var mine) && !SendUpUnit(mine))
                                ScheduleUpRetry(Environment.TickCount64);
                            _upLedger.ReleasePairing();
                        }
                        break;
                    case ReplayKind.ReleaseOwed:
                        // 还那一笔欠着的抬起。认领的是**指定**的那一笔（token），
                        // 不是「随便一笔右键抬起」——那一笔欠的是某个具体的键。
                        if (_upLedger.TryClaimToken(item.Token, out var owed) && !SendUpUnit(owed))
                            ScheduleUpRetry(Environment.TickCount64);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            // 队列被 Dispose / 线程被中止：补发就此停住。不会留下卡住的按键——摘钩那一路
            // 由 SettleOwedKeys 无条件还键并结清欠着的抬起，与这条线程活不活无关。
            //
            // 但**这件事必须看得见**：这条线程是 `ReleaseOwed` 那件补发的唯一认领人，
            // 它死了而钩子还装着，之后的补发会一路填满队列、每一次 ReplayClick 都退化成放行，
            // 日志里「从没需要补发」与「补发线程死了」长得一模一样。
            // 配对期也在这里收回来：异常若正好落在配对中间，闸门留着只会让看门狗白等
            //（它自己会过期，但那一拍本来就能还账）。
            _upLedger.ReleasePairing();
            _diag($"replay loop ended: {ex}");
        }
    }

    // 看门狗：把欠着的抬起按退避重试还上。
    // 500ms 一拍：它不在任何输入路径上，代价只是一条睡着的定时器。
    //
    // **它不碰「交还给程序的那次按下」**：那个按下等的是用户自己的抬起，
    // 而用户按多久都可能——计时器分不清「用户还按着」与「抬起真的丢了」，
    // 一响就给用户还按着的键补一个抬起（目标程序先收到一次提前的松开，
    // 稍后用户真松手时又来一条没有配对按下的抬起，凭它合成一个莫名菜单）。
    // 那一档由**下一次右键按下**认领（见 ClaimLostHandedDown）。

    /// <summary>
    /// 这只看门狗此刻可以退休吗：代号还是自己的 + 补发线程已退 + 账与在途都清零。
    ///
    /// 三个读都是各自原子的、合起来**不是**原子的，所以退休那一头要在
    /// <see cref="_watchdogArmLock"/> 里再问一遍（理由见 UpWatchdogTick 里那段）。
    /// </summary>
    private bool WatchdogRetirable(int gen)
    {
        var replay = Volatile.Read(ref _replayThread);
        return Volatile.Read(ref _upWatchdogGen) == gen
            && (replay == null || !replay.IsAlive)
            && _upLedger.ShouldRetireWatchdog(Volatile.Read(ref _tornDown) != 0);
    }

    internal void UpWatchdogTick()
    {
        // 这一拍属于哪一只看门狗：退休时只退自己那一只（见下面的代号）。
        int gen = Volatile.Read(ref _upWatchdogGen);
        try
        {
            // 「该不该动账」由账本判：没欠账、还没到重试时刻、或者补发线程正在配对，都不动。
            switch (_upLedger.Decide(Environment.TickCount64))
            {
                case MouseUpDecision.TakeOver:
                    // **快路径**：这一笔没人排过重试（补发线程卡住了）→ 看门狗接手。
                    // 记一笔：释放会落在**当前**前台窗口上，而那一笔原本是冲着别的窗口的
                    //（SendInput 只能送到当前前台窗口 —— 这条取舍见 SettleOwedUps）。
                    _diag($"up watchdog took over an unscheduled debt: {_upLedger.Describe(Environment.TickCount64)}");
                    SettleOwedUps(UpWatchdogMaxSends);
                    break;
                case MouseUpDecision.Due:
                    SettleOwedUps(UpWatchdogMaxSends);
                    break;
            }
            // 钩子已经拆完、账也还清了 → 看门狗退休。
            // **它不认输**：还不上就一直退避重试（见账本类头），所以这里没有「放弃」那一档；
            // 判据在账本里：**在途的那几笔也算没还清**——账目在认领时就少了，
            // 而抬起还在发送里；那一刻若把看门狗销毁，那笔在途的账一失败就没人还了
            //（失败时它把账还回去并排重试，可已经没有看门狗去认领那个重试）。
            //
            // **补发线程还活着时不许退休**：它正在配对的那一笔（按下已经送进系统、
            // 抬起还没发）此刻还**不在账上**——账是在按下送出去之后才记的。
            // 它要是正好在这个窗口里被拆掉，看门狗就会看着「账清零」先走，
            // 那一笔随后记上就再没人还了（那个键卡在全系统按下态）。
            // 补发线程退出前会把它手上那一笔还上，所以等它退干净再退休就够了。
            if (WatchdogRetirable(gen))
            {
                // **先把引用摘走，再释放**：反过来（先 Dispose 再置 null）的话，
                // 这一拍若正好被抢占在两句之间，而此刻装钩子那条路走到
                // `_upWatchdog ??= new Timer(...)`——它看到的是**非空但已经释放**的定时器，
                // 于是不新建；而这一拍回来再把字段置空：**看门狗就此彻底没有了**，
                // 之后的欠账没人还（那个键卡在全系统按下态）。
                //
                // 摘走与释放还要和装钩子那条路的「武装」抢同一把锁，并在锁里**把判据再看一遍**：
                // `??=` 是读后写、不是原子的——那一头读到非空就不新建，而这一头的判据是几条指令
                // 之前读的（代号、补发线程、已拆标志）。那时顺序变成「它读到旧的非空 → 不新建 →
                // 我释放掉那只唯一的定时器」，这一代同样没有看门狗。Install 与 Dispose 会并发是
                // 这个仓库写明的既有事实（见 `_installed` 那段：安装改到后台线程上等之后，
                // 重叠成了必然），所以这不是纸面上的竞态。
                // 这把锁只护定时器指针：不跨 SendInput，也不在任何别的锁里再拿它。
                lock (_watchdogArmLock)
                {
                    if (!WatchdogRetirable(gen)) return;   // 换代了：这一拍不是这只看门狗的主人
                    var retired = Interlocked.Exchange(ref _upWatchdog, null);
                    try { retired?.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            // **不静默**：这个看门狗的职责是解掉一个卡在按下态的键。
            // 它自己抛异常 = 那笔欠账没人还，而用户那边什么症状都没有。
            // 诊断通道（生产写进错误日志）就是为这类事准备的。
            _diag($"up watchdog failed: {ex}");
        }
    }

    // 一次注入：耗时记进 `_replayMaxMs`，被 UIPI 丢掉就计 `_injectRejected`。
    // 跑在补发线程、看门狗与结清路径上——它们都不在钩子回调里，所以这里怎么慢都越不过
    // LowLevelHooksTimeout。返回「送出去了没有」——按下被丢时不该再补一次无配对的抬起（见上面的台账）。
    /// <summary>
    /// **装配级守卫**：测试宿主里不许把真的鼠标事件送进系统。两个同步注入出口（`SendTimed`、
    /// `SendSync`）都先问这一句，所以这不靠每条测试自己记得清账、关闸门。
    ///
    /// 为什么值得收成一处：这条纪律原先写在每条测试的收尾里（`gate.Reset()`、`ClearOwedUps()`），
    /// 而纪律不是守卫——任何一条新测试忘了，就往开发者的桌面上注入一次真的按下或抬起；
    /// 抬起那一路的后果是「一个用户没按过的键被松开」（莫名右键菜单），收不回来。
    /// 拒发之后账还留着，看门狗继续退避重试（少发一次是有主人的），所以拒发不会把键留在按下态。
    ///
    /// 认不出测试宿主的一律按生产放行：生产进程叫 Clockwork.exe，不是 Clockwork.Tests.exe，
    /// 所以这道闸对用户端是空转。真的要走真注入，用 `--hookprobe`（那是生产 exe 里的开关）。
    /// </summary>
    private bool RefusedInTestHost()
    {
        if (!TestHostProcess) return false;
        Interlocked.Increment(ref _refusedTestInjections);
        _diag("refused a real mouse injection under the test host");
        return true;
    }

    /// <summary>测试宿主里被挡下的真注入次数（装配级守卫的读数，见 <see cref="RefusedInTestHost"/>）。</summary>
    internal static int RefusedTestInjections => Volatile.Read(ref _refusedTestInjections);

    private bool SendTimed(Func<uint> send)
    {
        if (RefusedInTestHost()) return false;
        long t = Environment.TickCount64;
        uint sent;
        try { sent = send(); }
        finally
        {
            RecordReplayMs(Environment.TickCount64 - t);
        }
        if (sent == 0) { Interlocked.Increment(ref _injectRejected); return false; }
        return true;
    }

    // 起补发线程。与钩子线程同起同收（见 Install / Teardown）。
    // 必须 `IsBackground`：否则用户点了托盘退出，进程还要陪一条睡在队列上的线程赖着不走
    //（本仓库为写盘线程钉过同一件事，见 OffUiThreadTests）。名字是排障时认出它的唯一办法。
    private void StartReplayWorker()
    {
        // 拆过之后必须**连线程带队列一起换**：老队列已被 CompleteAdding 收掉，
        // 老线程也可能还在收尾（它最多再跑 15ms）。
        // 早退会留下一个「队列已收、没有看门狗、_tornDown 还是 1」的半拉子钩子——
        // 那样之后任何一次发失败的抬起都永远没人重试（那个键就卡在按下态）。
        bool torn = Volatile.Read(ref _tornDown) != 0;
        if (!torn && _replayThread != null && _replayThread.IsAlive) return;
        // **先把「已拆完」清回去，再换线程**：这个对象可能拆过又装回来（队列已经换新，见下面），
        // 而看门狗在「已拆完 + 欠账清零」时会自己退休——标志不回去的话，
        // 新起的看门狗第一拍就退休，之后的欠账没人还。
        //
        // 顺序是承重的：换线程要花几毫秒（要换队列、要起线程），而**看门狗每一拍都在跑**。
        // 标志在这几毫秒里还是「已拆完」的话，那一拍就可能看着「账清零 + 补发线程还没活」
        // 判定可以退休——而补发线程马上就要记一笔新账（按下已经送进系统、抬起还没发），
        // 那一笔就再没人还了（那个键卡在全系统按下态）。
        Volatile.Write(ref _tornDown, 0);
        // 换一只看门狗就换一个号：在途的一拍拿着旧号，退休时就不会把新的这只也销毁。
        // 同样要**在换线程之前**换号：旧号的那一拍还在路上，它不能把新起的这只也退休掉。
        Volatile.Write(ref _upWatchdogGen, Interlocked.Increment(ref _upWatchdogSeq));
        // 线程死了（拆过之后）要**连队列一起换**：老队列在 Teardown 里被 CompleteAdding
        // 收掉了，不再接受新件——只换线程的话新件会永远排队，那条线程起来也只是空转。
        lock (_replayLock)
        {
            if (_replay == null || _replay.IsAddingCompleted)
                _replay = new System.Collections.Concurrent.BlockingCollection<ReplayItem>(ReplayQueueCapacity);
        }
        // **发布**：看门狗会在别的线程上读它（退休判据里要看它是不是还活着），
        // 而读到的若是旧值（null），它就会以为补发线程已经收工而先走。
        var replay = new Thread(ReplayLoop) { Name = "Clockwork.Replay", IsBackground = true };
        Volatile.Write(ref _replayThread, replay);
        replay.Start();
        // 抬起账的看门狗与补发线程同起同收（见 UpWatchdogTick）。放在这里而不是构造函数：
        // 构造函数不许偷开线程/定时器（与钩子线程同一条纪律）。
        // 与退休抢同一把锁：那一头在锁里会把代号再看一遍，两头不会互相踩到。
        lock (_watchdogArmLock)
        {
            _upWatchdog ??= new System.Threading.Timer(_ => UpWatchdogTick(), null, UpWatchdogPeriodMs, UpWatchdogPeriodMs);
        }
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
    // 还的时候走 ReplayClick(RightClick)——按下、吞掉当前这条真抬起、抬起由补发线程隔 ClickGapMs 补上，
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

    // 回调超时后向泵线程投一条自救请求。只投一次；泵线程不在（单测 / 已退出）就不投。
    private void RequestSelfRecovery()
    {
        uint id = _threadId;
        if (id == 0) return;
        if (Interlocked.Exchange(ref _recoverPosted, 1) != 0) return;
        try
        {
            if (!PostThreadMessage(id, WM_HOOK_RECOVER, IntPtr.Zero, IntPtr.Zero))
                Volatile.Write(ref _recoverPosted, 0);
        }
        catch { Volatile.Write(ref _recoverPosted, 0); }
    }

    // 自救的落点：在钩子线程的泵里摘钩、重装。**绝不在回调里做**——回调已经超预算，
    // 再在它里面摘装只会把这一格拖得更长；而 PostThreadMessage 本身是微秒级。
    private void RecoverHook()
    {
        Volatile.Write(ref _recoverPosted, 0);
        if (Volatile.Read(ref _disposePosted) != 0) return;   // 正在拆：不许把钩子装回来

        // **先摘钩，再还键。** 还键那一步最多要睡 200ms（等注入送进去，见 SettleOwedKeys），
        // 而旧钩子此刻还在链上：它的回调这 200ms 里没人服务，正好越过 LowLevelHooksTimeout，
        // Windows 会静默摘掉它——而这正是我们要换掉它的那一次（自愈把「钩子被摘了」当判据，
        // 于是下一次自愈又白跑一趟）。
        // 摘掉之后用户那条真抬起直接进系统，与还回去的那个按下配成一次普通右键——
        // 与「钩子还在链上」时得到的结果一样。
        var old = Volatile.Read(ref _hook);
        Volatile.Write(ref _hook, IntPtr.Zero);
        UnhookNow(old);

        // 把这一代欠系统的键结掉（还键），再重置 gate；顺序反了会把「用户还按着」的按下留在半空。
        try { SettleOwedKeys(); } catch { }
        // 账本里那笔「交还给程序的按下」与 Teardown 同一口径：钩子一摘，用户那条真抬起就直接进系统，
        // 与还回去的那个按下配成一次普通右键——**不需要**再补一个抬起（补了反而凭空多一次右键）。
        //
        // 这一步与「把旧钩子摘掉」的先后顺序有关：摘钩前到达的抬起走的是正常路径
        // （当场配对，标记当场清掉）；摘钩后到达的抬起直接进系统（也配上了），
        // 只是**钩子看不见它**——所以标记必须在这里清掉，否则下一次右键会认领一笔
        // 已经还过的账，补出一个没有配对按下的抬起。
        _upLedger.ClearHandedDown();
        Arm(false);
        _gate?.Reset();
        _gesture?.Reset();
        _owedRightClick = false;
        try { _post(_trailEnd); } catch { }

        if (Volatile.Read(ref _disposePosted) != 0) { ResetGenerationMetrics(); return; }

        IntPtr h;
        try
        {
            h = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
            LastError = h == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        }
        catch { h = IntPtr.Zero; LastError = -1; }

        // 拆钩可能在我们重装的同时刚开始：这时新钩子已经不合法，立刻摘掉，别留给链。
        if (Volatile.Read(ref _disposePosted) != 0)
        {
            if (h != IntPtr.Zero) UnhookNow(h);
            ResetGenerationMetrics();
            return;
        }

        Volatile.Write(ref _hook, h);
        // 装上之后**再核一次**「正在拆」：上面那次核对与这一行之间仍有一个窗口，
        // DisposeCore 可能正好在中间把 _disposePosted 置上了。那时这个新钩子就没人摘——
        // 它比这个对象活得久，而回调会打到一个已被回收的桩（进程随机崩在鼠标移动上）。
        if (Volatile.Read(ref _disposePosted) != 0)
        {
            if (h != IntPtr.Zero) UnhookNow(h);
            Volatile.Write(ref _hook, IntPtr.Zero);
            ResetGenerationMetrics();
            return;
        }
        _installResult = h != IntPtr.Zero ? 1 : 0;
        var account = SnapshotAccount();
        ResetGenerationMetrics();
        Volatile.Write(ref _downsSettled, 0);
        try { _selfRecovered?.Invoke(this, new HookRecoveryReport(h != IntPtr.Zero, account)); } catch { }
    }

    // 认领「上一次交还给程序的那次按下，它的抬起丢了」：补一个抬起，把那个键收掉。
    //
    // **只在「这一笔按下真的被我们吞了」的时候才认领**（见 Callback 里 PressVerdict.Swallow）：
    // 放行的那些（修饰键组合手势、前台刚切过去、用户自己的不接管名单），
    // 它们自己的抬起会原样进系统——那个抬起已经把卡住的键松开了。
    // 这时再补一个抬起就是没有配对按下的抬起 → DefWindowProc 凭它合成一个莫名的右键菜单。
    //
    // **不能靠计时器**：用户按着不放多久都可能，计时器分不清「用户还按着」
    // 与「抬起真的丢了」，而猜错的方向是给一个用户还按着的键补一个抬起（凭空多出一次右键）。
    private void ClaimLostHandedDown()
    {
        // 认领与记账是**一次**状态转换（账本里那一句）：分两步做的话，
        // 中间那一瞬可能出现「标记清了、账还没记上」——那笔账就永远没人还。
        long now = Environment.TickCount64;
        // **它被交还之后等了多久**：用户按了多久才认领——这个数字能分清两种病：
        // 刚抬手就丢（安全桌面吃掉了抬起）与按了半天才松（抬起一直不来）。
        long heldMs = _upLedger.HandedDownAgeMs(now);
        if (!_upLedger.ClaimHandedDown(now, Win32.ForegroundWindow().ToInt64(), out var unit))
            return;
        // **投不进去也得有人还这笔账**：队列满了或已收 → 这一件没人领，这里排一次重试
        // 是把它交给退避梯子（500ms → 5s）。不排也不会永远挂着：看门狗的 TakeOver 快路径
        // 会接手（见 Decide 那一档），但那条路**每拍立刻试一次、不退避**——而此刻的处境
        // 正是「前台是提权窗口，这一次注入注定被拒」，半秒撞一次没有意义。
        //（2026-10-28 那次整机左键失效的同一类事故：欠着的抬起必须有主人，只是主人要退避着试。）
        if (!EnqueueReplay(ReplayKind.ReleaseOwed, unit.Token))
            ScheduleUpRetry(now);
        _diag($"handed right down was never released by the user (held {heldMs}ms); queued one release");
    }

    // 让路切换的钩子线程落点。进入时：
    //   · 已经扣下的右键按下必须还给系统（否则让路本身又吞了用户一键）；
    //   · 丢掉这一笔手势与屏上的线；
    //   · **不碰中键 gate**——让路只放右键，面板照常。
    private void ApplyYield(bool on)
    {
        // 标志已经由 SetYielding 先落了；这里只负责钩子线程上的结算。
        // 若在消息排队期间又被切回 false，就不许再结算——那会把刚恢复的一笔手势清掉。
        if (!on || Volatile.Read(ref _yielding) == 0) return;
        // **只发一个**（见 SettleOwedUps 的 maxSends）：钩子线程不能连着发好几个抬起。
        try { SettleOwedUps(1); } catch { }
        // 这一下是**交还给程序**的按下：它等的是用户自己的抬起（随后原样流过钩子）。
        //
        // **只在真抬起还没到时才还**：闸门 Pending = 用户的手还没离开 = 真抬起还没到。
        // 让路路径上的真抬起会重置闸门（见 Callback 里让路那一段），所以
        // 抬起先到了的那一档，这里 Pending 已经是 false —— 此时按下从没进过系统，
        // 再补一个按下就是**没有配对抬起**的按下：那个键会卡在全系统按下态，
        // 直到有人替它补一个抬起（而那一下又会多出一次右键）。
        if (_gesture?.Pending == true && SendTimed(() => Win32.SendRightDownTagged()))
            _upLedger.MarkHandedDown(Environment.TickCount64);
        _gesture?.Reset();
        _owedRightClick = false;
        try { _post(_trailEnd); } catch { }
    }

    private HookAccount SnapshotAccount()
        => new(EverBeat, EverRightDown, EverRightUp, EverMiddleDown, EverMiddleUp,
               MiddleFireCount, MiddleDragEscapeCount, SinceBeatMs, InjectRejected,
               UpstreamMaxMs, CallbackMaxMs, ChainMaxMs, ReplayMaxMs, ReplayQueued,
               ReplayDropped, RescueSkipped);

    // 自救重装 = 新的一代钩子：lifetime 读数必须跟着新钩子归零，否则下一次日志会把
    // 上一代的峰值当成这一代的账。只由钩子线程调用（或拆钩后不再有回调的路径）。
    private void ResetGenerationMetrics()
    {
        Interlocked.Exchange(ref _beat, 0);
        Interlocked.Exchange(ref _beatRDown, 0);
        Interlocked.Exchange(ref _beatRUp, 0);
        Interlocked.Exchange(ref _beatMDown, 0);
        Interlocked.Exchange(ref _beatMUp, 0);
        Volatile.Write(ref _upstreamMax, 0u);
        Volatile.Write(ref _callbackMax, 0u);
        Volatile.Write(ref _chainMax, 0u);
        _chainMs = 0;
        Interlocked.Exchange(ref _middleFired, 0);
        Interlocked.Exchange(ref _dragEscapes, 0);
        Interlocked.Exchange(ref _injectRejected, 0);
        Interlocked.Exchange(ref _replayMaxMs, 0);
        // **不重置 _callbackWindowMax / _replayWindowMax**：自救正是由一次超预算回调触发的，
        // 这两个窗口峰值是让路判据要读的证据；在这里清掉，下一拍就看不到「刚刚超时过」。
        // lifetime 峰值归零供新的重装账；窗口峰值由 TakeCongestionPeaks 每拍读后清零。
        Interlocked.Exchange(ref _replayQueued, 0);
        Interlocked.Exchange(ref _replayDropped, 0);
        Interlocked.Exchange(ref _rescueSkipped, 0);
        // **不动抬起账本**（`_upLedger`）：那是「已经送进系统的按下」，跨代际仍然欠着。
        // 自救重装前刚用 SettleOwedKeys 试过一次；没送出去的部分要留给看门狗继续还，
        // 在这里清零就等于宣告它已经还了——而系统那边那个键还按着。
        _gesturePid = 0;
        _gestureOrigin = IntPtr.Zero;
    }

    private void RecordReplayMs(long ms)
    {
        int v = (int)ms;
        MaxInto(ref _replayMaxMs, v);
        MaxInto(ref _replayWindowMax, v);
    }

    // 取大：CAS，而不是「读—比—写」。
    //
    // 读—比—写会让两个并发的回调里**较小的那个盖掉较大的那个**：峰值是让路判据的输入
    // （它决定要不要让路），盖掉一次峰值 = 一次超预算回调没被看见。
    // internal（不是 private）：并发取大这件事只能用真的并发验——
    // 顺序跑的话，「读—比—写」那种朴素写法也会全过，而那正是会盖掉峰值的写法。
    internal static void MaxInto(ref int target, int value)
    {
        int cur = Volatile.Read(ref target);
        while (value > cur)
        {
            int prev = Interlocked.CompareExchange(ref target, value, cur);
            if (prev == cur) return;
            cur = prev;
        }
    }

    internal static void MaxInto(ref uint target, uint value)
    {
        uint cur = Volatile.Read(ref target);
        while (value > cur)
        {
            uint prev = Interlocked.CompareExchange(ref target, value, cur);
            if (prev == cur) return;
            cur = prev;
        }
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
    private void ScheduleTrail(int x, int y, bool armed, LaunchStep? hit)
    {
        lock (_trailLock) { _trailX = x; _trailY = y; _trailArmed = armed; _trailHit = hit; }
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
        LaunchStep? hit;
        lock (_trailLock) { x = _trailX; y = _trailY; armed = _trailArmed; hit = _trailHit; }
        _trailPoint(x, y, armed, hit);
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
                else if (msg.Message == WM_HOOK_RECOVER) RecoverHook();
                else if (msg.Message == WM_YIELD) ApplyYield(msg.WParam != IntPtr.Zero);
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
            SettleOwedUps(1);   // 欠着的抬起先补上，否则键卡在按下态
            // 还送不进去就再等两拍：这是拆卸之前最后一次有人替这笔账重试的机会
            // （看门狗在拆卸时若账还没清零会留下来继续退避重试，见 Teardown）。
            // 最多多花 200ms，换的是不把卡死键态留给用户。
            for (int i = 0; i < SettleRetryCount && _upLedger.HasDebt; i++)
            {
                Thread.Sleep(SettleRetryDelayMs);
                SettleOwedUps(1);   // **一次一个**（见 SettleOwedUps 的 maxSends）：这条线程上还挂着钩子
            }
            // **扣着的那次真按下要还回去，不是清掉就算完。**
            // 两个 gate 处于 pending，意思就是「用户按下去了，而那一下被我们 return 1 吞掉了」。
            // 只 Reset 的话那次按下永远不存在，而用户的手指还在键上——松手时真正的抬起会原样流下去
            //（新 gate 不在 pending，不拦），于是光标底下那个程序收到一记没有配对按下的抬起：
            // DefWindowProc 凭它合成一个莫名其妙的右键菜单，自己做按下-抬起配对的程序则拖拽状态错乱。
            // 走到这里的路很日常：Windows 静默摘掉钩子 → 自愈重装（HealMouseHookIfDead），
            // 而用户此刻正按着右键画手势；托盘退出、每一次带键重装同理。
            if (_gesture?.Pending == true)
            {
                // **注入失败就不能记下那笔按下**：系统那边根本没收到这个按下，
                // 记了它就等于记了一笔不存在的外债——后来的「补一个抬起」会变成野抬起
                //（没有配对按下的抬起，DefWindowProc 凭它合成一个莫名菜单）。
                if (SendTimed(() => Win32.SendRightDownTagged()))
                    _upLedger.MarkHandedDown(Environment.TickCount64);
            }
            // 中键只在 Pending 时还，**`_fired` 那一档故意不还**，与右键不对称是有理由的：
            // · `_fired` 意思是面板已经弹出来了，此刻注入一个中键按下很可能把它当场点掉
            //   （LongPressGate 那边为同一个理由把随后的抬起吞掉：「否则面板刚弹出来就被点掉了」）。
            // · 不还的代价又比右键小一个量级：孤立的中键抬起在绝大多数程序里是惰性的，
            //   而孤立的右键抬起会被 DefWindowProc 合成一个 WM_CONTEXTMENU（那才是看得见的毛病）。
            // 已经被当成「漏了 _fired 一半」提过一次，留这段免得反复。
            //
            // 还回去的那个中键按下**同样要记账**（与上面右键那一档同一个理由）：它是真进了系统的
            // 按下，没人替它配对就是中键卡在全系统按下态 = 中键拖拽永远在拖。
            // 右键那一档不记账是有主人的（交还给程序的按下由用户自己的抬起配对），
            // 而这里没有：钩子已经要摘了，之后再也没有一条路会为它补抬起。
            // 发送前会与真实键态对账（见 SendUpUnit），所以这条账不会多补一个抬起。
            // 顺带把它收进 SendTimed：送不进去要计 `_injectRejected`、耗时要进 `_replayMaxMs`，
            // 否则这一句在仪表上是完全隐形的。
            if (_gate?.Pending == true && SendTimed(() => Win32.SendMiddleDownTagged()))
                _upLedger.Owe(MouseUpButton.Middle, Environment.TickCount64, Win32.ForegroundWindow().ToInt64());
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
        try { ReplayQueue?.CompleteAdding(); } catch { }
        // **看门狗只在欠账清零时才收工**：欠着的抬起是系统里真按下去的那个键，
        // 丢了它就是整台机器的左键失效（2026-10-28 17:50 那次整机左键失效就是这个）。
        // 欠着就让它继续按退避重试（Timer 跑在后台，不挡进程退出；而它持有 this，
        // 欠账还清之前这个对象也不会被回收）。看门狗在欠账还清之后自己退休（见 UpWatchdogTick）。
        // **看门狗不在这里销毁**：账目在认领时就少了（那一笔还在发送里），
        // 此刻读出来「清零」不代表系统那边不欠抬起；而在途的那一笔一失败就把账还回去
        // 并排重试——看门狗要是已经没了，那个重试没人认领 → 那个键卡在全系统按下态
        //（2026-10-28 那次整机左键失效的同一类事故）。
        // 退休交给看门狗自己：钩子已拆 + 账清零 + 没有在途的那几笔（见 UpWatchdogTick）。
        Volatile.Write(ref _tornDown, 1);
        // 账本里那笔「交还给程序的按下」也清掉：钩子摘掉之后，用户那条真抬起会直接进系统，
        // 不存在「没人替它配对」的问题；再补一个抬起反而会在程序里凭空多出一次抬起。
        //
        // 这里**故意不转成一笔欠着的抬起**（与上面还中键那一档相反），三档摊开说：
        //   · 用户还按着 → 他的真抬起马上进系统配平它。要是这里记了账，看门狗下一拍（≤500ms）
        //     问键态答「按着」就会补一个抬起——在他手里提前松开，而他真松手时又是一条野抬起。
        //     右键拖拽、按住选菜单都是这一档，常见。
        //   · 抬起已经被安全桌面吃掉（手已经离开）→ 那个键此时刻在全系统按下态，而标记被清掉
        //     之后就没人再管它。这是这一档留下的缝。
        //   · 记了账也救不了第二条：区分「他还按着」与「已经丢了」要的正是那个我们拿不到的信息。
        // 取向：宁可留第二条那个少见的缝，不在第一条那一档每次退出/重装都掐断用户的右键拖拽。
        // 与「不用计时器兜底」是同一条决定的两半（见 MouseUpLedger.MarkHandedDown）。
        _upLedger.ClearHandedDown();
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

    /// <summary>拖拽救援因已在重入中而被跳过的次数（见 <see cref="RescueDragDown"/> 的限深闸）。
    /// 它非零 = 那段时间下游的链正在堵，而我们是**主动**不再往里叠注入，不是判定没走到。</summary>
    public int RescueSkipped => Volatile.Read(ref _rescueSkipped);

    /// <summary>欠着的抬起账本（internal：测试要读它的状态；生产只经由本类那三条路动它）。</summary>
    internal MouseUpLedger UpLedger => _upLedger;

    /// <summary>读走「本让路窗口」的回调/注入峰值，并把它们清零。lifetime 峰值不受影响。</summary>
    public (int CallbackMaxMs, int ReplayMaxMs) TakeCongestionPeaks()
        => ((int)Interlocked.Exchange(ref _callbackWindowMax, 0),
            Interlocked.Exchange(ref _replayWindowMax, 0));

    /// <summary>手势是否正在让路（只影响右键，中键面板照常）。</summary>
    public bool Yielding => Volatile.Read(ref _yielding) != 0;

    /// <summary>切换让路。必须落在钩子线程上处理：进入时要结算已吞下的右键。</summary>
    public void SetYielding(bool on)
    {
        // 标志先落，回调立刻就能看到「别再吞右键」；结算消息随后交给钩子线程做。
        int target = on ? 1 : 0;
        int previous = Interlocked.Exchange(ref _yielding, target);
        if (previous == target) return;
        uint id = _threadId;
        if (id == 0) return;   // 单测 / 还没装：没有 gate 在跑，改标志就够
        try
        {
            if (PostThreadMessage(id, WM_YIELD, new IntPtr(target), IntPtr.Zero)) return;
            // 投递失败（钩子线程的消息队列满了）。
            _diag($"set yielding failed: hook thread queue full (target={target})");
            // **只有「进入让路」那一档要退回去**：结算消息没到 = 那一笔被吞掉的按下永远还不了，
            // 而退回去之后回调继续按老路接管（那条抬起会被判成「手势没命中」→ 补一次点击）。
            //
            // **「退出让路」那一档不能退**：退出这一边根本没有结算要做
            //（ApplyYield 对 on=false 直接返回），**标志本身就是退出这件事的全部**。
            // 退回去 = 让路永远退不掉，钩子会一直放行右键 = 手势静默失效。
            if (target == 1) Interlocked.Exchange(ref _yielding, previous);
        }
        catch
        {
            _diag($"set yielding failed: post threw (target={target})");
            if (target == 1) Interlocked.Exchange(ref _yielding, previous);
        }
    }

    /// <summary>把一次回调的时长拆成「链 / 剩下才是我们自己的计算」。</summary>
    // 纯函数，为了这一句能被断言钉住（日志归因写错方向比没有归因更糟）。
    // TickCount64 是毫秒粒度，取整会让 chain 略大于 total，ours 兜到 0，不代表算错。
    internal static string Attribution(long totalMs, long chainMs)
        => $"chain={chainMs}ms ours={Math.Max(0, totalMs - chainMs)}ms";

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        _cbDepth++;
        bool outermost = _cbDepth == 1;
        if (outermost) _chainMs = 0;   // 只有最外层清账，理由见字段处
        long t0 = Core.UiStallWatch.Begin();
        try { return CallbackCore(code, wParam, lParam); }
        finally
        {
            // 先记峰值再交给 slow 日志：那行的阈值是 500ms，而钩子的预算是 300ms，
            // 中间那一段只有这一格抓得住（见 _callbackMax）。
            uint ms = (uint)(Environment.TickCount64 - t0);
            MaxInto(ref _callbackMax, ms);
            MaxInto(ref _callbackWindowMax, ms);
            // 回调已经越过了系统预算：Windows 多半已经把钩子静默摘掉了。给泵线程投一条
            // 自救消息，让它在回调**外面**重装；这里绝不能再做任何慢事。
            if (outermost && HookWatch.ShouldSelfRecover((int)ms, _recoveryThresholdMs, depth: 1,
                    recoveryPending: Volatile.Read(ref _recoverPosted) != 0,
                    disposePosted: Volatile.Read(ref _disposePosted) != 0))
                RequestSelfRecovery();
            _cbDepth--;
            // 归因尾巴只在最外层给：内层与外层是同一份活的两次记账，报两遍会误导。
            if (outermost)
            {
                if (_chainMs > _chainMax) Volatile.Write(ref _chainMax, (uint)_chainMs);
            }
            Core.UiStallWatch.End("hook-callback", t0,
                outermost ? Attribution(ms, _chainMs) : null);
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
    //
    // ── 限深：已经在重入里就不再注一层 ──
    //
    // **为什么这道闸是必需的（本机日志，不是推测）**：注入出去的那条中键按下回到本回调时，
    // 在 `CallbackCore` 里被 `DwExtraInfo == Win32.InjectTag` 认出后**原样放行**——而「放行」
    // 就是一次 `CallNextHookEx`，也就是它要把整条下游链**同步走完**才返回。于是链不堵时它几微秒，
    // 链堵时它就是一次完整的阻塞，而这次阻塞**发生在回调内部**，于是下一个注入又能再套一层。
    //
    // 2026-10-14 06:42–06:49 那 41 条 `hook-callback slow` 正是这个形状：同一秒里若干条
    // **无归因尾巴**的递增时长（735 → 1078 → 1422 → 1781…），最后一条才是带
    // `chain=…ms ours=0ms` 的最外层。无归因 = `_cbDepth > 1`（见 Callback 的 finally），
    // 递增 = 真的层层嵌套，最深一簇套了 **8 层**，最外层 4047ms。
    // 那一簇 `ours=0ms` 说明时间全在链上、不是我们的计算——**所以不是新病，是老堵塞的放大版**：
    // 一次链堵被我们自己的注入乘上了层数。而 `LowLevelHooksTimeout` 本机只有 300ms。
    //
    // 为什么不改到别的线程上去：上面那条「必须抢在这一条移动放行之前」的理由仍然成立，
    // 排队就落到移动后面了，中键自动滚动整个功能消失。这里能退的只有**层数**。
    //
    // 为什么取向是「跳过」而不是「照注」：注入是为了让下游看到中键按下，而重入中的这一次
    // 注入，下游**已经**在祖先那一帧里收到过一次按下了（那才是它生效的那一次）。再注一层
    // 不增加下游看到按下的机会，只把回调的阻塞乘二。跳过的那一次中键自动滚动这一次不响应，
    // 与 `MayEngage` 答 false 就整笔放行是同一个决定的两半：宁可这一次没手势，也不要把按键吞了。
    private void RescueDragDown()
    {
        // 只在**最外层**注：`_cbDepth > 1` 说明这次移动本身就是某个注入的产物，
        // 而那个祖先已经替我们注入过了。闸放在注入之前，不放在回调入口——
        // 入口那道闸会把祖先那次也一起挡掉，功能直接失效。
        if (_cbDepth > 1)
        {
            Interlocked.Increment(ref _rescueSkipped);
            return;
        }
        // 这一笔按下**故意不进账本**（与拆卸时还键那一档不同，理由是时机不是键）：
        // 拖拽正在用户手里跑着，它的抬起会原样流过这条链并把按下配平。要是这里也记一笔欠账，
        // 看门狗下一次醒（≤500ms）就会在他还按着的时候补一个中键抬起——孤立的中键抬起在别的
        // 程序里是惰性的，在这里不是：它把中键自动滚动当场掐断。
        // 拆卸那一档反过来：钩子正在离开，没人再为它配对，所以那边必须记。
        SendSync(() => Win32.SendMiddleDownTagged());
    }

    private uint SendSync(Func<uint> send)
    {
        if (RefusedInTestHost()) return 0;
        long t = Core.UiStallWatch.Begin();
        try
        {
            uint sent = send();
            if (sent == 0) Interlocked.Increment(ref _injectRejected);
            return sent;
        }
        finally
        {
            RecordReplayMs(Environment.TickCount64 - t);
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
            // 真的抬起到了：让路时交还给程序的那个按下由它配对，看门狗不必再收尾
            //（再补一个抬起会打断用户随后真按下去的那一下）。
            if (msg == WM_RBUTTONUP) _upLedger.ClearHandedDown();

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
                // 上一次交还给程序的那次按下，它的抬起没等到（抬手那一刻弹了 UAC 安全桌面，
                // 低级钩子收不到安全桌面上的输入）：系统那边那个右键还按着。
                // **认领（补一个抬起）放在「这一笔按下真的被我们吞了」那一档**（见下面的
                // PressVerdict.Swallow）：放行的那一笔，它自己的抬起会原样进系统，
                // 那个抬起已经把卡住的键松开了——这时再补一个抬起就是没有配对按下的抬起，
                // DefWindowProc 会凭它合成一个莫名的右键菜单。
                if (_gesture == null) return Chain(code, wParam, lParam);
                // 让路中：右键一个字节都不碰——**但「不碰」不等于「放行」**。
                // 若让路开始前那一笔手势已经在飞（按下已被吞掉、没进系统），
                // 这条真抬起原样放行就是一条**没有配对按下**的抬起：
                // DefWindowProc 会凭它合成一个莫名的右键菜单（用户报的「凭空多出一次右键」）。
                //
                // 所以让路中的抬起分两种收法：
                //   · 闸门还开着 = 那次按下被我们吞了、没进系统 → 放行它就是一条野抬起。
                //     吞掉它，按一次普通右键补回去（与「手势没命中」同一条路）：
                //     用户拿到的是一次像样的右键，而不是一个凭空的菜单。
                //   · 闸门已经关了 = 按下要么原样放行过、要么被 ApplyYield 还回去了
                //     → 系统那边有配对的按下，原样放行，配成一次普通右键。
                //
                // 闸门这一关（Reset）同时是 ApplyYield 的判据：它据此知道真抬起还没到，
                // 那一次按下才可以还回去（见 ApplyYield）。
                if (Volatile.Read(ref _yielding) != 0)
                {
                    if (msg == WM_RBUTTONUP)
                    {
                        // **两种「按下被吞了」都要认**：闸门还开着（手势在飞），
                        // 或者闸门已经被那条超时兜底关掉了、但欠着一笔按下（_owedRightClick）。
                        // 后者这一档原先漏了：那条真抬起原样放行 = 没有配对按下的抬起
                        // → DefWindowProc 凭它合成一个莫名的右键菜单。
                        bool swallowedDown = _gesture.Pending || _owedRightClick;
                        _gesture.Reset();
                        try { _post(_trailEnd); } catch { }
                        if (swallowedDown)
                        {
                            // 按下被吞了 → 这一条抬起不能原样放行（野抬起）。
                            // 补一次完整的点击：走补发线程，不在回调里注入。
                            _owedRightClick = false;
                            if (!ReplayClick(ReplayKind.RightClick)) return Chain(code, wParam, lParam);
                            return 1;
                        }
                    }
                    return Chain(code, wParam, lParam);
                }
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
                //
                // **前台 pid 这一句也包一圈计时**：它是回调里剩下的两处跨进程窗口查询之一
                // （GetForegroundWindow + GetWindowThreadProcessId），而这一处此前**没有名字**——
                // 用户 2026-10-20 的 clockwork.error.log 里有一行
                // `ui thread slow: hook-callback took 1093ms (thread=bg) chain=0ms ours=1093ms`：
                // 链排除了（不是别人堵的），ours 那一秒多却说不出是哪一句。
                // 与旁边两格（hook-proc-name / hook-window-at）同一口径：让它下次自己报名。
                // **只在按下这一侧问**（原样）：抬起时不问前台是谁——那一句在回调路径上没有价值。
                if (msg == WM_RBUTTONDOWN)
                {
                    long pfg = Core.UiStallWatch.Begin();
                    uint fgPid;
                    try { fgPid = Win32.ForegroundPid(); }
                    finally { Core.UiStallWatch.End("hook-foreground-pid", pfg); }
                    if (!_mayEngage(fgPid))
                    {
                        _owedRightClick = false;
                        return Chain(code, wParam, lParam);
                    }
                }
                // **这个程序在「不接管手势」名单里 → 整笔放行，一个字节都不碰。**
                //
                // 名单是用户自己列的（远程桌面、虚拟机、游戏、3D 视口……那些地方程序自己就管右键）。
                // 判据是纯函数（GestureGate.IsIgnoredApp），这里只负责把进程名问出来。
                //
                // 问的是**前台窗口**的进程名，与分应用手势绑定（ForProcess）同一口径：
                // 「我现在在哪个程序里」这一个问题，全应用只问一次、只答一次。
                //
                // 读缓存（探针后台算好的），不做跨进程查询——那一句在回调里能到秒级（见类头）。
                // **认不出名字时按「不在名单里」处理**：反过来会让手势在每一个刚切过去的窗口里
                // 都失效，而名字解析只差几十毫秒（见 GestureGate.IsIgnoredApp 的注释）。
                string? downProc = null;
                if (msg == WM_RBUTTONDOWN)
                {
                    // 同一句前台 pid 查询，这里是第二处调用点——也包一圈计时（同一个名字）。
                    long pfg2 = Core.UiStallWatch.Begin();
                    try { _gesturePid = Win32.ForegroundPid(); }
                    finally { Core.UiStallWatch.End("hook-foreground-pid", pfg2); }
                    // 进程名这一句要 OpenProcess，而它在回调路径上实测能到秒级（见 HookProbe 类头）。
                    // 读的是探针的缓存（冷缓存时排一次后台解析，这一格第一次真可能慢），
                    // 所以它仍带着自己的计时格：哪一次真的慢了，日志里得有个名字。
                    long p1 = Core.UiStallWatch.Begin();
                    try { downProc = _procNameForPid(_gesturePid); }
                    finally { Core.UiStallWatch.End("hook-proc-name", p1); }
                    if (downProc.Length > 0 && _ignoredApp(downProc))
                    {
                        // 作废旧欠账：这一笔根本没接管，没有按下被吞，也就没有要还的账。
                        _owedRightClick = false;
                        // 收掉可能还挂在屏上的上一笔笔迹：这一笔不画了，但上一笔的线不能留在屏幕上。
                        try { _post(_trailEnd); } catch { }
                        return Chain(code, wParam, lParam);
                    }
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
                if (msg == WM_RBUTTONUP && string.IsNullOrEmpty(_gesture.ContextProcess))
                {
                    // **这一句也要计时**：它与上面那句是同一个调用（探针缓存，冷缓存时第一次真可能慢），
                    // 但只有按下那一侧包了计时——抬起这一侧没包，于是它在日志里是个没有名字的
                    // 「ours=NNNNms」。抬起这一侧恰恰是每次右键抬手都走的路。
                    long p2 = Core.UiStallWatch.Begin();
                    try { _gesture.ContextProcess = _procNameForPid(_gesturePid); }
                    finally { Core.UiStallWatch.End("hook-proc-name", p2); }
                }
                // A1 的兜底：抬起时进程名仍没回来，就别接管这一笔。
                // 手势按程序绑定，名字空着时匹配只能走全局那一档；在拥堵里让一个未知程序
                // 触发全局手势，比「这一次手势没生效」更坏。按下已经吞了，按普通点击补发；
                // 补发投不进去就放行真抬起（DefWindowProc 仍能合成菜单）。
                //
                // 代际守卫就在 _procNameForPid 的实现里：HookProbe 按 pid 存名字，晚到的结果
                // 只会落到它自己的 pid 上，不可能覆盖下一笔；这里只判断「本笔抬起时有没有」。
                if (msg == WM_RBUTTONUP && _gesture.Pending
                    && (string.IsNullOrEmpty(_gesture.ContextProcess) || _ignoredApp(_gesture.ContextProcess)))
                {
                    // 两种晚到都要退回普通右键：名字还没回来（未知程序不能跑全局手势），
                    // 或名字回来了却发现它在「不接管」名单里（按下那一刻缓存还是空的，
                    // 名单没来得及生效）。按下已经吞了，按普通点击补发；补发投不进去就放行真抬起。
                    _gesture.Reset();
                    _owedRightClick = false;
                    try { _post(_trailEnd); } catch { }
                    if (!ReplayClick(ReplayKind.RightClick)) return Chain(code, wParam, lParam);
                    return 1;
                }
                var gv = msg == WM_RBUTTONDOWN ? _gesture.OnRightDown(data.X, data.Y) : _gesture.OnRightUp();
                if (msg == WM_RBUTTONDOWN)
                {
                    // 进程名在上面问过一次了（downProc），这里直接用——**不再问一遍**。
                    // 问的那一句要 OpenProcess，在这条回调路径上实测能到秒级（见 HookProbe 类头）；
                    // 而 OnRightDown 刚把 ContextProcess 清空（它描述的是新的一笔），
                    // 所以这里必须补上，但值是同一个，不必重新去探针里取。
                    _gesture.ContextProcess = downProc;
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
                    if (!ReplayClick(ReplayKind.RightClick)) return Chain(code, wParam, lParam);
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
                if (msg == WM_RBUTTONDOWN) { try { _post(_trailEnd); } catch { } ScheduleTrail(data.X, data.Y, false, null); }
                else { try { _post(_trailEnd); } catch { } }
                switch (gv)
                {
                    case PressVerdict.Swallow:
                        // 只有「按下要吞掉」这一档还会走到这里。画完没匹配上不再吞——
                        // 它改成 ReplayClick 把右键还给用户（判据见 GestureGate.OnRightUp），
                        // 那句「你画的是 R，它没绑动作」的提示跟着搬到那里去报。
                        //
                        // **这一笔按下被我们吞了** → 它的抬起也不会进系统。
                        // 但系统那边**并不欠**这一笔的抬起：按下压根没进过系统，
                        // 所以没有配对的抬起要补（补一个就是野抬起，凭它合成一个莫名的右键菜单）。
                        //
                        // 系统那边真正欠着的抬起只有**另一个**来源：上一次让路时
                        // **交还给程序**的那次按下——那一次按下是真的进了系统，
                        // 而它的抬起被安全桌面吃掉了（抬手那一刻弹了 UAC）。
                        // 那一笔由这里认领（见 ClaimLostHandedDown）：
                        // 用户要再按一次右键才认领，说明上一次的手已经离开了。
                        if (msg == WM_RBUTTONDOWN) ClaimLostHandedDown();
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
                        if (!ReplayClick(ReplayKind.RightClick)) break;
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
                // A1 的“下一次节流点修正”：名字由 HookProbe 在后台解析，这里只读缓存。
                // 解析完成前 LiveMatch 按无程序限制判；一旦名字到了，后面的采样点就按真正的
                // 程序匹配，笔迹高亮不会一直等到抬起来。pid 是本笔按下时锁定的，晚到的
                // 别的 pid 结果不可能进来（HookProbe 按 pid 存，见它的类头）。
                if (_gesture.Pending && string.IsNullOrEmpty(_gesture.ContextProcess) && _gesturePid != 0)
                {
                    var resolved = _cachedProcNameForPid(_gesturePid);
                    if (resolved.Length > 0) _gesture.ContextProcess = resolved;
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
                    bool armed = _gesture.LiveMatch();
                    // 命中的是**哪一条**也一起捎过去：笔迹旁那句「这将是哪件事」要靠它。
                    // 捎的是**步骤**而不是文案——文案要查 i18n 资源，那件事留给 UI 线程做
                    // （见构造器 trailPoint 的注释）。
                    ScheduleTrail(data.X, data.Y, armed, armed ? _gesture.LiveStep : null);
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
                    if (!ReplayClick(ReplayKind.MiddleClick)) break;
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
