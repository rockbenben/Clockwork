namespace Clockwork.Core;

/// <summary>欠着的抬起是哪个键的。</summary>
public enum MouseUpButton
{
    /// <summary>右键。</summary>
    Right,
    /// <summary>中键。</summary>
    Middle,
}

/// <summary>一笔欠着的抬起。</summary>
/// <param name="Token">这一笔的编号。认领与结清都靠它，不会认错别人的那一笔。</param>
/// <param name="Button">欠的是哪个键的抬起。</param>
/// <param name="OwedAtMs">记下这笔账的时刻（TickCount64）。</param>
/// <param name="ForegroundWindow">记下这笔账时的前景窗口句柄——**只用于诊断**：
/// SendInput 只能送到当前前台窗口，所以它没法用来改投（见 MouseHook 的说明）。</param>
public readonly record struct MouseUpUnit(long Token, MouseUpButton Button, long OwedAtMs, long ForegroundWindow);

/// <summary>认领的结果。</summary>
public enum MouseUpClaim
{
    /// <summary>没有欠账可领。</summary>
    None,
    /// <summary>领到了。</summary>
    Claimed,
    /// <summary>补发线程正在配对（按下 → 抬起）的那一小段，这一拍不许动账。</summary>
    Pairing,
}

/// <summary>看门狗这一拍该不该动账。</summary>
public enum MouseUpDecision
{
    /// <summary>没欠账，或者还没到重试的时刻。</summary>
    Idle,
    /// <summary>欠着，但没人排过重试（补发线程卡住了）→ 看门狗接手。</summary>
    TakeOver,
    /// <summary>到点了。</summary>
    Due,
    /// <summary>配对期里，不许动账。</summary>
    Pairing,
}

/// <summary>
/// 「欠着的抬起」的账本：谁欠谁、欠几笔、什么时候算清。
///
/// **为什么要有它**：补发出去的按下如果配上抬起，那个键才不会卡在全系统按下态——
/// 而卡住的右键会让**所有左键点击**都被当成「按住右键时的左键」（任务栏不切换、点哪都没反应）。
/// 那是这个类最不能出的一种事故，所以欠账必须有人一直记着、一直还。
///
/// **为什么是状态机而不是几个计数器**：账目的形状散在各处时，它们之间的不变式只能靠注释维持，
/// 而注释不会跟着代码一起被改——每一次改动都要重新想一遍那几个字段的关系。
/// 收成状态机之后：
///   · **一笔账有编号**（<see cref="MouseUpUnit.Token"/>），认领与结清按编号走，
///     不会「领走别人的那一笔」——那是零长度点击与凭空多出一次右键的共同来源。
///   · **状态只有三段**：欠着 → 在途 → 结清（或发失败后回到欠着）。转段的地方只有一处。
///   · **不变量写在代码里**：在途的账一定还挂在某一笔上（认领时从队列移到在途表，
///     发出后才从在途表里消失），所以「账清零」不再可能掩盖「还有一笔正在发」。
///
/// （从前这里是**一个槽**：抬起由补发线程隔 15ms 结清，清早了没关系，因为那条线程顺手就发了。
/// 后来抬起允许失败——UIPI 会静默丢注入——失败就必须留着账重试，而单槽撑不住
/// 「前一条抬起还欠着、后一条按下又发出去」：后一条会把前一条覆盖掉，前一条就永远欠着了。）
///
/// **它不认输**：重试到什么时候都不放弃。
/// 认输（到某个次数之后就不再试了）听上去像个节省，而它省下的东西比它害处的小得多：
///   · 认输 = 那一笔欠着的抬起**永远送不出去**了。系统那边那个键就一直按着，
///     而左键点击从此全被当成「按住右键时的左键」。
///     而它没有别的出路：一次补发的点击是 DOWN+UP 净零，补不平那个多出来的按下；
///     只有一次**光秃秃的抬起**能补平它。
///   · 不认输 = 看门狗每 500ms 醒一次，把整个 MouseHook 对象图钉在内存里
///     （几 KB，而它的命就是进程的命）。
/// 拿「几 KB + 一个每 500ms 的空转」换「那个键永远按着」，不值得。
///
/// 而「每 500ms 往（早就没有钩子的）前台窗口里注入抬起」这一条也不成立：
/// **一次成功的注入就把那笔账结清了**，看门狗随即退休——所以它顶多误伤一次，
/// 换来的是那个卡住的键被松开。
///
/// **它不碰 Win32**：时间由调用方喂进来（TickCount64），发不发的决定权在调用方。
/// 于是这一整块能被真的测——不必往开发者的桌面上注入一个真的右键抬起。
///
/// **锁的边界**：所有状态转换都在同一把锁里，但**绝不跨 SendInput 持锁**。
/// 一次 SendInput 实测能到 313ms，而钩子线程也会走这条路（让路时结账、拆卸时结清），
/// 它在锁上等着就越过了 LowLevelHooksTimeout，Windows 会静默摘掉钩子。
/// 所以「认领」与「把它发出去」是分开的两步：认领在锁里，发在锁外。
/// </summary>
public sealed class MouseUpLedger
{
    /// <summary>配对期的长度：补发线程按下→抬起那一小段，看门狗不许动账。
    /// 抢走那一笔会把点击变成 0~15ms 的零长度点击（同批两条事件时间戳几乎相同，
    /// Chromium 那类程序直接当噪声丢掉），症状就是「这一下右键凭空消失」。
    /// 用时间兜底而不是一把锁：补发线程若死在半路，这一刻自己会过期，看门狗照常接手。</summary>
    public const int PairingGuardMs = 200;

