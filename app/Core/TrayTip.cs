namespace Clockwork.Core;

// 托盘悬停文案：图标是唯一常露面的状态灯，但「有没有东西在跑」「勿扰生效到几点」
// 此前只有点开菜单才知道——开着录屏/投屏时恰恰不敢点。
//
// 文案刻意**零新增译名**：托盘 tooltip 由 shell 绘制（Segoe UI），塞一句新话就要动 18 份
// resx，而这里两个符号 + 一个 HH:mm 自己说明自己——「▶ 21:40」任何语言都读得懂。
// 纯函数在这里（同 PanelFocus/HotkeyRebindGate 的纪律）：拼装、128 字符上限的截断
// 都测得动，TrayIcon 只喂三个现读状态。
public static class TrayTip
{
    /// <summary>NotifyIcon.Text。静息 = 裸名；在跑加 ▶；勿扰加 ⏸+恢复钟点，两态可叠加。</summary>
    public static string Build(bool running, DateTime? dndUntil, DateTime now)
    {
        var s = "Clockwork";
        if (running) s += "  \u25B6";                              // ▶ 有运行中的清单/动作组
        if (dndUntil is DateTime u)
        {
            // 过期不画：DndRemaining 的 getter 自己会清，这里只兜住「拿着旧引用调用」的一帧。
            if (u > now) s += $"  \u23F8 {u:HH:mm}";                // ⏸ 到点自动恢复的钟点
        }
        // WinForms 的 NotifyIcon.Text  setter 对 >63 字符直接抛 ArgumentException——这条串没有
        // 越界的现实场景（名字 9 字符 + 两个符号 + 钟点），但上限是 setter 的契约，写在这。
        return s.Length > 63 ? s[..63] : s;
    }
}
