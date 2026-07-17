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

    // 系统级保留组合：注册成功会把它从全系统劫走（如 Alt+F4 让所有程序关不了窗、Ctrl+Shift+Esc 打不开任务管理器）。
    // 存「规范化 (修饰键掩码, 虚拟键)」而非字符串：配置手改可以写 "Alt + F4"/"Control+Escape"/"Alt+esc"/乱序修饰键，
    // 注册解析器全都认——按字符串比对会被这些拼法绕过，恰恰放走了要拦的东西。掩码 Alt=1 Ctrl=2 Shift=4（同 RegisterHotKey）。
    // （Ctrl+Alt+Del 是 SAS，根本到不了应用层，无须列。）
    private static readonly HashSet<(uint Mods, uint Vk)> ReservedKeys = new()
    {
        (0x1, 0x73),   // Alt+F4
        (0x1, 0x09),   // Alt+Tab
        (0x1, 0x1B),   // Alt+Esc
        (0x1, 0x20),   // Alt+Space
        (0x2, 0x1B),   // Ctrl+Esc
        (0x6, 0x1B),   // Ctrl+Shift+Esc（任务管理器）
    };

    // 是否系统保留组合。捕捉 UI 与注册路径都要查——先经与注册完全相同的解析（KeyInput.ToHotkeyParams）
    // 规范化，再比对；解析不了的交由后续「无效键」路径处理，此处返回 false。
    public static bool IsReserved(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var p = KeyInput.ToHotkeyParams(combo);
        return p != null && ReservedKeys.Contains((p.Modifiers, p.Vk));
    }

    // 由修饰键 + 主键组出组合键串。要求至少一个修饰键（避免注册裸键把某键从全局劫走），
    // 排除系统保留组合，且最终组合必须可被 RegisterHotKey 注册；否则返回 null（调用方忽略本次按键）。
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
        if (IsReserved(combo)) return null;                             // 系统保留组合：拒绝
        return KeyInput.ToHotkeyParams(combo) == null ? null : combo;   // 不可注册就作废
    }

    // 捕捉框按键的统一决策（设置页急停键 / 组编辑器全局热键共用，两处不再各抄一份状态机）。
    public enum CaptureAction
    {
        Ignore,       // 吞掉本次按键，继续等（只按修饰键 / 组不出可注册组合）
        PassThrough,  // 不拦截（e.Handled=false）：裸 Tab/Enter 留给焦点导航与默认按钮——否则键盘用户被困在框里
        Cancel,       // Esc：恢复原值、退出捕捉
        Clear,        // Delete/Backspace：清空热键
        Captured,     // 捕捉成功，combo 即结果
    }

    // 调用方先把 Key.System 解包成 SystemKey 再传入。
    public static CaptureAction ProcessCaptureKey(Key key, ModifierKeys mods, out string? combo)
    {
        combo = null;
        bool bare = (mods & ~ModifierKeys.Shift) == ModifierKeys.None;   // 无修饰（Shift 单独不算）
        if (key == Key.Tab && bare) return CaptureAction.PassThrough;    // Tab/Shift+Tab：移动焦点
        if (key == Key.Enter && mods == ModifierKeys.None) return CaptureAction.PassThrough;  // 裸 Enter：默认按钮
        if (IsModifierKey(key)) return CaptureAction.Ignore;
        if (key == Key.Escape) return CaptureAction.Cancel;
        // 只有「裸」Delete/Backspace 才是清空；带修饰键的（如 Ctrl+Delete）是用户想录的组合，交给 BuildCombo。
        if (key is Key.Delete or Key.Back && mods == ModifierKeys.None) return CaptureAction.Clear;
        combo = BuildCombo(mods, key);
        return combo == null ? CaptureAction.Ignore : CaptureAction.Captured;
    }
}