    /// <summary>退避重试的底数。</summary>
    private const long RetryBaseMs = 500;
    /// <summary>退避的倍增档数（2^4 = 16 → 500×16 = 8000ms，再被封顶压到 5000）。</summary>
    private const int RetryMaxShift = 4;
    /// <summary>退避的封顶。</summary>
    private const long RetryCapMs = 5000;
    /// <summary>退避档位计数器的上界：只是不让它溢出（移位已经被上面封顶）。</summary>
    private const int RetryLadderCap = 32;

    private readonly object _gate = new();
    /// <summary>欠着的账，按记下的先后排着：队首的先还。</summary>
    private readonly List<MouseUpUnit> _owed = new();
    /// <summary>正在发出去、还没回来的那几笔。</summary>
    private readonly HashSet<long> _inFlight = new();
    private long _nextToken;
    private long _pairingUntilMs;
    /// <summary>下一次重试的时刻（TickCount64）；0 = 没人排过（看门狗快路径接手）。</summary>
    private long _retryAtMs;
    private int _retryAttempt;
    /// <summary>「交还给程序的那次按下」在等用户自己的抬起。</summary>
    private bool _hasHandedDown;
    private long _handedDownAtMs;

    /// <summary>还欠着几笔。</summary>
    public int OwedCount { get { lock (_gate) return _owed.Count; } }

    /// <summary>有几笔正在发（认领了、还没回来）。</summary>
    public int InFlightCount { get { lock (_gate) return _inFlight.Count; } }

    /// <summary>此刻还有没有欠账。</summary>
    public bool HasDebt { get { lock (_gate) return _owed.Count > 0; } }

    /// <summary>下一次重试的时刻（0 = 没人排过）。</summary>
    public long RetryAtMs { get { lock (_gate) return _retryAtMs; } }

    /// <summary>退避到第几次了。</summary>
    public int RetryAttempt { get { lock (_gate) return _retryAttempt; } }

    /// <summary>有没有一笔「交还给程序、等着用户自己抬起」的按下。</summary>
    public bool HasHandedDown { get { lock (_gate) return _hasHandedDown; } }

    /// <summary>欠着某个键的几笔。</summary>
    public int OwedCountOf(MouseUpButton button)
    {
        lock (_gate) return OwedCountOfLocked(button);
    }

    private int OwedCountOfLocked(MouseUpButton button)
    {
        int n = 0;
        foreach (var u in _owed) if (u.Button == button) n++;
        return n;
    }

    /// <summary>队首那一笔记下时的前景窗口句柄（诊断用；没有欠账时是 0）。
    ///
    /// 「队首」不是「最老」：发失败的那一笔会被放回**队尾**，而它仍带着自己最初记下的时刻，
    /// 所以队列里可能同时有「更新的队首」和「更老的队尾」。</summary>
    public long HeadForegroundWindow
    {
        get { lock (_gate) return _owed.Count > 0 ? _owed[0].ForegroundWindow : 0; }
    }

