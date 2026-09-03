namespace Clockwork.Core;

// 右键手势的判定。纯状态机，不碰 Win32，坐标由调用方喂进来——与 LongPressGate 同一套纪律。
//
// 八个方向，存成单字符：轴向沿用 L/U/R/D，斜角借小键盘的空间布局 7↖ 9↗ 1↙ 3↘
//（小键盘上 7 就在左上、3 就在右下，手编 json 时不用查表）。显示时由 Arrows() 换成箭头。
//
// **方向要按「腿」测，不能按滑动窗口测**。这是这个量化器唯一的难点，也是它长这样的全部理由：
// 若拿「上一个锚点 → 当前点」的向量来定方向，拐角处那个向量横跨两条腿，本身就是斜的——
// 画 →↓，锚点停在拐角前 20px、再往下走 20px，向量 (20,20) 恰好 45°，于是凭空多出一个 ↘，
// 两段的手势读成三个方向。这个缺陷四向时同样存在，只是四向没有斜角格子，那个混合向量只能
// 落到 R 或 D 上（都是相邻的合法方向），去重时自己消失了——八向把它暴露出来而已。
//
// 所以：按 StepPx 细采样存点，抬起时再一次性分析——按方向切成若干「腿」，
// 不够格的腿并入邻居，剩下的每条腿用自己的首尾两点重定方向。
//
// **「细采样让拐角只占一步」是错的**，这句曾经写在这儿，而它正是这个量化器最久的一个 bug：
// 人画不出直角。→↓ 的拐角只要圆到半径 60px，笔尖就在 ↘ 那格里实打实走了 47px 净位移——
// 绝对门槛拦不住它，RD 稳定读成 R3D，画得再标准也不匹配。同理，一条画歪了的「直线」
// 两端切线偏得最多，一条弓 12% 的 ↗ 会甩出 → 和 ↑，只画了一个方向却得到三个。
// 补法是两条（都在 Analyze 里，各自有注释）：拐弯扫出的那格按**扇区相邻 + 短一半**并掉；
// 并的时候**只并长度、不并区间**——噪声的采样点绝不能参与定方向。
//
//   实测天花板（app.Tests 里钉着）：轴向容忍到弓 16%、任意圆角；斜角到弓 12%。
//   再往上修不动了，因为一条弓 16% 的 ↗ 与「→ 接一段 ↘」画出来就是同一条折线。
//
//   轴向偏置——八份不平分。轴向扇区各 56°、斜角各 34°（合计仍是 360°）。
//     轴向是默认意图、斜角是刻意为之，所以把宽容度给轴向：横线歪到 28° 仍读作「→」，
//     而认真画的 45° 斜线离两侧边界都还有 17°。没有它，加斜角等于把轴向的容错砍掉一半。
//
// 「短腿并入邻居」同时替掉了早先那个迟滞：画在扇区边界上的直线会切出一串交替的短腿，
// 它们逐个被吸收进最长的那条，最终仍是一个方向——不必再单设一条迟滞规则。
//
// 判定协议与 LongPressGate 同构——先吞按下、抬起时回头定性，四条出口：
//   没动就抬起          → ReplayClick 补发真右键。上下文菜单本来就出在 button-up，用户无感。
//   按住不动到点        → ReleaseIfStill：把按下还给系统，此后原样放行。按住的点击不必等松手才有反应，
//                         右键拖拽也由此可用（先停一下再拖）。
//   画出的串匹配某个组  → Fire。
//   画了但没匹配        → Swallow，什么都不发生。补发点击会在轨迹终点弹一个莫名的菜单，更糟。
//   （移动本身一律 Pass：光标位移吞不掉也不必吞，下游没收到按下，移动对它无意义。）
/// <summary>右键监听此刻的状态。给界面显示用。</summary>
public enum GestureWatchState
{
    /// <summary>没在监听：总开关关着，或一条启用的手势都没有。右键完全照常。</summary>
    Off,
    /// <summary>在监听：钩子装上了，右键会被判成手势。</summary>
    Live,
    /// <summary>装不上：Windows 拒绝了（受限令牌 / 组策略 / 安全软件拦）。</summary>
    Failed,
    /// <summary>装是装上了，但已经收不到输入——Windows 把它静默卸掉了
    /// （UI 线程占满 LowLevelHooksTimeout 就会发生，且不通知任何人）。</summary>
    Stale,
    /// <summary>装上了，却**一次输入都没收到过**。与 <see cref="Stale"/> 的分界很重要：
    /// 那一档是「收到过、后来停了」（事后被摘），这一档是「从头到尾就没通」（装的那一刻就不成立）。
    /// 实测最常见的成因是本进程没提权：某些机器上有更高权限的程序排在输入链前面，
    /// 低完整性的钩子从此一个消息都拿不到——而两者在界面上都表现为「按了没反应」。</summary>
    Deaf,
    /// <summary>右键按下被排在前面的程序吞了：抬起收得到，按下一次都没有。</summary>
    //
    // 鼠标不可能只发抬起不发按下，所以这个组合只有一种成因——有人排在钩子链前面拦截。
    // 手势类工具正是这么干的（吞掉按下去判断是不是手势）。这一档单列，是因为它与其余几档
    // 的补救完全不同：这儿一切正常，要动的是那个程序的设置，不是 Clockwork。
    Blocked,
}

