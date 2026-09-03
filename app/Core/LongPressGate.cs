namespace Clockwork.Core;

/// <summary>钩子该怎么处置这一个鼠标事件。</summary>
public enum PressVerdict
{
    /// <summary>原样放行，下游照常收到。</summary>
    Pass,
    /// <summary>吞掉，别让下游看见（按下的当口还不知道是长按还是点击，只能先扣住）。</summary>
    Swallow,
    /// <summary>吞掉这次抬起，并补发一次完整的真实中键——判定为普通点击。</summary>
    ReplayClick,
    /// <summary>吞掉这次抬起，唤出面板——判定为长按。</summary>
    Fire,
    /// <summary>先补发一次中键按下、再放行本事件——判定为拖拽，把扣住的那次按下还回去。</summary>
    ReplayDownThenPass,
}

// 「长按中键」的判定。纯状态机，不碰 Win32，时间与坐标都由调用方喂进来。
//
// 这件事的难点全在**普通中键点击必须照常可用**：中键在浏览器里开新标签、在编辑器里粘贴、
// 在 Windows 里是自动滚动。而按下的那一瞬间无法知道这是长按还是点击——所以只有一条路：
// 先吞掉按下，等抬起时再回头判断，判成点击就补发一次真的。这也是同类鼠标手势工具的通行做法。
//
// **按满时长就弹**，不等你松手。长按这类交互必须在到时间的那一刻给出反馈，否则按下去的人
// 得盲等——不知道按够了没有，只能一直按着猜，手感上跟"没反应"没区别。
// 代价是多一个状态：已经弹过了，那这次抬起就得吞掉（不能再补发一次中键，否则面板刚出来
// 就被一次中键点掉了）。这个代价值得付。
//
// 因此判定不只靠事件驱动，还需要一个到点的闹钟——按下之后没有任何鼠标事件也要能触发。
// 那个计时器在 Native.MouseHook 里，本类只提供 PollFire 供它询问。
//
// 四条出口对应四种真实意图，缺一条就会咬人：
//   点击（短按）      → 补发真中键。少了它，中键在全系统失灵。
//   按满时长          → 唤出面板（PollFire）。
//   弹过之后的抬起    → 吞掉。少了它，面板会被那次抬起当场点掉。
//   按住后移动（拖拽）→ 补发按下再放行。少了它，浏览器的中键自动滚动会彻底没法用。
public sealed class LongPressGate
{
    /// <summary>此刻要不要盯中键：面板总开关开着，且勾了「长按中键唤面板」。</summary>
    //
    // 抽出来的理由与 GestureGate.ShouldWatch 一字不差：这句话是承重的，而它原来只活在
    // App.ApplyMouseHook 里——那儿碰 Win32，测不到。于是那条本该盯着它的用例只能把
    // `PanelEnabled && PanelMiddleLongPress` 重敲一遍，而重敲一遍的断言什么都守不住：
    // 把 ApplyMouseHook 里那个判断删掉，它照旧绿。总开关的全部意义就是「关掉之后
    // 一根毫毛不动」（钩子必须先吞掉每一次中键按下才能判意图），那件事没有断言盯着，
    // 就只是一个存得下 false 的字段。
    public static bool ShouldWatch(bool masterOn, bool middleLongPress) => masterOn && middleLongPress;

    private readonly int _holdMs;
    private readonly int _moveTolerance;

    private bool _pending;      // 已经吞下一次按下，还没判出结果
    private bool _fired;        // 已按满时长、面板已弹：只等那次抬起来把它吞掉
    private long _downMs;
    private int _downX, _downY;

    /// <param name="holdMs">按住至少这么久才算长按。</param>
    /// <param name="moveTolerance">按住期间移动超过这么多像素即判为拖拽（抖动容差）。</param>
    public LongPressGate(int holdMs, int moveTolerance = 6)
    {
        // 下界不是洁癖：holdMs 配成 0 会让每一次普通点击都变成长按，中键就再也点不出来了。
        _holdMs = holdMs < 50 ? 50 : holdMs;
        _moveTolerance = moveTolerance < 0 ? 0 : moveTolerance;
    }

