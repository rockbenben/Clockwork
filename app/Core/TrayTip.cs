namespace Clockwork.Core;

// 托盘悬停文案：图标是唯一常露面的状态灯，但「有没有东西在跑」「勿扰生效到几点」
// 此前只有点开菜单才知道——开着录屏/投屏时恰恰不敢点。
//
// 运行/勿扰两态刻意用符号：托盘 tooltip 由 shell 绘制（Segoe UI），那两个符号自己说明自己。
// 让路不一样——它必须说清「右键现在不归我们管」，符号表达不了，所以由调用方传入
// 本地化短句（Gesture_WatchYielding）；这里只负责拼装与 NotifyIcon 的 63 字符上限。
// 让路那句是本地化长句，63 字符装不下三段状态，所以让路与勿扰钟点互斥（见方法体）。
// 纯函数在这里（同 PanelFocus/HotkeyRebindGate 的纪律）：拼装、截断都测得动。
public static class TrayTip
{
    /// <summary>
    /// NotifyIcon.Text。静息 = 裸名；在跑加 ▶；勿扰加 ⏸+恢复钟点；让路加本地化短句。
    /// 让路与勿扰**互斥**：两者同时在场时让路赢（见方法体）。
    /// </summary>
    public static string Build(bool running, DateTime? dndUntil, DateTime now, string? yielding = null)
    {
        var s = "Clockwork";
        if (running) s += "  \u25B6";                              // ▶ 有运行中的清单/动作组

        // 让路是「现在右键不归我们管」的状态，必须和勿扰一样能从托盘一眼看出来。
        //
        // **让路与勿扰钟点只能留一个。** WinForms 的 NotifyIcon.Text setter 对 >63 字符
        // 直接抛 ArgumentException，而让路那句是本地化的：德语 46 字、印地语与荷兰语 47 字、
        // 俄语 41 字——那句本身就把 63 里的 47 用掉了。三段同时在场（名字 + ▶ + 钟点 + 让路）
        // 必然越界，而硬切会在句中截断（德语会切成「… wird nicht erfa」这种半句话）。
        //
        // 弃的是钟点不是让路：让路回答「为什么右键没反应」，缺了它用户无从判断；
        // 钟点回答「勿扰到几点恢复」，让路结束后下一次 SyncTip 就补回来（≤30 秒）。
        if (!string.IsNullOrEmpty(yielding))
        {
            s += "  " + yielding;
        }
        else if (dndUntil is DateTime u)
        {
            // 过期不画：DndRemaining 的 getter 自己会清，这里只兜住「拿着旧引用调用」的一帧。
            if (u > now) s += $"  \u23F8 {u:HH:mm}";                // ⏸ 到点自动恢复的钟点
        }
        // 63 是 setter 的硬契约，超了直接抛 ArgumentException。按上面那条取舍，最长的一句
        // （荷兰语/印地语 47 字）+ 名字 + ▶ + 两个分隔空格 = 61，稳稳在界内；
        // 这里的截断只是兜底，正常路径不该走到。
        return s.Length > 63 ? s[..63] : s;
    }
}
