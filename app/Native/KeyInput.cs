using System.Text.RegularExpressions;
using Clockwork.Core;
using Clockwork.I18n;
using WinKeys = System.Windows.Forms.Keys;

namespace Clockwork.Native;

// 组合键串 → RegisterHotKey 参数。
public sealed class HotkeyParams
{
    public uint Modifiers { get; init; }
    public uint Vk { get; init; }
}

// 键名→VK 与组合键注入。
public static class KeyInput
{
    // 常用键名别名 → System.Windows.Forms.Keys 枚举正名。发键与急停键注册共用。
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = "Escape", ["del"] = "Delete", ["ins"] = "Insert", ["bs"] = "Back", ["backspace"] = "Back",
        ["pgup"] = "PageUp", ["pageup"] = "PageUp", ["pgdn"] = "PageDown", ["pagedown"] = "PageDown",
        ["prtsc"] = "PrintScreen", ["return"] = "Enter",
    };

    // 键名 → Keys 枚举虚拟键码。0 = 不认（调用方兜底）。多位纯数字拒绝（'10' 会静默变 VK 10）→ 单数字 D0-D9 → 别名 → 枚举。
    public static int KeysVk(string key)
    {
        if (string.IsNullOrEmpty(key) || Regex.IsMatch(key, @"^\d\d+$")) return 0;
        var name = Regex.IsMatch(key, @"^\d$") ? "D" + key : key;
        if (Alias.TryGetValue(name, out var a)) name = a;
        return Enum.TryParse<WinKeys>(name, true, out var k) ? (int)k : 0;
    }

    public static HotkeyParams? ToHotkeyParams(string combo)
    {
        var p = KeyCombo.ParseCombo(combo);
        if (string.IsNullOrWhiteSpace(p.Key)) return null;
        var vk = (uint)KeysVk(p.Key!);
        if (vk == 0) return null;
        uint mods = 0;
        if (p.Modifiers.Contains("Alt")) mods |= 0x1;
        if (p.Modifiers.Contains("Ctrl")) mods |= 0x2;
        if (p.Modifiers.Contains("Shift")) mods |= 0x4;
        if (p.UseWin) mods |= 0x8;
        return new HotkeyParams { Modifiers = mods, Vk = vk };
    }

    // 修饰键 → 虚拟键码。键盘与滚轮两条注入路共用一份：顺序即按下顺序，抬起按逆序。
    // addShift 供键盘路使用——'+' 这类字符本身就需要 Shift，与用户写没写 Shift 无关。
    private static ushort[] ModifierVks(ParsedCombo p, bool addShift = false)
    {
        var mods = new List<ushort>();
        if (p.UseWin) mods.Add(0x5B);                                       // LWIN
        if (p.Modifiers.Contains("Ctrl")) mods.Add(0x11);
        if (p.Modifiers.Contains("Shift") || addShift) mods.Add(0x10);
        if (p.Modifiers.Contains("Alt")) mods.Add(0x12);
        return mods.ToArray();
    }

    // 「发送按键」框里可接受的内容：能绑成热键的组合，或滚轮伪键。
    // 与热键绑定的判据（ToHotkeyParams）刻意分开：RegisterHotKey 表达不了滚轮，
    // 合成一个判据的话，要么滚轮进不了发送框，要么滚轮能被绑成一个永远不触发的全局热键。
    public static bool CanSendCombo(string combo)
        => !KeyCombo.HasUnknownModifier(combo)
           && (ToHotkeyParams(combo) != null || KeyCombo.WheelNotches(KeyCombo.ParseCombo(combo).Key) != 0);

    // 活：发送组合键（SendInput 原子注入）。成功→Unverified；各失败态→Warn。
    public static ActionResult SendKeyCombo(string combo)
    {
        var p = KeyCombo.ParseCombo(combo);
        if (string.IsNullOrWhiteSpace(p.Key))
            return ActionResult.Warn(Strings.Lf("Warn_KeyNoMain", combo));

        // 滚轮伪键：走鼠标通道。修饰键仍按下面那套解析，Ctrl+WheelDown 因此免费可用。
        // 放在主键→虚拟键码解析之前——WheelDown 不是任何虚拟键，往下走只会得到「无法识别的键」。
        int notches = KeyCombo.WheelNotches(p.Key);
        if (notches != 0)
        {
            var wmods = ModifierVks(p);
            int wexpected = wmods.Length * 2 + 1;   // 修饰键按下 + 一格滚轮 + 修饰键抬起
            bool wgot = InjectionLock.Enter();
            try
            {
                uint n = Win32.SendWheel(wmods, notches);
                if (n == 0) return ActionResult.Warn(Strings.Lf("Warn_KeyRejected", combo));
                if (n < wexpected)
                {
                    Win32.ReleaseKeys(wmods);   // 与键盘那条路同一套善后：别把修饰键卡在按下态
                    return ActionResult.Warn(Strings.Lf("Warn_KeyPartial", combo, n, wexpected));
                }
                return ActionResult.Unver();
            }
            finally { InjectionLock.Exit(wgot); }
        }
        if (Regex.IsMatch(p.Key!, @"^\d\d+$"))
            return ActionResult.Warn(Strings.Lf("Warn_KeyMultiDigit", p.Key!, combo));

        bool needShift = false;
        ushort vk = (ushort)KeysVk(p.Key!);
        if (vk == 0)
        {
            if (p.Key!.Length == 1)
            {
                short vs = Win32.VkKeyScan(p.Key[0]);
                if (vs == -1) return ActionResult.Warn(Strings.Lf("Warn_KeyUnknown", p.Key!, combo));
                vk = (ushort)(vs & 0xFF);
                if ((vs & 0x100) != 0) needShift = true;   // 该字符本身需要 Shift（如 '+'）
            }
            else return ActionResult.Warn(Strings.Lf("Warn_KeyUnknown", p.Key!, combo));
        }

        var mods = ModifierVks(p, addShift: needShift);

        bool got = InjectionLock.Enter();
        try
        {
            uint sent = Win32.SendCombo(mods, vk);
            int expected = mods.Length * 2 + 2;
            if (sent == 0)
                return ActionResult.Warn(Strings.Lf("Warn_KeyRejected", combo));
            if (sent < expected)
            {
                // 部分注入：补发全部抬起事件善后（防修饰键卡在按下态），并如实报失败。
                var all = new List<ushort> { vk };
                all.AddRange(mods);
                Win32.ReleaseKeys(all.ToArray());
                return ActionResult.Warn(Strings.Lf("Warn_KeyPartial", combo, sent, expected));
            }
            return ActionResult.Unver();
        }
        finally { InjectionLock.Exit(got); }
    }
}
