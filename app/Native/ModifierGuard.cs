using System;
using System.Runtime.InteropServices;
using System.Threading;
using Clockwork.Core;

namespace Clockwork.Native;

// 发键前的一道闸：**此刻还按着的修饰键，会跟着主键一起到目标程序**。
//
// 事实经过（2026-10-31，用户实报「发送 enter 还是无效」）：
// 热键触发的发键，注入那一刻用户正按着**热键自己的修饰键**（Ctrl+Shift）——
// 于是发出去的不是裸回车，而是 Ctrl+Shift+Enter。目标程序收到的是「带修饰键的回车」，
// 多数程序对它毫无反应（浏览器里是「在新窗口打开」，聊天软件里什么也不发生），
// 用户看到的正是「按了没反应」，而日志那边只写一句「已发送（未校验）」。
//
// 探针实测（一次性 WPF 探针，2026-10-31，同一台机器）：
//   裸回车                → 目标程序看到 mods=None
//   按着 Ctrl 注入回车    → 目标程序看到 mods=Control   ← 修饰键跟着主键到了目标程序
//   先抬起 Ctrl 再注入     → 目标程序看到 mods=None
//
// 所以：用户写下的组合是**绝对**的——框里写「回车」就发裸回车，
// 不因为「他这会儿还按着 Ctrl」就变成 Ctrl+回车。
// 两步处理：
//   ① 先等一小会儿（热键触发的那一刻他多半正在松手；等到了就什么都不用补，也没有任何副作用）；
//   ② 还没松就补发抬起事件——把「他按着的修饰键」从这次注入里摘出去。
//
// 代价要说清：②会让系统以为那些修饰键已经松开，而用户手上还按着——
// 在他松手之前那一下键里，系统不再替他带修饰键。这是「发出去的键要对」与
// 「系统对物理按键的认知」之间的取舍，取前者：发出去的键是用户写的那个。
//
// **为什么这条闸只管修饰键**：修饰键是「这一下键带着哪些修饰」的来源；
// 别的键（字母、数字）不受影响，而漏检的后果是「发出去的主键被改了身份」——
// 那是用户完全看不懂的故障（框里写着回车，程序收到的却是 Ctrl+回车）。
public static class ModifierGuard
{
    // 修饰键的「家族」：通用虚拟键码 + 左右两个具体键码。
    // wanted 里给出通用码（0x11 之类）就算整个家族都要留着——组合键写的是 Ctrl，
    // 不分左右；而系统状态是分左右的（VK_LCONTROL 0xA2 / VK_RCONTROL 0xA3）。
    private static readonly (ushort Generic, ushort Left, ushort Right)[] Families =
    {
        (0x11, 0xA2, 0xA3),   // Ctrl
        (0x10, 0xA0, 0xA1),   // Shift
        (0x12, 0xA4, 0xA5),   // Alt
        (0x5B, 0x5B, 0x5C),   // Win
    };

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>这个虚拟键此刻是不是按着。</summary>
    public static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>此刻还按着、但 wanted 里没有的修饰键（左右各算，返回具体键码）。</summary>
    //
    // wanted 用组合键那套通用码（0x11 / 0x10 / 0x12 / 0x5B）。
    // 判定本身是纯的（见 StrayAmong），只有「此刻按着哪些」这一步要看系统状态。
    public static ushort[] Stray(ushort[] wanted) => StrayAmong(Held(), wanted);

    /// <summary>此刻按着的修饰键（具体键码 + 通用码，左右各算）。</summary>
    //
    // **通用码也问一遍**：物理按下时系统报的是具体码（VK_LCONTROL 之类），
    // 而注入走的是通用码（VK_CONTROL）——实测注入一次通用码，目标程序那边收到的是
    // 具体码 0xA2，但异步状态两个码都置上了。两种都问，才不靠某一条路的假设。
    public static ushort[] Held()
    {
        var list = new System.Collections.Generic.List<ushort>(8);
        foreach (var fam in Families)
        {
            if (IsDown(fam.Left)) list.Add(fam.Left);
            if (fam.Right != fam.Left && IsDown(fam.Right)) list.Add(fam.Right);
            if (fam.Generic != fam.Left && IsDown(fam.Generic)) list.Add(fam.Generic);
        }
        return list.ToArray();
    }