    /// <summary>一句给日志看的账目摘要（带队首那一笔欠了多少毫秒）。</summary>
    public string Describe(long nowMs)
    {
        lock (_gate)
        {
            long head = _owed.Count > 0 ? nowMs - _owed[0].OwedAtMs : 0;
            return $"right={OwedCountOfLocked(MouseUpButton.Right)} "
                 + $"middle={OwedCountOfLocked(MouseUpButton.Middle)} head={head}ms";
        }
    }

    /// <summary>那一笔交还给程序的按下已经等了多久（诊断用；没有则是 0）。</summary>
    public long HandedDownAgeMs(long nowMs)
    {
        lock (_gate) { return _hasHandedDown ? nowMs - _handedDownAtMs : 0; }
    }

    /// <summary>记下一笔欠着的抬起。</summary>
    /// <param name="button">欠的是哪个键。</param>
    /// <param name="nowMs">此刻（TickCount64）。</param>
    /// <param name="foregroundWindow">此刻的前台窗口句柄（诊断用）。</param>
    public MouseUpUnit Owe(MouseUpButton button, long nowMs, long foregroundWindow)
    {
        lock (_gate) return OweLocked(button, nowMs, foregroundWindow);
    }

    private MouseUpUnit OweLocked(MouseUpButton button, long nowMs, long foregroundWindow)
    {
        var unit = new MouseUpUnit(++_nextToken, button, nowMs, foregroundWindow);
        _owed.Add(unit);
        return unit;
    }

    /// <summary>认领**队首**那一笔欠账。</summary>
    /// <param name="nowMs">此刻（TickCount64）。</param>
    /// <param name="unit">领到的那一笔（没领到时是默认值）。</param>
    /// <remarks>受配对闸保护：补发线程正在按下→抬起配对时，这一拍不许动账。</remarks>
    public MouseUpClaim TryClaimOldest(long nowMs, out MouseUpUnit unit)
    {
        lock (_gate)
        {
            if (_owed.Count == 0) { unit = default; return MouseUpClaim.None; }
            if (nowMs < _pairingUntilMs) { unit = default; return MouseUpClaim.Pairing; }
            unit = _owed[0];
            _owed.RemoveAt(0);
            _inFlight.Add(unit.Token);
            return MouseUpClaim.Claimed;
        }
    }

    /// <summary>认领**指定**的那一笔。</summary>
    /// <remarks>
    /// 补发线程用它自己刚记下的那一笔：认领「队首那一笔」会把别人的账领走，
    /// 于是自己那一笔的抬起反而没人发。而它不受配对闸限制——正在配对的就是它自己。
    /// </remarks>
    public bool TryClaimToken(long token, out MouseUpUnit unit)
    {
        lock (_gate)
        {
            for (int i = 0; i < _owed.Count; i++)
            {
                if (_owed[i].Token != token) continue;
                unit = _owed[i];
                _owed.RemoveAt(i);
                _inFlight.Add(token);
                return true;
            }
            unit = default;
            return false;
        }
    }

    /// <summary>这一笔发出去了：结清。</summary>
    public void Settle(MouseUpUnit unit)
    {
        lock (_gate) { _inFlight.Remove(unit.Token); }
    }

    /// <summary>这一笔没发出去：账还在（回到队列末尾）。</summary>
    /// <remarks>
    /// 回到**队尾**是有意的：它刚失败过，让别的账先试——一条一直失败
    /// （提权前台拦着注入）时，另一条不该被饿死（中键卡在全系统按下态 = 中键拖拽永远在拖）。
    /// </remarks>
    public void ReOwe(MouseUpUnit unit)
    {
        lock (_gate)
        {
            _inFlight.Remove(unit.Token);
            _owed.Add(unit);
        }
    }

    /// <summary>占住配对期：这一小段里看门狗不许动账。</summary>
    public void ReservePairing(long nowMs, int guardMs)
    {
        lock (_gate)
        {
            long until = nowMs + guardMs;
            if (until > _pairingUntilMs) _pairingUntilMs = until;
        }
    }

    /// <summary>配对期结束（补发线程把抬起发完了）。</summary>
    public void ReleasePairing()
    {
        lock (_gate) { _pairingUntilMs = 0; }
    }