    /// <summary>是否正扣着一次按下（钩子据此决定要不要拦后续事件）。</summary>
    public bool Pending => _pending;

    /// <summary>按满时长了吗。由计时器定期问；返回 true 表示「就是现在，弹面板」，且只会返回一次。</summary>
    //
    // 由外部驱动而不是自己起线程：本类要保持纯粹（没有计时器就没有可测性，也不必管释放），
    // 而真正的闹钟在 MouseHook 那边——它本来就要管钩子的生命周期，多管一个计时器是顺手的事。
    public bool PollFire(long nowMs)
    {
        if (!_pending || nowMs - _downMs < _holdMs) return false;
        _pending = false;
        _fired = true;   // 这次的抬起要吞掉，不能再补发中键——否则面板刚弹出来就被点掉了
        return true;
    }

    public PressVerdict OnMiddleDown(long nowMs, int x, int y)
    {
        // 已经在扣着又来一次按下（上一次的抬起被别的钩子吞了、或系统丢了事件）：
        // 以新的这次为准重新起算，而不是保留一个永远等不到抬起的旧状态。
        _pending = true;
        _fired = false;
        _downMs = nowMs;
        _downX = x; _downY = y;
        return PressVerdict.Swallow;
    }

    public PressVerdict OnMove(int x, int y)
    {
        // 已经弹过面板了：这次按住期间的移动与拖拽无关，原样放行。
        if (_fired) return PressVerdict.Pass;
        if (!_pending) return PressVerdict.Pass;
        // 曼哈顿距离够用，且不用开平方：这里要的是「动了没有」，不是精确位移。
        //
        // **还没超出容差时返回 Pass，不是 Swallow。** 钩子把 Swallow 变成「返回 1」，
        // 而在低级鼠标钩子里返回 1 会把这条 WM_MOUSEMOVE 整个拦下来——**光标就此钉住不动**。
        // 更糟的是它自锁：光标没动，下一条移动消息的坐标仍从原地算起，位移永远累加不到容差之上，
        // 于是 ReplayDownThenPass 永远不触发，中键拖动的自动滚动（本类开头点名要保住的用例）
        // 再也起不来，按满时长后弹出来的反而是面板。
        // 手势那半边早就写着同一条规矩：「移动本身一律 Pass：光标位移吞不掉也不必吞」。
        if (Math.Abs(x - _downX) + Math.Abs(y - _downY) <= _moveTolerance) return PressVerdict.Pass;
        // 判成拖拽：把扣住的按下补发回去，此后不再拦——后续的移动和抬起自然流到下游。
        // 补发的按下落在指针**当前**位置（SendInput 的按键事件不带坐标），与原始按下差几像素，
        // 对自动滚动这类用途无影响，而这是唯一不引入"记录并回放坐标"整套复杂度的做法。
        _pending = false;
        return PressVerdict.ReplayDownThenPass;
    }

    public PressVerdict OnMiddleUp(long nowMs)
    {
        // 面板已经弹出来了：这次抬起是那一次长按的尾巴，吞掉。
        // 放行的话下游会收到一次中键，而此刻指针正压在刚弹出来的面板上——面板会被自己那一下点掉。
        if (_fired) { _fired = false; return PressVerdict.Swallow; }
        if (!_pending) return PressVerdict.Pass;   // 按下已经放行过（拖拽），这次抬起不该再动它
        _pending = false;
        // 用减法比较而不是 >= 某个绝对时刻：调用方喂的是 Environment.TickCount64，
        // 单调递增且不受改系统时间影响。
        return nowMs - _downMs >= _holdMs ? PressVerdict.Fire : PressVerdict.ReplayClick;
    }

    /// <summary>放弃当前扣着的按下，不补发任何东西。钩子被卸载 / 功能被关掉时用，避免留下悬空状态。</summary>
    public void Reset() { _pending = false; _fired = false; }
}
