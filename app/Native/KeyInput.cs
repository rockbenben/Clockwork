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

    // 活：发送组合键（SendInput 原子注入）。成功→Unverified；各失败态→Warn。
    public static ActionResult SendKeyCombo(string combo)
    {
        var p = KeyCombo.ParseCombo(combo);
        if (string.IsNullOrWhiteSpace(p.Key))
            return ActionResult.Warn(Strings.Lf("Warn_KeyNoMain", combo));
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

        var mods = new List<ushort>();
        if (p.UseWin) mods.Add(0x5B);                                       // LWIN
        if (p.Modifiers.Contains("Ctrl")) mods.Add(0x11);
        if (p.Modifiers.Contains("Shift") || needShift) mods.Add(0x10);
        if (p.Modifiers.Contains("Alt")) mods.Add(0x12);

        bool got = InjectionLock.Enter();
        try
        {
            uint sent = Win32.SendCombo(mods.ToArray(), vk);
            int expected = mods.Count * 2 + 2;
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
