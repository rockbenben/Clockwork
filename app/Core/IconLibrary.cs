namespace Clockwork.Core;

// 内置图标库：给选择器铺格子用的一串 MDL2 码位。
//
// 为什么是「一张按主题排好的表」而不是「整个字体」：Segoe MDL2 Assets 有一千多个字形，
// 其中大半是 Windows 自己的界面零件（各种角标、拼写波浪线、输入法状态），摆进选择器只是噪音。
// 挑出来的这些是**动作**能用得上的：开关一块屏、闭一次麦、清一次剪贴板。
//
// 为什么不给每个图标配名字：那是 18 种语言乘一百多条。而挑图标本来就是看着挑的，
// 不是搜着挑的——按主题排好、一屏铺开，眼睛比搜索框快。ToolTip 里给码位，够改回文本框的人对照。
//
// **这张表的验收方式是渲染，不是读码表**：挑错的码位不会报错，只会画成一个空心方框。
// 选择器窗口本身就是那张验收图——跑 `--shots` 看 IconPicker 那几张，有方框就从这儿删掉。
public static class IconLibrary
{
    /// <summary>按主题排好的码位。选择器按这个顺序铺格子，所以相近的图标会挨在一起。</summary>
    public static readonly int[] Glyphs =
    {
        // ── 屏幕 / 画面 ──
        0xE7F4, 0xEBFC, 0xE7C4, 0xE8A7, 0xE706, 0xEC46, 0xE708, 0xE123,
        0xE114, 0xE722, 0xEB9F,

        // ── 声音 / 媒体 ──
        0xE767, 0xE74F, 0xE720, 0xEC54, 0xE768, 0xE769, 0xE71A, 0xE72C,
        0xE893, 0xE892, 0xE8B1, 0xEC4F, 0xE8D6,

        // ── 输入 / 交互 ──
        0xE765, 0xE962, 0xE70F, 0xE8BD, 0xE7E7, 0xEA8F, 0xE711, 0xE710,
        0xE738, 0xE8FB, 0xE71C, 0xE721, 0xE712, 0xE700,

        // ── 文件 / 剪贴板 ──
        0xE8F4, 0xE838, 0xE7C3, 0xE8F1, 0xE77F, 0xE8C8, 0xE75C, 0xE74D,
        0xE74E, 0xE896, 0xE898, 0xE723, 0xE71B,

        // ── 时间 / 标记 ──
        0xE916, 0xE81C, 0xE787, 0xE823, 0xE945, 0xE9F3, 0xE895, 0xE72D,
        0xE734, 0xE735, 0xE7C1, 0xE718, 0xE840, 0xE946, 0xE9CE, 0xE897,

        // ── 网络 / 设备 ──
        0xE701, 0xE702, 0xE839, 0xE705, 0xE709, 0xE70A, 0xE703, 0xE704,
        0xE717, 0xE716, 0xE77B, 0xE715, 0xE774, 0xE7FC, 0xE950,

        // ── 系统 / 电源 ──
        0xE713, 0xE72E, 0xE785, 0xE7E8, 0xE7BA, 0xE783, 0xE8AB, 0xE71D,
        0xECAA, 0xE80F, 0xE890, 0xE8C4, 0xE72B, 0xE72A,
    };

    /// <summary>「E72E」这样的四位十六进制文本 → 码位；不是码位就返回 -1。</summary>
    public static int Parse(string? icon)
    {
        var s = (icon ?? "").Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length is < 4 or > 5) return -1;
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1;
    }

    /// <summary>码位 → 存进配置里的那串文本。四位补零，与手写的「E72E」同形。</summary>
    public static string Format(int codepoint) => codepoint.ToString("X4");
}