public sealed class GestureGate
{
    /// <summary>此刻要不要监听右键：总开关开着，且至少有一条**启用且真有笔迹**的手势。</summary>
    //
    // 抽出来是因为这句话是承重的，而它原来只活在 App.ApplyMouseHook 里——那儿碰 Win32，
    // 测不到。总开关的全部意义就是「关掉之后一根毫毛不动」，那件事没有断言盯着，
    // 就只是一个存得下 false 的字段。
    //
    // 判的是 **Normalize 之后**的串，不是原串。钩子拿规范形去比对（App 里那句
    // `s.Gesture == p`，p 是 Analyze 出来的规范路径），而 ConfigStore **故意不把洗成空的那一类
    // 写回盘**（那是「这串不合法」的判定，不是修正）。于是手改 json 里一条 "R-D"
    // 会原样存着：拿原串判就是非空 → 装钩子，而它永远比不上任何规范路径。
    // 代价不是「不生效」而已：装了钩子就要先**吞掉每一次右键按下**再补发（晚 30ms，
    // 而前台窗口提权时 SendRightDownTagged 被 UIPI 直接丢掉，MouseHook 那边记的是
    // 「右键拖拽在某些程序上完全没反应」）——换不来任何东西。
    public static bool ShouldWatch(bool masterOn, IEnumerable<LaunchStep>? gestures)
        => masterOn && gestures != null
        && gestures.Any(s => s != null && s.Enabled && !string.IsNullOrEmpty(Normalize(s.Gesture)));

    // 扇区中心角与半宽（度，屏幕坐标 y 向下：0=→ 90=↓ 180=← -90=↑）。
    // 半宽是可调的旋钮而不是写死的 45：轴向宽、斜角窄，理由见类头注释。
    private const double AxisHalf = 28, DiagHalf = 17;   // 4*56 + 4*34 = 360，恰好铺满

    // **八个方向只在这里定义一次**：扇区中心角、半宽、显示箭头、单位位移，四件事一行说完。
    // 曾经它们散在四处（扇区表、Arrows 的 switch、Normalize 的 switch、GestureGlyph 的位移表），
    // 加一个方向要改四张表，而且谁也拦不住它们对不上——比如笔迹画的方向和判定用的角度悄悄错开。
    public readonly record struct Dir(char C, double Center, double Half, char Arrow, int Dx, int Dy);

    public static readonly Dir[] Dirs =
    {
        new('R',    0, AxisHalf, '→',  1,  0),
        new('3',   45, DiagHalf, '↘',  1,  1),
        new('D',   90, AxisHalf, '↓',  0,  1),
        new('1',  135, DiagHalf, '↙', -1,  1),
        new('L',  180, AxisHalf, '←', -1,  0),
        new('7', -135, DiagHalf, '↖', -1, -1),
        new('U',  -90, AxisHalf, '↑',  0, -1),
        new('9',  -45, DiagHalf, '↗',  1, -1),
    };