    /// <summary>纯判定：held 里哪些是「这次组合不要的」。</summary>
    //
    // 抽成纯函数是为了能单测：真正要看系统状态的只有 Held() 那一步，
    // 而「要留着哪些」这条规则一旦漂移，症状是「发出去的键被改了身份」——
    // 那是用户完全看不懂的故障，钉不住就会再犯。
    public static ushort[] StrayAmong(ushort[] held, ushort[] wanted)
    {
        var list = new System.Collections.Generic.List<ushort>(4);
        foreach (var fam in Families)
        {
            if (FamilyWanted(wanted, fam)) continue;
            foreach (var v in held)
                if (v == fam.Generic || v == fam.Left || v == fam.Right) list.Add(v);
        }
        return list.ToArray();
    }

    // 这个家族要不要留着：wanted 里出现通用码或它的任一左右具体码，就算要留着。
    private static bool FamilyWanted(ushort[] wanted, (ushort Generic, ushort Left, ushort Right) fam)
    {
        foreach (var v in wanted)
            if (v == fam.Generic || v == fam.Left || v == fam.Right) return true;
        return false;
    }

    /// <summary>
    /// 等用户把「还按着、又不属于这次组合」的修饰键松开。
    /// 返回**到那一刻为止还没松开**的那些——调用方要在注入前把它们补发抬起。
    /// </summary>
    //
    /// 等，是为了大多数情况下的「什么都不用补」：热键触发的那一刻用户多半正在松手，
    /// 等到他松了，这一步就没有任何副作用（不碰他的键盘状态）。
    /// 等不到，才由调用方补发抬起——那意味着他一直按着（或者按的是别的键）。
    ///
    /// 等待可被急停/取消打断：等的是别人的手，不该比这一组本身还难停。
    public static ushort[] AwaitRelease(ushort[] wanted, RunCancel? cancel = null, int waitMs = 300)
    {
        var stray = Stray(wanted);
        if (stray.Length == 0) return stray;
        long deadline = Environment.TickCount64 + waitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!RunCancel.Sleep(cancel, 20)) break;   // 急停/取消：不等了，剩下的按「还按着」处理
            stray = Stray(wanted);
            if (stray.Length == 0) return stray;        // 松开了：什么都不用补
        }
        return stray;
    }

    /// <summary>补发抬起事件：把这些修饰键从接下来的注入里摘出去。</summary>
    //
    // 走的是 Win32.ReleaseKeys（部分注入的善后那套）——同一种事件、同一个通道，
    // 不另写一份注入，免得两处的抬起语义漂移。
    public static void Release(ushort[] vks)
    {
        if (vks.Length > 0) Win32.ReleaseKeys(vks);
    }

    /// <summary>SendKeys 序列里带了哪些修饰键（要留着的那些）。</summary>
    //
    // SendKeys 的写法：^ = Ctrl、% = Alt、+ = Shift，且只作前缀用。
    // 花括号组与圆括号里的内容是**字面量**（"{+}" 是加号、"(" 是左括号），不算修饰键。
    public static ushort[] WantedFromSendKeys(string sequence)
    {
        ushort ctrl = 0, alt = 0, shift = 0;
        int depth = 0;
        foreach (var ch in sequence ?? "")
        {
            if (ch == '{') { depth++; continue; }
            if (ch == '}') { if (depth > 0) depth--; continue; }
            if (ch == '(') { depth++; continue; }
            if (ch == ')') { if (depth > 0) depth--; continue; }
            if (depth > 0) continue;
            if (ch == '^') ctrl = 0x11;
            else if (ch == '%') alt = 0x12;
            else if (ch == '+') shift = 0x10;
        }
        var list = new System.Collections.Generic.List<ushort>(3);
        if (ctrl != 0) list.Add(ctrl);
        if (alt != 0) list.Add(alt);
        if (shift != 0) list.Add(shift);
        return list.ToArray();
    }
}