    /// <summary>此刻是不是配对期。</summary>
    public bool PairingActive(long nowMs)
    {
        lock (_gate) { return nowMs < _pairingUntilMs; }
    }

    /// <summary>排下一次退避重试。</summary>
    /// <param name="nowMs">此刻（TickCount64）。</param>
    /// <remarks>
    /// 退避 500ms → 1s → 2s → 4s → 5s 封顶，够等到用户切走那个提权窗口。
    /// **它不认输**——理由见本类的类头注释：认输会让那个卡在按下态的键永远解不开。
    /// </remarks>
    public void ScheduleRetry(long nowMs)
    {
        lock (_gate)
        {
            if (_retryAttempt < RetryLadderCap) _retryAttempt++;
            long delay = RetryBaseMs << Math.Min(RetryMaxShift, _retryAttempt - 1);
            if (delay > RetryCapMs) delay = RetryCapMs;
            _retryAtMs = nowMs + delay;
        }
    }

    /// <summary>账还清了：把重试状态清掉。</summary>
    public void ClearRetry()
    {
        lock (_gate)
        {
            _retryAtMs = 0;
            _retryAttempt = 0;
        }
    }

    /// <summary>看门狗这一拍该不该动账。</summary>
    public MouseUpDecision Decide(long nowMs)
    {
        lock (_gate)
        {
            if (_owed.Count == 0) return MouseUpDecision.Idle;
            if (nowMs < _pairingUntilMs) return MouseUpDecision.Pairing;
            // 「没人排过重试」= 补发线程卡住了 → 看门狗接手（快路径）。
            if (_retryAtMs == 0) return MouseUpDecision.TakeOver;
            if (nowMs >= _retryAtMs) return MouseUpDecision.Due;
            return MouseUpDecision.Idle;
        }
    }

    /// <summary>看门狗该不该退休：钩子已拆 + 没有在途的那几笔 + 账已还清。</summary>
    /// <remarks>
    /// **在途的那几笔也算没还清**：账目在认领时就少了，而抬起还在发送里；
    /// 那一刻若把看门狗销毁，那笔在途的账一失败就没人还了。
    /// </remarks>
    public bool ShouldRetireWatchdog(bool tornDown)
    {
        lock (_gate)
        {
            return tornDown
                && _inFlight.Count == 0
                && _owed.Count == 0;
        }
    }

    /// <summary>记下「交还给程序的那次按下」——它在等用户自己的抬起。</summary>
    /// <remarks>
    /// **不用计时器兜底**：按下是用户还按着的时候注入的，用户按多久都可能——
    /// 而计时器只知道「还没收到抬起」，分不清「用户还按着」与「抬起真的丢了」。
    /// 计时器一响就给一个用户还按着的键补一个抬起：目标程序先收到一次提前的松开，
    /// 稍后用户真松手时又来一条没有配对按下的抬起，凭它合成一个莫名菜单。
    /// </remarks>
    public void MarkHandedDown(long nowMs)
    {
        lock (_gate)
        {
            _hasHandedDown = true;
            _handedDownAtMs = nowMs;
        }
    }

    /// <summary>真抬起到了（或钩子拆了）：那笔交还的按下不再欠抬起。返回这次是否真的清掉了。</summary>
    public bool ClearHandedDown()
    {
        lock (_gate)
        {
            if (!_hasHandedDown) return false;
            _hasHandedDown = false;
            _handedDownAtMs = 0;
            return true;
        }
    }

    /// <summary>
    /// 认领那笔丢失的抬起：把「交还给程序的按下」换成一笔欠着的抬起。
    /// </summary>
    /// <remarks>
    /// **只在「那一笔按下真的被我们吞了」时才该调它**：放行的那些，
    /// 它们自己的抬起会原样进系统——那个抬起已经把卡住的键松开了，
    /// 这时再补一个抬起就是没有配对按下的抬起 → DefWindowProc 凭它合成一个莫名的右键菜单。
    /// </remarks>
    public bool ClaimHandedDown(long nowMs, long foregroundWindow, out MouseUpUnit unit)
    {
        lock (_gate)
        {
            if (!_hasHandedDown) { unit = default; return false; }
            _hasHandedDown = false;
            _handedDownAtMs = 0;
            unit = OweLocked(MouseUpButton.Right, nowMs, foregroundWindow);
            return true;
        }
    }
}