    /// <summary>按规范字符查方向；不是八方向之一时返回 null。</summary>
    public static Dir? Of(char c)
    {
        char u = char.ToUpperInvariant(c);
        foreach (var d in Dirs) if (d.C == u) return d;
        return null;
    }

    private readonly Func<string, bool> _matches;
    private readonly int _minLegPx;
    private readonly int _stepPx;

    private bool _pending;
    private readonly List<(int X, int Y)> _pts = new();

    private const int MaxPath = 24;       // 一条手势最多几个方向。再长就不是手势是涂鸦；也防住状态泄漏时的无界增长

    // 采样点上限——**它决定的是一笔能画多长**，撞上之后新的点直接被丢掉：
    // 判定用的采样停在那儿，屏幕上那条线也就跟着不动了（笔迹只在真记下新点时才重画），
    // 手还在移动，线却冻住——很显眼，而且看起来像程序卡了。
    //
    // 512 是按「每 10px 一个点」算的，那是 StepPx 还写死 10 的年代；现在 StepPx 跟着
    // MinLegPx 走（四分之一），4K 上是 24px，512 个点约 12000px。
    //
    // **真正封住这个数的不是内存，是钩子的 300ms 预算。**
    // 上一版按内存算账（4096×8B = 32KB，不值一提）把它放到 4096，那笔账没错，但算漏了一项：
    // MouseHook 每记下一个采样点就调一次 LiveMatch，而 LiveMatch 会把 Analyze **整个重跑一遍**，
    // 那里面是 O(legs²) 的合并循环，legs 随点数一起长。于是后面的每一个点都越来越贵，
    // 而这笔开销全花在低级钩子那条线程上——Windows 的 LowLevelHooksTimeout 一超时就**静默**
    // 把钩子摘掉，手势和中键长按一起失灵，重启才好，任何日志里都没有一个字（见 MouseHook 类头第 2 条）。
    //
    // 实测（之字形最坏形状，每个采样点都拐一次，这台机器）：
    //   4096 点 → 单次 LiveMatch 85.3 ms      ← 已经吃掉 300ms 预算的四分之一，而这还是台快机器
    //   1024 点 → 单次 5.28 ms（512 点 1.13、256 点 0.22；每翻一倍约 4.7–5×，它确实是二次的）
    //
    // 这些数不再写成断言。GestureGatePerfTests 曾经断言 `perCallMs < 30`，
    // 而绝对毫秒界在未知硬件上站不住：并发跑多个测试套件时实测 82.4 / 70.7 ms，
    // 6 次里红 5 次，而 CI 的 release 是 `needs: test`。那边现在断言的是**采样点数**（夹子有没有在夹），
    // 不受负载影响，也不必等某台机器刚好越界。
    // 1024 × 24px ≈ 24000px，仍是 4K 屏的六个屏宽——「任何手势都撞不到」这个目标照样成立，
    // 而最坏单次开销回到有一个数量级余量的水平。
    //
    // 想再放宽的话，要先把 LiveMatch 改成增量的（别每个点都重跑 Analyze），不是再调大这个数。
    private const int MaxPoints = 1024;

    /// <summary>这块屏上一条笔画至少该多长（物理像素）。屏幕越大，手势就该画得越大。</summary>
    //
    // **固定 40px 是错的，而且错在最难受的那一头**：它比同类工具都小，于是右键点一下时
    // 手上那点位移就够格成一条腿——菜单被吞掉，还弹一句「你画的 → 没绑任何东西」。
    //
    // 取屏幕宽度的 2.5%，是抄 WGestures 的（Win32MousePathTracker2 里
    // `EffectiveMove = 屏幕宽度 * 0.025f`，方向只在每走满这么远时才记一次）：
    // 1920→48，2560→64，3840→96。moosegesture 用的是固定 60px，同一个量级。
    // 按屏幕算而不是按 DPI 算，因为要跟着的是「这一划在屏幕上占多大比例」——
    // 手势是相对屏幕比划的，4K 屏上一道 48px 的笔画短得像手抖。
    //
    // 下限仍是 40：那是从前的值，谁也不该在换屏之后发现手势比以前更容易误触。
    // 不设上限——WGestures 也不设；屏幕真有那么宽，那一划本来就该那么长。
    public static int MinLegForScreen(int screenWidthPx)
        => screenWidthPx <= 0 ? 40 : Math.Max(40, screenWidthPx * 25 / 1000);

