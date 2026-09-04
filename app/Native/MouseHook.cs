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
//    装在后台线程上会「装得上、永不回调」。故 Install 只允许从 UI 线程调。
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

    private readonly LongPressGate? _gate;
    private readonly GestureGate? _gesture;
    private readonly Action<string> _unmatched;
    private long _beat;        // 回调最后一次被叫到的时刻（Environment.TickCount64）
    // 右键的按下与抬起**分开记**。这不是记账癖：只收到抬起、从没收到按下，是「有人排在我们
    // 前面把按下吞了」的确凿指纹——手势类工具正是这么干的（吞掉按下去判断是不是手势，
    // 抬起时再放行或补发）。合成一格的话，这个唯一能自证的信号就没了。
    private long _beatRDown, _beatRUp;
    // 一笔手势按住不放的上限。到点还没收到抬起，就当那条抬起丢了（安全桌面）并收笔。
    // 取 5 秒是「宽到不会误伤真手势、又短到不至于让笔迹挂很久」：真手势一两秒到头，
    // 而没有这道兜底的话，丢失的抬起要挂到下一次右键才收得掉。
    private const long GestureStaleMs = 5000;
    private readonly Action _fire;
    private readonly Action<string> _fireGesture;
    private readonly Action<int, int, bool> _trailPoint;   // 第三个参数：画到此刻已经命中了吗（笔迹据此变色）
    private readonly Action _trailEnd;
    private readonly Action<Action> _post;

    // 委托必须由本对象持有：SetWindowsHookEx 只存了个裸函数指针，GC 不知道它被引用着。
    // 只传一个 lambda 进去、不留字段的话，下一次 GC 之后回调地址就是野指针——进程直接崩，
    // 而且崩在任意一次鼠标移动上，看不出和这里有关。
    private readonly HookProc _proc;
    private IntPtr _hook;

    // 到点的闹钟：按满时长就弹面板，不等抬起。按下时上弦，抬起 / 判成拖拽 / 卸载时收掉。
    // 回调在线程池线程上来，但它只做一件事——把询问 post 回 UI 线程，于是 gate 的所有读写
    // 仍然只发生在 UI 线程上，不必加锁。
    private readonly System.Threading.Timer _alarm;
    private readonly int _holdMs;

    private readonly System.Threading.Timer _clickUp;
    // 补发那次点击的按下与抬起之间隔多久。
    //
    // **不能是 0，而同一批 SendInput 里的 down+up 就是 0**：两条事件的时间戳完全相同，
    // 于是补发出去的是一次「零长度」的点击。不少程序据此把它当噪声丢掉（自己做按下-抬起
    // 配对判定的、以及 Chromium 系那种异步处理输入的），表现就是**快速点一下右键没有菜单**，
    // 而按住久一点反而正常——因为那条路走的是「把按下还给系统」，时序是真实的。
    // 30ms 比人手最快的一次点击还短，却足够让下游把它当成两件事。
    private const int ClickGapMs = 30;

    // 补发的按下已经发出、抬起还欠着——欠的是哪个键：0=没有，1=右键，2=中键。
    // 卸载时要补上，否则那个键会在全系统卡在按下态，是这个类最不能出的一种事故。
    private int _clickUpDue;
    // 注入被系统拒掉的次数（UIPI：前台是提权窗口时，非提权进程的 SendInput 会被静默丢弃）。
    //
    // 这一格是**诊断**，不是状态。被拒时本类没有可做的补救：那次真按下早在几十毫秒前就被吞掉了，
    // 此刻已经没有「放行真事件」这条路可选（ReplayClick 能 return false 让调用方放行，
    // 是因为它就跑在回调里）。
    // 也**不能**照 ReplayClick 记一笔 _clickUpDue：那笔账只由 _clickUp 闹钟或 Dispose 结清，
    // 而这里用户的手还按着、真正的抬起随后会自己来——记了账就会在卸载时多补一次抬起，
    // 正是上面那条注释说的「打断用户随后真按下去的那一下」。
    // 能做的只有让它可查：不记的话，「右键拖拽在某些窗口上就是没反应」永远只能靠猜。
    private int _injectRejected;

    /// <param name="holdMs">按住多久算长按；&lt;=0 表示不管中键（只做手势）。</param>
    /// <param name="fire">判定为长按时要做的事（唤出面板）。会经 <paramref name="post"/> 派发，不在回调里直接跑。</param>
    /// <param name="post">把动作投到 UI 队列的方式（App 传 Dispatcher.BeginInvoke）。</param>
    /// <param name="gesture">右键手势的判定器；null 表示不监听右键——没配手势时右键必须一根毫毛都不动。</param>
    /// <param name="fireGesture">手势命中时要做的事，参数是方向串。同 fire，经 post 派发。</param>
    /// <param name="trailPoint">屏幕上那条笔迹又画到了哪（物理像素），以及**画到此刻是否已经命中**。
    /// 同样经 post——低级钩子回调里有 LowLevelHooksTimeout 的预算，绝不在这里碰 UI。
    /// 命中与否在回调线程上算完（GestureGate.LiveMatch，纯计算、微秒级）再把结论捎过去：
    /// 把 gate 的内部状态交给 UI 线程去读，会撞上「钩子线程正往里加采样点」的竞态。</param>
    /// <param name="trailEnd">笔迹收笔（抬起、或钩子卸载）。</param>
    /// <param name="unmatched">画出来了却没绑任何东西时报一声，参数是画出来的方向串。</param>
    public MouseHook(int holdMs, Action fire, Action<Action> post,
                     GestureGate? gesture = null, Action<string>? fireGesture = null,
                     Action<int, int, bool>? trailPoint = null, Action? trailEnd = null,
                     Action<string>? unmatched = null)
    {
        _trailPoint = trailPoint ?? ((_, _, _) => { });
        _trailEnd = trailEnd ?? (() => { });
        _gate = holdMs > 0 ? new LongPressGate(holdMs) : null;
        _gesture = gesture;
        _unmatched = unmatched ?? (_ => { });
        _holdMs = holdMs;
        _fire = fire;
        _fireGesture = fireGesture ?? (_ => { });
        _post = post;
        _proc = Callback;
        // 建成停着的（Infinite）：按下时才上弦。
        //
        // **两个回调都要把 _post 兜住。** _post 是 Dispatcher.BeginInvoke，调度器正在关闭时它会抛，
        // 而这里是线程池线程——逃出去的异常没人接，直接终结进程，且 clockwork.error.log 里一个字都没有。
        // Timer.Dispose 挡不住这一下：它不取消**已经派发出去**的那次回调，本类的 Set() 里那句
        // `catch (ObjectDisposedException)` 承认的正是同一个竞态。
        // 触发窗口很窄但很日常：中键按住（上了 _alarm 的弦）或右键按下（上了 _hold 的弦）的那一瞬
        // 从托盘退出。第三个 timer（_clickUp）不需要这层——它压根不走 _post，见下面那行注释。
        _alarm = new System.Threading.Timer(_ => { try { _post(FireIfDue); } catch { } }, null,
                                            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        // 这一个不必回 UI 线程：只有一句 SendInput，微秒级，且不碰 gate 也不碰 UI。
        _clickUp = new System.Threading.Timer(_ => ReplayUp(), null,
                                              System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    // 补上补发点击欠着的那次抬起。发过就不再发（Exchange 保证只发一次）：
    // 闹钟、Dispose、以及下一次补发前的清账都会叫它，多发一次抬起会打断用户随后真按下去的那一下。
    private void ReplayUp()
    {
        switch (Interlocked.Exchange(ref _clickUpDue, 0))
        {
            case 1: Win32.SendRightUpTagged(); break;
            case 2: Win32.SendMiddleUpTagged(); break;
        }
    }

    // 补发一次带真实间隔的点击：按下现在发，抬起交给闹钟。返回 false = 注入被拒（调用方该放行真事件）。
    // 先清上一笔欠账：两个键在 30ms 内各点一下的话，后来者会顶掉前者的账，那个键就卡在按下态了。
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
    // 让 ReplayClick 去发而不是自己 SendInput：顺序靠它那套（先发按下、吞掉当前抬起、
    // 由 _clickUp 定时器隔 ClickGapMs 补抬起），否则注入的按下会排到真抬起后面去。
    private bool _owedRightClick;

    private bool ReplayClick(int which)
    {
        ReplayUp();
        if ((which == 1 ? Win32.SendRightDownTagged() : Win32.SendMiddleDownTagged()) == 0) return false;
        Volatile.Write(ref _clickUpDue, which);
        Set(_clickUp, ClickGapMs);
        return true;
    }

    // 闹钟响了：问 gate 是不是真该弹（中途抬起 / 拖走的话它会说不该）。只在 UI 线程上跑。
    private void FireIfDue()
    {
        if (_gate != null && _gate.PollFire(Environment.TickCount64)) _fire();
    }

    // 上弦 / 收弦。收弦不必等待回调结束——最坏是一次已经在路上的询问照常发生，
    // 而 PollFire 本身会因为 gate 已不在 pending 而什么都不做。
    private static void Set(System.Threading.Timer t, int dueMs)
    {
        try { t.Change(dueMs, System.Threading.Timeout.Infinite); }
        catch (ObjectDisposedException) { }   // 正在卸载：闹钟已释放，不必再管
    }

    private void Arm(bool on) => Set(_alarm, on ? _holdMs : System.Threading.Timeout.Infinite);

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

    /// <summary>右键按下被上游吞了：抬起收得到，按下一次都没有。</summary>
    //
    // 只有「有人排在我们前面拦截」会造成这个组合——鼠标本身不可能只发抬起不发按下。
    // 实测（--hookprobe 注入 5 次按下+5 次抬起）：按下 0、抬起 5。
    // 这是这个功能唯一能自证的失败信号，值得单独有个名字。
    public bool RightDownSwallowed => EverRightUp && !EverRightDown;

    public long SinceBeatMs => Volatile.Read(ref _beat) == 0
        ? long.MaxValue
        : Environment.TickCount64 - Volatile.Read(ref _beat);

    /// <summary>装钩子。返回是否成功——失败时调用方该如实告知用户，而不是让功能静默不存在。
    /// 必须从 UI 线程调（见类头注释第 1 条）。</summary>
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
        if (_hook != IntPtr.Zero) return true;
        // 模块句柄传 0：WH_MOUSE_LL 是全局低级钩子，回调跑在本进程内，不需要注入 DLL，
        // 因此也不需要真实的模块句柄（这一点与需要注入的 WH_MOUSE 不同）。
        try
        {
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
            LastError = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        }
        catch { _hook = IntPtr.Zero; LastError = -1; }
        return _hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        // 卸载前收弦并清掉扣着的那次按下：留着它没有意义，而且万一钩子又被装回来，
        // 那个陈旧的按下时刻会让下一次抬起被算成一次超长的长按。
        Arm(false);
        try { _alarm.Dispose(); } catch { }
        ReplayUp();   // 欠着的抬起先补上，否则右键卡在按下态
        try { _clickUp.Dispose(); } catch { }
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
        _gate?.Reset();
        _gesture?.Reset();
        _owedRightClick = false;   // 这笔账随钩子一起作废：上面已经无条件还过了
        // 钩子卸载时手可能还按着：那条线不收笔就会一直挂在屏幕上。
        // **必须兜住**：它排在摘钩子**之前**，而 _post 在调度器关闭时会抛（同上面两个 timer 回调）。
        // 抛出去就跳过了下面的 UnhookWindowsHookEx、_hook 也不会清零，而调用方紧接着把
        // _mouseHook 置空（App.xaml.cs 三处）——本对象从此可回收，而它的 _proc 字段是
        // Windows 那个裸函数指针**唯一的**托管根。类头写着这之后会发生什么：
        // 「下一次 GC 之后回调地址就是野指针——进程会死，而且死在一次随机的鼠标移动上」。
        // 本方法里其它每一个原生 / timer 调用都是各自单独包着的，只有这一句漏了。
        try { _post(_trailEnd); } catch { }
        if (_hook == IntPtr.Zero) return;
        try { UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
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
            if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
            int msg = wParam.ToInt32();
            if (msg is not (WM_MOUSEMOVE or WM_MBUTTONDOWN or WM_MBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP))
                return CallNextHookEx(_hook, code, wParam, lParam);

            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // 自己补发的那些：原样放行（见类头注释第 3 条）。
            if (data.DwExtraInfo == Win32.InjectTag) return CallNextHookEx(_hook, code, wParam, lParam);

            // —— 右键：手势 ——（gate 词汇与中键共用，但这边永远用不到 ReplayDownThenPass）
            if (msg is WM_RBUTTONDOWN or WM_RBUTTONUP)
            {
                if (msg == WM_RBUTTONDOWN) Volatile.Write(ref _beatRDown, Environment.TickCount64);
                else Volatile.Write(ref _beatRUp, Environment.TickCount64);
                if (_gesture == null) return CallNextHookEx(_hook, code, wParam, lParam);
                var gv = msg == WM_RBUTTONDOWN ? _gesture.OnRightDown(data.X, data.Y) : _gesture.OnRightUp();
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
                    if (!ReplayClick(1)) return CallNextHookEx(_hook, code, wParam, lParam);
                    return 1;
                }
                if (msg == WM_RBUTTONDOWN) _owedRightClick = false;   // 新的一按作废旧账
                // 抬起：先收笔，再让下面的 switch 去跑动作——收笔排在 Fire 前面，
                //   免得动作已经开跑了，屏幕上那条线还挂着。
                // 这两句要包 try：它们是本类里仅剩的无守卫 `_post`。gate 已经把裁决记下了
                // （上面 gv 那一句），而这里一抛就会被最外层的 catch 接走、走到 CallNextHookEx——
                // 于是一条已经记成 Swallow 的事件实际上 Pass 了下去，gate 的状态与真实发生的事分家。
                // 笔迹画不画得出来是小事，裁决对不上号是大事。（定时器里那两处已经是这么写的。）
                if (msg == WM_RBUTTONDOWN) { int dx = data.X, dy = data.Y; try { _post(() => { _trailEnd(); _trailPoint(dx, dy, false); }); } catch { } }
                else { try { _post(_trailEnd); } catch { } }
                switch (gv)
                {
                    case PressVerdict.Swallow:
                        // 画出来了却没绑任何东西：照旧吞掉（补发点击会在轨迹终点弹一个莫名的菜单），
                        // 但要让上层说一句——沉默地吞掉是这个功能最难自证的一种状态。
                        //
                        // **只在抬起那一档报。** Swallow 这个裁决有两个来源：按下要吞掉，
                        // 以及画完了没匹配上。两者共用一个分支，于是「按下」也会去读 Path 报一次——
                        // 右键点一下就弹出上一笔的形状（Path 那头已经改成按下即清，这里再堵一道：
                        // 按下那一刻本来就还没有「画出来的东西」可报，让它够不着才是对的）。
                        var drawn = msg == WM_RBUTTONUP ? _gesture.Path : "";
                        if (drawn.Length > 0) _post(() => _unmatched(drawn));
                        return 1;
                    case PressVerdict.ReplayClick:
                        // 补发按下，抬起隔 ClickGapMs 再发（见那个常量：down+up 同一批发出去是一次
                        // 零长度的点击，会被不少程序当噪声丢掉——「快速点右键没菜单」就是它）。
                        //
                        // 补发被拒（前台是提权进程时 UIPI 拦 SendInput、或正在安全桌面）就放行这次真抬起：
                        // 按下已经吞了救不回来，但 DefWindowProc 光凭 WM_RBUTTONUP 就会发 WM_CONTEXTMENU，
                        // 多数程序的菜单靠这一下能出来。原先不看返回值直接 return 1，那一下右键在提权窗口里就彻底没了。
                        if (!ReplayClick(1)) break;
                        return 1;
                    case PressVerdict.Fire:
                        var path = _gesture.Path;
                        _post(() => _fireGesture(path));   // 绝不在回调里跑动作（见类头注释第 2 条）
                        return 1;
                }
                return CallNextHookEx(_hook, code, wParam, lParam);
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
                    return CallNextHookEx(_hook, code, wParam, lParam);
                }
                int before = _gesture.PointCount;
                _gesture.OnMove(data.X, data.Y);
                // 只有真记下了新采样点才画。不比这一下的话，每个 WM_MOUSEMOVE 都要 post 一次——
                // 划一道手势能有上千个移动消息，UI 队列会被这些重绘塞满。
                if (_gesture.PointCount != before)
                {
                    // 命中与否在**这条线程**上算完再捎过去：LiveMatch 是纯计算（微秒级），
                    // 而把 gate 的内部状态丢给 UI 线程去读会撞上「钩子线程正在往里加点」的竞态。
                    int mx = data.X, my = data.Y;
                    bool armed = _gesture.LiveMatch();
                    _post(() => _trailPoint(mx, my, armed));
                }
            }
            if (_gate == null) return CallNextHookEx(_hook, code, wParam, lParam);

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
                    // 保留分支：现在弹面板走的是闹钟（PollFire），这里不会再命中。
                    // 留着是因为 PressVerdict 是公开枚举，漏一个分支等于默默放行。
                    _post(_fire);   // 绝不在回调里开窗口（见类头注释第 2 条）
                    return 1;
                case PressVerdict.ReplayDownThenPass:
                    // 返回值不能丢：补发被 UIPI 拒掉时（前台是提权进程），下游会看到一串移动
                    // 加一个无配对的中键抬起——浏览器的中键自动滚动彻底失效，而那正是这一分支
                    // 存在的理由（见类头四条出口那段）。救不回来，但必须让它**可见**：
                    // 记进同一份账，诊断行的 injectRejected= 会把它报出去。
                    if (Win32.SendMiddleDownTagged() == 0) Interlocked.Increment(ref _injectRejected);
                    break;      // 补发按下后放行本次移动，拖拽继续
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
        catch
        {
            try { return CallNextHookEx(_hook, code, wParam, lParam); } catch { return IntPtr.Zero; }
        }
    }
}
