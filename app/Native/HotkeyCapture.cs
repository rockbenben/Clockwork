using System.Collections.Generic;
using System.Windows.Input;

namespace Clockwork.Native;

// 急停键「按键捕捉」的纯逻辑：WPF 按键 → 可注册的组合键串。抽出来便于单测（避免依赖实时键盘状态）。
public static class HotkeyCapture
{
    // 是否为修饰键本身（含 Alt 时 e.Key=System）——捕捉时忽略、等主键。
    public static bool IsModifierKey(Key k)
        => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
             or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    // WPF Key → token。数字/小键盘取好看的显示名；其余用枚举名（WPF Key 名与 WinForms Keys 名基本一致，
    // 字母/F 键/Oem 符号/媒体键等都覆盖），能否注册/发送交给各执行路径判定，不再靠窄白名单。
    // WPF 对共享枚举值可能报出旧别名（PageDown→"Next" 等），规范化成两条发送路径都认的名字。
    private static readonly Dictionary<string, string> TokenAlias = new()
    {
        ["Next"] = "PageDown", ["Prior"] = "PageUp", ["Snapshot"] = "PrintScreen",
        ["Capital"] = "CapsLock", ["Return"] = "Enter",
    };

    public static string? KeyToken(Key k)
    {
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();          // 显示 0-9 而非 D0-D9
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "NumPad" + (int)(k - Key.NumPad0);
        var name = k.ToString();
        if (string.IsNullOrEmpty(name)) return null;
        return TokenAlias.TryGetValue(name, out var alias) ? alias : name;
    }

    // 由修饰键 + 主键组出组合键串。要求至少一个修饰键（避免注册裸键把某键从全局劫走），
    // 且最终组合必须可被 RegisterHotKey 注册；否则返回 null（调用方忽略本次按键）。
    public static string? BuildCombo(ModifierKeys mods, Key key)
    {
        if (IsModifierKey(key)) return null;
        var tok = KeyToken(key);
        if (tok == null) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return null;   // 至少一个修饰键
        parts.Add(tok);
        var combo = string.Join("+", parts);
        return KeyInput.ToHotkeyParams(combo) == null ? null : combo;   // 不可注册就作废
    }
}