    /// <param name="matches">画出的方向串是否命中某个已配置的手势（由调用方对着配置查）。</param>
    /// <param name="minLegPx">一条腿至少这么长才算数。短于它的（拐角混合、手抖）并入邻居。
    /// 采样间隔取它的四分之一：采样越细，拐角的混合占的比重越小。</param>
    public GestureGate(Func<string, bool> matches, int minLegPx = 40)
    {
        _matches = matches;
        _minLegPx = minLegPx < 12 ? 12 : minLegPx;
        _stepPx = Math.Max(4, _minLegPx / 4);
    }

    public bool Pending => _pending;

    /// <summary>已记下的采样点数。屏幕上那条笔迹靠它判断「刚才这一下有没有新点」——
    /// 每次移动都画的话，一条手势要往 UI 队列里塞几百次重绘，而采样本来就是每 StepPx 才记一个。</summary>
    public int PointCount => _pts.Count;

    /// <summary>**刚画完的那一笔**的方向串；没画或还没抬起时是空的。
    /// 抬起时写入，下一次按下（以及 Reset）立刻清空——它绝不能活得比它描述的那一笔久。</summary>
    //
    // 上层对 Swallow 这一档读它去报「你画的 X 没绑任何动作」，而**按下也返回 Swallow**。
    // 早先这里只写不清，于是右键点一下就会拿上一笔的残留再弹一次提示，且弹在按下那一刻。
    public string Path { get; private set; } = "";

    /// <summary>画到此刻为止，这一笔已经命中某条配好的手势了吗。</summary>
    //
    // 给「边画边认」用：笔迹在命中的那一刻变色，松手之前你就知道认没认出来。
    // 这是 Quicker 体验里最值钱的一半——它的手册写着「识别到手势后（线条变色）松开鼠标即可触发」，
    // 而我们从前是彻底沉默到抬起，画错了要松手才知道。
    //
    // **必须便宜。** 它跑在低级钩子的回调线程上（每记下一个采样点问一次），而那条路有
    // LowLevelHooksTimeout 的预算（默认 300ms），超了整个钩子会被系统静默摘掉。
    // Analyze 是 O(采样点)，一笔手势几十个点、算下来是微秒级，离那个预算差着三个数量级；
    // 真正贵的活（开窗、跑动作）照旧一律 post 给 UI 线程，这里只做纯计算、不碰任何 UI。
    //
    // 会随着继续画而变回 false：画出 ↑ 命中「复制」，继续往下画成 ↑↓ 就该灭掉、
    // 再成为「搜索」时重新亮起。命中是**此刻这一串**的属性，不是一个一旦点亮就锁住的状态。
    public bool LiveMatch()
    {
        if (!_pending || _pts.Count < 2) return false;
        var p = Analyze();
        return p.Length > 0 && _matches(p);
    }

    public PressVerdict OnRightDown(int x, int y)
    {
        // 重复按下（上一次的抬起被丢了）：按新的重新起算，与 LongPressGate 同理。
        _pending = true;
        // **Path 必须一起清掉。** 它描述的是「刚画完的那一笔」，一旦活得比那一笔久就会骗人：
        // 上层对 Swallow 这一档读它去报「你画的 X 没绑动作」，而按下本身也返回 Swallow，
        // 于是右键**点一下**都会拿上一笔的残留再弹一次提示。调阈值治不了——那串不是这次画的。
        Path = "";
        _pts.Clear();
        _pts.Add((x, y));
        return PressVerdict.Swallow;
    }

    // 只管存点，不在这里定方向——方向要等整条轨迹画完才测得准（见类头注释）。
    public PressVerdict OnMove(int x, int y)
    {
        if (!_pending) return PressVerdict.Pass;
        var (lx, ly) = _pts[^1];
        long dx = x - lx, dy = y - ly;
        // 欧氏距离而不是各轴分开比：分轴比会让斜线要多走 √2 倍才记一个点，
        // 同样的手速下斜角比轴向迟钝一截，画起来手感不一致。
        if (dx * dx + dy * dy < (long)_stepPx * _stepPx) return PressVerdict.Pass;
        if (_pts.Count < MaxPoints) _pts.Add((x, y));
        return PressVerdict.Pass;
    }

    /// <summary>按住没动：把右键还给系统。到点时还停在起点（一个采样点都没攒下）就放弃这一笔，
    /// 返回 true——调用方该补发一次真右键**按下**，此后的移动与抬起原样放行（抬起走 <see cref="OnRightUp"/> 的
    /// 「不在 pending」早退）。已经画开了、或早已抬起，返回 false，什么都不动。</summary>
    //
    // 这是「先吞按下、抬起再定性」那套协议的第四条出口。没有它，下游要等到松手才知道有过一次按下：
    // 按住 300ms 再松的普通右键，菜单和高亮全都晚 300ms 才出，用起来就是「右键变慢了」；
    // 而右键拖拽（资源管理器右键拖文件、3D 里右键旋转）在监听期间彻底没有。
    // 手势的第一下移动紧跟着按下（按与划是同一个动作），所以「按住不动」就是「这不是手势」——
    // StrokesPlus / WGestures 都拿这个当判据。代价：先停一下再画的手势会变成一次右键拖拽。
    public bool ReleaseIfStill()
    {
        if (!_pending || _pts.Count > 1) return false;
        _pending = false;
        _pts.Clear();
        return true;
    }

    public PressVerdict OnRightUp()
    {
        if (!_pending) return PressVerdict.Pass;
        _pending = false;
        string p = Analyze();
        if (p.Length == 0) return PressVerdict.ReplayClick;
        // 没匹配上也把串留下：上层要能说出「你画的是 ←↓，它没绑任何东西」。
        // 此前这一档是彻底的沉默，而沉默把三件事混成一件——功能没开、笔画画歪了、
        // 动作跑失败了，看起来都是「按了没反应」，可这三件事的补救完全不同。
        Path = p;
        return _matches(p) ? PressVerdict.Fire : PressVerdict.Swallow;
    }

    // 采样点 → 方向串：切腿、吸收短腿、剩下的就是答案。
    private string Analyze()
    {
        // 每一小步先粗定方向，同向的连成一条腿并记下它在 _pts 里的首尾（长度用来判够不够格）。
        // 这一步只用来**切腿**，方向稍后重定——逐步投票的角度噪声太大，见下。
        var legs = new List<(char D, double Len, int S, int E)>();
        for (int i = 1; i < _pts.Count; i++)
        {
            double dx = _pts[i].X - _pts[i - 1].X, dy = _pts[i].Y - _pts[i - 1].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            char d = Quantize(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            if (legs.Count > 0 && legs[^1].D == d) legs[^1] = (d, legs[^1].Len + len, legs[^1].S, i);
            else legs.Add((d, len, i - 1, i));
        }
        if (legs.Count == 0) return "";

        // 一条腿「够不够长」看的是**首尾两点之间的净位移**，不是它走过的路径长度。
        //
        // 两者对一条真正的直腿几乎相等，但对「来回」差得离谱：右划 30px 再划回来 30px，
        // 是两条各 30 的短腿（都不达标），并成一条之后路径长度 60 —— 看着合格了，
        // 可它的首尾几乎是同一个点。而下面重定方向恰恰用首尾两点，于是方向由那几像素的
        // 残余抖动决定：**画一个来回，触发一个随机方向的手势**，右键菜单也不出来。
        // 用净位移就没有这回事：来回的净位移接近 0，照旧被判成手抖，补发右键菜单。
        double Span((char D, double Len, int S, int E) l)
        {
            double dx = _pts[l.E].X - _pts[l.S].X, dy = _pts[l.E].Y - _pts[l.S].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // **绝对门槛不够，因为拐弯本身会造出一条合格的腿。**
        // 八向里 → 和 ↓ 之间夹着 ↘：拐角画得圆一点，笔尖就在 ↘ 那格里实打实走一段。
        // 半径 60px 的圆角，那段的净位移已经过 40px 了——于是 RD 读成 R3D，怎么画都不匹配。
        // 同理，一条画弯了的「直线」两端会甩出 ↘ 和 ↗，实测 3R9。
        //
        // 判据用**扇区相邻 + 更短**：拐弯扫出来的那格必定与某个邻居挨着（→ 旁边就是 ↘），
        // 而且短于它拐的那两笔。隔了一格的 R 与 D 互不吸收，所以「长横 + 短竖」那种手势
        // 照旧成立——短的那笔只要自己够长。
        //
        // **首尾腿与中间腿的阈值不一样**，因为它们是两种不同的东西：
        //   中间的那格是拐弯扫出来的，圆角再大也远短于它拐的那两笔 → 要求「短一半以上」，
        //     这样两笔都认真画的 →↘ 不会被吃掉（两条腿差不多长，比例过不去）。
        //   首尾那格是起笔收笔甩出来的。一条画歪了的线，切线偏得最多的正是两个端点——
        //     实测一条弓形 12% 的 ↗ 读成 →↗↑，那两截各占三分之一，「短一半」根本吸收不掉。
        //     所以首尾只要求「比邻居短」。代价是首尾两条相邻扇区、又一长一短的手势
        //     （→ 长横接一小段 ↘）会并成一条——那种手势本来也画不清楚，八向里没人这么配。
        bool Turn(int i)
        {
            for (int j = i - 1; j <= i + 1; j += 2)
                if (j >= 0 && j < legs.Count && Adjacent(legs[i].D, legs[j].D)
                    && Span(legs[i]) * 2 < Span(legs[j])) return true;
            return false;
        }

        // 不达标的腿并入较长的邻居，直到全部达标。每轮必删一条，故一定收敛。
        // 并入后可能出现相邻同向，顺手合掉（那一种是真合并，区间要接起来）。
        while (legs.Count > 1)
        {
            // 挑一条该并掉的：绝对太短的，或者「拐弯扫出来的那一格」（下面 Turn）。
            // 都有候选时并最短的那条。每轮必删一条，故一定收敛。
            int k = -1;
            for (int i = 0; i < legs.Count; i++)
                if (Span(legs[i]) < _minLegPx || Turn(i))
                    if (k < 0 || Span(legs[i]) < Span(legs[k])) k = i;
            if (k < 0) break;
            int nb = k == 0 ? 1
                   : k == legs.Count - 1 ? k - 1
                   : Span(legs[k - 1]) >= Span(legs[k + 1]) ? k - 1 : k + 1;
            legs[nb] = Absorb(legs[nb], legs[k]);
            legs.RemoveAt(k);
            for (int i = legs.Count - 1; i > 0; i--)
                if (legs[i].D == legs[i - 1].D) { legs[i - 1] = Join(legs[i - 1], legs[i]); legs.RemoveAt(i); }
        }
        // 全程就一条腿且还不够长：这是手抖，不是手势——返回空，让调用方补发右键菜单。
        if (legs.Count == 1 && Span(legs[0]) < _minLegPx) return "";

        // **每条腿的方向用它的首尾两点重定**，而不是沿用逐步投票的结果。
        // 整数坐标下相邻两个采样点的夹角噪声有 ±3° 上下，一条画在 26° 的直线（离 28° 边界只有 2°）
        // 逐步投票就是掷硬币，切出一串交替的碎腿，谁活下来看运气——实测得到过 "3R"。
        // 而同一条腿首尾相隔上百像素，噪声被摊薄成零点几度，方向是确定的。
        // 「按腿测方向」这句话，到这一步才真正兑现。
        var sb = new System.Text.StringBuilder();
        char prev = '\0';
        foreach (var l in legs)
        {
            if (sb.Length >= MaxPath) break;
            double dx = _pts[l.E].X - _pts[l.S].X, dy = _pts[l.E].Y - _pts[l.S].Y;
            char d = dx == 0 && dy == 0 ? l.D : Quantize(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            if (d == prev) continue;   // 重定之后可能与前一条同向，合掉
            sb.Append(d);
            prev = d;
        }
        return sb.ToString();
    }

    // 方向字符 → Dirs 下标。手写循环而不用 LINQ：调用方在钩子线程的热路径上（见 Adjacent）。
    private static int IndexOfDir(char c)
    {
        for (int i = 0; i < Dirs.Length; i++) if (Dirs[i].C == c) return i;
        return -1;
    }

    // 两个方向在八向环上挨不挨着。Dirs 本身就是按环序排的（R 3 D 1 L 7 U 9），所以就是下标差 1。
    // 「挨着」＝拐这个弯时笔尖会扫过对方那一格；隔一格（R 与 D）则不会。
    private static bool Adjacent(char a, char b)
    {
        // 不用 Array.FindIndex(Dirs, d => d.C == a)：那两个 lambda 各自捕获 a / b，
        // 每次调用都要分配一个闭包对象加两个委托——而本方法在 Analyze 里那个
        // O(legs²) 的合并循环里，而那段代码跑在**低级鼠标钩子的线程**上，
        // 那里有 300ms 硬预算（超时 Windows 静默摘钩子）。循环查表 8 次比这一堆分配便宜，
        // 而且不给那条线程制造 Gen0 压力。
        int i = IndexOfDir(a), j = IndexOfDir(b);
        if (i < 0 || j < 0) return false;
        int k = Math.Abs(i - j);
        return k == 1 || k == Dirs.Length - 1;
    }

    // 吸收一条噪声腿：长度记过来，**首尾区间不动**。
    //
    // 这一条是承重的。被吸收的那格（拐弯扫过的、起笔收笔甩出的）本来就是噪声，
    // 把它的采样点并进幸存腿的区间，等于让噪声去定那条腿的方向——而方向恰恰是拿首尾两点测的。
    // 实测 ↗↘：顶点的圆弧并进第二条腿之后，那条腿的弦被拉平，↗↘ 读成 ↗→。
    // 顺带也收紧了误触发：30px 右 + 30px 下这种小抖动，区间不接起来就凑不出一条 42px 的「腿」，
    // 照旧判成手抖去补发右键菜单。
    private static (char D, double Len, int S, int E) Absorb(
        (char D, double Len, int S, int E) keep, (char D, double Len, int S, int E) drop)
        => (keep.D, keep.Len + drop.Len, keep.S, keep.E);

    // 合并**同向**的两条腿：这一种是真合并，长度相加、首尾区间取并。
    private static (char D, double Len, int S, int E) Join(
        (char D, double Len, int S, int E) a, (char D, double Len, int S, int E) b)
        => (a.D, a.Len + b.Len, Math.Min(a.S, b.S), Math.Max(a.E, b.E));

    public void Reset() { _pending = false; Path = ""; _pts.Clear(); }

    // 两个角之间的最短夹角（度），处理 ±180 的绕回。
    private static double Delta(double a, double center)
    {
        double d = Math.Abs(a - center) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static char Quantize(double deg)
    {
        foreach (var d in Dirs)
            if (Delta(deg, d.Center) <= d.Half) return d.C;
        return 'R';   // 不可达（扇区恰好铺满 360°）：兜底免得 '\0' 混进串里
    }

    /// <summary>方向串 → 箭头（显示用）。非法字符原样保留，让坏数据显形而不是被吃掉。</summary>
    public static string Arrows(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var sb = new System.Text.StringBuilder(path.Length);
        // 非法字符原样保留，让坏数据显形而不是被吃掉。
        foreach (var c in path) sb.Append(Of(c)?.Arrow ?? c);
        return sb.ToString();
    }

    /// <summary>配置里的手势串规范化：去空白、只认八个方向、合并连续重复。不合法返回 ""。
    /// 箭头与小键盘数字（4682）都收，统一折成 L/U/R/D + 7913 这一种存储形态。</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var ch in raw)
        {
            if (ch == ' ') continue;
            char c = Canon(ch);
            if (c == '?') return "";          // 非法字符整串拒收，不静默剪掉
            if (sb.Length >= MaxPath) return "";
            if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
        }
        return sb.ToString();
    }

    // 单个字符 → 规范方向字符，认不出返回 '?'。
    // 规范字符与箭头都查 Dirs（唯一出处）；小键盘的轴向数字是纯输入宽容，与方向表无关，单列在此。
    private static char Canon(char ch)
    {
        char u = char.ToUpperInvariant(ch);
        foreach (var d in Dirs) if (d.C == u || d.Arrow == ch) return d.C;
        return u switch
        {
            '4' => 'L',
            '8' => 'U',
            '6' => 'R',
            '2' => 'D',
            _ => '?',
        };
    }
}
